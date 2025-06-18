using System.IO;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;
using Sirenix.OdinInspector;

public class SaveManager : MonoBehaviour
{
    #region �ֶζ���

    [Header("���������")]
    public SaveAndLoad saveAndLoad;
    public static SaveManager Ins;

    [Header("�浵��Ϣ")]
    public List<string> saves = new List<string>();
    private string pathToSaveFolder = "Assets/Saves/LoaclSave/";

    [Header("��ť�븸����")]
    public GameObject Save_Player_SelectButton_Prefab; // �浵/��Ұ�ťԤ����
    public Transform SaveSelectButton_Parent_Content; // �浵��ť������
    public Transform Player_SelectButton_Parent_Content; // ��Ұ�ť������

    [Header("UI ��ʾ")]
    public TextMeshProUGUI SelectedSave_Name_Text; // ��ǰѡ�еĴ浵��
    public Button StartGameButton; // ��ʼ��Ϸ��ť
    public Button DeletSave; // ɾ���浵��ť

    [Header("����� - ����Ϸ")]
    public TMP_InputField NewPlayer_Name_Text; // ����������
    public TMP_InputField NewSave_Name_Text; // �´浵�����
    public Button Start_New_GameButton; // ����Ϸ��ť

    [Header("�������ʾ")]
    public TMP_InputField SelectedPlayer_Name_Text; // ��ǰѡ�������ʾ��

    public string PathToSaveFolder { get => saveAndLoad.PlayerSavePath;}


    #endregion
    public void Awake()
    {
        Ins = this;
    }
    #region ��ʼ��
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
            Debug.LogError("StartGameButton δ��ȷ��ֵ��");
        }

        SelectedPlayer_Name_Text.onValueChanged.AddListener(OnUpdate_PlayerNameChanged_Text);
        NewPlayer_Name_Text.onValueChanged.AddListener(OnUpdate_PlayerNameChanged_Text);
        NewSave_Name_Text.onValueChanged.AddListener(OnPlayerSaveNameChanged);
        DeletSave.onClick.AddListener(OnClick_DeletSave_Button);
    }
    #endregion

    #region �浵����
    /// <summary>
    /// ���ش浵�ļ���
    /// </summary>
    public void LoadSaveFileNames()
    {
        saves.Clear();

        if (!Directory.Exists(PathToSaveFolder))
        {
            Debug.LogWarning("����·��������: " + PathToSaveFolder);
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

    #region ��ť����
    /// <summary>
    /// ���ɴ浵ѡ��ť
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
    /// �������ѡ��ť
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

    #region UI�¼�

    /// <summary>
    /// ɾ���浵
    /// </summary>
    public void OnClick_DeletSave_Button()
    {
        saveAndLoad.DeletSave(saveAndLoad.PlayerSavePath, SelectedSave_Name_Text.text);
        Refresh();
    }

    /// <summary>
    /// ����浵��ť
    /// </summary>
    public void OnClick_List_Save_Button(string saveName)
    {
        if (saveAndLoad != null)
        {
            saveAndLoad.LoadSaveByDisk(saveAndLoad.PlayerSavePath + saveName);
        }
        else
        {
            Debug.LogWarning("SaveAndLoad���δ�󶨣�");
        }

        SelectedSave_Name_Text.text = saveName;
        GeneratePlayerButtons();
    }

    /// <summary>
    /// �����Ұ�ť
    /// </summary>
    public void OnClick_List_PlayerName_Button(string playerName)
    {
        if (saveAndLoad != null)
        {
            saveAndLoad.CurrentContrrolPlayerName = playerName;
        }
        else
        {
            Debug.LogWarning("SaveAndLoad���δ�󶨣�");
        }

        SelectedPlayer_Name_Text.text = playerName;
    }

    /// <summary>
    /// �����ʼ��Ϸ��ť
    /// </summary>
    public void OnClick_StartGame_Button()
    {
        if (saveAndLoad != null)
        {
            saveAndLoad.ChangeScene(saveAndLoad.SaveData.ActiveSceneName);
        }
        else
        {
            Debug.LogWarning("SaveAndLoad���δ�󶨣�");
        }
    }

    /// <summary>
    /// �����ʼ����Ϸ��ť
    /// </summary>
    private void OnClick_StartNewGame_Button()
    {
        if (saveAndLoad != null)
        {
            saveAndLoad.ChangeScene(saveAndLoad.SaveData.ActiveSceneName);
        }
        else
        {
            Debug.LogWarning("SaveAndLoad���δ�󶨣�");
        }
    }

    /// <summary>
    /// ������������ʵʱ�����¼�
    /// </summary>
    private void OnUpdate_PlayerNameChanged_Text(string newName)
    {
        if (saveAndLoad != null)
        {
            saveAndLoad.CurrentContrrolPlayerName = newName;
        }
    }

    /// <summary>
    /// �浵���������ʵʱ�����¼�
    /// </summary>
    private void OnPlayerSaveNameChanged(string newName)
    {
        if (saveAndLoad != null && saveAndLoad.SaveData != null)
        {
            saveAndLoad.SaveData.saveName = newName;
        }
    }

    #endregion

    #region ��������
    [Button("ˢ�´浵��ť")]
    /// <summary>
    /// ˢ�´浵��ť
    /// </summary>
    public void Refresh()
    {
        LoadSaveFileNames();
        GenerateSaveButtons();
    }
    #endregion
}
