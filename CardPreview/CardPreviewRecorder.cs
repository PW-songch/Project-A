using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
using Pathfinding;
using Thor;
using MVVM;
using CardPreview;

namespace CardPreview
{
    /// <summary>
    /// 프리뷰 기록 정보
    /// </summary>
    [Serializable]
    public struct RecordData
    {
        public int version;
        public int randomSeed;
        public int metaID;
        public int playerLevel;
        public int enemyLevel;
        public int playerBigUnitLevel;
        public int enemyBigUnitLevel;
        public int playerCardLevel;
        public int enemyCardLevel;
        public List<ActionInfo> actionList;
        public List<ViewInfo> viewList;

        public ActionCard[] GetActionCardList(bool isHost)
        {
            if (actionList != null && actionList.Count > 0)
            {
                List<ActionCard> list = new List<ActionCard>();
                for (int i = 0; i < actionList.Count; ++i)
                {
                    if (actionList[i].isHost == isHost && Utility.ConvertMetaIDType(actionList[i].ActionID) == eMetaID_Type.Action)
                        list.Add(new ActionCard(actionList[i].ActionID, isHost ? playerCardLevel : enemyCardLevel));
                }

                return list.ToArray();
            }

            return null;
        }

        public UnitCard[] GetUnitCardList(bool isHost)
        {
            if (actionList != null && actionList.Count > 0)
            {
                List<UnitCard> list = new List<UnitCard>();
                for (int i = 0; i < actionList.Count; ++i)
                {
                    if (actionList[i].isHost == isHost && Utility.ConvertMetaIDType(actionList[i].ActionID) == eMetaID_Type.BigUnit)
                        list.Add(new UnitCard(actionList[i].ActionID, isHost ? playerBigUnitLevel : enemyBigUnitLevel));
                }

                return list.ToArray();
            }

            return null;
        }
    }

    /// <summary>
    /// 액션 정보
    /// </summary>
    [Serializable]
    public class ActionInfo
    {
        public bool isHost;
        public uint turn;                            //턴
        public ActionEvent actionEvent;

#if UNITY_EDITOR
        public BattleUnit Unit { get; private set; }
#endif

        public int ActionID => actionEvent.event_string.id;
        public Int2 ActionPos => new Int2(GetCommandValue(0), GetCommandValue(1));
        public Int2 DoubleActionPos => new Int2(GetCommandValue(3), GetCommandValue(4));

        public const int ACTION_VALUE_START_INDEX = 2;

        public void SetHost(bool isHost)
        {
            this.isHost = isHost;
            actionEvent.user_index = isHost ? CardPreviewUpdater.HOST_INDEX : CardPreviewUpdater.ENEMY_INDEX;
        }

        public void SetTurn(uint turn)
        {
            this.turn = turn;
            actionEvent.turn = turn;
        }

        public int GetCommandValue(int index = ACTION_VALUE_START_INDEX)
        {
            return actionEvent.event_string.values != null && 
                index < actionEvent.event_string.values.Length ? actionEvent.event_string.values[index] : 0;
        }

        public int[] GetCommandValues()
        {
            if (actionEvent.event_string.values != null && actionEvent.event_string.values.Length > ACTION_VALUE_START_INDEX)
            {
                int[] values = new int[actionEvent.event_string.values.Length - ACTION_VALUE_START_INDEX];
                for (int i = 0; i < values.Length; ++i)
                    values[i] = actionEvent.event_string.values[i + ACTION_VALUE_START_INDEX];
                return values;
            }

            return null;
        }

#if UNITY_EDITOR
        public void SetUnit(BattleUnit unit)
        {
            Unit = unit;
        }
#endif
    }

    /// <summary>
    /// 뷰 정보
    /// </summary>
    [Serializable]
    public class ViewInfo
    {
        public uint turn;
        public bool isActiveSplitView;
        public CameraAction mainViewAction;
        public CameraAction splitViewAction;
        public UIInfo uiInfo;

        public ViewInfo(uint turn)
        {
            this.turn = turn;
            mainViewAction = new CameraAction();
            splitViewAction = new CameraAction();
            uiInfo = new UIInfo();
        }
    }

    /// <summary>
    /// 카메라 액션
    /// </summary>
    [Serializable]
    public class CameraAction
    {
        public enum eType
        {
            NONE,
            MOVE,
            FOLLOW,
            RESTORE,
        }

        public eType type;
        public float time;
        public float orthogragphicSize = BattleCamera.orthorSize;
        public int moveGridX = -1;
        public int moveGridY = -1;
        public Vector3 posOffset;
        public int followTargetIID;
        public bool isEnemyView;

        public Vector2 moveGridCoord => new Vector2(moveGridX, moveGridY);
    }

    /// <summary>
    /// UI 정보
    /// </summary>
    [Serializable]
    public class UIInfo
    {
        [Flags]
        public enum eUIType
        {
            NONE = 0,
            COST_MANA = 1 << 1,
            MAIN_TEXT = 1 << 2,
            SPLIT_TEXT = 1 << 3,
        }

        public eUIType activeUIType;
        public int mainViewTextID;
        public int splitViewTextID;
    }
}

/// <summary>
/// 카드 프리뷰 기록
/// </summary>
public class CardPreviewRecorder
{
    private const int START_SUB_INDEX = 2;
    public const string FILE_EXTENTION = "asset";
    public const string FOLDER_NAME = "card_preview";
    public static readonly string FILE_PATH = AddressableAssetLoader.EDITOR_ASSET_PATH + FOLDER_NAME;

    private CardPreview.RecordData mPreviewInfo;        //기본정보
    private List<ActionInfo> mActionInfoList = new List<ActionInfo>();
    private List<ViewInfo> mViewInfoList = new List<ViewInfo>();
    private List<KeyValuePair<string, string>> mCardPreviewDataList;
    private int mCurrentDataIndex;
    
    private int CurrentDataIndex
    {
        get
        {
            if (mCurrentDataIndex + 1 >= mCardPreviewDataList.Count)
                mCurrentDataIndex = 0;
            else
                mCurrentDataIndex++;
            return mCurrentDataIndex;
        }
    }

    public ReadOnlyCollection<ActionInfo> ActionInfoList { get => mActionInfoList.AsReadOnly(); }
    public ReadOnlyCollection<ViewInfo> ViewInfoList { get => mViewInfoList.AsReadOnly(); }

    public string LoadedFileName { get; private set; }
    public bool IsLoaded { get => !string.IsNullOrEmpty(LoadedFileName); }
    public bool IsMultipleData => mCardPreviewDataList?.Count > 1;

    /// <summary>
    /// 레코딩 시작
    /// </summary>
    public void InitRecording(int metaID, int randomSeed, int version)
    {
        InitRecording(metaID);
        mPreviewInfo.randomSeed = randomSeed;
        mPreviewInfo.version = version;
        SetLevel(MetaDataManager.Instance.GetPlayerMaxLevel(), MetaDataManager.Instance.GetPlayerMaxLevel());
        SetBigUnitLevel(MetaDataManager.Instance.GetBigUnitGrowthMaxLevel(), MetaDataManager.Instance.GetBigUnitGrowthMaxLevel());
        SetCardLevel(MetaDataManager.Instance.GetActionGrowthMaxLevel(), MetaDataManager.Instance.GetActionGrowthMaxLevel());
    }

    public void InitRecording(int metaID)
    {
        mPreviewInfo = new CardPreview.RecordData();
        mPreviewInfo.metaID = metaID;
        LoadedFileName = string.Empty;
        ClearRecordList();
        AddEndActionInfo();
        Reset();

        if (mCardPreviewDataList == null)
            mCardPreviewDataList = new List<KeyValuePair<string, string>>();
        else
            mCardPreviewDataList.Clear();
    }

    public void Reset(bool setData = false)
    {
        int index = mCurrentDataIndex;
        mCurrentDataIndex = -1;
        if (setData)
        {
            UpdateCardPreviewData(index);
            SetCardPreviewData(false);
        }
    }

    public bool IsLoadedPreview(int metaID)
    {
        return IsLoaded && mPreviewInfo.metaID == metaID;
    }

    /// <summary>
    /// 플레이어 레벨 설정
    /// </summary>
    public void SetLevel(int playerLevel, int enemyLevel)
    {
        mPreviewInfo.playerLevel = playerLevel;
        mPreviewInfo.enemyLevel = enemyLevel;
    }

    /// <summary>
    /// 빅유닛 레벨 설정
    /// </summary>
    public void SetBigUnitLevel(int playerLevel, int enemyLevel)
    {
        mPreviewInfo.playerBigUnitLevel = playerLevel;
        mPreviewInfo.enemyBigUnitLevel = enemyLevel;
    }

    /// <summary>
    /// 카드 레벨 설정
    /// </summary>
    public void SetCardLevel(int playerLevel, int enemyLevel)
    {
        mPreviewInfo.playerCardLevel = playerLevel;
        mPreviewInfo.enemyCardLevel = enemyLevel;
    }

    public int GetLevel(bool isPlayer)
    {
        return isPlayer ? mPreviewInfo.playerLevel : mPreviewInfo.enemyLevel;
    }

    public int GetBigUnitLevel(bool isPlayer)
    {
        return isPlayer ? mPreviewInfo.playerBigUnitLevel : mPreviewInfo.enemyBigUnitLevel;
    }

    public int GetCardLevel(bool isPlayer)
    {
        return isPlayer ? mPreviewInfo.playerCardLevel : mPreviewInfo.enemyCardLevel;
    }

    /// <summary>
    /// 카드 프리뷰 data 셋팅
    /// </summary>
    public bool SetCardPreviewData(bool updateData, bool isDecompress = true)
    {
        if (updateData)
            UpdateCardPreviewData(mCurrentDataIndex);

        int index = CurrentDataIndex;
        if (mCardPreviewDataList != null && index >= 0 && mCardPreviewDataList.Count > index)
        {
            LoadedFileName = mCardPreviewDataList[index].Key;
            CardPreview.RecordData info = ConvertRecordData(mCardPreviewDataList[index].Value, isDecompress);
            //if (info.metaID == mPreviewInfo.metaID)
            {
                ClearRecordList();
                info.metaID = mPreviewInfo.metaID;
                mPreviewInfo = info;
                mActionInfoList.AddRange(mPreviewInfo.actionList);
                mViewInfoList.AddRange(mPreviewInfo.viewList);
                SortActionInfoList();
                SortViewInfoList();
                return true;
            }
        }

        return false;
    }

    public CardPreview.RecordData ConvertRecordData(string data, bool isDecompress = true)
    {
        if (isDecompress)
            data = Utility.DecompressionText(data);
        return JsonUtility.FromJson<CardPreview.RecordData>(data);
    }

    /// <summary>
    /// 압축된 카드 프리뷰 데이터 리턴
    /// </summary>
    public string GetCardPreviewData(bool compress = true)
    {
        mPreviewInfo.actionList = new List<ActionInfo>(mActionInfoList);
        mPreviewInfo.viewList = new List<ViewInfo>(mViewInfoList);

        string data = JsonUtility.ToJson(mPreviewInfo);
        if (compress)
            return Utility.CompressionText(data);
        return data;
    }

    public ActionInfo[] GetActionInfoList()
    {
        if (mActionInfoList != null && mActionInfoList.Count > 0)
        {
            List<ActionInfo> list = new List<ActionInfo>();
            mActionInfoList.ForEach(e =>
            {
                if (e.turn > 0)
                    list.Add(e);
            });
            return list.ToArray();
        }

        return null;
    }

    public ViewInfo[] GetViewInfoList(bool isStarting)
    {
        if (mViewInfoList != null && mViewInfoList.Count > 0)
        {
            List<ViewInfo> list = new List<ViewInfo>();
            mViewInfoList.ForEach(c =>
            {
                if (isStarting)
                {
                    if (c.turn == 0)
                        list.Add(c);
                }
                else if (c.turn > 0)
                    list.Add(c);
            });
            return list.ToArray();
        }

        return null;
    }

    public ViewInfo GetViewInfo(int index)
    {
        return 0 <= index && index < mViewInfoList.Count ? mViewInfoList[index] : null;
    }

    /// <summary>
    /// 액션 정보 삭제
    /// </summary>
    public void ClearRecordList(bool maintainStarting = false)
    {
        if (maintainStarting == false)
        {
            mActionInfoList.Clear();
            mViewInfoList.Clear();
        }
        else
        {
            List<ActionInfo> maintainActionList = new List<ActionInfo>();
            mActionInfoList.ForEach(action =>
            {
                if (action.turn == 0 && action.ActionID > 0)
                    maintainActionList.Add(action);
            });
            if (mActionInfoList.Count > 0 && mActionInfoList[mActionInfoList.Count - 1].ActionID == 0)
                maintainActionList.Add(mActionInfoList[mActionInfoList.Count - 1]);

            mActionInfoList.Clear();
            mActionInfoList.AddRange(maintainActionList);

            List<ViewInfo> maintainViewList = new List<ViewInfo>();
            foreach (var cam in mViewInfoList)
            {
                if (cam.turn == 0)
                    maintainViewList.Add(cam);
            }

            mViewInfoList.Clear();
            mViewInfoList.AddRange(maintainViewList);
        }
    }

    /// <summary>
    /// 액션 정보 기록
    /// </summary>
    public ActionInfo RecordActionInfo(bool isHost, uint turn, ActionInfo actionInfo, bool addEndAction = true)
    {
        if (addEndAction)
            AddEndActionInfo(turn + 1);

        actionInfo.SetHost(isHost);
        actionInfo.SetTurn(turn);
        actionInfo.actionEvent.user_index = actionInfo.isHost ? CardPreviewUpdater.HOST_INDEX : CardPreviewUpdater.ENEMY_INDEX;
        actionInfo.actionEvent.event_string.et = (int)RunEventBase.eType.Action;

        if (mActionInfoList.Contains(actionInfo) == false)
        {
            mActionInfoList.Add(actionInfo);
            SortActionInfoList();
        }

        int num = 0;
        LogConsole.NormalFormat(E_Category.CardPreview, "Record Preview - " +
            "Turn: {" + num++ + "}, metaID: {" + num++ + "}",
            actionInfo.turn, actionInfo.actionEvent.event_string.id);

        return actionInfo;
    }

    /// <summary>
    /// 끝 액션 정보 추가
    /// </summary>
    public void AddEndActionInfo(uint turn = 0)
    {
        if (mActionInfoList.Count > 1)
            turn = (uint)Mathf.Max(Mathf.Max(turn, 1), mActionInfoList[mActionInfoList.Count - 1].turn);
        if (mActionInfoList.Count > 0 && mActionInfoList[mActionInfoList.Count - 1].ActionID == 0)
            RemoveActionInfo(mActionInfoList[mActionInfoList.Count - 1]);
        RecordActionInfo(true, turn, new ActionInfo() { turn = turn, actionEvent = new ActionEvent() { turn = turn, } }, false);
    }

    /// <summary>
    /// 액션 정보 삭제
    /// </summary>
    public void RemoveActionInfo(ActionInfo actionInfo)
    {
        mActionInfoList.Remove(actionInfo);
    }

    /// <summary>
    /// 액션 정보 리스트 정렬
    /// </summary>
    public void SortActionInfoList()
    {
        // 종료 턴 찾기
        uint lastTurn = 0;
        mActionInfoList.ForEach(
            (info) =>
            {
                if (lastTurn < info.turn)
                    lastTurn = info.turn;
            });
        for (int i = 0; i < mActionInfoList.Count; ++i)
        {
            if (mActionInfoList[i].ActionID != 0 && mActionInfoList[i].turn >= lastTurn)
            {
                // 종료 턴 갱신
                mActionInfoList[mActionInfoList.Count - 1].SetTurn(mActionInfoList[i].turn + 1);
                break;
            }
        }

        mActionInfoList.Sort((l, r) => l.turn.CompareTo(r.turn));
    }

    /// <summary>
    /// 뷰 정보 기록
    /// </summary>
    public void RecordViewInfo(ViewInfo camInfo)
    {
        mViewInfoList.Add(camInfo);
        SortViewInfoList();
    }

    /// <summary>
    /// 뷰 정보 삭제
    /// </summary>
    public void RemoveViewInfo(ViewInfo camInfo)
    {
        mViewInfoList.Remove(camInfo);
    }

    /// <summary>
    /// 뷰 정보 리스트 정렬
    /// </summary>
    public void SortViewInfoList()
    {
        if (mActionInfoList.Count > 0)
        {
            // 종료 턴보다 큰지 체크
            uint lastTurn = mActionInfoList[mActionInfoList.Count - 1].turn - 1;
            for (int i = 0; i < mViewInfoList.Count; ++i)
            {
                if (mViewInfoList[i].turn > lastTurn)
                {
                    // 종료 턴으로 갱신
                    mViewInfoList[i].turn = lastTurn;
                    break;
                }
            }
        }

        mViewInfoList.Sort((l, r) => l.turn.CompareTo(r.turn));
    }

    /// <summary>
    /// 시작 액션 정보 리스트 리턴
    /// </summary>
    public ActionInfo[] GetStartingUnitList()
    {
        List<ActionInfo> idList = new List<ActionInfo>();
        for (int i = 0; i < mActionInfoList.Count; ++i)
        {
            if (mActionInfoList[i].turn == 0)
            {
                switch (Utility.ConvertMetaIDType(mActionInfoList[i].ActionID))
                {
                    case eMetaID_Type.Unit:
                    case eMetaID_Type.BigUnit:
                        idList.Add(mActionInfoList[i]);
                        break;
                }
            }
        }

        return idList.ToArray();
    }

    /// <summary>
    /// BattleUserInfo 생성
    /// </summary>
    public BattleUserInfo CreateBattleUserInfo(bool isHost, int index = -1)
    {
        if (index < 0)
            index = mCurrentDataIndex;
        if (IsLoaded == false || index >= mCardPreviewDataList?.Count)
            return null;

        CardPreview.RecordData info = ConvertRecordData(mCardPreviewDataList[index].Value);
        return new BattleUserInfo(string.Empty, isHost ? CardPreviewUpdater.HOST_INDEX : CardPreviewUpdater.ENEMY_INDEX,
            info.GetActionCardList(isHost), info.GetUnitCardList(isHost), 0, isHost ? info.playerLevel : info.enemyLevel, 0, !isHost);
    }

#if UNITY_EDITOR
    /// <summary>
    /// 레코드 데이터 저장
    /// </summary>
    public void SaveRecordData(string path)
    {
        if (string.IsNullOrEmpty(path) == false)
        {
            CardPreviewData dataAsset = ScriptableObject.CreateInstance<CardPreviewData>();
            dataAsset.SetData(GetCardPreviewData());
            UnityEditor.AssetDatabase.DeleteAsset(path);
            UnityEditor.AssetDatabase.CreateAsset(dataAsset, path);
            UnityEditor.AssetDatabase.Refresh();
        }
    }
#endif

    /// <summary>
    /// 레코드 데이터 로드
    /// </summary>
    public bool LoadRecordData(string path, bool isTool = false, Action<string> callback = null, int subIndex = START_SUB_INDEX)
    {
        if (string.IsNullOrEmpty(path) == false)
        {
            int index = path.IndexOf(FOLDER_NAME);
            path = path.Substring(index, path.Length - index);

#if UNITY_EDITOR
            if (isTool)
            {
                path = AddressableAssetLoader.EDITOR_ASSET_PATH + path;
                CardPreviewData dataAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<CardPreviewData>(path);
                if (dataAsset != null)
                {
                    if (subIndex == START_SUB_INDEX)
                        mCardPreviewDataList.Clear();
                    mCardPreviewDataList.Add(new KeyValuePair<string, string>(System.IO.Path.GetFileName(path), dataAsset.Data));

                    if (subIndex == START_SUB_INDEX)
                    {
                        if (SetCardPreviewData(false))
                        {
                            // 다음 연결 기록 정보 로드
                            string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
                            if (int.TryParse(fileName, out int metaID))
                            {
                                RecursiveLoadRecordData(metaID, subIndex, isTool, callback);
                                return true;
                            }
                        }
                    }

                    callback?.Invoke(dataAsset.Data);
                    return true;
                }
            }
            else
#endif
            {
                if (Utility.IsExistAddressables(path))
                {
                    AddressableAssetLoader.Instance.GetAssetAndLoad<CardPreviewData>(path,
                        (dataAsset) =>
                        {
                            if (dataAsset != null)
                            {
                                if (subIndex == START_SUB_INDEX)
                                    mCardPreviewDataList.Clear();
                                mCardPreviewDataList.Add(new KeyValuePair<string, string>(System.IO.Path.GetFileName(path), dataAsset.Data));

                                if (subIndex == START_SUB_INDEX)
                                {
                                    if (SetCardPreviewData(false))
                                    {
                                        // 다음 연결 기록 정보 로드
                                        string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
                                        if (int.TryParse(fileName, out int metaID))
                                        {
                                            RecursiveLoadRecordData(metaID, subIndex, isTool, callback);
                                            return;
                                        }
                                    }
                                }

                                callback?.Invoke(dataAsset.Data);
                            }
                        });

                    return true;
                }
            }
        }

        return false;
    }

    public bool LoadRecordData(int metaID, Action<string> callback = null)
    {
        return LoadRecordData(GetRecordFilePath(metaID), callback: callback);
    }

    /// <summary>
    /// 연결된 기록 정보 로드
    /// 파일명 규칙 : MetaID_index (ex: 21000_2)
    /// index는 2부터
    /// </summary>
    private void RecursiveLoadRecordData(int metaID, int index, bool isTool, Action<string> callback)
    {
        if (LoadRecordData(GetRecordFilePath(metaID, index), isTool, (data) =>
        {
            RecursiveLoadRecordData(metaID, index, isTool, callback);
        }, ++index) == false)
        {
            callback?.Invoke(mCardPreviewDataList[0].Value);
        }
    }

    private void UpdateCardPreviewData(int index)
    {
        if (mCardPreviewDataList != null && index >= 0 && mCardPreviewDataList.Count > index)
            mCardPreviewDataList[index] = new KeyValuePair<string, string>(mCardPreviewDataList[index].Key, GetCardPreviewData());
    }

    /// <summary>
    /// 레코드 파일 이름 리턴
    /// </summary>
    private string GetRecordFileName(int metaID, int index = 0)
    {
        string name = string.Empty;
        if (index > 0)
        {
            // 연결되는 파일명 (MetaID_index (ex: 21000_2))
            name = StringBuilderPool.Format("{0}_{1}.{2}", metaID, index, FILE_EXTENTION);
        }
        else
            name = StringBuilderPool.Format("{0}.{1}", metaID, FILE_EXTENTION);
        return name;
    }

    /// <summary>
    /// 레코드 파일 경로 리턴
    /// </summary>
    private string GetRecordFilePath(int metaID, int index = 0, bool fullPath = false)
    {
        string path = StringBuilderPool.Format("{0}/{1}/{2}", FOLDER_NAME, metaID, GetRecordFileName(metaID, index));
        if (fullPath)
            path = StringBuilderPool.Format("{0}{1}", AddressableAssetLoader.EDITOR_ASSET_PATH, path);
        return path;
    }

    /// <summary>
    /// 현재 레코드 폴더 경로 리턴
    /// </summary>
    public string GetCurrentRecordFolderPath()
    {
        return FILE_PATH + "/" + GetCurrentRecordFileName();
    }

    /// <summary>
    /// 현재 레코드 파일 이름 리턴
    /// </summary>
    public string GetCurrentRecordFileName()
    {
        return mPreviewInfo.metaID.ToString();
    }
}