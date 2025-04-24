using System.IO;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;
using NaughtyAttributes;

public class SaveManager : MonoBehaviour
{
    public SaveAndLoad saveAndLoad;

    public List<string> saves = new List<string>();

    public string PathToSaveFolder = "Assets/Saves/";

    public GameObject Save_Player_SelectButton_Prefab;//玩家 存档 按钮预制体

    public Transform SaveSelectButton_Parent_Content;//存档选择按钮父物体

    public Transform Player_SelectButton_Parent_Content;//玩家选择按钮父物体

    public TextMeshProUGUI SelectedSave_Name_Text;//显示当前选中的存档名称
    #region 按钮

    public Button StartGameButton; // 游戏开始按钮

    public Button Start_New_GameButton; // 开始新游戏按钮
    #endregion

    #region 新玩家和存档输入框
    [Header("新玩家和存档输入框")]
    public TMP_InputField NewPlayer_Name_Text;//新玩家输入框

    public TMP_InputField NewSave_Name_Text;//新存档输入框

    public TMP_InputField SelectedPlayer_Name_Text;
    #endregion
    [ShowNativeProperty]
    public string PlayerName
    {
        get
        {
            return saveAndLoad.playerName;
        }
        set
        {
            saveAndLoad.playerName = value;
        }
    }
[ShowNativeProperty]
    public string SaveName
    {
        get
        {
            return saveAndLoad.SaveName;
        }
        set
        {
            saveAndLoad.SaveName = value;
        }
    }




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
    #region 列表按钮生成

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
            {
                tmpText.text = saveName;
            }

            Button btn = buttonObj.GetComponent<Button>();
            if (btn != null)
            {
                string capturedName = saveName;
                btn.onClick.AddListener(() => OnClickSaveButton(capturedName));
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
            {
                tmpText.text = playerName;
            }

            Button btn = buttonObj.GetComponent<Button>();
            if (btn != null)
            {
                string capturedName = playerName;
                btn.onClick.AddListener(() => OnClickPlayerButton(capturedName));
            }
        }
    }
    #endregion

    #region UI事件
    // 玩家按钮点击事件
    public void OnClickPlayerButton(string playerName)
    {
        if (saveAndLoad != null)
        {
            PlayerName = playerName;
            SelectedPlayer_Name_Text.text = playerName;
        }
        else
        {
            Debug.LogWarning("SaveAndLoad组件未绑定！");
        }

        SelectedPlayer_Name_Text.text = playerName;
    }

    // 存档按钮点击事件
    public void OnClickSaveButton(string saveName)
    {
        if (saveAndLoad != null)
        {
            saveAndLoad.LoadByDisk(saveName);
        }
        else
        {
            Debug.LogWarning("SaveAndLoad组件未绑定！");
        }

        SelectedSave_Name_Text.text = saveName;
        GeneratePlayerButtons();
    }

    // 游戏开始按钮的空方法（点击时触发）
    public void OnStartGameClicked()
    {
        // 这里可以留空，后续根据需求添加逻辑（例如加载游戏场景）
        saveAndLoad.ChangeScene();//TODO 默认进入平原 后续修改为从玩家身上获取最后一次离开的场景名称
    }

    void On_StartNewSave()
    {
        //saveAndLoad.Save();
        // 这里可以留空，后续根据需求添加逻辑（例如加载游戏场景）
        saveAndLoad.ChangeScene();//TODO 默认进入平原 后续修改为从玩家身上获取最后一次离开的场景名称
    }
    // Method to handle TMP_InputField value changes
    private void OnPlayerNameChanged(string newName)
    {
        PlayerName = newName; // Update the PlayerName when TMP_InputField changes
        Debug.Log("PlayerName updated to: " + PlayerName);
    }
    // Method to handle TMP_InputField value changes
    private void OnPlayerSaveNameChanged(string newName)
    {
        SaveName = newName; // Update the PlayerName when TMP_InputField changes
        Debug.Log("PlayerName updated to: " + PlayerName);
    }

    #endregion

    // 初始化
    public void Start()
    {
        LoadSaveFileNames();
        GenerateSaveButtons();
        // 绑定游戏开始按钮的点击事件
        if (StartGameButton != null)
        {
            StartGameButton.onClick.AddListener(OnStartGameClicked);
            Start_New_GameButton.onClick.AddListener(On_StartNewSave);
        }
        else
        {
            Debug.LogError("StartGameButton 未正确赋值！");
        }
        SelectedPlayer_Name_Text.onValueChanged.AddListener(OnPlayerNameChanged);
        NewPlayer_Name_Text.onValueChanged.AddListener(OnPlayerNameChanged);
        NewSave_Name_Text.onValueChanged.AddListener(OnPlayerSaveNameChanged);
    }

    // 刷新存档列表
    public void Refresh()
    {
        LoadSaveFileNames();
        GenerateSaveButtons();
    }
}