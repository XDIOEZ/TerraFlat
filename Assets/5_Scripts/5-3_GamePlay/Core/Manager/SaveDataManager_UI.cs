using System.IO;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;
using Sirenix.OdinInspector;
using UnityEngine.EventSystems;

public class SaveDataManager_UI : SingletonMono<SaveDataManager_UI>
{
    #region 字段定义

    private const string SaveItemNamePrefix = "存档条目_";
    private const string PlayerItemNamePrefix = "角色条目_";

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

        // 刷新存档时不保留旧世界的角色列表，避免焦点和数据同时指向过期条目。
        ClearDynamicButtons(SaveSelectButton_Parent_Content);
        ClearDynamicButtons(Player_SelectButton_Parent_Content);

        // 生成存档按钮
        for (int index = 0; index < saves.Count; index++)
        {
            string saveName = saves[index];
            GameObject buttonObj = Instantiate(Save_Player_SelectButton_Prefab, SaveSelectButton_Parent_Content);
            buttonObj.name = SaveItemNamePrefix + (index + 1);

            ButtonInfoData SaveInfo = buttonObj.GetComponent<ButtonInfoData>();
            if (SaveInfo != null)
            {
                SaveInfo.Name = saveName;
                SaveInfo.Path = Path.Combine(PathToSaveFolder, saveName + ".bytes");
            }

            SetItemLabel(buttonObj, saveName);

            var btn = buttonObj.GetComponent<Button>();
            if (btn != null)
                btn.onClick.AddListener(() => OnClick_List_Save_Button(saveName, buttonObj));
        }

        RefreshGamepadNavigation();
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

        ClearDynamicButtons(Player_SelectButton_Parent_Content);

        if (saveAndLoad?.SaveData?.PlayerData_Dict != null)
        {
            int index = 0;
            foreach (string playerName in saveAndLoad.SaveData.PlayerData_Dict.Keys)
            {
                GameObject buttonObj = Instantiate(Save_Player_SelectButton_Prefab, Player_SelectButton_Parent_Content);
                buttonObj.name = PlayerItemNamePrefix + (++index);

                ButtonInfoData SaveInfo = buttonObj.GetComponent<ButtonInfoData>();
                if (SaveInfo != null)
                    SaveInfo.Name = playerName;

                SetItemLabel(buttonObj, playerName);

                var btn = buttonObj.GetComponent<Button>();
                if (btn != null)
                    btn.onClick.AddListener(() => OnClick_List_PlayerName_Button(playerName, buttonObj));
            }
        }

        RefreshGamepadNavigation();
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

        // 清除旧选择态
        foreach (var saveInfo in SaveSelectButton_Parent_Content.GetComponentsInChildren<ButtonInfoData>())
        {
            GameSaveItemView itemView = saveInfo.GetComponent<GameSaveItemView>();
            if (itemView != null)
                itemView.SetSelected(false);
            else if (saveInfo.SelectImage != null)
                saveInfo.SelectImage.enabled = false;
        }

        // 启用当前条目的选择态
        var currentInfo = buttonObj.GetComponent<ButtonInfoData>();
        GameSaveItemView currentView = buttonObj.GetComponent<GameSaveItemView>();
        if (currentView != null)
            currentView.SetSelected(true);
        else if (currentInfo != null && currentInfo.SelectImage != null)
            currentInfo.SelectImage.enabled = true;

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
        foreach (GameSaveItemView itemView in Player_SelectButton_Parent_Content.GetComponentsInChildren<GameSaveItemView>())
            itemView.SetSelected(false);

        if (buttonObj != null)
            buttonObj.GetComponent<GameSaveItemView>()?.SetSelected(true);

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
        ClearDynamicButtons(Player_SelectButton_Parent_Content);

        if (!TryGetSavePanel(out BasePanel panel))
            return;

        panel.SetText(GameManager.GameSaveSelectedTextKey, GameManager.GameSaveNoSelectionText);
        panel.SetInputFieldText(GameManager.GameSavePlayerInputKey, string.Empty);

        Button deleteButton = panel.GetButton(GameManager.GameSaveDeleteButtonKey);
        if (deleteButton != null)
            deleteButton.interactable = false;

        RefreshGamepadNavigation(panel);
        FocusFirstSaveOrBackForGamepad();
    }
    #endregion

    #region 手柄焦点

    /// <summary>载入世界后优先聚焦首个角色；没有角色时转到名称输入框。</summary>
    public void FocusFirstPlayerOrNameInputForGamepad()
    {
        if (!TryGetSavePanel(out BasePanel panel))
            return;

        RefreshGamepadNavigation(panel);
        Button firstPlayer = FindFirstInteractableButton(Player_SelectButton_Parent_Content);
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

        RefreshGamepadNavigation(panel);
        Button firstSave = FindFirstInteractableButton(SaveSelectButton_Parent_Content);
        if (FocusGamepadControl(panel, firstSave))
            return;

        if (!FocusGamepadControl(panel, panel.GetButton(GameManager.GameSaveBackButtonKey)))
            panel.SelectDefaultForGamepad();
    }

    /// <summary>刷新动态布局、导航关系和滚动跟随器。</summary>
    private void RefreshGamepadNavigation(BasePanel panel = null)
    {
        if (panel == null && !TryGetSavePanel(out panel))
            return;
        if (panel == null || !panel.IsGamepadNavigationPrepared)
            return;

        Canvas.ForceUpdateCanvases();
        ForceRebuildLayout(SaveSelectButton_Parent_Content);
        ForceRebuildLayout(Player_SelectButton_Parent_Content);
        panel.RefreshUIComponents();
    }

    private static void ForceRebuildLayout(Transform content)
    {
        if (content is RectTransform rectTransform)
            LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
    }

    private static Button FindFirstInteractableButton(Transform content)
    {
        if (content == null)
            return null;

        Button[] buttons = content.GetComponentsInChildren<Button>(false);
        foreach (Button button in buttons)
        {
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

    /// <summary>清除动态条目时先释放其焦点，防止 EventSystem 持有已销毁对象。</summary>
    private static void ClearDynamicButtons(Transform content)
    {
        if (content == null)
            return;

        EventSystem eventSystem = EventSystem.current;
        GameObject selectedObject = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
        if (selectedObject != null && selectedObject.transform.IsChildOf(content))
            eventSystem.SetSelectedGameObject(null);

        foreach (Transform child in content)
        {
            // Destroy 会延迟到帧末执行；先禁用可避免旧条目继续参与本帧导航计算。
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
    }

    private static void SetItemLabel(GameObject buttonObj, string value)
    {
        TextMeshProUGUI label = buttonObj != null
            ? buttonObj.GetComponentInChildren<TextMeshProUGUI>(true)
            : null;
        if (label != null)
            label.text = value;
    }

    #endregion
}
