using MemoryPack;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SaveAndLoad : SingletonAutoMono<SaveAndLoad>
{
    [Header("�浵���")]
    [Tooltip("�浵����")]
    public string SaveName = "DefaultSave";
    [Tooltip("�浵·��")]
    public string SavePath = "Assets/Saves/";
    [Tooltip("��ǰʹ�õĴ浵����")]
    public GameSaveData SaveData;

    [Tooltip("��ʱʧЧ����")]
    public List<GameObject> GameObject_False;

    [Header("Ԥ��ģ�����")]
    [Tooltip("Ԥ��ģ��浵����")]
    public string TemplateSaveName = "ģ��";
    [Tooltip("Ԥ��ģ��浵ȫ·��")]
    public string TemplateSavePath = "Assets/Saves/DefaultTemplate/";
    [Tooltip("��ǰʹ�õ�Ԥ��ģ��浵����")]
    public GameSaveData TemplateSaveData;

    public string playerName;


    public void Start()
    {
        DontDestroyOnLoad(gameObject);
    }



    #region ����
 
     [Button("�������")]
    public int SavePlayer()
    {
        int playerCount = 0;
        //��ȡ����������Player
        Player[] players = FindObjectsOfType<Player>();

        //�����������
        foreach (Player player in players)
        {
            player.Save();
            SaveData.PlayerData_Dict[player.Data.PlayerUserName] = player.Data;
            player.gameObject.SetActive(false);
            //������ʱʧЧ�����б�
            GameObject_False.Add(player.gameObject);

            playerCount++;
        }

       // Debug.Log("������ݱ���ɹ������������" + playerCount);
        return playerCount;
    }

    [Button("����浵��������")]
    void Save()
    {
        SaveActiveMapToSaveData();

        SaveToDisk(SaveData);
        
    }

    [Tooltip("���浱ǰ��������뵱ǰ�ĳ����ֵ���")]
    public void SaveActiveMapToSaveData()
    {

        SaveData ??= new GameSaveData();
        GameObject_False.Clear();
        SavePlayer();
        // ���浱ǰ����ĵ�ͼ
        MapSave mapSave = SaveActiveScene_Map();
        SaveData.MapSaves_Dict[mapSave.MapName] = mapSave;

     //   Debug.Log("�ɹ�����ǰ��������뵱ǰʹ�õĴ浵");
    }

    #endregion

    #region ����
    [Button("�������")]
    public void LoadPlayer(string playerName)
    {
        PlayerData _data;

        if (SaveData.PlayerData_Dict.ContainsKey(playerName))
        {
            _data = SaveData.PlayerData_Dict[playerName];
            Debug.Log("�ɹ�������ң�" + playerName);
        }
        else
        {
            if (TemplateSaveData.PlayerData_Dict.Count == 0)//���ģ��浵��û��������ݣ������Ĭ��ģ��浵
            {
                TemplateSaveData = GetSaveByDisk(TemplateSavePath, TemplateSaveName);
            }
            _data = TemplateSaveData.PlayerData_Dict["Ikun"];
            Debug.Log("δ�ҵ�������ݣ�����Ĭ��ģ���������");
        }

        GameObject newPlayerObj = GameRes.Instance.InstantiatePrefab("Player");
        Player newPlayer = newPlayerObj.GetComponentInChildren<Player>();
        newPlayer.Data = _data;
        newPlayer.Load();
       // Debug.Log("��Ҽ��سɹ���");
    }

    [Button("���ش浵")]
    public void Load()
    {
        LoadByDisk(SaveName);
    }

    [Button("����ָ����ͼ")]
    public void LoadMap(string mapName)
    {
        MapSave mapSave;

        //���浵�Ƿ���ڶ�Ӧ�ĵ�ͼ���� ���û�������Ĭ��ģ��
        if (!SaveData.MapSaves_Dict.ContainsKey(mapName))
        {
            Debug.LogWarning($"δ�ҵ���ͼ���ݣ����ƣ�{mapName} , ����Ĭ��ģ��");
            mapSave = LoadOnDefaultTemplateMap(mapName);
        }
        else
        {
            mapSave = SaveData.MapSaves_Dict[mapName];
            Debug.Log("�ɹ����ص�ͼ��" + mapName);
        }
       

        // �����ֵ���ÿ����ֵ��
        foreach (var kvp in mapSave.items)
        {
            string itemName = kvp.Key;
            List<ItemData> itemDataList = kvp.Value;
            // ����ͬһ���ƵĶ����Ʒ���ʵ����
            foreach (ItemData forLoadItemData in itemDataList)
            {
                GameObject itemPrefab;
                // ���� GameResManager.Instance.AllPrefabs ����Ʒ����Ϊ key
                if (!GameRes.Instance.AllPrefabs.TryGetValue(forLoadItemData.Name, out itemPrefab))
                {
                    Debug.LogWarning($"δ�ҵ�Ԥ���壬���ƣ�{forLoadItemData.Name}");
                    continue;
                }
                // ʵ������Ʒ
                GameObject newItemObj = Instantiate(itemPrefab, Vector3.zero, Quaternion.identity);
                // ������Ʒ����
                newItemObj.GetComponent<Item>().Item_Data = forLoadItemData;
                // ����λ�á���ת������
                newItemObj.transform.position = forLoadItemData._transform.Position;
                newItemObj.transform.rotation = forLoadItemData._transform.Rotation;
                newItemObj.transform.localScale = forLoadItemData._transform.Scale;
                newItemObj.SetActive(true);

                if(newItemObj.GetComponent<Item>() is ISave_Load save_Load)
                {
                    save_Load.Load();
                }
            }
        }
        //  Debug.Log("������سɹ���");

        LoadPlayer(playerName);
    }

    public MapSave LoadOnDefaultTemplateMap(string mapName)
    {
        if (TemplateSaveData.MapSaves_Dict.Count == 0)
        {
            TemplateSaveData = GetSaveByDisk(TemplateSavePath, TemplateSaveName);
        }
        return TemplateSaveData.MapSaves_Dict[mapName];
    }

    #endregion

    #region ���߷���
    #region ���浽����

    public void SaveToDisk(GameSaveData SaveData)
    {
        SaveData.saveName = SaveName;

        byte[] dataBytes = MemoryPackSerializer.Serialize(SaveData);
        //��ȡ��ǰ������������Ϊ�浵����
       // SaveData.saveName = SceneManager.GetActiveScene().name;
        //���浽����
        File.WriteAllBytes(SavePath + SaveName + ".QAQ", dataBytes);
        Debug.Log("�浵�ɹ���");
    }

    /// <summary>
    /// ���浱ǰ����ĳ����ĵ�ͼ����
    /// </summary>
    /// <param name="MapName"></param>
    /// <returns></returns>
    public MapSave SaveActiveScene_Map()
    {
        MapSave worldSave = new MapSave();
        // ��ȡ��ǰ�����������
        worldSave.MapName = SceneManager.GetActiveScene().name;
        // ��ȡ��ǰ�����е�������Ʒ���ݣ��Ѱ����Ʒ��飩
        worldSave.items = GetActiveSceneAllItemData();
        //����ʧЧ����
        foreach (GameObject go in GameObject_False)
        {
            go.SetActive(true);
        }

        return worldSave;
    }
    /// <summary>
    /// ��ȡ��ǰ����ĳ����е�������Ʒ����
    /// </summary>
    /// <returns></returns>
    public Dictionary<string, List<ItemData>> GetActiveSceneAllItemData()
    {
        Dictionary<string, List<ItemData>> itemDataDict = new Dictionary<string, List<ItemData>>();

        // ��һ������ȡ���������е� Item�������Ǽ���״̬��
        Item[] allItems = FindObjectsOfType<Item>(includeInactive: false);

        // �ڶ������ȵ��� Save()��������©
        foreach (Item item in allItems)
        {
            if (item == null)
                continue;

            if (item is ISave_Load saveableItem)
            {
                try
                {
                    saveableItem.Save();
                    //����Ч����Ʒ��ӵ���ʱʧЧ�����б�
                    if (!item.gameObject.activeInHierarchy)
                    {
                        GameObject_False.Add(item.gameObject);
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"������Ʒʧ��: {item.name}", item);
                    Debug.LogException(ex);
                }
            }

           
        }

        // ���������ٴα������� Item��ɸѡ�� still active �ģ���ͬ��λ�á��ռ�����
        foreach (Item item in allItems)
        {
            if (item == null || item.transform == null || item.gameObject == null)
                continue;

            // ֻ����ǰ�Դ��ڼ���״̬�� Item
            if (!item.gameObject.activeInHierarchy)
                continue;

            item.SyncPosition();

            ItemData itemData = item.Item_Data;
            if (itemData == null)
                continue;

            if (!itemDataDict.TryGetValue(itemData.Name, out List<ItemData> list))
            {
                list = new List<ItemData>();
                itemDataDict[itemData.Name] = list;
            }

            list.Add(itemData);
        }

        return itemDataDict;
    }

    #endregion


    #region �Ӵ����ϼ���

    public void LoadByDisk(string Load_saveName)
    {
        Debug.Log("��ʼ�Ӵ��̼��ش浵��" + Load_saveName);
        SaveData = null;
        SaveData = MemoryPackSerializer.Deserialize<GameSaveData>(File.ReadAllBytes(SavePath + Load_saveName + ".QAQ"));
    }

    public GameSaveData GetSaveByDisk(string savePath, string Load_saveName)
    {
        return MemoryPackSerializer.Deserialize<GameSaveData>(File.ReadAllBytes(savePath + Load_saveName + ".QAQ"));
    }
    #endregion

    #endregion

    #region �����л�

    [Button("�ı䳡��")]
    public void ChangeScene(string sceneName = "ƽԭ")
    {
        // ���浱ǰ�����ĵ�ͼ����
        SaveActiveMapToSaveData();
        // �л�����
        EnterScene(sceneName);
    }

    private Scene newScene;

    /// <summary>
    /// �л�����
    /// </summary>
    /// <param name="sceneName"></param>
    public void EnterScene(string sceneName = "ƽԭ")
    {
        // 1. �����³������ճ�����
        newScene = SceneManager.CreateScene(sceneName);

        // 2. ��ȡ��ǰ�����
        Scene previousScene = SceneManager.GetActiveScene();

        // 3. ע��ж����ɺ�Ļص�
        SceneManager.sceneUnloaded += OnPreviousSceneUnloaded;

        // 4. ��ʼж�ؾɳ���
        SceneManager.UnloadSceneAsync(previousScene);
    }

    private void OnPreviousSceneUnloaded(Scene unloadedScene)
    {
        // ȡ���¼�ע�ᣬ�����ظ�����
        SceneManager.sceneUnloaded -= OnPreviousSceneUnloaded;

        // ��Ϊ�³���Ϊ�����
        SceneManager.SetActiveScene(newScene);

        // ��������
        LoadMap(newScene.name);
    }




    #endregion
}

[MemoryPackable]
[System.Serializable]
public partial class MapSave
{
    public string MapName;

    [ShowInInspector]
    // ��ԭ�ȴ洢���� ItemData ���ֵ��Ϊ�洢 List<ItemData>��key Ϊ��Ʒ����
    public Dictionary<string, List<ItemData>> items = new Dictionary<string, List<ItemData>>();

    // ˵�����ڱ�����Ʒʱ��ͬһ���Ƶ���Ʒ��洢��ͬһ List �У�
    // �����������ʱ����ʵ��������ֵ
}


[MemoryPackable]
[System.Serializable]
public partial class GameSaveData
{
    public string saveName = "defaultSaveName";//�浵����
    [ShowInInspector]
    //�浵���ݽṹ
    public Dictionary<string, MapSave> MapSaves_Dict = new Dictionary<string, MapSave>();
    //�������
    [ShowInInspector]
    public Dictionary<string, PlayerData> PlayerData_Dict = new Dictionary<string, PlayerData>();

    public string leaveTime = "0";//�뿪ʱ��

    //���캯��
    public GameSaveData()
    {
        MapSaves_Dict = new Dictionary<string, MapSave>();
        PlayerData_Dict = new Dictionary<string, PlayerData>();
    }
}