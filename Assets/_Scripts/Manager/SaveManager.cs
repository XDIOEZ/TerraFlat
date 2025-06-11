using System.IO;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;
using NaughtyAttributes;

public class SaveManager : MonoBehaviour
{
    [Header("保存与加载")]
    public SaveAndLoad saveAndLoad;

    [Header("存档信息")]
    public List<string> saves = new List<string>();
    public string PathToSaveFolder = "Assets/Saves/LoaclSave/";

    [Header("按钮与父物体")]
    public GameObject Save_Player_SelectButton_Prefab; // 存档/玩家按钮预制体

    public Transform SaveSelectButton_Parent_Content; // 存档按钮父物体
    public Transform Player_SelectButton_Parent_Content; // 玩家按钮父物体

    [Header("UI 显示")]
    public TextMeshProUGUI SelectedSave_Name_Text; // 当前选中的存档名


    public Button StartGameButton; // 开始游戏按钮
    [Header("按钮")]
    public Button DeletSave; // 新游戏按钮

    [Header("输入框")]
    #region 新游戏相关UI

    public TMP_InputField NewPlayer_Name_Text; // 新玩家输入框
    public TMP_InputField NewSave_Name_Text; // 新存档输入框
    public Button Start_New_GameButton; // 新游戏按钮
    #endregion


    #region 选中的_存档的_玩家的名字

    public TMP_InputField SelectedPlayer_Name_Text; // 当前选中玩家显示框

    #endregion

    

    #region 初始化
    private void Start()
    {
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
    // 加载存档文件名
    public void LoadSaveFileNames()
    {
        saves.Clear();

        if (!Directory.Exists(PathToSaveFolder))
        {
            Debug.LogWarning("保存路径不存在: " + PathToSaveFolder);
            return;
        }

        string[] files = Directory.GetFiles(PathToSaveFolder, "*.QAQ");
        foreach (string file in files)
        {
            string fileName = Path.GetFileNameWithoutExtension(file);
            saves.Add(fileName);
        }
    }
    #endregion

    #region 按钮生成
    // 生成存档选择按钮
    public void GenerateSaveButtons()
    {
        foreach (Transform child in SaveSelectButton_Parent_Content)
        {
            Destroy(child.gameObject);
        }

        foreach (string saveName in saves)
        {
            GameObject buttonObj = Instantiate(Save_Player_SelectButton_Prefab, SaveSelectButton_Parent_Content);
            buttonObj.name = saveName;

            TextMeshProUGUI tmpText = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
            if (tmpText != null)
                tmpText.text = saveName;

            Button btn = buttonObj.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.AddListener(() => OnClick_List_Save_Button(saveName));
            }
        }
    }

    // 生成玩家选择按钮
    public void GeneratePlayerButtons()
    {
        foreach (Transform child in Player_SelectButton_Parent_Content)
        {
            Destroy(child.gameObject);
        }

        foreach (string playerName in saveAndLoad.SaveData.PlayerData_Dict.Keys)
        {
            GameObject buttonObj = Instantiate(Save_Player_SelectButton_Prefab, Player_SelectButton_Parent_Content);
            buttonObj.name = playerName;

            TextMeshProUGUI tmpText = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
            if (tmpText != null)
                tmpText.text = playerName;

            Button btn = buttonObj.GetComponent<Button>();
            if (btn != null)
            {
                string capturedName = playerName;
                btn.onClick.AddListener(() => OnClick_List_PlayerName_Button(capturedName));
            }
        }
    }
    #endregion

    #region UI事件
    public void OnClick_DeletSave_Button()
    {
        saveAndLoad.DeletSave(saveAndLoad.PlayerSavePath, SelectedSave_Name_Text.text);
        LoadSaveFileNames();
        GenerateSaveButtons();
    }

    // 存档按钮点击事件
    public void OnClick_List_Save_Button(string saveName)
    {
        if (saveAndLoad != null)
        {
            saveAndLoad.LoadSaveByDisk(saveAndLoad.PlayerSavePath+ saveName);
        }
        else
        {
            Debug.LogWarning("SaveAndLoad组件未绑定！");
        }
        //同步存档名字
        SelectedSave_Name_Text.text = saveName;
        GeneratePlayerButtons();
    }

    // 玩家按钮点击事件
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

    // 游戏开始按钮点击事件
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

    // 开始新游戏按钮点击事件
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

    // 玩家输入框更新事件
    private void OnUpdate_PlayerNameChanged_Text(string newName)
    {
        if (saveAndLoad != null)
        {
            saveAndLoad.CurrentContrrolPlayerName = newName;
        }
    }

    // 存档输入框更新事件
    private void OnPlayerSaveNameChanged(string newName)
    {
        if (saveAndLoad != null && saveAndLoad.SaveData != null)
        {
            saveAndLoad.SaveData.saveName = newName;
        }
    }
    #endregion

    #region 公共方法
    // 刷新存档列表
    public void Refresh()
    {
        LoadSaveFileNames();
        GenerateSaveButtons();
    }
    #endregion
}
