using System.IO;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;
using Sirenix.OdinInspector;

public class SaveManager : MonoBehaviour
{
    #region 字段定义

    [Header("保存与加载")]
    public SaveAndLoad saveAndLoad;
    public static SaveManager Ins;

    [Header("存档信息")]
    public List<string> saves = new List<string>();
    private string pathToSaveFolder = "Assets/Saves/LoaclSave/";

    [Header("按钮与父物体")]
    public GameObject Save_Player_SelectButton_Prefab; // 存档/玩家按钮预制体
    public Transform SaveSelectButton_Parent_Content; // 存档按钮父物体
    public Transform Player_SelectButton_Parent_Content; // 玩家按钮父物体

    [Header("UI 显示")]
    public TextMeshProUGUI SelectedSave_Name_Text; // 当前选中的存档名
    public Button StartGameButton; // 开始游戏按钮
    public Button DeletSave; // 删除存档按钮

    [Header("输入框 - 新游戏")]
    public TMP_InputField NewPlayer_Name_Text; // 新玩家输入框
    public TMP_InputField NewSave_Name_Text; // 新存档输入框
    public Button Start_New_GameButton; // 新游戏按钮

    [Header("玩家名显示")]
    public TMP_InputField SelectedPlayer_Name_Text; // 当前选中玩家显示框

    public string PathToSaveFolder { get => saveAndLoad.PlayerSavePath;}


    #endregion
    public void Awake()
    {
        Ins = this;
    }
    #region 初始化
    private void Start()
    {
        saveAndLoad = SaveAndLoad.Instance;

        LoadSaveFileNames();
        GenerateSaveButtons();

        if (StartGameButton != null)
        {
            StartGameButton.onClick.AddListener(OnClick_StartGame_Button);
            Start_New_GameButton.onClick.AddListener(OnClick_StartNewGame_Button);
        }
        else
        {
            Debug.LogError("StartGameButton 未正确赋值！");
        }

        SelectedPlayer_Name_Text.onValueChanged.AddListener(OnUpdate_PlayerNameChanged_Text);
        NewPlayer_Name_Text.onValueChanged.AddListener(OnUpdate_PlayerNameChanged_Text);
        NewSave_Name_Text.onValueChanged.AddListener(OnPlayerSaveNameChanged);
        DeletSave.onClick.AddListener(OnClick_DeletSave_Button);
    }
    #endregion

    #region 存档加载
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

        string[] files = Directory.GetFiles(PathToSaveFolder, "*.GameSaveData");
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
        foreach (Transform child in SaveSelectButton_Parent_Content)
            Destroy(child.gameObject);

        foreach (string saveName in saves)
        {
            GameObject buttonObj = Instantiate(Save_Player_SelectButton_Prefab, SaveSelectButton_Parent_Content);

            SaveInfo SaveInfo = buttonObj.GetComponent<SaveInfo>();
            SaveInfo.saveName = saveName;
            SaveInfo.savePath = PathToSaveFolder + saveName + ".GameSaveData";
/*            buttonObj.name = saveName;

            var tmpText = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
            if (tmpText != null)
                tmpText.text = saveName;*/

            var btn = buttonObj.GetComponent<Button>();
            if (btn != null)
                btn.onClick.AddListener(() => OnClick_List_Save_Button(saveName));
        }
    }

    /// <summary>
    /// 生成玩家选择按钮
    /// </summary>
    public void GeneratePlayerButtons()
    {
        foreach (Transform child in Player_SelectButton_Parent_Content)
            Destroy(child.gameObject);

        foreach (string playerName in saveAndLoad.SaveData.PlayerData_Dict.Keys)
        {
            GameObject buttonObj = Instantiate(Save_Player_SelectButton_Prefab, Player_SelectButton_Parent_Content);
            buttonObj.name = playerName;

            var tmpText = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
            if (tmpText != null)
                tmpText.text = playerName;

            var btn = buttonObj.GetComponent<Button>();
            if (btn != null)
                btn.onClick.AddListener(() => OnClick_List_PlayerName_Button(playerName));
        }
    }
    #endregion

    #region UI事件

    /// <summary>
    /// 删除存档
    /// </summary>
    public void OnClick_DeletSave_Button()
    {
        saveAndLoad.DeletSave(saveAndLoad.PlayerSavePath, SelectedSave_Name_Text.text);
        Refresh();
    }

    /// <summary>
    /// 点击存档按钮
    /// </summary>
    public void OnClick_List_Save_Button(string saveName)
    {
        if (saveAndLoad != null)
        {
            saveAndLoad.LoadSaveByDisk(saveAndLoad.PlayerSavePath + saveName);
        }
        else
        {
            Debug.LogWarning("SaveAndLoad组件未绑定！");
        }

        SelectedSave_Name_Text.text = saveName;
        GeneratePlayerButtons();
    }

    /// <summary>
    /// 点击玩家按钮
    /// </summary>
    public void OnClick_List_PlayerName_Button(string playerName)
    {
        if (saveAndLoad != null)
        {
            saveAndLoad.CurrentContrrolPlayerName = playerName;
        }
        else
        {
            Debug.LogWarning("SaveAndLoad组件未绑定！");
        }

        SelectedPlayer_Name_Text.text = playerName;
    }

    /// <summary>
    /// 点击开始游戏按钮
    /// </summary>
    public void OnClick_StartGame_Button()
    {
        if (saveAndLoad != null)
        {
            saveAndLoad.ChangeScene(saveAndLoad.SaveData.ActiveSceneName);
        }
        else
        {
            Debug.LogWarning("SaveAndLoad组件未绑定！");
        }
    }

    /// <summary>
    /// 点击开始新游戏按钮
    /// </summary>
    private void OnClick_StartNewGame_Button()
    {
        if (saveAndLoad != null)
        {
            saveAndLoad.ChangeScene(saveAndLoad.SaveData.ActiveSceneName);
        }
        else
        {
            Debug.LogWarning("SaveAndLoad组件未绑定！");
        }
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
    #endregion
}
