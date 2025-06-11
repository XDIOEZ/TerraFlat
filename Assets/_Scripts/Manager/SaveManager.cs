using System.IO;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;
using NaughtyAttributes;

public class SaveManager : MonoBehaviour
{
    [Header("���������")]
    public SaveAndLoad saveAndLoad;

    [Header("�浵��Ϣ")]
    public List<string> saves = new List<string>();
    public string PathToSaveFolder = "Assets/Saves/LoaclSave/";

    [Header("��ť�븸����")]
    public GameObject Save_Player_SelectButton_Prefab; // �浵/��Ұ�ťԤ����

    public Transform SaveSelectButton_Parent_Content; // �浵��ť������
    public Transform Player_SelectButton_Parent_Content; // ��Ұ�ť������

    [Header("UI ��ʾ")]
    public TextMeshProUGUI SelectedSave_Name_Text; // ��ǰѡ�еĴ浵��


    public Button StartGameButton; // ��ʼ��Ϸ��ť
    [Header("��ť")]
    public Button DeletSave; // ����Ϸ��ť

    [Header("�����")]
    #region ����Ϸ���UI

    public TMP_InputField NewPlayer_Name_Text; // ����������
    public TMP_InputField NewSave_Name_Text; // �´浵�����
    public Button Start_New_GameButton; // ����Ϸ��ť
    #endregion


    #region ѡ�е�_�浵��_��ҵ�����

    public TMP_InputField SelectedPlayer_Name_Text; // ��ǰѡ�������ʾ��

    #endregion

    

    #region ��ʼ��
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
            Debug.LogError("StartGameButton δ��ȷ��ֵ��");
        }

        SelectedPlayer_Name_Text.onValueChanged.AddListener(OnUpdate_PlayerNameChanged_Text);
        NewPlayer_Name_Text.onValueChanged.AddListener(OnUpdate_PlayerNameChanged_Text);
        NewSave_Name_Text.onValueChanged.AddListener(OnPlayerSaveNameChanged);
        DeletSave.onClick.AddListener(OnClick_DeletSave_Button);
    }
    #endregion

    #region �浵����
    // ���ش浵�ļ���
    public void LoadSaveFileNames()
    {
        saves.Clear();

        if (!Directory.Exists(PathToSaveFolder))
        {
            Debug.LogWarning("����·��������: " + PathToSaveFolder);
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

    #region ��ť����
    // ���ɴ浵ѡ��ť
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

    // �������ѡ��ť
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

    #region UI�¼�
    public void OnClick_DeletSave_Button()
    {
        saveAndLoad.DeletSave(saveAndLoad.PlayerSavePath, SelectedSave_Name_Text.text);
        LoadSaveFileNames();
        GenerateSaveButtons();
    }

    // �浵��ť����¼�
    public void OnClick_List_Save_Button(string saveName)
    {
        if (saveAndLoad != null)
        {
            saveAndLoad.LoadSaveByDisk(saveAndLoad.PlayerSavePath+ saveName);
        }
        else
        {
            Debug.LogWarning("SaveAndLoad���δ�󶨣�");
        }
        //ͬ���浵����
        SelectedSave_Name_Text.text = saveName;
        GeneratePlayerButtons();
    }

    // ��Ұ�ť����¼�
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

    // ��Ϸ��ʼ��ť����¼�
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

    // ��ʼ����Ϸ��ť����¼�
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

    // ������������¼�
    private void OnUpdate_PlayerNameChanged_Text(string newName)
    {
        if (saveAndLoad != null)
        {
            saveAndLoad.CurrentContrrolPlayerName = newName;
        }
    }

    // �浵���������¼�
    private void OnPlayerSaveNameChanged(string newName)
    {
        if (saveAndLoad != null && saveAndLoad.SaveData != null)
        {
            saveAndLoad.SaveData.saveName = newName;
        }
    }
    #endregion

    #region ��������
    // ˢ�´浵�б�
    public void Refresh()
    {
        LoadSaveFileNames();
        GenerateSaveButtons();
    }
    #endregion
}
