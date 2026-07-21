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

    public PlanetData Ready_planetData = new PlanetData();


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

    public void Awake()
    {
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

    /// <summary>
    /// 设置UI事件
    /// </summary>
    private void SetupUIEvents()
    {
        // 设置按钮点击事件
        uiManager.GetPanel("存档选择面板").SetButtonOnClick("开始游戏按钮", OnClick_StartGame_Button);
        uiManager.GetPanel("开始新游戏").SetButtonOnClick("开始新游戏", OnClick_StartNewGame_Button);
        uiManager.GetPanel("存档选择面板").SetButtonOnClick("加载存档按钮", OnClick_LoadSaveData_Button);
        uiManager.GetPanel("右键菜单").SetButtonOnClick("删除按钮", OnClick_DeletSave_Button);

        // 设置输入框值改变事件
        GetSelectedPlayerNameInput()?.onValueChanged.AddListener(OnUpdate_PlayerNameChanged_Text);
        GetNewPlayerNameInput()?.onValueChanged.AddListener(OnUpdate_PlayerNameChanged_Text);
        GetNewSaveNameInput()?.onValueChanged.AddListener(OnPlayerSaveNameChanged);
        GetPlanetRadiusInput()?.onValueChanged.AddListener(OnPlanetReadiusChanged);
        GetPlanetNoiseInput()?.onValueChanged.AddListener(OnPlanetNoiseScaleChanged);
    }

    // 通过BaseUIManager获取UI控件的便捷方法
    private TMP_InputField GetSelectedPlayerNameInput() => uiManager.GetPanel("存档选择面板").GetInputField("选择或新增玩家名称输入框");
    private TMP_InputField GetNewPlayerNameInput() => uiManager.GetPanel(BasePanelName).GetInputField("新增玩家名称输入框");
    private TMP_InputField GetNewSaveNameInput() => uiManager.GetPanel(BasePanelName).GetInputField("新增存档名称输入框");
    private TMP_InputField GetPlanetRadiusInput() => uiManager.GetPanel(BasePanelName).GetInputField("星球半径输入框");
    private TMP_InputField GetPlanetNoiseInput() => uiManager.GetPanel(BasePanelName).GetInputField("噪声缩放输入框");
    private TextMeshProUGUI GetSelectedSaveNameText() => uiManager.GetPanel("存档选择面板").GetText("选中的存档名称");
    private Button GetStartGameButton() => uiManager.GetPanel(name).GetButton("StartGameButton");
    private Button GetStartNewGameButton() => uiManager.GetPanel(name).GetButton("StartNewGameButton");
    private Button GetLoadSaveDataButton() => uiManager.GetPanel(name).GetButton("LoadSaveDataButton");
    private Button GetDeleteSaveButton() => uiManager.GetPanel(name).GetButton("DeleteSaveButton");

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
    #region 存档读取

    public void OnClick_LoadSaveData_Button()
    {
        if (saveAndLoad != null)
        {
            var selectedSaveText = GetSelectedSaveNameText();
            if (selectedSaveText != null)
            {
                string fullPath = Path.Combine(PathToSaveFolder, selectedSaveText.text + ".bytes");
                saveAndLoad.LoadSaveByDisk(fullPath);
            }
        }
        else
        {
            Debug.LogWarning("SaveAndLoad组件未绑定！");
        }
        // 生成玩家按钮
        GeneratePlayerButtons();
    }
    #endregion

    /// <summary>
    /// 删除存档
    /// </summary>
    public void OnClick_DeletSave_Button()
    {
        if(SaveMenuRightMenuUI.Instance.SelectInfo.Path == "")
        {
            //删除玩家
            saveAndLoad.SaveData.PlayerData_Dict.Remove(SaveMenuRightMenuUI.Instance.SelectInfo.Name);

        } else if(SaveMenuRightMenuUI.Instance.SelectInfo.Path != "")
        {
            // 删除存档
            if (saveAndLoad != null)
            {
                var selectedSaveText = GetSelectedSaveNameText();
                if (selectedSaveText != null)
                {
                    string fullPath = Path.Combine(PathToSaveFolder, selectedSaveText.text + ".bytes");
                    saveAndLoad.DeletSave(fullPath);
                }
            }
        }
        Refresh();
    }

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
        uiManager.GetPanel(name).SetText("选中的存档名称", saveName);
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
        uiManager.GetPanel(name).SetInputFieldText("选择或新增玩家名称输入框", playerName);
    }

    /// <summary>
    /// 点击开始游戏按钮
    /// </summary>
    public void OnClick_StartGame_Button()
    {
        if (saveAndLoad?.SaveData == null || saveAndLoad.SaveData.Seed == 0)
        {
            Debug.LogWarning("请先选择存档或创建新游戏");
            return;
        }
        GameManager.Instance.ContinueGame(GetSelectedPlayerNameInput().text);
    }

    /// <summary>
    /// 点击开始新游戏按钮
    /// </summary>
    private void OnClick_StartNewGame_Button()
    {

    }

    /// <summary>
    /// 玩家名字输入框实时更新事件
    /// </summary>
    private void OnUpdate_PlayerNameChanged_Text(string newName)
    {
        if (saveAndLoad != null)
        {
            saveAndLoad.CurrentContrrolPlayerName = newName;
        }
    }

    /// <summary>
    /// 存档名字输入框实时更新事件
    /// </summary>
    private void OnPlayerSaveNameChanged(string newName)
    {
        if (saveAndLoad != null && saveAndLoad.SaveData != null)
        {
            saveAndLoad.SaveData.saveName = newName;
        }
    }

    /// <summary>
    /// 星球半径输入框实时更新事件
    /// </summary>
    private void OnPlanetReadiusChanged(string newValue)
    {
        // 检测传入的字符串是否为有效的整数
        if (int.TryParse(newValue, out int radius))
        {
            Ready_planetData.Radius = radius;
        }
        else
        {
            // 非法输入，不做处理，必要时可提示用户
            Debug.LogWarning($"输入的半径值无效：{newValue}");
        }
    }

    /// <summary>
    /// 星球噪声缩放输入框实时更新事件
    /// </summary>
    private void OnPlanetNoiseScaleChanged(string newValue)
    {
        // 检测传入的字符串是否为有效的浮点数
        if (float.TryParse(newValue, out float noiseScale))
        {
            Ready_planetData.NoiseScale = noiseScale;
        }
        else
        {
            // 非法输入，不做处理，必要时可提示用户
            Debug.LogWarning($"输入的噪声缩放值无效：{newValue}");
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
        
        // 刷新BaseUIManager中的组件
        uiManager.GetPanel(name).RefreshUIComponents();
    }
    #endregion
}
