#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using System.Collections.ObjectModel;
using CHAR;
using CardPreview;
using Pathfinding;

public class CardPreviewObject : MonoBehaviour
{
    public CardPreviewInfo mCardPreview { get; set; }

    private void Start()
    {
        int hitRadius = 0;
        int hitHeight = 0;
        if (mCardPreview.MetaData is UnitMetaData umd)
        {
            hitRadius = umd.HitRadius;
            hitHeight = umd.HitHeight;
        }
        else if (mCardPreview.MetaData is BigUnitGroupMetaData bugmd)
        {
            for (int i = 0; i < bugmd.GetMetaArrayCount(); ++i)
            {
                umd = MetaDataManager.Instance.GetUnitMetaData(bugmd.GetUnitMetaID(i));
                if (umd != null)
                {
                    hitRadius = umd.HitRadius;
                    hitHeight = umd.HitHeight;
                }
            }
        }
        else if (mCardPreview.MetaData is ActionMetaData amd)
        {
            if (amd.ActionType == ActionMetaData.eActionType.UNIT)
            {
                umd = MetaDataManager.Instance.GetUnitMetaData(amd.SubMetaID);
                if (umd != null)
                {
                    hitRadius = umd.HitRadius;
                    hitHeight = umd.HitHeight;
                }
            }
        }

        gameObject.layer = LayerMask.NameToLayer("Unit");
        BoxCollider collider = gameObject.AddComponent<BoxCollider>();
        float size = Mathf.Max(hitRadius / 40f, 4f);
        collider.size = new Vector3(size, hitHeight / 200f, size);
    }
}

public class CardPreviewInfo
{
    private ActionMetaData.eActionType mActionType;
    private object mMetaData;
    private ActionPreview[] mArrPreview;
    private bool mIsAddCardPreviewObject;
    private bool mAddedCardPreviewObject;
    private bool mEnabled;
    private Character[] mArrUnitData;
    
    public GameObject ActionCardObj { get; private set; }
    public ActionInfo ActionInfo { get; set; }
    public ActionCommandBase Command { get; set; }
    public bool IsHost { get; private set; }
    public bool IsAction { get; private set; }
    public bool IsRecording { get; private set; }
    public bool IsEnd { get; set; }

    public int MetaID
    {
        get
        {
            if (mMetaData is ActionMetaData amd)
                return amd.MetaID;
            else if (mMetaData is UnitMetaData umd)
                return umd.MetaID;
            else if (mMetaData is BigUnitGroupMetaData bmd)
                return bmd.MetaID;
            return 0;
        }
    }

    public Character UnitData { get { return mArrUnitData != null && mArrUnitData.Length > 0 ? mArrUnitData[0] : default; } }
    public Character[] ArrUnitData { get { return mArrUnitData; } }

    public object MetaData { get { return mMetaData; } }

    public CardPreviewInfo(bool isHost, bool isAddCardPreviewObject, bool isRecording, ActionMetaData.eActionType actionType, 
        eActionPreviewType previewType, object metaData, Character[] arrUnitData = null)
    {
        IsHost = isHost;
        IsAction = metaData is ActionMetaData;
        IsRecording = isRecording;
        mIsAddCardPreviewObject = isAddCardPreviewObject;
        mActionType = actionType;
        mMetaData = metaData;
        mArrUnitData = arrUnitData;

        switch (this.mActionType)
        {
            case ActionMetaData.eActionType.NOR:
                ActionPreview ap = CreateActionPreview(previewType, isHost);
                if (ap != null)
                {
                    mArrPreview = new ActionPreview[] { ap };
                    if (previewType == eActionPreviewType.CIRCLE || previewType == eActionPreviewType.CUSTOM)
                    {
                        ActionMetaData amd = metaData as ActionMetaData;
                        Texture2D texture = EditorGUIUtility.FindTexture(AddressableAssetLoader.EDITOR_ASSET_PATH + MetaDataManager.Instance.GetImageKey(amd.MetaID));
                        if (texture != null)
                        {
                            ActionCardObj = new GameObject(MetaDataManager.Instance.GetNameText(amd.MetaID), typeof(RectTransform));
                            (ActionCardObj.transform as RectTransform).sizeDelta = Vector2.one;
                            SpriteRenderer sr = ActionCardObj.AddComponent<SpriteRenderer>();
                            sr.sprite = Sprite.Create(texture, new Rect(Vector2.zero, new Vector2(texture.width, texture.height)), new Vector2(0.5f, 0f));
                            sr.transform.localScale = new Vector3(4 * 2.3f, 7 * 2.3f, 1f);
                            ActionCardObj.AddComponent<SortingGroup>().sortingOrder = 1;
                        }
                    }
                }
                break;
            case ActionMetaData.eActionType.UNIT:
                if (arrUnitData != null && arrUnitData.Length > 0)
                {
                    mArrPreview = new ActionPreview[arrUnitData.Length];
                    for (int i = 0; i < arrUnitData.Length; ++i)
                    {
                        ap = CreateActionPreview(previewType, isHost);
                        if (ap != null)
                            mArrPreview[i] = ap;
                    }
                }
                else
                {
                    ap = CreateActionPreview(previewType, isHost);
                    if (ap != null)
                        mArrPreview = new ActionPreview[] { ap };
                }
                break;
        }

        if (mArrPreview != null)
        {
            for (int i = 0; i < mArrPreview.Length; ++i)
                mArrPreview[i].ActivePreview(false);
        }
    }

    private ActionPreview CreateActionPreview(eActionPreviewType previewType, bool isHost)
    {
        ActionPreview actionPreview = null;
        int teamID = isHost ? BattleManager.HOST_TEAM_ID : BattleManager.GUEST_TEAM_ID;

        switch (previewType)
        {
            case eActionPreviewType.CIRCLE:
                actionPreview = new ActionPreviewCircle(null, eActionPreviewType.CIRCLE, teamID);
                break;
            case eActionPreviewType.PREVIEW:
                actionPreview = new ActionPreviewObject(null, eActionPreviewType.PREVIEW, teamID, false);
                break;
            case eActionPreviewType.BOTH:
                actionPreview =  new ActionPreviewBoth(null, eActionPreviewType.BOTH, teamID, false);
                break;
            case eActionPreviewType.CUSTOM:
                actionPreview = new ActionPreviewCustom(null, eActionPreviewType.CUSTOM, teamID);
                break;
        }

        return actionPreview;
    }

    public void StartPreview(Transform previewRoot)
    {
        if (mArrPreview == null || mArrPreview.Length == 0)
            return;

        EnablePreview(true);

        ActionMetaData amd = mMetaData as ActionMetaData;
        if (amd != null)
        {
            for (int i = 0; i < mArrPreview.Length; ++i)
                mArrPreview[i]?.StartPreview(amd, 1);

            switch (amd.ActionType)
            {
                case ActionMetaData.eActionType.NOR:
                    if (ActionCardObj != null)
                    {
                        for (int i = 0; i < mArrPreview.Length; ++i)
                        {
                            if (mArrPreview[i] is ActionPreviewCircle apc && apc.ActionTarget != null)
                            {
                                apc.ActionTarget.transform.parent = previewRoot;
                                ActionCardObj.transform.parent = apc.ActionTarget.transform;
                                if (mIsAddCardPreviewObject)
                                    AddCardPreviewObject(apc.ActionTarget.gameObject);
                            }
                            else if (mIsAddCardPreviewObject)
                                AddCardPreviewObject(ActionCardObj);
                        }
                    }
                    break;
            }
        }
        else
        {
            UnitMetaData umd = null;
            if (mMetaData is UnitMetaData)
                umd = mMetaData as UnitMetaData;
            else if (mMetaData is BigUnitGroupMetaData bmd)
                umd = MetaDataManager.Instance.GetUnitMetaData(bmd.GetUnitMetaID(0));

            if (umd != null)
            {
                for (int i = 0; i < mArrPreview.Length; ++i)
                {
                    mArrPreview[i].StartPreview(new ActionMetaData(umd.MetaID, 1, eGrade.NORMAL, mActionType, 0, umd.GetDurationValueByLevel(), 
                        0, "", eCommandType.NORMAL, null, ActionMetaData.eCommandBuffKind.N, null, 0, eActionPreviewType.PREVIEW, "", 
                        ActionMetaData.eShowCastingRange.None, ActionMetaData.eSpawnType.Field, 0, false, 0, umd.AssetKey), 1);
                }
            }
        }

        for (int i = 0; i < mArrPreview.Length; ++i)
        {
            mArrPreview[i].ActivePreview(true);
            mArrPreview[i].UpdatePosition(Vector3.one * 10000f, IsHost);

            if (ActionCardObj != null)
            {
                Vector3 scale = ActionCardObj.transform.localScale;
                if (mArrPreview[i] is ActionPreviewCircle apc)
                {
                    Vector3 rootScale = apc.ActionTarget.transform.localScale;
                    ActionCardObj.transform.localScale = new Vector3(0.5f / rootScale.x * scale.x, 0.5f / rootScale.y * scale.y, scale.z);
                }
                else
                    ActionCardObj.transform.localScale = scale * 0.3f;
            }
        }

        UpdatePosition(amd == null || amd.ActionType == ActionMetaData.eActionType.UNIT ? Int2.zero.ToVec3(Constants.GROUND_HEIGHT) : Vector3.one * 10000f);
    }

    public void EndPreview(bool clear = true, bool maintainCardObj = false)
    {
        if (mArrPreview != null)
        {
            for (int i = 0; i < mArrPreview.Length; ++i)
                mArrPreview[i].EndPreview();
        }

        if (ActionCardObj != null)
        {
            if (maintainCardObj)
            {
                for (int i = 0; i < mArrPreview.Length; ++i)
                {
                    if (mArrPreview[i] is ActionPreviewCircle apc)
                    {
                        apc.ActionTarget.Show(true);
                        apc.ActionTarget.ActiveEffect(false);
                    }
                }
            }
            else
            {
                GameObject.DestroyImmediate(ActionCardObj);
                ActionCardObj = null;
            }
        }

        if (clear && mArrPreview != null)
        {
            for (int i = 0; i < mArrPreview.Length; ++i)
                mArrPreview[i].Clear();
        }
    }

    public void ShowCardObj(bool show)
    {
        ActionCardObj?.SetActive(show);
    }

    public void EnablePreview(bool enabled)
    {
        this.mEnabled = enabled;
    }

    public void UpdatePosition(Vector3 pos)
    {
        if (!mEnabled || IsEnd)
            return;

        pos.y = Constants.GROUND_HEIGHT;

        if (mArrPreview != null)
        {
            for (int i = 0; i < mArrPreview.Length; ++i)
            {
                Vector3 prevPos = mArrPreview[i].SelectPoint;
                Vector3 updatePos = mArrPreview[i].UpdatePosition(pos, IsHost);
                Command?.UpdatePositionFromPreview(updatePos - prevPos, BattleManager.Instance.BattleCam);

                if (mAddedCardPreviewObject == false)
                {
                    ReadOnlyCollection<GameObject> previewObjectList = null;
                    if (mArrPreview[i] is ActionPreviewObject apo)
                        previewObjectList = apo.previewObjectList;
                    else if (mArrPreview[i] is ActionPreviewBoth apb)
                        previewObjectList = apb.PrevieObject.previewObjectList;

                    if (previewObjectList != null && previewObjectList.Count > 0)
                    {
                        mAddedCardPreviewObject = true;
                        mArrPreview[i].ActivePreview(true);

                        for (int j = 0; j < previewObjectList.Count; ++j)
                        {
                            var obj = previewObjectList[j];
                            if (mIsAddCardPreviewObject)
                                AddCardPreviewObject(obj);

                            if (IsHost == false)
                                obj.transform.eulerAngles = new Vector3(obj.transform.eulerAngles.x, 180f, obj.transform.eulerAngles.z);

                            if (mArrUnitData != null && i < mArrUnitData.Length)
                            {
                                BattleUnit bu = obj.GetComponent<BattleUnit>();
                                if (bu != null)
                                {
                                    bu.Init(mArrUnitData[i]);
                                    bu.UnitData.SetPosition(pos);
                                    if (j == 0)
                                        ActionInfo?.SetUnit(bu);

                                    if (IsAction == false)
                                    {
                                        mArrUnitData[i].SetTargetNexus();
                                        mArrUnitData[i].Move();

                                        CharacterContext context = new CharacterContext();
                                        bu.GetComponent<View>().SetContext(context);
                                        mArrUnitData[i].Context = context;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        if (ActionCardObj != null)
            ActionCardObj.transform.position = pos;

        if (IsAction == false && mArrUnitData != null && mArrUnitData.Length > 0)
        {
            // 유닛의 위치 정보 갱신
            for (int i = 0; i < mArrUnitData.Length; ++i)
            {
                mArrUnitData[i].SetPosition(pos);
                mArrUnitData[i].UpdateFrame((float)Constants.FIXED_FRAME_TIME);
                mArrUnitData[i].UpdateRender();
            }
        }
    }

    public void UpdatePreview()
    {
        if (!IsEnd)
        {
            if (mArrPreview != null)
            {
                for (int i = 0; i < mArrPreview.Length; ++i)
                    mArrPreview[i].UpdatePreview();
            }
        }
    }

    private void AddCardPreviewObject(GameObject obj)
    {
        if (obj != null)
        {
            CardPreviewObject cpo = obj.GetComponent<CardPreviewObject>();
            if (cpo == null)
                cpo = obj.AddComponent<CardPreviewObject>();
            cpo.mCardPreview = this;
        }
    }

    public void ArrangementOnField(bool isAddUnitInGrid, bool isApplyOnField = true)
    {
        if (mArrPreview != null)
        {
            for (int i = 0; i < mArrPreview.Length; ++i)
            {
                ReadOnlyCollection<GameObject> previewObjectList = null;
                if (mArrPreview[i] is ActionPreviewObject apo)
                    previewObjectList = apo.previewObjectList;
                else if (mArrPreview[i] is ActionPreviewBoth apb)
                    previewObjectList = apb.PrevieObject.previewObjectList;

                if (previewObjectList != null && previewObjectList.Count > 0)
                {
                    for (int j = 0; j < previewObjectList.Count; ++j)
                    {
                        BattleUnit bu = previewObjectList[j].GetComponent<BattleUnit>();
                        if (bu != null)
                        {
                            #if UNITY_EDITOR
                            if (j == 0)
                                ActionInfo?.SetUnit(bu);
                            #endif
                            bu.UnitData.Controller.SetPosition(mArrPreview[i].SelectPoint.ToInt2());
                            bu.UnitData.Controller.UpdateCurrentGrid();
                            if ((isApplyOnField || bu.UnitData.IsOnGrid()) && BattleField.Instance.IsPositionInGrid(bu.UnitData.PosVec3))
                            {
                                if (isAddUnitInGrid)
                                    BattleField.Instance.AddUnitInGrid(bu.UnitData.CurrentGrid, bu.UnitData);
                                else
                                    BattleField.Instance.RemoveUnitInGrid(bu.UnitData.CurrentGrid, bu.UnitData.UnitPos);
                            }
                        }
                    }
                }
            }
        }
    }

    public void ChangeUnitState(Character.State state, bool setCurrent = true)
    {
        if (mArrUnitData != null && mArrUnitData.Length > 0)
        {
            // 유닛의 프리뷰 상태 해제
            for (int i = 0; i < mArrPreview.Length; ++i)
            {
                ReadOnlyCollection<GameObject> previewObjectList = null;
                if (mArrPreview[i] is ActionPreviewObject apo)
                    previewObjectList = apo.previewObjectList;
                else if (mArrPreview[i] is ActionPreviewBoth apb)
                    previewObjectList = apb.PrevieObject.previewObjectList;

                if (previewObjectList != null && previewObjectList.Count > 0)
                {
                    for (int j = 0; j < previewObjectList.Count; ++j)
                    {
                        BattleUnit bu = previewObjectList[j].GetComponent<BattleUnit>();
                        if (bu != null)
                            bu.DestroyPreview();
                    }
                }
            }

            for (int i = 0; i < mArrUnitData.Length; ++i)
            {
                if (setCurrent)
                    mArrUnitData[i].SetCurrentPosRotation();
                mArrUnitData[i].ChangeState(state);
                mArrUnitData[i].UpdateRender();
            }
        }
    }
}
#endif