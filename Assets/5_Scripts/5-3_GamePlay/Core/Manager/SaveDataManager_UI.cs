using System.IO;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;
using Sirenix.OdinInspector;

public class SaveDataManager_UI : SingletonMono<SaveDataManager_UI>
{
    #region 字段定义

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
        LoadSaveFileNames();
        GenerateSaveButtons();
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
        // 清理现有按钮
        foreach (Transform child in SaveSelectButton_Parent_Content)
            Destroy(child.gameObject);

        foreach (Transform child in Player_SelectButton_Parent_Content)
            Destroy(child.gameObject);

        // 生成存档按钮
        foreach (string saveName in saves)
        {
            GameObject buttonObj = Instantiate(Save_Player_SelectButton_Prefab, SaveSelectButton_Parent_Content);

            ButtonInfoData SaveInfo = buttonObj.GetComponent<ButtonInfoData>();
            SaveInfo.Name = saveName;
            SaveInfo.Path = Path.Combine(PathToSaveFolder, saveName + ".bytes");

            var btn = buttonObj.GetComponent<Button>();
            if (btn != null)
                btn.onClick.AddListener(() => OnClick_List_Save_Button(saveName, buttonObj));
        }
        
        // 生成玩家按钮
        GeneratePlayerButtons();
    }

    /// <summary>
    /// 生成玩家选择按钮
    /// </summary>
    public void GeneratePlayerButtons()
    {
        foreach (Transform child in Player_SelectButton_Parent_Content)
            Destroy(child.gameObject);

        if (saveAndLoad?.SaveData?.PlayerData_Dict != null)
        {
            foreach (string playerName in saveAndLoad.SaveData.PlayerData_Dict.Keys)
            {
                GameObject buttonObj = Instantiate(Save_Player_SelectButton_Prefab, Player_SelectButton_Parent_Content);
                buttonObj.name = playerName;

                ButtonInfoData SaveInfo = buttonObj.GetComponent<ButtonInfoData>();
                SaveInfo.Name = playerName;

                var tmpText = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
                if (tmpText != null)
                    tmpText.text = playerName;

                var btn = buttonObj.GetComponent<Button>();
                if (btn != null)
                btn.onClick.AddListener(() => OnClick_List_PlayerName_Button(playerName, buttonObj));
            }
        }
    }
    #endregion

    #region UI事件

    #region 存档选择

    /// <summary>
    /// 点击存档按钮
    /// </summary>
    public void OnClick_List_Save_Button(string saveName, GameObject buttonObj)
    {
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
        BasePanel panel = uiManager.GetPanel(GameManager.GameSavePanelKey);
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

        // 使用BaseUIManager更新输入框
        uiManager.GetPanel(GameManager.GameSavePanelKey)?.SetInputFieldText(GameManager.GameSavePlayerInputKey, playerName);
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
        
        // 刷新BaseUIManager中的组件
        if (!uiManager.TryGetPanel(GameManager.GameSavePanelKey, out BasePanel panel))
            uiManager.TryGetPanel(BasePanelName, out panel);
        panel?.RefreshUIComponents();
    }

    /// <summary>
    /// 清空已删除存档对应的 UI 选择与角色列表，避免继续进入已不存在的世界。
    /// </summary>
    public void ClearSaveSelection()
    {
        if (Player_SelectButton_Parent_Content != null)
        {
            foreach (Transform child in Player_SelectButton_Parent_Content)
                Destroy(child.gameObject);
        }

        if (!uiManager.TryGetPanel(GameManager.GameSavePanelKey, out BasePanel panel))
            uiManager.TryGetPanel(BasePanelName, out panel);

        if (panel == null)
            return;

        panel.SetText(GameManager.GameSaveSelectedTextKey, GameManager.GameSaveNoSelectionText);
        panel.SetInputFieldText(GameManager.GameSavePlayerInputKey, string.Empty);

        Button deleteButton = panel.GetButton(GameManager.GameSaveDeleteButtonKey);
        if (deleteButton != null)
            deleteButton.interactable = false;
    }
    #endregion
}
