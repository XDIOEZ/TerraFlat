using System.IO;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;
using Sirenix.OdinInspector;
using UnityEngine.EventSystems;

/// <summary>
/// 维护本地存档与角色选择面板的数据、选择态和手柄焦点。
/// 两类动态条目共享复用池；列表刷新只更新业务值、差异选择态和所属 Content 的延迟布局标记。
/// </summary>
public class SaveDataManager_UI : SingletonMono<SaveDataManager_UI>
{
    #region 字段定义

    private const string SaveItemNamePrefix = "存档条目_";
    private const string PlayerItemNamePrefix = "角色条目_";

    /// <summary>缓存动态条目的组件引用；复用时只替换业务数据和父容器。</summary>
    private sealed class SelectionRow
    {
        public GameObject Root;
        public Button Button;
        public ButtonInfoData Info;
        public GameSaveItemView View;
        public TextMeshProUGUI Label;
        public string Value;
        public bool IsSave;
    }

    [Header("保存与加载")]
    public SaveDataMgr saveAndLoad;
    public static SaveDataManager_UI Ins;

    public UIManager uiManager => UIManager.Instance; // BaseUIManager字段

    [Header("存档信息")]
    public List<string> saves = new List<string>();

    [Header("按钮与父物体")]
    public GameObject Save_Player_SelectButton_Prefab; // 存档/玩家按钮预制体
    public Transform SaveSelectButton_Parent_Content; // 存档按钮父物体
    public Transform Player_SelectButton_Parent_Content; // 玩家按钮父物体
    public string BasePanelName = "存档选择面板";

    private readonly List<SelectionRow> saveRows = new List<SelectionRow>();
    private readonly List<SelectionRow> playerRows = new List<SelectionRow>();
    private readonly Stack<SelectionRow> pooledRows = new Stack<SelectionRow>();
    private readonly Dictionary<GameObject, SelectionRow> rowsByObject =
        new Dictionary<GameObject, SelectionRow>();
    private readonly List<string> playerNameBuffer = new List<string>();
    private SelectionRow selectedSaveRow;
    private SelectionRow selectedPlayerRow;

    /// <summary>当前显示的存档条目数量。</summary>
    public int ActiveSaveEntryCount => saveRows.Count;

    /// <summary>当前显示的玩家条目数量。</summary>
    public int ActivePlayerEntryCount => playerRows.Count;

    /// <summary>动态列表当前保留的条目总数，供 Profiler 检查复用是否稳定。</summary>
    public int RetainedEntryCount => saveRows.Count + playerRows.Count + pooledRows.Count;

    // 移除了原来的所有UI控件字段，通过BaseUIManager获取引用

    // 使用Application.persistentDataPath作为基础路径
    private string PathToSaveFolder 
    { 
        get 
        { 
            return Path.Combine(Application.persistentDataPath, "Saves", "LocalSaveData") + Path.DirectorySeparatorChar;
        }
    }

    #endregion

    protected override void Awake()
    {
        base.Awake();
        Ins = this;
        // 确保存档目录存在
        EnsureSaveDirectoryExists();
    }

    #region 初始化
    private void Start()
    {
        saveAndLoad = SaveDataMgr.Instance;
        if (TryGetSavePanel(out BasePanel panel) && panel.IsOpen())
            RefreshForGamepadOpen();
        else
            Refresh();
    }

    #endregion

    #region 存档加载
    /// <summary>
    /// 确保存档目录存在
    /// </summary>
    private void EnsureSaveDirectoryExists()
    {
        string directory = Path.GetDirectoryName(PathToSaveFolder);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>
    /// 加载存档文件名
    /// </summary>
    public void LoadSaveFileNames()
    {
        saves.Clear();

        if (!Directory.Exists(PathToSaveFolder))
        {
            Debug.LogWarning("保存路径不存在: " + PathToSaveFolder);
            return;
        }

        string[] files = Directory.GetFiles(PathToSaveFolder, "*.bytes");
        System.Array.Sort(files, (left, right) =>
        {
            System.DateTime leftExitTime = SaveDataMgr.GetLastExitTimeUtc(left);
            System.DateTime rightExitTime = SaveDataMgr.GetLastExitTimeUtc(right);
            int timeComparison = rightExitTime.CompareTo(leftExitTime);
            if (timeComparison != 0)
                return timeComparison;

            return System.StringComparer.OrdinalIgnoreCase.Compare(
                Path.GetFileNameWithoutExtension(left),
                Path.GetFileNameWithoutExtension(right));
        });

        foreach (string file in files)
        {
            string fileName = Path.GetFileNameWithoutExtension(file);
            saves.Add(fileName);
        }
    }
    #endregion

    #region 按钮生成
    /// <summary>
    /// 生成存档选择按钮
    /// </summary>
    public void GenerateSaveButtons()
    {
        if (SaveSelectButton_Parent_Content == null)
        {
            Debug.LogWarning("存档按钮容器未绑定！");
            return;
        }

        ClearSelectedRow(ref selectedSaveRow);
        ClearSelectedRow(ref selectedPlayerRow);

        // 刷新存档时不保留旧世界的角色列表，避免焦点和数据同时指向过期条目。
        bool playerStructureChanged = ReleaseRows(playerRows);
        bool saveStructureChanged = SyncRows(
            saveRows,
            SaveSelectButton_Parent_Content,
            saves,
            true,
            out bool createdRow);

        CommitDynamicListChanges(
            saveStructureChanged,
            playerStructureChanged,
            createdRow);
    }

    /// <summary>
    /// 生成玩家选择按钮
    /// </summary>
    public void GeneratePlayerButtons()
    {
        if (Player_SelectButton_Parent_Content == null)
        {
            Debug.LogWarning("角色按钮容器未绑定！");
            return;
        }

        ClearSelectedRow(ref selectedPlayerRow);
        playerNameBuffer.Clear();
        if (saveAndLoad?.SaveData?.PlayerData_Dict != null)
        {
            foreach (string playerName in saveAndLoad.SaveData.PlayerData_Dict.Keys)
                playerNameBuffer.Add(playerName);
        }

        bool playerStructureChanged = SyncRows(
            playerRows,
            Player_SelectButton_Parent_Content,
            playerNameBuffer,
            false,
            out bool createdRow);
        CommitDynamicListChanges(false, playerStructureChanged, createdRow);
    }
    #endregion

    #region UI事件

    #region 存档选择

    /// <summary>
    /// 点击存档按钮
    /// </summary>
    public void OnClick_List_Save_Button(string saveName, GameObject buttonObj)
    {
        if (buttonObj == null)
            return;

        rowsByObject.TryGetValue(buttonObj, out SelectionRow currentRow);
        SetSelectedRow(ref selectedSaveRow, currentRow, buttonObj);

        // 使用BaseUIManager更新文本
        TryGetSavePanel(out BasePanel panel);
        panel?.SetText(GameManager.GameSaveSelectedTextKey, saveName);
        Button deleteButton = panel?.GetButton(GameManager.GameSaveDeleteButtonKey);
        if (deleteButton != null)
            deleteButton.interactable = true;

        // 选择存档后立即复用现有加载流程，并刷新可用角色列表。
        GameManager.Instance?.OnClick_LoadSaveData_Button();
    }

    #endregion

    /// <summary>
    /// 点击玩家按钮
    /// </summary>
    public void OnClick_List_PlayerName_Button(string playerName)
    {
        OnClick_List_PlayerName_Button(playerName, null);
    }

    private void OnClick_List_PlayerName_Button(string playerName, GameObject buttonObj)
    {
        SelectionRow currentRow = FindRow(playerRows, playerName, buttonObj);
        SetSelectedRow(ref selectedPlayerRow, currentRow, buttonObj);

        if (saveAndLoad != null)
        {
            saveAndLoad.CurrentContrrolPlayerName = playerName;
        }
        else
        {
            Debug.LogWarning("SaveAndLoad组件未绑定！");
        }

        // 使用BaseUIManager更新输入框，并将手柄流程推进到进入世界。
        if (TryGetSavePanel(out BasePanel panel))
        {
            panel.SetInputFieldText(GameManager.GameSavePlayerInputKey, playerName);
            FocusStartGameForGamepad();
        }
    }

    #endregion

    #region 公共方法
    [Button("刷新存档按钮")]
    /// <summary>
    /// 刷新存档按钮
    /// </summary>
    public void Refresh()
    {
        LoadSaveFileNames();
        GenerateSaveButtons();
    }

    /// <summary>
    /// 打开存档面板时重置旧选择，并将手柄焦点交给第一条可用存档。
    /// </summary>
    public void RefreshForGamepadOpen()
    {
        Refresh();
        ClearSaveSelection();
    }

    /// <summary>
    /// 清空已删除存档对应的 UI 选择与角色列表，避免继续进入已不存在的世界。
    /// </summary>
    public void ClearSaveSelection()
    {
        ClearSelectedRow(ref selectedSaveRow);
        ClearSelectedRow(ref selectedPlayerRow);
        bool playerStructureChanged = ReleaseRows(playerRows);

        if (!TryGetSavePanel(out BasePanel panel))
        {
            CommitDynamicListChanges(false, playerStructureChanged, false);
            return;
        }

        panel.SetText(GameManager.GameSaveSelectedTextKey, GameManager.GameSaveNoSelectionText);
        panel.SetInputFieldText(GameManager.GameSavePlayerInputKey, string.Empty);

        Button deleteButton = panel.GetButton(GameManager.GameSaveDeleteButtonKey);
        if (deleteButton != null)
            deleteButton.interactable = false;

        CommitDynamicListChanges(false, playerStructureChanged, false, panel);
        FocusFirstSaveOrBackForGamepad();
    }
    #endregion

    #region 手柄焦点

    /// <summary>载入世界后优先聚焦首个角色；没有角色时转到名称输入框。</summary>
    public void FocusFirstPlayerOrNameInputForGamepad()
    {
        if (!TryGetSavePanel(out BasePanel panel))
            return;

        RefreshGamepadNavigation(panel, false);
        Button firstPlayer = FindFirstInteractableButton(playerRows);
        if (FocusGamepadControl(panel, firstPlayer))
            return;

        FocusGamepadControl(panel, panel.GetInputField(GameManager.GameSavePlayerInputKey));
    }

    /// <summary>确认角色后把焦点交给进入世界按钮，完成流程闭环。</summary>
    public void FocusStartGameForGamepad()
    {
        if (!TryGetSavePanel(out BasePanel panel))
            return;

        FocusGamepadControl(panel, panel.GetButton(GameManager.GameSaveStartButtonKey));
    }

    /// <summary>没有存档时回退到返回按钮，避免 EventSystem 留下已销毁对象。</summary>
    private void FocusFirstSaveOrBackForGamepad()
    {
        if (!TryGetSavePanel(out BasePanel panel))
            return;

        RefreshGamepadNavigation(panel, false);
        Button firstSave = FindFirstInteractableButton(saveRows);
        if (FocusGamepadControl(panel, firstSave))
            return;

        if (!FocusGamepadControl(panel, panel.GetButton(GameManager.GameSaveBackButtonKey)))
            panel.SelectDefaultForGamepad();
    }

    /// <summary>刷新导航状态；仅在新增条目时重建一次 BasePanel 层级快照。</summary>
    private void RefreshGamepadNavigation(BasePanel panel = null, bool hierarchyChanged = false)
    {
        if (panel == null && !TryGetSavePanel(out panel))
            return;
        if (panel == null)
            return;

        if (hierarchyChanged)
            panel.RefreshUIComponents();
        else
            panel.RefreshGamepadNavigationState();
    }

    private static Button FindFirstInteractableButton(IReadOnlyList<SelectionRow> rows)
    {
        if (rows == null)
            return null;

        for (int i = 0; i < rows.Count; i++)
        {
            Button button = rows[i]?.Button;
            if (button != null && button.IsInteractable())
                return button;
        }

        return null;
    }

    private static bool FocusGamepadControl(BasePanel panel, Selectable selectable)
    {
        if (panel == null || selectable == null || !panel.IsOpen() ||
            !panel.IsGamepadNavigationPrepared || !selectable.IsInteractable() ||
            FlatWorldUITheme.IsGamepadNavigationExcluded(selectable))
        {
            return false;
        }

        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
            return false;

        eventSystem.SetSelectedGameObject(null);
        eventSystem.SetSelectedGameObject(selectable.gameObject);
        return true;
    }

    private bool TryGetSavePanel(out BasePanel panel)
    {
        panel = null;
        if (!uiManager.TryGetPanel(GameManager.GameSavePanelKey, out panel))
            uiManager.TryGetPanel(BasePanelName, out panel);

        return panel != null;
    }

    #endregion

    #region 动态列表辅助

    /// <summary>按索引复用条目；只有历史容量不足时才实例化新的 Prefab。</summary>
    private bool SyncRows(
        List<SelectionRow> activeRows,
        Transform parent,
        IReadOnlyList<string> values,
        bool isSave,
        out bool createdRow)
    {
        createdRow = false;
        if (activeRows == null || parent == null || values == null)
            return false;

        for (int index = activeRows.Count - 1; index >= 0; index--)
        {
            if (activeRows[index]?.Root != null)
                continue;

            activeRows.RemoveAt(index);
        }

        for (int index = 0; index < values.Count; index++)
        {
            SelectionRow row;
            if (index < activeRows.Count)
            {
                row = activeRows[index];
            }
            else
            {
                row = AcquireRow(parent, ref createdRow);
                if (row == null)
                    continue;
                activeRows.Add(row);
            }

            ConfigureRow(row, values[index], isSave, index);
            if (row.Root.transform.parent != parent)
                row.Root.transform.SetParent(parent, false);
            row.Root.transform.SetSiblingIndex(index);
            if (!row.Root.activeSelf)
                row.Root.SetActive(true);
        }

        for (int index = activeRows.Count - 1; index >= values.Count; index--)
        {
            ReleaseRow(activeRows[index]);
            activeRows.RemoveAt(index);
        }

        return true;
    }

    private SelectionRow AcquireRow(Transform parent, ref bool createdRow)
    {
        SelectionRow row = null;
        while (pooledRows.Count > 0 && row == null)
        {
            SelectionRow candidate = pooledRows.Pop();
            if (candidate?.Root != null)
                row = candidate;
        }

        if (row == null)
        {
            row = CreateRow(parent);
            createdRow |= row != null;
        }

        return row;
    }

    private SelectionRow CreateRow(Transform parent)
    {
        if (Save_Player_SelectButton_Prefab == null)
        {
            Debug.LogError("[SaveDataManager_UI] 存档/角色条目 Prefab 未绑定。", this);
            return null;
        }

        GameObject root = Instantiate(Save_Player_SelectButton_Prefab, parent, false);
        root.SetActive(false);
        SelectionRow row = new SelectionRow
        {
            Root = root,
            Button = root.GetComponent<Button>(),
            Info = root.GetComponent<ButtonInfoData>(),
            View = root.GetComponent<GameSaveItemView>(),
            Label = root.GetComponentInChildren<TextMeshProUGUI>(true)
        };
        if (row.Button == null || row.Label == null)
        {
            Debug.LogError("[SaveDataManager_UI] 动态条目 Prefab 缺少 Button 或文字组件。", root);
            Destroy(root);
            return null;
        }

        row.Button.onClick.AddListener(() => HandleRowClicked(row));
        rowsByObject[root] = row;
        return row;
    }

    private void ConfigureRow(SelectionRow row, string value, bool isSave, int index)
    {
        if (row?.Root == null)
            return;

        row.Value = value ?? string.Empty;
        row.IsSave = isSave;
        row.Root.name = (isSave ? SaveItemNamePrefix : PlayerItemNamePrefix) + (index + 1);
        row.Label.text = row.Value;
        row.Button.interactable = true;
        if (row.Info != null)
        {
            row.Info.Name = row.Value;
            row.Info.Path = isSave
                ? Path.Combine(PathToSaveFolder, row.Value + ".bytes")
                : string.Empty;
        }

        SetRowVisual(row, false);
    }

    private bool ReleaseRows(List<SelectionRow> activeRows)
    {
        if (activeRows == null || activeRows.Count == 0)
            return false;

        for (int index = activeRows.Count - 1; index >= 0; index--)
            ReleaseRow(activeRows[index]);
        activeRows.Clear();
        return true;
    }

    /// <summary>条目入池前释放焦点并停用，避免旧对象继续参与本帧导航。</summary>
    private void ReleaseRow(SelectionRow row)
    {
        if (row?.Root == null)
            return;

        EventSystem eventSystem = EventSystem.current;
        GameObject selectedObject = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
        if (selectedObject != null &&
            (selectedObject == row.Root || selectedObject.transform.IsChildOf(row.Root.transform)))
        {
            eventSystem.SetSelectedGameObject(null);
        }

        SetRowVisual(row, false);
        row.Value = string.Empty;
        row.IsSave = false;
        if (row.Info != null)
        {
            row.Info.Name = string.Empty;
            row.Info.Path = string.Empty;
        }

        row.Root.SetActive(false);
        pooledRows.Push(row);
    }

    private void HandleRowClicked(SelectionRow row)
    {
        if (row?.Root == null || !row.Root.activeInHierarchy)
            return;

        if (row.IsSave)
            OnClick_List_Save_Button(row.Value, row.Root);
        else
            OnClick_List_PlayerName_Button(row.Value, row.Root);
    }

    private static SelectionRow FindRow(
        IReadOnlyList<SelectionRow> rows,
        string value,
        GameObject root)
    {
        if (rows == null)
            return null;

        for (int index = 0; index < rows.Count; index++)
        {
            SelectionRow row = rows[index];
            if (row == null)
                continue;
            if (root != null && row.Root == root)
                return row;
            if (root == null && string.Equals(row.Value, value, System.StringComparison.Ordinal))
                return row;
        }

        return null;
    }

    private static void SetSelectedRow(
        ref SelectionRow selectedRow,
        SelectionRow nextRow,
        GameObject fallbackRoot)
    {
        if (!ReferenceEquals(selectedRow, nextRow))
            SetRowVisual(selectedRow, false);

        selectedRow = nextRow;
        if (nextRow != null)
        {
            SetRowVisual(nextRow, true);
            return;
        }

        GameSaveItemView fallbackView = fallbackRoot?.GetComponent<GameSaveItemView>();
        if (fallbackView != null)
        {
            fallbackView.SetSelected(true);
            return;
        }

        ButtonInfoData fallbackInfo = fallbackRoot?.GetComponent<ButtonInfoData>();
        if (fallbackInfo?.SelectImage != null)
            fallbackInfo.SelectImage.enabled = true;
    }

    private static void ClearSelectedRow(ref SelectionRow selectedRow)
    {
        SetRowVisual(selectedRow, false);
        selectedRow = null;
    }

    private static void SetRowVisual(SelectionRow row, bool selected)
    {
        if (row == null)
            return;

        if (row.View != null)
            row.View.SetSelected(selected);
        else if (row.Info?.SelectImage != null)
            row.Info.SelectImage.enabled = selected;
    }

    /// <summary>提交局部布局和导航变化；新增组件时才扫描整个面板一次。</summary>
    private void CommitDynamicListChanges(
        bool saveLayoutChanged,
        bool playerLayoutChanged,
        bool hierarchyChanged,
        BasePanel panel = null)
    {
        if (saveLayoutChanged && SaveSelectButton_Parent_Content is RectTransform saveContent)
            LayoutRebuilder.MarkLayoutForRebuild(saveContent);
        if (playerLayoutChanged && Player_SelectButton_Parent_Content is RectTransform playerContent)
            LayoutRebuilder.MarkLayoutForRebuild(playerContent);

        if (panel == null)
            TryGetSavePanel(out panel);
        if (panel != null)
            RefreshGamepadNavigation(panel, hierarchyChanged);
    }

    #endregion
}
