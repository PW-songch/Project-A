using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using Battle;
using CHAR;
using Pathfinding;
using Thor;
using CardPreview;
using Optimizing;

public class CardPreviewSettingTool : EditorWindow
{
    /// <summary>
    /// 메뉴 타입
    /// </summary>
    private enum eMenuType
    {
        NONE,
        PREVIEW_LIST,                       // 프리뷰 목록
        FRIENDLY_ARRANGEMENT_ITEM_LIST,     // 아군 배치 대상 목록
        ENEMY_ARRANGEMENT_ITEM_LIST,        // 적 배치 대상 목록
        ACTION_INFO_RECORD_LIST,            // 액션 정보 기록 목록
        VIEW_INFO_RECORD_LIST,              // 뷰 정보 기록 목록
    }

    /// <summary>
    /// 프리뷰 아이템 타입
    /// </summary>
    private enum eItemType
    {
        NONE,
        ACTION,
        UNIT,
        BIG_UNIT,
    }

    /// <summary>
    /// 플레이 상태
    /// </summary>
    [Flags]
    private enum ePlayState
    {
        NONE = 0,
        RECORDING = 1 << 1,
        PLAYING = 1 << 2,
        PAUSE = 1 << 3,
        STOP = 1 << 4
    }

    /// <summary>
    /// 프리뷰 아이템 정보
    /// </summary>
    private class ItemInfo
    {
        public eItemType type { get; private set; }
        public int metaID { get; private set; }
        public string name { get; private set; }

        public ItemInfo(eItemType type, int metaID, string name)
        {
            this.type = type;
            this.metaID = metaID;
            this.name = name;
        }
    }

    private const string SAVE_PARENT_WINDOW_TYPE = "SAVE_PARENT_WINDOW_TYPE";

    public static CardPreviewSettingTool Instance { get; private set; }

    private Scene mPreviewScene;
    private string mPrevScenePath;
    private List<GameObject> mActiveRoots;
    private GameObject[] mPickIgnoreObjs;

    private List<CardPreviewInfo> mPlacedPreviewList;
    private Dictionary<eItemType, KeyValuePair<List<ItemInfo>, GUIContent[]>> mDicItemList;
    private Dictionary<KeyValuePair<eMenuType, eItemType>, Vector2> mDicScrollPos;
    private Dictionary<eMenuType, Dictionary<eItemType, int>> mDicSelected;

    private eMenuType mMenuType = eMenuType.NONE;
    private eMenuType mPrevMenuType = eMenuType.NONE;
    private ePlayState mPlayState = ePlayState.STOP;
    private ItemInfo mSelectedItem;
    private CardPreviewInfo mCardPreview;
    private CardPreviewObject mPickedPreviewObj;
    private ActionCommandBase mCurrentCommand;
    private Transform mPreviewObjectRoot;
    private bool mPickGameObject;

    private CameraAction mSelectedCamAction;
    private string mSelectedControlName;
    private bool mIsSelectControl;

    private float mTime;
    private float mRepaintTime;
    private Stopwatch mStopWatch;
    private Type mGameViewType;

    private int mMaxLevel;
    private int mMaxBigUnitLevel;
    private int mCardMaxLevel;

    private CardPreviewUpdater mUpdater;
    private CardPreviewRecorder mRecorder;
    public CardPreviewRecorder Recorder => mRecorder;
    private ReadOnlyCollection<ActionInfo> mActionInfoList;
    private ReadOnlyCollection<ViewInfo> mViewInfoList;

    private RectTransform mRenderTextureRT;
    private Vector2 mRenderTextureSize;

    private int mGameViewSizeIndex;
    private bool mUseRT;
    private Rect mPosition;

    private GUIStyle mTitleStyle;
    private GUIStyle mTabLeftStyle;
    private GUIStyle mTabMiddleStyle;
    private GUIStyle mTabRightStyle;
    private GUIStyle mGridButtonStyle;
    private GUIStyle mTimeLabelStyle;
    private GUIContent mPlayButtonContent;
    private GUIContent mRecordButtonContent;
    private GUIContent mPauseButtonContent;
    private GUIContent mAddButtonContent;
    private GUIContent mRemoveButtonContent;

    private uint CurrentTurn { get => mUpdater.FixedFrameHandler.GetCurrentTurn(); }

    private const int TAB_HEIGHT = 50;

    [MenuItem("Tools/Card Preview Setting Tool", priority = 600)]
    static void ShowWindow()
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("알림", "Play 중에는 사용할 수 없습니다.", "확인");
            return;
        }

        if (Instance == null)
        {
            // 이전 도킹 상태 적용
            Type[] desiredDock = null;
            if (EditorPrefs.HasKey(SAVE_PARENT_WINDOW_TYPE))
            {
                string types = EditorPrefs.GetString(SAVE_PARENT_WINDOW_TYPE);
                if (string.IsNullOrEmpty(types) == false)
                {
                    string[] dockedTypes = types.Split(',');
                    if (dockedTypes != null && dockedTypes.Length > 0)
                    {
                        desiredDock = new Type[dockedTypes.Length];
                        for (int i = 0; i < dockedTypes.Length; ++i)
                            desiredDock[i] = Utility.GetTypeFromAssemblies(dockedTypes[i]);
                    }
                }
            }

            if (desiredDock != null)
                Instance = GetWindow<CardPreviewSettingTool>("Card Preview Setting Tool", desiredDock);
            else
                Instance = GetWindow<CardPreviewSettingTool>("Card Preview Setting Tool");
            Instance.minSize = new Vector2(680, 700);
            Instance.RunCardPreviewSetting();
        }
        Instance.Show();
        Instance.Focus();
    }

    private void OnEnable()
    {
        Initialize();

        mPosition = position;
        SaveDockedWindows();

        mTime = Time.realtimeSinceStartup;
        mStopWatch = new Stopwatch();

        mGameViewType = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.PlayModeView");

        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.pauseStateChanged += OnPauseStateChanged;
        EditorApplication.modifierKeysChanged += OnKeysChanged;

    }

    private void OnDestroy()
    {
        if (GameSetting.IsActive)
            GameSetting.Instance.useRT = mUseRT;

        ResetPreview(true);

        if (EditorApplication.isPlaying)
            EditorApplication.isPlaying = false;

        if (mActiveRoots != null)
        {
            for (int i = 0; i < mActiveRoots.Count; ++i)
                mActiveRoots[i]?.SetActive(true);
            mActiveRoots.Clear();
            mActiveRoots = null;
        }

        if (mDicItemList != null)
        {
            mDicItemList.Clear();
            mDicItemList = null;
        }
        if (mPlacedPreviewList != null)
        {
            mPlacedPreviewList.ForEach(p => p.EndPreview());
            mPlacedPreviewList.Clear();
            mPlacedPreviewList = null;
        }
        if (mDicScrollPos != null)
        {
            mDicScrollPos.Clear();
            mDicScrollPos = null;
        }
        if (mDicSelected != null)
        {
            mDicSelected.Clear();
            mDicSelected = null;
        }

        if (AddressableAssetLoader.IsValid)
        {
            AddressableAssetLoader.Instance.CleanUp();
            DestroyImmediate(AddressableAssetLoader.Instance.gameObject);
        }

        EditorApplication.pauseStateChanged -= OnPauseStateChanged;
        EditorApplication.modifierKeysChanged -= OnKeysChanged;
    }

    private void Initialize()
    {
        if (mTitleStyle == null)
            mTitleStyle = new GUIStyle(EditorStyles.helpBox) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
        if (mTabLeftStyle == null)
            mTabLeftStyle = new GUIStyle("ButtonLeft");
        if (mTabMiddleStyle == null)
            mTabMiddleStyle = new GUIStyle("ButtonMid");
        if (mTabRightStyle == null)
            mTabRightStyle = new GUIStyle("ButtonRight");
        if (mGridButtonStyle == null)
        {
            mGridButtonStyle = new GUIStyle("ButtonMid");
            mGridButtonStyle.alignment = TextAnchor.MiddleLeft;
        }
        if (mTimeLabelStyle == null)
            mTimeLabelStyle = new GUIStyle(EditorStyles.textArea);

        if (mPlayButtonContent == null)
            mPlayButtonContent = EditorGUIUtility.IconContent("PlayButton@2x");
        if (mRecordButtonContent == null)
            mRecordButtonContent = EditorGUIUtility.IconContent("Record On@2x");
        if (mPauseButtonContent == null)
            mPauseButtonContent = EditorGUIUtility.IconContent("PauseButton@2x");
        if (mAddButtonContent == null)
            mAddButtonContent = EditorGUIUtility.IconContent("Toolbar Plus@2x");
        if (mRemoveButtonContent == null)
            mRemoveButtonContent = EditorGUIUtility.IconContent("Toolbar Minus@2x");

        mMenuType = eMenuType.PREVIEW_LIST;
        mPrevMenuType = eMenuType.PREVIEW_LIST;
        ChangeMenu(eMenuType.PREVIEW_LIST);
    }

    /// <summary>
    /// 프리뷰 셋팅 실행
    /// </summary>
    private void RunCardPreviewSetting()
    {
        OpenPreviewSettingScene();

        mGameViewSizeIndex = GameViewSize.SelectedSizeIndex;
        GameViewSizeGroupType type = GameViewSizeGroupType.Android;
        switch (EditorUserBuildSettings.activeBuildTarget)
        {
            case BuildTarget.iOS:
                type = GameViewSizeGroupType.iOS;
                break;
            case BuildTarget.StandaloneWindows:
                type = GameViewSizeGroupType.Standalone;
                break;
        }
        GameViewSize.SetSize(GameViewSize.FindSize(type, string.Format("{0}x{1} Portrait", Constants.STANDARD_RESOLUTION_HEIGHT, Constants.STANDARD_RESOLUTION_WIDTH)));

        if (!EditorApplication.isPlaying)
            EditorApplication.isPlaying = true;
    }

    /// <summary>
    /// 프리뷰 셋팅 시작 코루틴
    /// </summary>
    private IEnumerator CoroutineStartCardPreviewSetting()
    {
        mRenderTextureRT = Utility.FindChild(UIManager.Instance.transform, "Preview_RenderTexture") as RectTransform;
        if (mRenderTextureRT != null)
        {
            mRenderTextureSize = mRenderTextureRT.sizeDelta;
            CardPreviewSettingToolGUI.ChangeViewCallback = ChangeRenderTextureSize;
            ChangeRenderTextureSize();
        }

        PlayerDataManager.Instance.Initialize();
        AtlasManager.Instance.Initialize();

        GameSetting.Instance = Resources.Load<GameSetting>(nameof(GameSetting));

        ResourcePath.Init();

        CardPreviewController.LoadBattleMap();

        if (AnimationCurveList.Instance == null)
        {
            AnimationCurveList.Instance = Resources.Load<AnimationCurveList>("AnimationCurveList");
        }

        MetaDataManager.Instance.Initialize();
        yield return CoroutineManager.Instance.StartCoroutineOnManager(MetaDataManager.Instance.Corouine_LoadBaseMetaData());

        MetaDataManager.Instance.LoadAllMetaData(false);

        while (!MetaDataManager.Instance.IsLoadComplete)
        {
            //EditorUtility.DisplayProgressBar("Loading", "MetaTable Loading", MetaDataManager.Instance.LoadingProgress);
            yield return null;
        }

        SettingPreviewData();
        Focus();

        yield return new WaitUntil(() => BattleMapLoader.Instance.Success);
        yield return CoroutineManager.Instance.StartCoroutineOnManager(LoadBattleResources());

        if (GameSetting.IsActive)
        {
            mUseRT = GameSetting.Instance.useRT;
            GameSetting.Instance.useRT = false;
        }

        List<GameObject> mapObjList = new List<GameObject>();
        GetAllChilds(BattleMapLoader.Instance.MapGameObject.transform, mapObjList);
        mPickIgnoreObjs = mapObjList.ToArray();

        mPreviewObjectRoot = new GameObject("PreviewObjectRoot").transform;

        PlayerDataManager.Instance.SettingUserInfo(CreateBattleUserInfo(true));
        PlayerDataManager.Instance.SettingEnemyInfo(CreateBattleUserInfo(false));
        BattleManager.Instance.StartBattle(eBattleType.CARD_PREVIEW);

        mUpdater = BattleManager.Instance.BattleUpdater as CardPreviewUpdater;
        mUpdater.OnUserActionInputCallback += OnUserActionInputCallback;
        mUpdater.OnRestartCallback += OnRestartCallback;

        CardPreviewBattleInputHandler.Instance.Initialize(BattleManager.Instance.BattleCam.Cam);
        CardPreviewBattleInputHandler.Instance.onMouseInField = OnMouseUpInField;
        CardPreviewBattleInputHandler.Instance.onMouseUp = OnMouseUpCallback;
        CardPreviewBattleInputHandler.Instance.onDragCallback = OnDragInputCallback;
        CardPreviewBattleInputHandler.Instance.onDragCancelCallback = OnMouseDragCancelCallback;

        RenderTexture rt = AssetDatabase.LoadAssetAtPath<RenderTexture>("Assets/Editor/Tools/CardPreview/ToolRenderTexture.renderTexture");
        BattleManager.Instance.BattleCam.SetRenderTexture(rt);
        if (UIManager.Instance.previewBattleUICamera != null)
        {
            UIManager.Instance.previewBattleUICamera.targetTexture = rt;
            if (rt != null)
            {
                BattleUI ui = UIBaseList.Get<BattleUI>();
                while (ui.RootCanvas == null)
                    yield return null;
                ui.SetPreviewBattleUICam();
            }
        }
    }

    /// <summary>
    /// 전투관련 리소스 로드
    /// </summary>
    private IEnumerator LoadBattleResources()
    {
        // 로딩할 에셋 키 리스트
        List<string> assetList = new List<string>();
        // 특수스킬 메타 ID list
        List<int> specificMetaIDList = new List<int>();

        // 유닛 추가
        foreach (var unitMD in MetaDataManager.Instance.UnitLoader.ListUnitMetaData)
        {
            if (!string.IsNullOrEmpty(unitMD.AssetKey))
                assetList.Add(unitMD.AssetKey);

            for (int i = 0; i < unitMD.SpecificSkillCount; i++)
            {
                int metaID = unitMD.GetSpecificMetaID(i);
                if (!specificMetaIDList.Contains(metaID))
                    specificMetaIDList.Add(metaID);
            }
        }

        // 액션 이펙트 추가
        foreach (var actionMD in MetaDataManager.Instance.ActionLoader.ListActionMetaData)
        {
            if (!string.IsNullOrEmpty(actionMD.EffectName) && !assetList.Contains(actionMD.EffectName))
                assetList.Add(actionMD.EffectName);
            if (!string.IsNullOrEmpty(actionMD.PreviewAssetKey) && !assetList.Contains(actionMD.PreviewAssetKey))
                assetList.Add(actionMD.PreviewAssetKey);
        }

        // 특수스킬에서 사용하는 에셋 추가
        BattleScene.AddAssetKeysFromSpecificSkill(specificMetaIDList.ToArray(), assetList);

        // 액션 타겟 추가
        assetList.Add(ResourcePath.Get(ResourcePath.ACTION_TARGET_KEY));

        AddressableAssetLoader.Instance.LoadAssets(assetList);
        yield return new WaitUntil(() => AddressableAssetLoader.Instance.IsDone);
    }

    /// <summary>
    /// 프리뷰 셋팅 씬 오픈
    /// </summary>
    private void OpenPreviewSettingScene()
    {
        if (EditorSceneManager.GetActiveScene().name != "CardPreviewSettingScene")
        {
            mPrevScenePath = EditorSceneManager.GetActiveScene().path;
            mPreviewScene = EditorSceneManager.OpenScene("Assets/projectFile/scenes/CardPreviewSettingScene.unity", OpenSceneMode.Single);
        }
    }

    /// <summary>
    /// 레코딩 정보 설정
    /// </summary>
    private void InitRecording(ItemInfo info)
    {
        if (mRecorder == null)
            mRecorder = new CardPreviewRecorder();
        mActionInfoList = mRecorder.ActionInfoList;
        mViewInfoList = mRecorder.ViewInfoList;
        Recorder.InitRecording(info.metaID, CustomRandom.Instance.SeedValue, GameSetting.Instance?.ResourceVersion ?? 0);
        SetPlayerDataFromRecorder();
        mUpdater?.SetRecorder(Recorder);
    }

    /// <summary>
    /// 프리뷰 아이템 GUIContent 추가
    /// </summary>
    private void AddPreviewItemGUIContent(eItemType type, KeyValuePair<List<ItemInfo>, GUIContent[]> contentlist, int metaID, ref int index)
    {
        string name = MetaDataManager.Instance.GetNameText(metaID);
        contentlist.Key.Add(new ItemInfo(type, metaID, name));
        contentlist.Value[index++] = new GUIContent(name, LoadImage(metaID));
    }

    /// <summary>
    /// 메뉴 변경
    /// </summary>
    private void ChangeMenu(eMenuType menu)
    {
        if (mMenuType == menu)
            return;

        switch (menu)
        {
            case eMenuType.PREVIEW_LIST:
                mPrevMenuType = mMenuType;
                DeselectItem(menu);
                ResetPreview();
                break;
            case eMenuType.FRIENDLY_ARRANGEMENT_ITEM_LIST:
                if (mMenuType != eMenuType.PREVIEW_LIST)
                    mPrevMenuType = mMenuType;
                //BattleField.Instance.HideIndicator();
                break;
            case eMenuType.ENEMY_ARRANGEMENT_ITEM_LIST:
                if (mMenuType != eMenuType.PREVIEW_LIST)
                    mPrevMenuType = mMenuType;
                //BattleField.Instance.HideIndicator();
                break;
        }

        mMenuType = menu;
        ResetSelectCameraInfoControl();
        ReleaseFocusControl();

        //InactiveActionPreview();
        //DeselectItem();
    }

    private void ResetList(eMenuType menuType, eItemType itemType)
    {
        //if (dicSelected != null && dicSelected.ContainsKey(menuType) && dicSelected[menuType].ContainsKey(itemType))
        //    dicSelected[menuType][itemType] = -1;
        var key = new KeyValuePair<eMenuType, eItemType>(menuType, itemType);
        if (mDicScrollPos != null && mDicScrollPos.ContainsKey(key))
            mDicScrollPos[key] = Vector2.zero;
    }

    /// <summary>
    /// 툴 리셋
    /// </summary>
    private void ResetPreview(bool isDestroy = false)
    {
        mPlayState = ePlayState.STOP;
        ResetSelectCameraInfoControl();

        InactiveActionPreview();

        if (isDestroy == false)
        {
            mUpdater?.SetUsePoolBarracks(false);
            BattleManager.Instance.RollbackBattle(true);
        }

        if (mPlacedPreviewList != null)
        {
            mPlacedPreviewList.ForEach(p => p.EndPreview());
            mPlacedPreviewList.Clear();
        }

        if (mPreviewObjectRoot != null)
        {
            for (int i = 0; i < mPreviewObjectRoot.childCount; ++i)
                Destroy(mPreviewObjectRoot.GetChild(i).gameObject);
        }

        PrefabPool.ClearPool();
    }

    private T LoadPrefabContents<T>(string assetPath) where T : Component
    {
        GameObject prefab = PrefabUtility.LoadPrefabContents(AddressableAssetLoader.EDITOR_ASSET_PATH + assetPath);
        if (prefab != null)
            return Instantiate(prefab).GetComponent<T>();
        return default;
    }

    private Texture2D LoadImage(int metaID)
    {
        return EditorGUIUtility.FindTexture(AddressableAssetLoader.EDITOR_ASSET_PATH + MetaDataManager.Instance.GetImageKey(metaID));
    }

    private object GetMetaData(ItemInfo info)
    {
        switch (info.type)
        {
            case eItemType.ACTION:
                return MetaDataManager.Instance.GetActionMetaData(info.metaID);
            case eItemType.UNIT:
                return MetaDataManager.Instance.GetUnitMetaData(info.metaID);
            case eItemType.BIG_UNIT:
                return MetaDataManager.Instance.GetBigUnitGroupMetaData(info.metaID);
        }

        return null;
    }

    private void OnGUI()
    {
        GUILayout.Space(1);

        switch (mMenuType)
        {
            case eMenuType.PREVIEW_LIST:
                OnGUIPreviewList();
                break;
            case eMenuType.FRIENDLY_ARRANGEMENT_ITEM_LIST:
            case eMenuType.ENEMY_ARRANGEMENT_ITEM_LIST:
                OnGUIArrangementItemListTopMenu();
                OnGUIArrangementItemList();
                break;
            case eMenuType.ACTION_INFO_RECORD_LIST:
                OnGUIArrangementItemListTopMenu();
                DrawActionInfoList();
                break;
            case eMenuType.VIEW_INFO_RECORD_LIST:
                OnGUIArrangementItemListTopMenu();
                DrawViewInfoList();

                // 선택된 컨트롤 해제
                if (mIsSelectControl == false && (GUI.changed || GUIUtility.hotControl > 0 || Event.current.type == EventType.MouseDown))
                    ResetSelectCameraInfoControl();
                mIsSelectControl = false;
                break;
        }

        mRepaintTime = Time.realtimeSinceStartup;

        if (Event.current.type == EventType.Layout && mPosition != position)
        {
            mPosition = position;
            SaveDockedWindows();
        }
    }

    /// <summary>
    /// 프리뷰 목록 GUI
    /// </summary>
    private void OnGUIPreviewList()
    {
        GUILayout.Box("Preview List", mTitleStyle);
        GUILayout.Space(1);
        DrawPreviewList(mMenuType);
    }

    /// <summary>
    /// 프리뷰 배치 아이템 목록 GUI
    /// </summary>
    private void OnGUIArrangementItemListTopMenu()
    {
        GUILayout.Space(2);
        GUILayout.BeginHorizontal();
        {
            int height = TAB_HEIGHT / 2;
            GUILayoutOption guiHegit = GUILayout.Height(height);
            if (GUILayout.Button("◀ Preview List", mTabLeftStyle, guiHegit))
            {
                ChangeMenu(eMenuType.PREVIEW_LIST);
                return;
            }

            if (IsPlaying() || IsRecording())
            {
                GUILayout.Space(5);
                GUILayout.BeginVertical();
                GUILayout.BeginHorizontal();
                {
                    // 타이머
                    mTimeLabelStyle.alignment = TextAnchor.MiddleLeft;
                    GUILayout.Label(string.Format(" {0}", TimeToString(mStopWatch.Elapsed)), mTimeLabelStyle, GUILayout.Width(64), guiHegit);
                    GUILayout.Space(2);

                    // turn
                    string turn = CurrentTurn.ToString();
                    mTimeLabelStyle.alignment = TextAnchor.MiddleCenter;
                    GUILayout.Label(turn, mTimeLabelStyle, GUILayout.Width(Mathf.Max(30, mTimeLabelStyle.CalcSize(new GUIContent(turn)).x + 2)), guiHegit);
                    GUILayout.FlexibleSpace();
                }
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }

            GUILayout.FlexibleSpace();

            if (Recorder.IsLoaded)
            {
                // 파일명
                GUILayout.BeginVertical();
                string fileName = "Preview File: " + Recorder.LoadedFileName;
                GUIStyle style = new GUIStyle(EditorStyles.helpBox) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
                GUILayout.Label(fileName, style, GUILayout.Width(style.CalcSize(new GUIContent(fileName)).x + 6), guiHegit);
                GUILayout.EndVertical();
            }

            // 플레이 버튼
            DrawPlayButton(height, height);
            // 레코드 버튼
            DrawRecordButton(height, height);
            // 일시 정지 버튼
            DrawPauseButton(height, height);

            //if (Recorder.IsLoaded)
            //    GUI.enabled = false;
            if (GUILayout.Button("Save Preview", guiHegit))
            {
                bool paused = false;
                if (!IsPause())
                {
                    paused = true;
                    ChangeState(ePlayState.PAUSE);
                }
                SavePreviewFile(IsRecording());
                if (paused)
                    ChangeState(ePlayState.PAUSE);
            }
            GUI.enabled = true;
            if (GUILayout.Button("Load Preview", guiHegit))
                LoadPreviewFile();
        }
        GUILayout.EndHorizontal();

        //SetEnableArrangementGUI();
        GUILayout.Space(GUI.enabled ? 1 : 3);

        GUILayout.BeginHorizontal();
        {
            int playerLevel = 0, enemyLevel = 0, playerBigUnitLevel = 0, enemyBigUnitLevel = 0, playerCardLevel = 0, enemyCardLevel = 0;

            // 플레이어 레벨 설정
            GUILayout.BeginVertical();
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("P LV", GUILayout.Width(40));
                playerLevel = EditorGUILayout.IntSlider(Recorder.GetLevel(true), 1, mMaxLevel);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("E LV", GUILayout.Width(40));
                enemyLevel = EditorGUILayout.IntSlider(Recorder.GetLevel(false), 1, mMaxLevel);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();

            GUILayout.Space(5);

            // 플레이어 레벨 설정
            GUILayout.BeginVertical();
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("P Big LV", GUILayout.Width(60));
                playerBigUnitLevel = EditorGUILayout.IntSlider(Recorder.GetBigUnitLevel(true), 1, mMaxBigUnitLevel);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("E Big LV", GUILayout.Width(60));
                enemyBigUnitLevel = EditorGUILayout.IntSlider(Recorder.GetBigUnitLevel(false), 1, mMaxBigUnitLevel);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();

            GUILayout.Space(5);

            // 적 레벨 설정
            GUILayout.BeginVertical();
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("P Card LV", GUILayout.Width(70));
                playerCardLevel = EditorGUILayout.IntSlider(Recorder.GetCardLevel(true), 1, mCardMaxLevel);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("E Card LV", GUILayout.Width(70));
                enemyCardLevel = EditorGUILayout.IntSlider(Recorder.GetCardLevel(false), 1, mCardMaxLevel);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();

            Recorder.SetLevel(playerLevel, enemyLevel);
            Recorder.SetBigUnitLevel(playerBigUnitLevel, enemyBigUnitLevel);
            Recorder.SetCardLevel(playerCardLevel, enemyCardLevel);
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(1);

        GUILayout.BeginHorizontal();
        {
            GUILayout.Space(5);
            if (GUILayout.Toggle(mMenuType == eMenuType.FRIENDLY_ARRANGEMENT_ITEM_LIST, "Player 배치 대상 목록", mTabLeftStyle, GUILayout.Height(TAB_HEIGHT)))
                ChangeMenu(eMenuType.FRIENDLY_ARRANGEMENT_ITEM_LIST);
            if (GUILayout.Toggle(mMenuType == eMenuType.ENEMY_ARRANGEMENT_ITEM_LIST, "Enemy 배치 대상 목록", mTabMiddleStyle, GUILayout.Height(TAB_HEIGHT)))
                ChangeMenu(eMenuType.ENEMY_ARRANGEMENT_ITEM_LIST);
            if (GUILayout.Toggle(mMenuType == eMenuType.ACTION_INFO_RECORD_LIST, "액션 정보 목록", Recorder.IsLoaded ? mTabMiddleStyle : mTabRightStyle, GUILayout.Height(TAB_HEIGHT)))
                ChangeMenu(eMenuType.ACTION_INFO_RECORD_LIST);
            if (Recorder.IsLoaded && GUILayout.Toggle(mMenuType == eMenuType.VIEW_INFO_RECORD_LIST, "뷰 정보 목록", mTabRightStyle, GUILayout.Height(TAB_HEIGHT)))
                ChangeMenu(eMenuType.VIEW_INFO_RECORD_LIST);
            GUI.enabled = true;
        }
        GUILayout.EndHorizontal();
    }

    /// <summary>
    /// 프리뷰 배치 아이템 목록 GUI
    /// </summary>
    private void OnGUIArrangementItemList()
    {
        switch (mMenuType)
        {
            case eMenuType.FRIENDLY_ARRANGEMENT_ITEM_LIST:
            case eMenuType.ENEMY_ARRANGEMENT_ITEM_LIST:
                SetEnableArrangementGUI();
                DrawArrangementTargetList(mMenuType);
                break;
        }
    }

    /// <summary>
    /// 프리뷰 아이템 리스트 GUI
    /// </summary>
    private void DrawPreviewList(eMenuType menuTypeType)
    {
        if (mDicItemList != null)
        {
            // 액션 리스트
            EditorTools.BeginContents();
            if (EditorTools.DrawHeader("Actions"))
            {
                DrawGridList(menuTypeType, eItemType.ACTION);
            }
            EditorTools.EndContents();

            GUILayout.Space(1);

            // 빅유닛 리스트
            EditorTools.BeginContents();
            if (EditorTools.DrawHeader("Invaders"))
            {
                DrawGridList(menuTypeType, eItemType.BIG_UNIT, 500);
            }
            EditorTools.EndContents();
        }
    }

    /// <summary>
    /// 배치 가능한 아이템 리스트 GUI
    /// </summary>
    private void DrawArrangementTargetList(eMenuType menuType)
    {
        if (mDicItemList != null)
        {
            // 액션 리스트
            GUI.enabled = true;
            EditorTools.BeginContents();
            if (EditorTools.DrawHeader("Actions"))
            {
                DrawGridList(menuType, eItemType.ACTION, enableGUI: IsRecording() || IsPlaying());
            }
            EditorTools.EndContents();

            GUILayout.Space(1);

            // 유닛 리스트
            GUI.enabled = true;
            EditorTools.BeginContents();
            if (EditorTools.DrawHeader("Units"))
            {
                DrawGridList(menuType, eItemType.UNIT, 50, TextAnchor.MiddleCenter);
            }
            EditorTools.EndContents();

            GUILayout.Space(1);

            // 빅유닛 리스트
            GUI.enabled = true;
            EditorTools.BeginContents();
            if (EditorTools.DrawHeader("Invaders"))
            {
                DrawGridList(menuType, eItemType.BIG_UNIT, 500);
            }
            EditorTools.EndContents();
        }
    }

    /// <summary>
    /// 그리드 리스트 GUI
    /// </summary>
    private void DrawGridList(eMenuType menuType, eItemType itemType, float height = 1000f, TextAnchor textAnchor = TextAnchor.MiddleLeft, bool enableGUI = true)
    {
        if (mDicItemList != null && mDicItemList.TryGetValue(itemType, out var list))
        {
            GUI.changed = false;
            bool enabled = GUI.enabled;
            bool isHost = menuType == eMenuType.FRIENDLY_ARRANGEMENT_ITEM_LIST;
            int select = mDicSelected[menuType][itemType];
            var key = new KeyValuePair<eMenuType, eItemType>(menuType, itemType);

            mDicScrollPos[key] = GUILayout.BeginScrollView(mDicScrollPos[key]);
            {
                GUI.enabled = enableGUI;
                mGridButtonStyle.alignment = textAnchor;
                select = GUILayout.SelectionGrid(select, list.Value, 3, mGridButtonStyle, GUILayout.Height(height), GUILayout.Width(position.width - 30));
                GUI.enabled = enabled;
                if (mDicSelected[menuType][itemType] != select)
                {
                    DeselectItem(mPrevMenuType);
                    InactiveActionPreview();

                    ItemInfo itemInfo = list.Key[select];
                    if (menuType == eMenuType.FRIENDLY_ARRANGEMENT_ITEM_LIST || menuType == eMenuType.ENEMY_ARRANGEMENT_ITEM_LIST)
                    {
                        mSelectedItem = itemInfo;
                        ActiveActionPreview(isHost);
                    }
                    else
                    {
                        ChangeMenu(eMenuType.FRIENDLY_ARRANGEMENT_ITEM_LIST);
                        InitRecording(itemInfo);
                    }

                    mDicSelected[menuType][itemType] = select;
                    foreach (var selected in mDicSelected[menuType])
                    {
                        if (selected.Key != itemType && selected.Value >= 0)
                        {
                            mDicSelected[menuType][selected.Key] = -1;
                            break;
                        }
                    }
                }
                else
                {
                    if ((menuType == eMenuType.FRIENDLY_ARRANGEMENT_ITEM_LIST || menuType == eMenuType.ENEMY_ARRANGEMENT_ITEM_LIST) && GUI.changed)
                    {
                        DeselectItem(menuType, itemType);
                        InactiveActionPreview();
                    }
                }
            }
            GUILayout.EndScrollView();
        }
    }

    /// <summary>
    /// 액션 정보 리스트 GUI
    /// </summary>
    private void DrawActionInfoList()
    {
        if (mActionInfoList != null)
        {
            EditorTools.BeginContents();
            {
                var key = new KeyValuePair<eMenuType, eItemType>(eMenuType.ACTION_INFO_RECORD_LIST, eItemType.NONE);
                mDicScrollPos[key] = GUILayout.BeginScrollView(mDicScrollPos[key]);
                {
                    for (int i = 0; i < mActionInfoList.Count; ++i)
                    {
                        DrawActionInfo(mActionInfoList[i], i);
                        if (i < mActionInfoList.Count - 1)
                            GUILayout.Space(2);
                    }
                }
                GUILayout.EndScrollView();
            }
            EditorTools.EndContents();
        }
    }

    /// <summary>
    /// 액션 정보 GUI
    /// </summary>
    private void DrawActionInfo(ActionInfo actionInfo, int index)
    {
        Color bc = GUI.backgroundColor;
        if (index % 2 == 1)
            GUI.backgroundColor = Color.gray;
        GUILayout.BeginHorizontal(EditorStyles.helpBox);
        {
            GUI.backgroundColor = bc;
            GUILayout.BeginVertical();
            {
                GUILayout.BeginHorizontal();
                {
                    uint turn = actionInfo.turn;
                    GUILayout.Label("Turn", GUILayout.ExpandWidth(false));
                    actionInfo.SetTurn((uint)EditorGUILayout.DelayedIntField((int)actionInfo.turn, GUILayout.Width(50)));
                    if (turn != actionInfo.turn)
                        Recorder.SortActionInfoList();

                    bool isEnd = index == mActionInfoList.Count - 1;

                    GUILayout.Space(10);
                    string text = MetaDataManager.Instance.GetNameText(actionInfo.ActionID);
                    if (actionInfo.isHost == false)
                        text += " (Enemy)";
                    GUILayout.Label(isEnd ? "End" : text, GUILayout.ExpandWidth(false));
                    GUILayout.FlexibleSpace();

                    if (actionInfo.ActionID > 0 && Utility.ConvertMetaIDType(actionInfo.ActionID) != eMetaID_Type.Action && 
                        GUILayout.Button("Select", GUILayout.Width(100)))
                    {
                        if (actionInfo.Unit != null && actionInfo.Unit.UnitData?.CheckActiveUnit() == true)
                            Selection.activeGameObject = actionInfo.Unit.gameObject;
                    }

                    if (isEnd == false)
                    {
                        GUILayout.Space(5);
                        if (GUILayout.Button(mRemoveButtonContent, GUILayout.Width(40), GUILayout.Height(20)))
                        {
                            if (actionInfo.Unit != null)
                            {
                                CardPreviewObject previewObj = actionInfo.Unit.GetComponent<CardPreviewObject>();
                                if (previewObj != null)
                                {
                                    if (previewObj.mCardPreview == mCardPreview)
                                        InactiveActionPreview();
                                    else
                                        previewObj.mCardPreview.EndPreview();
                                }
                            }
                            RemoveActionInfo(actionInfo);
                            ReleaseFocusControl();
                        }
                    }
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
        }
        GUILayout.EndHorizontal();
    }

    /// <summary>
    /// 뷰 정보 리스트 GUI
    /// </summary>
    private void DrawViewInfoList()
    {
        if (mViewInfoList != null)
        {
            EditorTools.BeginContents();
            {
                var key = new KeyValuePair<eMenuType, eItemType>(eMenuType.VIEW_INFO_RECORD_LIST, eItemType.NONE);
                mDicScrollPos[key] = GUILayout.BeginScrollView(mDicScrollPos[key]);
                {
                    for (int i = 0; i < mViewInfoList.Count; ++i)
                    {
                        DrawViewInfo(mViewInfoList[i], i);
                        if (i < mViewInfoList.Count - 1)
                            GUILayout.Space(2);
                    }
                }
                GUILayout.EndScrollView();

                GUILayout.Space(1);
                if (GUILayout.Button(mAddButtonContent))
                {
                    AddViewInfo();
                    ReleaseFocusControl();
                }
            }
            EditorTools.EndContents();
        }
    }

    /// <summary>
    /// 뷰 정보 GUI
    /// </summary>
    private void DrawViewInfo(ViewInfo camInfo, int index)
    {
        Color bc = GUI.backgroundColor;
        if (index % 2 == 1)
            GUI.backgroundColor = Color.gray;
        GUILayout.BeginHorizontal(EditorStyles.helpBox);
        {
            GUI.backgroundColor = bc;
            GUILayout.BeginVertical();
            {
                GUILayout.BeginHorizontal();
                {
                    uint turn = camInfo.turn;
                    GUILayout.Label("Turn", GUILayout.ExpandWidth(false));
                    camInfo.turn = (uint)EditorGUILayout.DelayedIntField((int)camInfo.turn, GUILayout.Width(50));
                    if (turn != camInfo.turn)
                        Recorder.SortViewInfoList();

                    GUILayout.Space(5);
                    DrawUIInfo(camInfo.uiInfo);
                    GUILayout.FlexibleSpace();

                    GUILayout.Label("SplitView", GUILayout.ExpandWidth(false));
                    camInfo.isActiveSplitView = EditorGUILayout.Toggle(camInfo.isActiveSplitView, GUILayout.Width(20));
                    GUILayout.Space(3);

                    if (GUILayout.Button(mRemoveButtonContent, GUILayout.Width(40), GUILayout.Height(20)))
                    {
                        RemoveViewInfo(camInfo);
                        ReleaseFocusControl();
                    }
                }
                GUILayout.EndHorizontal();

                DrawCameraAction(camInfo.mainViewAction, index, false);
                if (camInfo.isActiveSplitView)
                    DrawCameraAction(camInfo.splitViewAction, index, true);
            }
            GUILayout.EndVertical();
        }
        GUILayout.EndHorizontal();
    }

    private void Update()
    {
        mUpdater?.UpdateForTool();
        mCardPreview?.UpdatePreview();

        UpdateInput();

        if (mCurrentCommand != null)
        {
            if (!mCurrentCommand.IsEnd())
                mCurrentCommand.UpdateCommand(Time.realtimeSinceStartup - mTime);

            if (!IsPause())
            {
                if (mCurrentCommand.IsEnd())
                {
                    if (mPickedPreviewObj == null)
                        ArrangementCardOnField();
                }
            }
        }

        if (mCardPreview != null && !mGameViewType.IsInstanceOfType(focusedWindow) && mGameViewType.IsInstanceOfType(mouseOverWindow))
            FocusGameView();

        if (Time.realtimeSinceStartup - mRepaintTime >= Time.deltaTime && (IsPlaying() || IsRecording()))
            Repaint();

        mTime = Time.realtimeSinceStartup;
    }

    /// <summary>
    /// 입력처리 업데이트
    /// </summary>
    private void UpdateInput()
    {
        if (BattleManager.IsValid == false)
            return;

        if (mCardPreview != null)
        {
            Ray ray = BattleManager.Instance.BattleCam.Cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, float.MaxValue, (int)eLayerMask.FIELD))
                mCardPreview.UpdatePosition(hit.point);
        }
        else if (Input.GetMouseButtonUp(0) && mCurrentCommand == null)
        {
            Ray ray = BattleManager.Instance.BattleCam.Cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, float.MaxValue, (int)eLayerMask.UNIT) && hit.transform.gameObject != null)
            //GameObject obj = HandleUtility.PickGameObject(Input.mousePosition, true, pickIgnoreObjs);
            //if (obj != null)
            {
                // 맵에 배치돼 있는 카드 선택
                CardPreviewObject previewObj = hit.transform.gameObject.GetComponentInParent<CardPreviewObject>();
                if (previewObj == null)
                    previewObj = hit.transform.gameObject.GetComponentInChildren<CardPreviewObject>();
                if (previewObj != null)
                {
                    ActionMetaData amd = previewObj.mCardPreview.MetaData as ActionMetaData;
                    eDetailCommandType commandType = amd != null ? GetCommandType(amd) : eDetailCommandType.NORMAL;
                    mPickGameObject = true;
                    mPickedPreviewObj = previewObj;
                    if (mPickedPreviewObj != null)
                        Selection.activeObject = mPickedPreviewObj.gameObject;
                    if (amd == null || (amd.CommandType != eCommandType.DRAG && amd.CommandType != eCommandType.MANY))
                    {
                        mCardPreview = previewObj.mCardPreview;
                        mCardPreview.EnablePreview(true);
                        if (mCardPreview.IsAction)
                            mCardPreview.ArrangementOnField(false);
                        ShowBattleFieldIndicator(mCardPreview.MetaData, mCardPreview.IsHost);
                        //if (amd != null && amd.PreviewType != eActionPreviewType.NONE)
                        //    ActiveCommand();
                    }
                }
            }
            else if (mSelectedCamAction != null && Physics.Raycast(ray, out hit, float.MaxValue, (int)eLayerMask.FIELD))
            {
                // 맵 그리드 위치 선택
                Vector2 gridPos = BattleField.Instance.ConvertWorldPositionToGridCoord(hit.point);
                if (BattleField.Instance.IsPositionInGrid(gridPos))
                {
                    mSelectedCamAction.moveGridX = (int)gridPos.x;
                    mSelectedCamAction.moveGridY = (int)gridPos.y;
                    Repaint();
                    ReleaseFocusControl();
                }
            }
        }
    }

    /// <summary>
    /// 아이템 선택 해제
    /// </summary>
    private void DeselectItem(eMenuType menuType, eItemType itemType = eItemType.NONE)
    {
        mSelectedItem = null;
        if (mDicSelected.ContainsKey(menuType))
        {
            if (itemType != eItemType.NONE)
                mDicSelected[menuType][itemType] = -1;
            else
            {
                var selected = mDicSelected[menuType];
                var types = Enum.GetValues(typeof(eItemType));
                foreach (var type in types)
                {
                    eItemType t = (eItemType)type;
                    if (selected.ContainsKey(t))
                        selected[t] = -1;
                }
            }
        }
    }

    /// <summary>
    /// 액션 미리보기 활성
    /// </summary>
    private void ActiveActionPreview(bool isHost)
    {
        if (mSelectedItem == null || mCardPreview != null)
            return;

        object metaData = GetMetaData(mSelectedItem);
        if (metaData is ActionMetaData amd)
        {
            if (amd.PreviewType != eActionPreviewType.NONE)
            {
                Character unitData = null;
                switch (amd.ActionType)
                {
                    case ActionMetaData.eActionType.UNIT:
                        UnitMetaData umd = MetaDataManager.Instance.GetUnitMetaData(amd.SubMetaID);
                        if (umd != null)
                        {
                            CharData cd = new CharData()
                            {
                                TeamID = isHost ? BattleManager.HOST_TEAM_ID : BattleManager.GUEST_TEAM_ID,
                                MetaID = umd.MetaID,
                                Level = Recorder.GetLevel(isHost),
                            };
                            cd.InitUnit(umd);

                            Vector3 pos = Vector3.zero;
                            pos.y = umd.UnitPos == eUnitPos.ONWALL ? BattleField.Instance.wallHeight : umd.Height + BattleField.Instance.groundHeight;
                            unitData = new Character();
                            unitData.IsSelfPlayer = isHost;
                            unitData.IsHostPlayer = isHost;
                            unitData.Init(cd, pos, Quaternion.identity);
                            unitData.OwnerNexus = isHost ? BattleManager.Instance.HostNexus : BattleManager.Instance.GuestNexus;
                            unitData.OtherNexus = isHost ? BattleManager.Instance.GuestNexus : BattleManager.Instance.HostNexus;
                            unitData.ClearSpecificSkill();
                            unitData.Controller.SetActive(true);
                            unitData.Controller.ApplyNowalkNode = false;
                        }
                        break;
                }

                mCardPreview = new CardPreviewInfo(isHost, IsPossibleEditing(), IsRecording(), 
                    amd.ActionType, amd.PreviewType, amd, unitData != null ? new Character[] { unitData } : null);
                mCardPreview.StartPreview(mPreviewObjectRoot);
                mPlacedPreviewList.Add(mCardPreview);
                ShowBattleFieldIndicator(metaData, isHost);
                ActiveCommand(isHost);
            }
        }
        else
        {
            mCardPreview = CreateUnitCardPreivew(isHost, mSelectedItem.type, metaData);
            ShowBattleFieldIndicator(metaData, isHost);
            FocusGameView();
        }
    }

    /// <summary>
    /// 유닛 CardPreivew 생성
    /// </summary>
    private CardPreviewInfo CreateUnitCardPreivew(bool isHost, eItemType type, object metaData, ActionInfo actionInfo = null, bool addPlacedPreviewList = true)
    {
        List<UnitMetaData> umdList = new List<UnitMetaData>();
        switch (type)
        {
            case eItemType.UNIT:
                umdList.Add(metaData as UnitMetaData);
                break;
            case eItemType.BIG_UNIT:
                BigUnitGroupMetaData bgm = metaData as BigUnitGroupMetaData;
                for (int i = 0; i < bgm.GetMetaArrayCount(); ++i)
                    umdList.Add(MetaDataManager.Instance.GetUnitMetaData(bgm.GetUnitMetaID(i)));
                break;
        }

        Character[] arrUnitData = new Character[umdList.Count];
        for (int i = 0; i < umdList.Count; ++i)
        {
            CharData cd = new CharData()
            {
                TeamID = isHost ? BattleManager.HOST_TEAM_ID : BattleManager.GUEST_TEAM_ID,
                MetaID = umdList[i].MetaID,
                Level = Recorder.GetLevel(isHost),
            };
            cd.InitUnit(umdList[i]);

            Vector3 pos = Vector3.zero;
            pos.y = umdList[i].UnitPos == eUnitPos.ONWALL ? BattleField.Instance.wallHeight : umdList[i].Height + BattleField.Instance.groundHeight;
            Character unitData = new Character();
            unitData.IsSelfPlayer = isHost;
            unitData.IsHostPlayer = isHost;
            unitData.Init(cd, pos, Quaternion.identity);
            unitData.OwnerNexus = isHost ? BattleManager.Instance.HostNexus : BattleManager.Instance.GuestNexus;
            unitData.OtherNexus = isHost ? BattleManager.Instance.GuestNexus : BattleManager.Instance.HostNexus;
            unitData.ClearSpecificSkill();
            unitData.ChangeState(Character.State.IDLE);
            unitData.Controller.SetActive(true);
            unitData.Controller.ApplyNowalkNode = false;
            arrUnitData[i] = unitData;
        }

        CardPreviewInfo cp = new CardPreviewInfo(isHost, true, IsRecording(),
            ActionMetaData.eActionType.UNIT, eActionPreviewType.PREVIEW, metaData, arrUnitData.Length > 0 ? arrUnitData : null);
        cp.ActionInfo = actionInfo != null ? actionInfo : CreateActionInfo(new ActionEvent()
        {
            event_string = new EventContent()
            {
                id = cp.MetaID,
                t = (short)eCommandType.NORMAL,
            }
        });
        cp.StartPreview(mPreviewObjectRoot);
        if (addPlacedPreviewList)
            mPlacedPreviewList.Add(cp);
        return cp;
    }

    /// <summary>
    /// 하위 오브젝트 리스트 리턴 (재귀호출)
    /// </summary>
    private void GetAllChilds(Transform root, List<GameObject> list)
    {
        for (int i = 0; i < root.childCount; ++i)
        {
            Transform child = root.GetChild(i);
            list.Add(child.gameObject);
            GetAllChilds(child, list);
        }
    }

    /// <summary>
    /// 액션 사용 가능한지 체크
    /// </summary>
    private bool CheckUseAction(int metaID, Vector3 pos, bool isHost)
    {
        bool gridSpawn = false;
        ActionMetaData.eSpawnType spawnType = ActionMetaData.eSpawnType.None;
        ActionMetaData amd = MetaDataManager.Instance.GetActionMetaData(metaID);

        if (amd != null)
        {
            // 스폰 위치에 맞는지
            if (amd.SpawnLength > 0)
            {
                Vector2 coord = BattleField.Instance.ConvertWorldPositionToGridCoord(pos);

                int height = isHost ? (int)coord.y : BattleField.Instance.GridHeight - (int)coord.y;
                if ((isHost && height >= amd.SpawnLength) || ((!isHost && height > amd.SpawnLength)))
                {
                    LogConsole.Nor("over spawn length : " + amd.SpawnLength);
                    return false;
                }
            }

            gridSpawn = amd.GridSpawn;
            spawnType = amd.SpawnType;
        }
        else
        {
            eUnitPos unitPos = eUnitPos.ALL;
            UnitMetaData umd = MetaDataManager.Instance.GetUnitMetaData(metaID);
            if (umd != null)
                unitPos = umd.UnitPos;
            else
            {
                BigUnitGroupMetaData bmd = MetaDataManager.Instance.GetBigUnitGroupMetaData(metaID);
                if (bmd != null)
                {
                    umd = MetaDataManager.Instance.GetUnitMetaData(bmd.GetUnitMetaID(0));
                    if (umd != null)
                        unitPos = umd.UnitPos;
                }
            }

            spawnType = GetSpawnType(unitPos);
        }


        // 스폰 위치 맞는지 체크
        // 그리드에만 가능
        if (gridSpawn)
        {
            pos = BattleField.Instance.GetGridCenterPosition(pos);
            if (BattleField.Instance.CheckUnitInGrid(pos, spawnType != ActionMetaData.eSpawnType.Wall ? BattleField.eLayer.Layer_0 : BattleField.eLayer.Layer_1))
            {
                //LogConsole.Nor("exist unit : " + vPos);
                return false;
            }
        }

        // 필드 스폰 = 벽에 불가
        if (spawnType == ActionMetaData.eSpawnType.Field)
        {
            Vector2 coord = BattleField.Instance.ConvertWorldPositionToGridCoord(pos);
            if (BattleField.Instance.IsWalkableCoord(coord))
            {
                return false;
            }
        }
        else if (spawnType == ActionMetaData.eSpawnType.Wall)
        {
            Vector2 coord = BattleField.Instance.ConvertWorldPositionToGridCoord(pos);
            if (!BattleField.Instance.IsWalkableCoord(coord))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 카드 필드에 배치
    /// </summary>
    private void ArrangementCardOnField()
    {
        if (mCardPreview.IsAction)
            mCardPreview.ArrangementOnField(true, IsPossibleEditing());
        
        if (!mCardPreview.IsAction && !IsPossibleEditing() && (IsRecording() || IsPlaying()))
        {
            // 액션이 아닌경우 유닛 생성
            Character[] arrCharacter = BattleManager.Instance.CreateUnitsOnField(mCardPreview.MetaID,
                Recorder.GetLevel(mCardPreview.IsHost), Int2.zero.ToVec3(Constants.GROUND_HEIGHT), mCardPreview.IsHost, true);
            if (arrCharacter != null)
            {
                for (int i = 0; i < arrCharacter.Length; ++i)
                {
                    arrCharacter[i].SetPosition(BattleField.Instance.GetGridCenterPosition(mCardPreview.ArrUnitData[i].PosVec3));
                    arrCharacter[i].SetTargetNexus();
                    arrCharacter[i].SetCurrentPosRotation();
                    arrCharacter[i].UpdateRender();
                }

                mCardPreview.ActionInfo.SetUnit(arrCharacter[0].BattleUnit);
            }
        }

        // 프리뷰 비활성
        ActionCommandBase command = mCurrentCommand;
        if (InactiveActionPreview((!mCardPreview.IsAction && (IsRecording() || IsPlaying())) || 
            (!IsPause() && !IsPossibleEditing() && command != null && command.IsEnd())))
        {
            if (mSelectedItem != null)
                DeselectItem(mMenuType, mSelectedItem.type);
            mSelectedItem = null;

            if (command != null)
                command.EndCommandFromPreview();

            Repaint();
        }
    }

    /// <summary>
    /// 프리뷰 파일로 저장
    /// </summary>
    private void SavePreviewFile(bool recordEnd)
    {
        AssetDatabase.Refresh();

        string fileName = Recorder.GetCurrentRecordFileName();
        string path = Recorder.GetCurrentRecordFolderPath();
        if (AssetDatabase.IsValidFolder(path) == false)
            AssetDatabase.CreateFolder(CardPreviewRecorder.FILE_PATH, fileName);

        string savePath = EditorUtility.SaveFilePanelInProject("Save Card Preview File",
            fileName, CardPreviewRecorder.FILE_EXTENTION, "", path);
        if (savePath.Contains(path))
        {
            if (recordEnd)
                Recorder.AddEndActionInfo(CurrentTurn);
            Recorder.SaveRecordData(savePath);
        }
    }

    /// <summary>
    /// 프리뷰 파일 로드
    /// </summary>
    private void LoadPreviewFile(Action callback = null)
    {
        string path = EditorUtility.OpenFilePanel("Load Card Preview File",
            Recorder.GetCurrentRecordFolderPath(), CardPreviewRecorder.FILE_EXTENTION);
        AssetDatabase.Refresh();
        Recorder.LoadRecordData(path, true, (data) =>
        {
            PreparePlayPreview();
            callback?.Invoke();
        });
    }

    /// <summary>
    /// 프리뷰 플레이 준비
    /// </summary>
    private void PreparePlayPreview(bool createStartingUnit = true)
    {
        DeselectItem(mMenuType);
        StopPreview(createStartingUnit);
        SetPlayerDataFromRecorder();
        mUpdater?.SetRecorder(Recorder);
    }

    /// <summary>
    /// 프리뷰 레코딩 시작
    /// </summary>
    private void StartRecordingPreview()
    {
        PreparePlayPreview(false);
        mUpdater?.StartRecordingPreview();
        mUpdater?.CreateStartingUnit();
        if (EditorApplication.isPaused == false)
            mStopWatch.Restart();
        else
            mStopWatch.Reset();
    }

    /// <summary>
    /// 프리뷰 플레이
    /// </summary>
    private bool PlayPreview()
    {
        ResetPreview();
        bool isPlay = mUpdater?.PlayPreview() ?? false;
        mUpdater?.CreateStartingUnit();
        if (EditorApplication.isPaused == false)
            mStopWatch.Restart();
        else
            mStopWatch.Reset();
        return isPlay;
    }

    /// <summary>
    /// 프리뷰 일시정지
    /// </summary>
    private void PausePreview()
    {
        mUpdater?.Pause();
        mStopWatch.Stop();
        if (mCurrentCommand != null)
            mCurrentCommand.IsPause = true;
    }

    /// <summary>
    /// 프리뷰 일시정지 해제
    /// </summary>
    private void ResumePreview()
    {
        mUpdater?.Resume();
        if (EditorApplication.isPaused == false)
            mStopWatch.Start();
        if (mCurrentCommand != null)
            mCurrentCommand.IsPause = false;

        if (mPlacedPreviewList != null)
        {
            mPlacedPreviewList.ForEach(p =>
            {
                if (p.IsEnd)
                    p.EndPreview();
            });
        }
    }

    /// <summary>
    /// 프리뷰 플레이 종료
    /// </summary>
    private void StopPreview(bool createStartingUnit = true)
    {
        ResetPreview();

        // 시작 유닛 배치
        if (createStartingUnit)
        {
            ActionInfo[] arrActionInfo = Recorder.GetStartingUnitList();
            if (arrActionInfo != null && arrActionInfo.Length > 0)
            {
                for (int i = 0; i < arrActionInfo.Length; ++i)
                {
                    var ai = arrActionInfo[i];
                    object metaData = null;
                    eItemType type = eItemType.UNIT;
                    if (Utility.ConvertMetaIDType(ai.ActionID) == eMetaID_Type.BigUnit)
                    {
                        type = eItemType.BIG_UNIT;
                        metaData = MetaDataManager.Instance.GetBigUnitGroupMetaData(ai.ActionID);
                    }
                    else
                        metaData = MetaDataManager.Instance.GetUnitMetaData(ai.ActionID);
                    CardPreviewInfo cp = CreateUnitCardPreivew(ai.isHost, type, metaData, ai);
                    cp.UpdatePosition(ai.ActionPos.ToVec3());
                    cp.ChangeUnitState(Character.State.IDLE);
                    if (cp.IsAction)
                        cp.ArrangementOnField(true);

                    ActionEvent ae = new ActionEvent()
                    {
                        event_string = new EventContent()
                        {
                            id = ai.ActionID,
                            t = (short)eCommandType.NORMAL,
                        }
                    };
                    ae.event_string = SetEventContentPosition(ae.event_string, ai.ActionPos);
                    //cp.ActionInfo = Recorder.RecordActionInfo(cp.IsHost, CurrentTurn, CreateActionInfo(ae));
                }
            }
        }
    }

    /// <summary>
    /// 플레이 상태 변경
    /// </summary>
    private void ChangeState(ePlayState state)
    {
        mPlayState = mPlayState.Unset(ePlayState.STOP);

        switch (state)
        {
            case ePlayState.RECORDING:
                StartRecordingPreview();
                mPlayState = state;
                break;
            case ePlayState.PLAYING:
                if (PlayPreview())
                    mPlayState = state;
                else
                    mPlayState = mPlayState.Set(ePlayState.STOP);
                break;
            case ePlayState.PAUSE:
                if (mPlayState.HasFlag(state))
                {
                    if (EditorApplication.isPaused == false)
                    {
                        ResumePreview();
                        mPlayState = mPlayState.Unset(state);
                    }
                }
                else
                {
                    PausePreview();
                    mPlayState = mPlayState.Set(state);
                }
                break;
            case ePlayState.STOP:
                if (IsRecording())
                {
                    if (Recorder.IsLoaded == false)
                        Recorder.ClearRecordList(true);
                    DeselectItem(mMenuType);
                }
                else
                    Recorder.Reset(true);
                StopPreview();
                mPlayState = state;
                break;
        }
    }

    /// <summary>
    /// 모든 유닛과 발사체 리스트 리턴
    /// </summary>
    private KeyValuePair<List<int>, List<string>> GetAllUnitAndProjectileList()
    {
        KeyValuePair<List<int>, List<string>> list = new KeyValuePair<List<int>, List<string>>(new List<int>(), new List<string>());
        var allUnits = BattleManager.Instance.DicAllUnit;
        if (allUnits != null)
        {
            foreach (var unit in allUnits)
            {
                if (unit.Value.CheckActiveUnit())
                {
                    list.Key.Add(unit.Key);
                    list.Value.Add(string.Format("{0} ({1})", unit.Value.Name, unit.Key));
                }
            }
        }

        var allProjectiles = mUpdater?.ProjectileList;
        if (allProjectiles != null)
        {
            foreach (var projectile in allProjectiles)
            {
                if (projectile.Object != null && !projectile.IsArrived)
                {
                    list.Key.Add(projectile.IID);
                    list.Value.Add(string.Format("{0} ({1})", projectile.Object.name, projectile.IID));
                }
            }
        }

        return list;
    }

    /// <summary>
    /// 도킹된 EditorWindow 정보 저장
    /// </summary>
    private void SaveDockedWindows()
    {
        EditorPrefs.DeleteKey(SAVE_PARENT_WINDOW_TYPE);
        if (docked)
        {
            object dockArea = typeof(EditorWindow).GetField("m_Parent", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(this);
            if (dockArea != null)
            {
                List<string> dockedTypeList = new List<string>();
                List<EditorWindow> windows = dockArea.GetType().GetField("m_Panes", 
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dockArea) as List<EditorWindow>;
                for (int i = 0; i < windows.Count; ++i)
                {
                    if (windows[i] != this)
                        dockedTypeList.Add(windows[i].GetType().FullName);
                }

                EditorPrefs.SetString(SAVE_PARENT_WINDOW_TYPE, String.Join(",", dockedTypeList.ToArray()));
            }
        }
    }

    #region [ callbacks ]
    /// <summary>
    /// 액션 입력 실패
    /// </summary>
    public void FailedActionInput()
    {
        mCurrentCommand?.OnDragField(Vector3.zero, false, BattleManager.Instance.BattleCam);
    }

    /// <summary>
    /// 필드 클릭 완료
    /// </summary>
    public void OnMouseUpInField(Vector3 position)
    {
        if (mCardPreview != null && !mPickGameObject && BattleField.Instance.IsPositionInGrid(position))
        {
            DeckActionData deckData = null;
            bool isAction = Utility.ConvertMetaIDType(mCardPreview.MetaID) == eMetaID_Type.Action;
            bool useAction = CheckUseAction(mCardPreview.MetaID, position, mCardPreview.IsHost);
            if (useAction && isAction)
            {
                if (mCurrentCommand != null)
                    deckData = mCurrentCommand.DeckData;
                else
                {
                    deckData = new DeckActionData(new ActionCard(mCardPreview.MetaID, Recorder.GetCardLevel(mCardPreview.IsHost)));
                    deckData.ActionCard.Init();
                    deckData.CommandData = new CommandInputData()
                    {
                        position = position.ToInt2()
                    };
                }
            }

            mCurrentCommand?.OnTouchField(position, BattleManager.Instance.BattleCam);

            if (useAction)
            {
                if (mCurrentCommand == null)
                {
                    uint turn = CurrentTurn;
                    if (isAction)
                    {
                        mCardPreview.ActionInfo = Recorder.RecordActionInfo(mCardPreview.IsHost, turn,
                            mCardPreview.ActionInfo != null ? mCardPreview.ActionInfo : CreateActionInfo(deckData));
                    }
                    else
                    {
                        ActionInfo ai = mCardPreview.ActionInfo != null ? mCardPreview.ActionInfo :
                            CreateActionInfo(new ActionEvent()
                            {
                                event_string = new EventContent()
                                {
                                    id = mCardPreview.MetaID,
                                    t = (short)eCommandType.NORMAL,
                                }
                            });

                        //Vector3 pos = BattleField.Instance.GetGridCenterPosition(position);
                        ai.actionEvent.event_string = SetEventContentPosition(ai.actionEvent.event_string, position.ToInt2());
                        mCardPreview.ActionInfo = Recorder.RecordActionInfo(mCardPreview.IsHost, turn, ai);
                    }
                }
            }
            else if (mCurrentCommand != null && mCurrentCommand.IsEnd())
            {
                ActionCommandBase command = mCurrentCommand;
                InactiveActionPreview();
                if (mSelectedItem != null)
                    DeselectItem(mMenuType, mSelectedItem.type);
                mSelectedItem = null;

                if (command != null)
                    command.EndCommandFromPreview();

                Repaint();
            }
        }
    }

    /// <summary>
    /// 드래그 중 처리
    /// </summary>
    /// <param name="position"></param>
    public void OnDragInputCallback(Vector3 position, bool isOnField)
    {
        mCurrentCommand?.OnDragField(position, isOnField, BattleManager.Instance.BattleCam);
    }

    /// <summary>
    /// 마우스 다운
    /// </summary>
    public void OnMouseDownCallback(Vector3 position)
    {
        InactiveActionPreview();
    }

    /// <summary>
    /// 마우스 업
    /// </summary>
    public void OnMouseUpCallback(Vector3 position)
    {
        // 선택된 카드 맵에 배치
        if (!mPickGameObject && mCardPreview != null)
        {
            if (CheckUseAction(mCardPreview.MetaID, position, mCardPreview.IsHost))
            {
                ArrangementCardOnField();
            }
            else
            {
                if (mCurrentCommand != null)
                    mCurrentCommand.SetEnd = false;
            }
        }

        mPickGameObject = false;
    }

    /// <summary>
    /// 마우스 드래그 캔슬
    /// </summary>
    public void OnMouseDragCancelCallback()
    {
        if (mCurrentCommand is DragActionCommand)
        {
            DeselectItem(mMenuType, mSelectedItem.type);
            InactiveActionPreview();
        }
    }

    /// <summary>
    /// 액션 입력 콜백
    /// </summary>
    public bool OnUserActionInputCallback(int teamID, DeckActionData deckData)
    {
        if (mCardPreview != null)
        {
            mCardPreview.ActionInfo = Recorder.RecordActionInfo(mCardPreview.IsHost, CurrentTurn,
                mCardPreview.ActionInfo != null ? mCardPreview.ActionInfo : CreateActionInfo(deckData));

            if (deckData.ActionCard?.ActionMetaData != null && 
                deckData.ActionCard?.ActionMetaData.CommandType == eCommandType.DRAG &&
                deckData.ActionCard.ActionMetaData.IsUseEdge())
            {
                var arr = deckData.DragIndexArray;
                deckData.DragIndexArray = new int[2];
                deckData.DragIndexArray[0] = arr[0];
                deckData.DragIndexArray[1] = arr[arr.Length - 1];
            }
        }
        return true;
    }

    public void OnRestartCallback()
    {
        if (mStopWatch?.IsRunning == true && EditorApplication.isPaused == false)
            mStopWatch.Restart();
        else
            mStopWatch.Reset();
    }
    #endregion
}