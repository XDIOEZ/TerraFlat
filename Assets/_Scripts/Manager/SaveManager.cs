using System.IO;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;
using Sirenix.OdinInspector;

public class SaveDataManager_UI : MonoBehaviour
{
    #region �ֶζ���

    [Header("���������")]
    public SaveDataMgr saveAndLoad;
    public static SaveDataManager_UI Ins;

    [Header("UI������")]
    public BaseUIManager uiManager; // BaseUIManager�ֶ�

    public PlanetData Ready_planetData = new PlanetData();


    [Header("�浵��Ϣ")]
    public List<string> saves = new List<string>();

    [Header("��ť�븸����")]
    public GameObject Save_Player_SelectButton_Prefab; // �浵/��Ұ�ťԤ����
    public Transform SaveSelectButton_Parent_Content; // �浵��ť������
    public Transform Player_SelectButton_Parent_Content; // ��Ұ�ť������

    // �Ƴ���ԭ��������UI�ؼ��ֶΣ�ͨ��BaseUIManager��ȡ����

    // ʹ��Application.persistentDataPath��Ϊ����·��
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
        // ȷ���浵Ŀ¼����
        EnsureSaveDirectoryExists();
    }

    #region ��ʼ��
    private void Start()
    {
        saveAndLoad = SaveDataMgr.Instance;
        
        // ��ʼ��BaseUIManager
        if (uiManager == null)
        {
            uiManager = GetComponent<BaseUIManager>() ?? gameObject.AddComponent<BaseUIManager>();
        }

        LoadSaveFileNames();
        GenerateSaveButtons();

        // ʹ��BaseUIManager���ð�ť�¼�
        SetupUIEvents();
    }

    /// <summary>
    /// ����UI�¼�
    /// </summary>
    private void SetupUIEvents()
    {
        // ���ð�ť����¼�
        uiManager.SetButtonOnClick("��ʼ��Ϸ��ť", OnClick_StartGame_Button);
        uiManager.SetButtonOnClick("��ʼ����Ϸ", OnClick_StartNewGame_Button);
        uiManager.SetButtonOnClick("���ش浵��ť", OnClick_LoadSaveData_Button);
        uiManager.SetButtonOnClick("ɾ����ť", OnClick_DeletSave_Button);

        // ���������ֵ�ı��¼�
        GetSelectedPlayerNameInput()?.onValueChanged.AddListener(OnUpdate_PlayerNameChanged_Text);
        GetNewPlayerNameInput()?.onValueChanged.AddListener(OnUpdate_PlayerNameChanged_Text);
        GetNewSaveNameInput()?.onValueChanged.AddListener(OnPlayerSaveNameChanged);
        GetPlanetRadiusInput()?.onValueChanged.AddListener(OnPlanetReadiusChanged);
        GetPlanetNoiseInput()?.onValueChanged.AddListener(OnPlanetNoiseScaleChanged);
    }

    // ͨ��BaseUIManager��ȡUI�ؼ��ı�ݷ���
    private TMP_InputField GetSelectedPlayerNameInput() => uiManager.GetInputField("ѡ�������������������");
    private TMP_InputField GetNewPlayerNameInput() => uiManager.GetInputField("����������������");
    private TMP_InputField GetNewSaveNameInput() => uiManager.GetInputField("�����浵���������");
    private TMP_InputField GetPlanetRadiusInput() => uiManager.GetInputField("����뾶�����");
    private TMP_InputField GetPlanetNoiseInput() => uiManager.GetInputField("�������������");
    private TextMeshProUGUI GetSelectedSaveNameText() => uiManager.GetText("ѡ�еĴ浵����");
    private Button GetStartGameButton() => uiManager.GetButton("StartGameButton");
    private Button GetStartNewGameButton() => uiManager.GetButton("StartNewGameButton");
    private Button GetLoadSaveDataButton() => uiManager.GetButton("LoadSaveDataButton");
    private Button GetDeleteSaveButton() => uiManager.GetButton("DeleteSaveButton");

    public void StartNewGame()
    {
        SaveDataMgr.Instance.SaveData.SaveSeed = Random.Range(0, int.MaxValue).ToString();
        // ��ʼ��������Ӳ�����ϵͳ���ʵ��
        SaveDataMgr.Instance.SaveData.Seed = SaveDataMgr.Instance.SaveData.SaveSeed.GetHashCode();
        Random.InitState(SaveDataMgr.Instance.SaveData.Seed);
        RandomMapGenerator.rng = new System.Random(SaveDataMgr.Instance.SaveData.Seed);
        SaveDataMgr.Instance.SaveData.PlanetData_Dict["����"] = Ready_planetData;
    }
    #endregion

    #region �浵����
    /// <summary>
    /// ȷ���浵Ŀ¼����
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

        string[] files = Directory.GetFiles(PathToSaveFolder, "*.bytes");
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
        // �������а�ť
        foreach (Transform child in SaveSelectButton_Parent_Content)
            Destroy(child.gameObject);

        foreach (Transform child in Player_SelectButton_Parent_Content)
            Destroy(child.gameObject);

        // ���ɴ浵��ť
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
        
        // ������Ұ�ť
        GeneratePlayerButtons();
    }

    /// <summary>
    /// �������ѡ��ť
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
                    btn.onClick.AddListener(() => OnClick_List_PlayerName_Button(playerName));
            }
        }
    }
    #endregion

    #region UI�¼�
    #region �浵��ȡ

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
            Debug.LogWarning("SaveAndLoad���δ�󶨣�");
        }
        // ������Ұ�ť
        GeneratePlayerButtons();
    }
    #endregion

    /// <summary>
    /// ɾ���浵
    /// </summary>
    public void OnClick_DeletSave_Button()
    {
        if(SaveMenuRightMenuUI.Instance.SelectInfo.Path == "")
        {
            //ɾ�����
            saveAndLoad.SaveData.PlayerData_Dict.Remove(SaveMenuRightMenuUI.Instance.SelectInfo.Name);

        } else if(SaveMenuRightMenuUI.Instance.SelectInfo.Path != "")
        {
            // ɾ���浵
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

    #region �浵ѡ��

    /// <summary>
    /// ����浵��ť
    /// </summary>
    public void OnClick_List_Save_Button(string saveName, GameObject buttonObj)
    {
        // �������д浵��ť��ѡ��ͼ�� & ��ԭ��ɫ
        foreach (var saveInfo in SaveSelectButton_Parent_Content.GetComponentsInChildren<ButtonInfoData>())
        {
            if (saveInfo.SelectImage != null)
                saveInfo.SelectImage.enabled = false;

            var img = saveInfo.GetComponent<UnityEngine.UI.Image>();
            if (img != null)
                img.color = Color.white; // ��ԭΪĬ�ϰ�ɫ
        }

        // ���õ�ǰ��ť��ѡ��ͼ�� & ������ɫ
        var currentInfo = buttonObj.GetComponent<ButtonInfoData>();
        if (currentInfo != null && currentInfo.SelectImage != null)
        {
            currentInfo.SelectImage.enabled = true;
        }

        var currentImg = buttonObj.GetComponent<UnityEngine.UI.Image>();
        if (currentImg != null)
        {
            currentImg.color = Color.green; // ѡ��ʱ����
        }

        // ʹ��BaseUIManager�����ı�
        uiManager.SetText("ѡ�еĴ浵����", saveName);
    }

    #endregion

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

        // ʹ��BaseUIManager���������
        uiManager.SetInputFieldText("ѡ�������������������", playerName);
    }

    /// <summary>
    /// �����ʼ��Ϸ��ť
    /// </summary>
    public void OnClick_StartGame_Button()
    {
        if (saveAndLoad?.SaveData == null || saveAndLoad.SaveData.Seed == 0)
        {
            Debug.LogWarning("����ѡ��浵�򴴽�����Ϸ");
            return;
        }
        GameManager.Instance.ContinueGame();
    }

    /// <summary>
    /// �����ʼ����Ϸ��ť
    /// </summary>
    private void OnClick_StartNewGame_Button()
    {
        if (saveAndLoad != null)
        {
            GameManager.Instance.StartNewGame();
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

    /// <summary>
    /// ����뾶�����ʵʱ�����¼�
    /// </summary>
    private void OnPlanetReadiusChanged(string newValue)
    {
        // ��⴫����ַ����Ƿ�Ϊ��Ч������
        if (int.TryParse(newValue, out int radius))
        {
            Ready_planetData.Radius = radius;
        }
        else
        {
            // �Ƿ����룬����������Ҫʱ����ʾ�û�
            Debug.LogWarning($"����İ뾶ֵ��Ч��{newValue}");
        }
    }

    /// <summary>
    /// �����������������ʵʱ�����¼�
    /// </summary>
    private void OnPlanetNoiseScaleChanged(string newValue)
    {
        // ��⴫����ַ����Ƿ�Ϊ��Ч�ĸ�����
        if (float.TryParse(newValue, out float noiseScale))
        {
            Ready_planetData.NoiseScale = noiseScale;
        }
        else
        {
            // �Ƿ����룬����������Ҫʱ����ʾ�û�
            Debug.LogWarning($"�������������ֵ��Ч��{newValue}");
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
        
        // ˢ��BaseUIManager�е����
        uiManager.RefreshUIComponents();
    }
    #endregion
}