using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using Boomlagoon.JSON;

/// <summary>
/// 진화 능력치 적용 테스트 툴
/// </summary>
public class EvolutionAbilityApplyTestTool : EditorWindow
{
    private enum eTab { BIG_UNIT, ACTION_CARD, COUNT }
    private readonly string[] tabNames = new string[] { "BIG UNIT", "ACTION CARD" };

    private Dictionary<KeyValuePair<bool, int>, int[]> dicEvolutionAbilities;
    private Dictionary<bool, bool> dicIsApply;

    private BigUnitGroupMetaLoader bigUnitGroupMetaData;
    private EvolutionAbilityMetaDataLoader bigUnitEvolutionMetaData;
    private ActionMetaDataLoader actionCardMetaData;
    private EvolutionAbilityMetaDataLoader actionCardEvolutionMetaData;
    private NameDescTextMetaDataLoader nameDescTextMetaData;

    private readonly Vector2 size = new Vector2(417, 525);
    private Vector2[] scroll = new Vector2[(int)eTab.COUNT];
    private eTab currentTab = eTab.BIG_UNIT;
    private int[] maxNameWith = new int[(int)eTab.COUNT];
    private int currentTabIndex { get { return (int)currentTab; } }

    private const int ABILITY_COUNT_MAX = 2;
    private const string SAVE_IS_APPLY_KEY_FORMAT = "TOOL_ABILITY_IS_APPLY_{0}";
    private const string SAVE_KEY_FORMAT = "TOOL_ABILITY_DATA_{0}";

    public static void OpenWindow()
    {
        GetWindow<EvolutionAbilityApplyTestTool>("Evolution Ability Apply Test Tool", true);
    }

    private void OnFocus()
    {
        Initialize();
        minSize = size;
    }

    private void Initialize()
    {
        LoadMetaData();

        if (dicIsApply == null)
            dicIsApply = new Dictionary<bool, bool>();
        else
            dicIsApply.Clear();

        dicIsApply.Add(true, GetSavedIsApply(true));
        dicIsApply.Add(false, GetSavedIsApply(false));

        if (dicEvolutionAbilities != null)
        {
            dicEvolutionAbilities.Clear();
            dicEvolutionAbilities = null;
        }

        dicEvolutionAbilities = GetSavedAbility(currentTab);
    }

    private void Refresh()
    {
        Initialize();
        Repaint();
    }

    private void LoadMetaData()
    {
        if (bigUnitGroupMetaData == null)
            bigUnitGroupMetaData = MetaDataLoader.LoadMetaDataOnEditor<BigUnitGroupMetaLoader>(eMetaType.BIGUNIT_GROUP_META);
        if (bigUnitEvolutionMetaData == null)
            bigUnitEvolutionMetaData = MetaDataLoader.LoadMetaDataOnEditor<EvolutionAbilityMetaDataLoader>(eMetaType.EVO_BIGUNIT_META);
        if (actionCardMetaData == null)
            actionCardMetaData = MetaDataLoader.LoadMetaDataOnEditor<ActionMetaDataLoader>(eMetaType.ACTION_META);
        if (actionCardEvolutionMetaData == null)
            actionCardEvolutionMetaData = MetaDataLoader.LoadMetaDataOnEditor<EvolutionAbilityMetaDataLoader>(eMetaType.EVO_ACTION_META);
        if (nameDescTextMetaData == null)
            nameDescTextMetaData = MetaDataLoader.LoadMetaDataOnEditor<NameDescTextMetaDataLoader>(eMetaType.NAME_DESC_TEXT_META);
    }

    private void ApplyAbilities(bool isPlayer, UnitCard[] arrUnitCard, ActionCard[] arrActionCard)
    {
        if (dicIsApply == null)
            Initialize();
        if (dicIsApply != null && dicIsApply.TryGetValue(isPlayer, out bool isApply) && isApply)
        {
            if (arrUnitCard != null)
            {
                var dicAbility = GetSavedAbility(eTab.BIG_UNIT);
                for (int i = 0; i < arrUnitCard.Length; ++i)
                {
                    if (arrUnitCard[i] != null && dicAbility.TryGetValue(new KeyValuePair<bool, int>(isPlayer, arrUnitCard[i].MetaID), out int[] ability))
                        arrUnitCard[i].SetEvolutionMetaData(ability);
                }
            }

            if (arrActionCard != null)
            {
                var dicAbility = GetSavedAbility(eTab.ACTION_CARD);
                for (int i = 0; i < arrActionCard.Length; ++i)
                {
                    if (arrActionCard[i] != null && dicAbility.TryGetValue(new KeyValuePair<bool, int>(isPlayer, arrActionCard[i].MetaID), out int[] ability))
                        arrActionCard[i].SetEvolutionAbility(ability);
                }
            }
        }
    }

    private void ResetAbilities(eTab tab, bool isPlayer)
    {
        int[] metaIDList = GetMetaIDList(tab);
        for (int i = 0; i < metaIDList.Length; ++i)
        {
            int[] abilityIDs = dicEvolutionAbilities[new KeyValuePair<bool, int>(isPlayer, metaIDList[i])];
            for (int j = 0; j < abilityIDs?.Length; ++j)
                abilityIDs[j] = 0;
        }

        SaveAbility(tab);

        GUI.FocusControl(string.Empty);
    }

    private int[] GetMetaIDList(eTab tab)
    {
        List<int> metaIDList = new List<int>();
        switch (tab)
        {
            case eTab.BIG_UNIT:
                var bigUnitList = bigUnitGroupMetaData.ListBigUnitMetaDataRO;
                for (int i = 0; i < bigUnitList.Count; ++i)
                {
                    if (bigUnitList[i].IsUse)
                        metaIDList.Add(bigUnitList[i].MetaID);
                }
                break;
            case eTab.ACTION_CARD:
                var actionList = actionCardMetaData.ListActionMetaData;
                for (int i = 0; i < actionList.Count; ++i)
                {
                    metaIDList.Add(actionList[i].MetaID);
                }
                break;
        }

        return metaIDList.ToArray();
    }

    private bool GetSavedIsApply(bool isPlayer)
    {
        string key = string.Format(SAVE_IS_APPLY_KEY_FORMAT, isPlayer);
        return EditorPrefs.HasKey(key) ? EditorPrefs.GetBool(key) : true;
    }

    private void SaveIsApply(bool isPlayer, bool isApply)
    {
        EditorPrefs.SetBool(string.Format(SAVE_IS_APPLY_KEY_FORMAT, isPlayer), isApply);
    }

    private Dictionary<KeyValuePair<bool, int>, int[]> GetSavedAbility(eTab tab)
    {
        Dictionary<KeyValuePair<bool, int>, int[]> savedAbility = new Dictionary<KeyValuePair<bool, int>, int[]>();

        int[] metaIDList = GetMetaIDList(tab);
        JSONObject mine = null;
        JSONObject enemy = null;
        string strSavedData = EditorPrefs.GetString(string.Format(SAVE_KEY_FORMAT, tab));
        if (string.IsNullOrEmpty(strSavedData) == false)
        {
            JSONObject savedData = JSONObject.Parse(strSavedData);
            mine = savedData.GetObject("true");
            enemy = savedData.GetObject("false");
        }

        for (int i = 0; i < metaIDList.Length; ++i)
        {
            string metaID = metaIDList[i].ToString();
            if (mine != null && mine.ContainsKey(metaID))
            {
                int[] arrAbilityID = mine.GetArray(metaID)?.ToArrayInt();
                savedAbility.Add(new KeyValuePair<bool, int>(true, metaIDList[i]), arrAbilityID);
            }
            else
                savedAbility.Add(new KeyValuePair<bool, int>(true, metaIDList[i]), new int[ABILITY_COUNT_MAX]);

            if (enemy != null && enemy.ContainsKey(metaID))
            {
                int[] arrAbilityID = enemy.GetArray(metaID)?.ToArrayInt();
                savedAbility.Add(new KeyValuePair<bool, int>(false, metaIDList[i]), arrAbilityID);
            }
            else
                savedAbility.Add(new KeyValuePair<bool, int>(false, metaIDList[i]), new int[ABILITY_COUNT_MAX]);
        }

        return savedAbility;
    }

    private void SaveAbility(eTab tab)
    {
        JSONObject saveData = new JSONObject();
        JSONObject mine = new JSONObject();
        JSONObject enemy = new JSONObject();
        saveData.Add("true", mine);
        saveData.Add("false", enemy);

        var e = dicEvolutionAbilities.GetEnumerator();
        while (e.MoveNext())
        {
            JSONArray arrAbility = new JSONArray();
            foreach (var abilities in e.Current.Value)
                arrAbility.Add(abilities);

            if (e.Current.Key.Key == true)
                mine.Add(e.Current.Key.Value.ToString(), arrAbility);
            else
                enemy.Add(e.Current.Key.Value.ToString(), arrAbility);
        }

        EditorPrefs.SetString(string.Format(SAVE_KEY_FORMAT, tab), saveData.ToString());
    }

    private void OnGUI()
    {
        if (dicEvolutionAbilities == null)
            return;

        GUILayout.Space(3f);

        eTab tab = currentTab;
        currentTab = (eTab)GUILayout.Toolbar(currentTabIndex, tabNames, GUILayout.Height(50));
        if (tab != currentTab)
            Initialize();

        switch (currentTab)
        {
            case eTab.BIG_UNIT:
                DrawBigUnitTab();
                break;
            case eTab.ACTION_CARD:
                DrawActionCardTab();
                break;
        }

        if (Event.current.type == EventType.Layout)
            minSize = Vector2.Max(size, GUILayoutUtility.GetLastRect().size);
    }

    private void DrawBigUnitTab()
    {
        scroll[currentTabIndex] = GUILayout.BeginScrollView(scroll[currentTabIndex]);
        GUILayout.BeginVertical();
        {
            EditorTools.BeginContents();
            if (EditorTools.DrawHeader("Player", addCallback: () =>
            {
                bool isApply = EditorGUILayout.Toggle(dicIsApply[true]);
                dicIsApply[true] = isApply;
                SaveIsApply(true, isApply);

                if (GUILayout.Button("Reset"))
                    ResetAbilities(eTab.BIG_UNIT, true);
            }))
            {
                DrawBigUnitList(true);
            }
            EditorTools.EndContents();

            EditorTools.BeginContents();
            if (EditorTools.DrawHeader("Enemy", addCallback: () =>
            {
                bool isApply = EditorGUILayout.Toggle(dicIsApply[false]);
                dicIsApply[false] = isApply;
                SaveIsApply(false, isApply);

                if (GUILayout.Button("Reset"))
                    ResetAbilities(eTab.BIG_UNIT, false);
            }))
            {
                DrawBigUnitList(false);
            }
            EditorTools.EndContents();
        }
        GUILayout.EndVertical();
        GUILayout.EndScrollView();
    }

    private void DrawBigUnitList(bool isPlayer)
    {
        if (bigUnitGroupMetaData == null)
            return;

        int[] bigUnits = null;
        bool isPlaying = Application.isPlaying && BattleManager.Instance != null;
        if (isPlaying && PlayerDataManager.Exist)
        {
            if (isPlayer)
                bigUnits = PlayerDataManager.Instance.GetCurrentDeck().BigUnits;
            else
            {
                var list = PlayerDataManager.Instance.EnemyInfo?.UnitList;
                if (list != null && list.Length > 0)
                {
                    bigUnits = new int[list.Length];
                    for (int i = 0; i < bigUnits.Length; ++i)
                        bigUnits[i] = list[i].MetaID;
                }
            }
        }

        var bigUnitList = bigUnitGroupMetaData.ListBigUnitMetaDataRO;
        for (int i = 0, index = 0; i < bigUnitList.Count; ++i)
        {
            BigUnitGroupMetaData metaData = bigUnitList[i];
            if (/*metaData.Tribe != tribe || */!metaData.IsUse)
                continue;

            GUILayout.BeginHorizontal();
            {
                if (isPlaying)
                    GUI.enabled = false;
                if (bigUnits != null)
                {
                    for (int j = 0; j < bigUnits.Length; ++j)
                    {
                        if (bigUnits[j] == metaData.MetaID)
                            GUI.enabled = true;
                    }
                }

                int[] abilityIDs = dicEvolutionAbilities[new KeyValuePair<bool, int>(isPlayer, metaData.MetaID)];
                string name = nameDescTextMetaData?.GetName(metaData.MetaID);
                if (maxNameWith[currentTabIndex] < name.Length)
                    maxNameWith[currentTabIndex] = name.Length;
                EditorGUILayout.LabelField(string.Format("{0}. {1}", index + 1, string.IsNullOrEmpty(name) ? "BigUnit" : name), 
                    GUILayout.Width(maxNameWith[currentTabIndex] * 15));
                GUI.enabled = false;
                EditorGUILayout.IntField(string.IsNullOrEmpty(name) ? 0 : metaData.MetaID, GUILayout.Width(50));
                GUI.enabled = true;

                GUILayout.Space(5f);

                EditorGUILayout.LabelField("Ability ID", GUILayout.Width(60));
                GUI.enabled = true;

                List<int> abilityList = bigUnitEvolutionMetaData.GetMetaIDs(metaData.MetaID);
                abilityList.Insert(0, 0);
                string[] displayedOptions = new string[abilityList.Count];
                displayedOptions[0] = "NONE";
                for (int j = 1; j < abilityList.Count; ++j)
                    displayedOptions[j] = abilityList[j].ToString();

                var arrAbility = abilityList.ToArray();
                for (int j = 0; j < ABILITY_COUNT_MAX; ++j)
                {
                    int ability = abilityIDs != null && j < abilityIDs.Length ? abilityIDs[j] : 0;
                    int changeAbility = EditorGUILayout.IntPopup(ability, displayedOptions, arrAbility, GUILayout.Width(60));
                    if (ability != changeAbility)
                    {
                        if (abilityIDs == null)
                            abilityIDs = new int[ABILITY_COUNT_MAX];
                        abilityIDs[j] = changeAbility;
                        dicEvolutionAbilities[new KeyValuePair<bool, int>(isPlayer, metaData.MetaID)] = abilityIDs;
                        SaveAbility(currentTab);
                    }
                }
                index++;
            }
            GUILayout.EndHorizontal();
        }
    }

    private void DrawActionCardTab()
    {
        scroll[currentTabIndex] = GUILayout.BeginScrollView(scroll[currentTabIndex]);
        GUILayout.BeginVertical();
        {
            EditorTools.BeginContents();
            if (EditorTools.DrawHeader("Player", addCallback: () =>
            {
                bool isApply = EditorGUILayout.Toggle(dicIsApply[true]);
                dicIsApply[true] = isApply;
                SaveIsApply(true, isApply);

                if (GUILayout.Button("Reset"))
                    ResetAbilities(eTab.ACTION_CARD, true);
            }))
            {
                DrawActionCardList(true);
            }
            EditorTools.EndContents();

            EditorTools.BeginContents();
            if (EditorTools.DrawHeader("Enemy", addCallback: () =>
            {
                bool isApply = EditorGUILayout.Toggle(dicIsApply[false]);
                dicIsApply[false] = isApply;
                SaveIsApply(false, isApply);

                if (GUILayout.Button("Reset"))
                    ResetAbilities(eTab.ACTION_CARD, false);
            }))
            {
                DrawActionCardList(false);
            }
            EditorTools.EndContents();
        }
        GUILayout.EndVertical();
        GUILayout.EndScrollView();
    }

    private void DrawActionCardList(bool isPlayer)
    {
        if (actionCardMetaData == null)
            return;

        int[] actionCards = null;
        bool isPlaying = Application.isPlaying && BattleManager.Instance != null;
        if (isPlaying && PlayerDataManager.Exist)
        {
            if (isPlayer)
                actionCards = PlayerDataManager.Instance.GetCurrentDeck().Actions;
            else
            {
                var list = PlayerDataManager.Instance.EnemyInfo?.CardList;
                if (list != null && list.Length > 0)
                {
                    actionCards = new int[list.Length];
                    for (int i = 0; i < actionCards.Length; ++i)
                        actionCards[i] = list[i] != null ? list[i].MetaID : 0;
                }
            }
        }

        var actionList = actionCardMetaData.ListActionMetaData;
        for (int i = 0, index = 0; i < actionList.Count; ++i)
        {
            ActionMetaData metaData = actionList[i];

            GUILayout.BeginHorizontal();
            {
                if (isPlaying)
                    GUI.enabled = false;
                if (actionCards != null)
                {
                    GUI.enabled = false;
                    for (int j = 0; j < actionCards.Length; ++j)
                    {
                        if (actionCards[j] == metaData.MetaID)
                            GUI.enabled = true;
                    }
                }

                int[] abilityIDs = dicEvolutionAbilities[new KeyValuePair<bool, int>(isPlayer, metaData.MetaID)];
                string name = nameDescTextMetaData?.GetName(metaData.MetaID);
                if (maxNameWith[currentTabIndex] < name.Length)
                    maxNameWith[currentTabIndex] = name.Length;
                EditorGUILayout.LabelField(string.Format("{0}. {1}", index + 1, string.IsNullOrEmpty(name) ? "ActionCard" : name), 
                    GUILayout.Width(maxNameWith[currentTabIndex] * 15));
                GUI.enabled = false;
                EditorGUILayout.IntField(string.IsNullOrEmpty(name) ? 0 : metaData.MetaID, GUILayout.Width(50));
                GUI.enabled = true;

                GUILayout.Space(5f);

                EditorGUILayout.LabelField("Ability ID", GUILayout.Width(60));
                GUI.enabled = true;

                List<int> abilityList = actionCardEvolutionMetaData.GetMetaIDs(metaData.MetaID);
                abilityList.Insert(0, 0);
                string[] displayedOptions = new string[abilityList.Count];
                displayedOptions[0] = "NONE";
                for (int j = 1; j < abilityList.Count; ++j)
                    displayedOptions[j] = abilityList[j].ToString();

                var arrAbility = abilityList.ToArray();
                for (int j = 0; j < ABILITY_COUNT_MAX; ++j)
                {
                    int ability = abilityIDs != null && j < abilityIDs.Length ? abilityIDs[j] : 0;
                    int changeAbility = EditorGUILayout.IntPopup(ability, displayedOptions, arrAbility, GUILayout.Width(60));
                    if (ability != changeAbility)
                    {
                        if (abilityIDs == null)
                            abilityIDs = new int[ABILITY_COUNT_MAX];
                        abilityIDs[j] = changeAbility;
                        dicEvolutionAbilities[new KeyValuePair<bool, int>(isPlayer, metaData.MetaID)] = abilityIDs;
                        SaveAbility(currentTab);
                    }
                }
                index++;
            }
            GUILayout.EndHorizontal();
        }
    }
}
