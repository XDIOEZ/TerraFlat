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

    public GameObject Save_Player_SelectButton_Prefab;//��� �浵 ��ťԤ����

    public Transform SaveSelectButton_Parent_Content;//�浵ѡ��ť������

    public Transform Player_SelectButton_Parent_Content;//���ѡ��ť������

    public TextMeshProUGUI SelectedSave_Name_Text;//��ʾ��ǰѡ�еĴ浵����
    #region ��ť

    public Button StartGameButton; // ��Ϸ��ʼ��ť

    public Button Start_New_GameButton; // ��ʼ����Ϸ��ť
    #endregion

    #region ����Һʹ浵�����
    [Header("����Һʹ浵�����")]
    public TMP_InputField NewPlayer_Name_Text;//����������

    public TMP_InputField NewSave_Name_Text;//�´浵�����

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
    #region �б�ť����

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

    #region UI�¼�
    // ��Ұ�ť����¼�
    public void OnClickPlayerButton(string playerName)
    {
        if (saveAndLoad != null)
        {
            PlayerName = playerName;
            SelectedPlayer_Name_Text.text = playerName;
        }
        else
        {
            Debug.LogWarning("SaveAndLoad���δ�󶨣�");
        }

        SelectedPlayer_Name_Text.text = playerName;
    }

    // �浵��ť����¼�
    public void OnClickSaveButton(string saveName)
    {
        if (saveAndLoad != null)
        {
            saveAndLoad.LoadByDisk(saveName);
        }
        else
        {
            Debug.LogWarning("SaveAndLoad���δ�󶨣�");
        }

        SelectedSave_Name_Text.text = saveName;
        GeneratePlayerButtons();
    }

    // ��Ϸ��ʼ��ť�Ŀշ��������ʱ������
    public void OnStartGameClicked()
    {
        // ����������գ�����������������߼������������Ϸ������
        saveAndLoad.ChangeScene();//TODO Ĭ�Ͻ���ƽԭ �����޸�Ϊ��������ϻ�ȡ���һ���뿪�ĳ�������
    }

    void On_StartNewSave()
    {
        //saveAndLoad.Save();
        // ����������գ�����������������߼������������Ϸ������
        saveAndLoad.ChangeScene();//TODO Ĭ�Ͻ���ƽԭ �����޸�Ϊ��������ϻ�ȡ���һ���뿪�ĳ�������
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

    // ��ʼ��
    public void Start()
    {
        LoadSaveFileNames();
        GenerateSaveButtons();
        // ����Ϸ��ʼ��ť�ĵ���¼�
        if (StartGameButton != null)
        {
            StartGameButton.onClick.AddListener(OnStartGameClicked);
            Start_New_GameButton.onClick.AddListener(On_StartNewSave);
        }
        else
        {
            Debug.LogError("StartGameButton δ��ȷ��ֵ��");
        }
        SelectedPlayer_Name_Text.onValueChanged.AddListener(OnPlayerNameChanged);
        NewPlayer_Name_Text.onValueChanged.AddListener(OnPlayerNameChanged);
        NewSave_Name_Text.onValueChanged.AddListener(OnPlayerSaveNameChanged);
    }

    // ˢ�´浵�б�
    public void Refresh()
    {
        LoadSaveFileNames();
        GenerateSaveButtons();
    }
}