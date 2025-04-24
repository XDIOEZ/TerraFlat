using MemoryPack;
using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using Newtonsoft.Json;

public class SaveAndLoad : SingletonAutoMono<SaveAndLoad>
{
    [Header("�浵���")]
    [Tooltip("�浵����")]
    public string SaveName = "DefaultSave";
    [Tooltip("�浵·��")]
    public string SavePath = "Assets/Saves/";
    [Tooltip("��ǰʹ�õĴ浵����")]
    public GameSaveData SaveData;



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
        print("SaveAndLoad Start");
        //��������
        DontDestroyOnLoad(gameObject);
    }

    //�������
  
    //�������
   


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
         //Destroy(player.gameObject);
            playerCount++;
        }

        Debug.Log("������ݱ���ɹ������������" + playerCount);
        return playerCount;
    }
    [Button("����浵")]
    void Save()
    {
        //ȷ��ģ��浵���ᱻ����
        if(SaveData.saveName == "ģ��"&&!Input.GetKey(KeyCode.S))
        {
            Load();
        }

        SaveActiveMapToSaveData();

        if (SaveData != null)
        {
            SaveToDisk(SaveData);
        }
    }

    public void SaveActiveMapToSaveData()
    {
        if (SaveData == null)
        {
            SaveData = new GameSaveData();
            SaveData.MapSaves_Dict = new Dictionary<string, MapSave>();
            SaveData.PlayerData_Dict = new Dictionary<string, PlayerData>();
        }

        // �ȴ�����������ݵ�Э�̣���� SavePlayer ���첽�ģ�
        SavePlayer();

        // ���浱ǰ����ĵ�ͼ
        MapSave mapSave = SaveActiveScene_Map();
        SaveData.MapSaves_Dict[mapSave.MapName] = mapSave;
        Debug.Log("��ǰ��ͼ���ݱ���ɹ���");
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
        }
        else
        {
            if (TemplateSaveData.PlayerData_Dict.Count == 0)//���ģ��浵��û��������ݣ������Ĭ��ģ��浵
            {
                TemplateSaveData = GetSaveByDisk(TemplateSavePath, TemplateSaveName);
            }
            _data = TemplateSaveData.PlayerData_Dict["Ikun"];
        }

        GameObject player = GameRes.Instance.GetPrefab("Player");
        //ʵ�������
        GameObject newPlayerObj = Instantiate(player, Vector3.zero, Quaternion.identity);
        Player newPlayer = newPlayerObj.GetComponentInChildren<Player>();
        newPlayer.Data = _data;
        newPlayer.Data.PlayerUserName = playerName;
        newPlayer.Load();
        Debug.Log("��Ҽ��سɹ���");
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

        //�����ҵĴ浵�Ƿ���ڶ�Ӧ�ĵ�ͼ���� ���û�������Ĭ��ģ��
        if (!SaveData.MapSaves_Dict.ContainsKey(mapName))
        {
            mapSave = LoadOnDefaultTemplateMap(mapName);
        }
        else
        {
            mapSave = SaveData.MapSaves_Dict[mapName];
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
        Debug.Log("������سɹ���");

        LoadPlayer(playerName);
    }

    public MapSave LoadOnDefaultTemplateMap(string mapName)
    {
        if (TemplateSaveData.MapSaves_Dict.Count == 0)
            TemplateSaveData = GetSaveByDisk(TemplateSavePath, TemplateSaveName);

       return TemplateSaveData.MapSaves_Dict[mapName];
    }

    #endregion

    #region ����
    //�����ʼ��ͼ�ϵ���Ʒ
    [Button("�����ʼ��ͼ�ϵ���Ʒ")]
    public void CleanActiveMapItems()
    {
        // ��ȡ��ǰ���������ж���
        Item[] allObjs = GameObject.FindObjectsOfType<Item>();

        foreach (Item obj in allObjs)
        {
            // ����Ƿ�������ֲ�Ϊ "MapCore"���Ҳ��� "WorldManager" ���Ӷ���
            if (obj.name == "WorldManager" || obj.transform.parent?.name == "WorldManager")
            {
                continue;
            }
            Destroy(obj); // ���ٶ���
        }
        Debug.Log("��ʼ��ͼ��Ʒ����ɹ���");
    }
    #endregion

    #region ���߷���

    #region ���浱ǰ����ĳ����ĵ�ͼ����
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
        return worldSave;
    }

    #endregion
    #region ��ȡ��ǰ����ĳ����е�������Ʒ����

    /// <summary>
    /// ��ȡ��ǰ����ĳ����е�������Ʒ����
    /// </summary>
    /// <returns></returns>
    public Dictionary<string, List<ItemData>> GetActiveSceneAllItemData()
    {
        // ��ȡ��ǰ�����е�������Ʒ
        Item[] itemsInScene = FindObjectsOfType<Item>();
        Dictionary<string, List<ItemData>> itemData_dict = new Dictionary<string, List<ItemData>>();
        // ����������Ʒ �ҳ��̳�ISave_Load����Ʒ������Save����
        foreach (Item item in itemsInScene)
        {
            // 4. �����Ʒ�Ƿ�ʵ���� ISave_Load �ӿ�
            if (item is ISave_Load saveableItem)
            {
                saveableItem.Save(); // ���� Save ����������Ʒ����
            }

            item.SyncPosition(); // ͬ��λ����Ϣ
                                 // ��ȡ��Ʒ���ݣ����� Item_Data ���� Name ���ԣ�
            ItemData itemDataTemp = item.Item_Data;
            // �������Ʒ��鱣��
            if (!itemData_dict.ContainsKey(itemDataTemp.Name))
            {
                itemData_dict[itemDataTemp.Name] = new List<ItemData>();
            }

            itemData_dict[itemDataTemp.Name].Add(itemDataTemp);
        }
        return itemData_dict;
    }

    #endregion
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

    #region ��ʼ��������Ϸ����

    [Button("��ʼ��������Ϸ����")]
    public void ChangeScene(string sceneName = "ƽԭ")
    {
        // �ȴ� Save() ��ɣ�Save() ��Э�̣�
        Save();
        // �첽���س���
        StartCoroutine(EnterScene(sceneName));
    }
    // Э�̣��첽���س��������ص�ͼ
    public IEnumerator EnterScene(string sceneName = "ƽԭ")
    {
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);

        // �ȴ������������
        while (!asyncLoad.isDone)
        {
            yield return null;
        }

        // ����������ɺ���ص�ͼ
        LoadMap(sceneName);
    }


    #endregion
}

[MemoryPackable]
[System.Serializable]
public partial class MapSave
{
    public string MapName;

    [ShowNonSerializedField]
    // ��ԭ�ȴ洢���� ItemData ���ֵ��Ϊ�洢 List<ItemData>��key Ϊ��Ʒ����
    public Dictionary<string, List<ItemData>> items = new Dictionary<string, List<ItemData>>();

    // ˵�����ڱ�����Ʒʱ��ͬһ���Ƶ���Ʒ��洢��ͬһ List �У�
    // �����������ʱ����ʵ��������ֵ
}


[MemoryPackable]
[System.Serializable]
public partial class GameSaveData
{
    public string saveName= "defaultSaveName";//�浵����
    [ShowNonSerializedField]
    //�浵���ݽṹ
    public Dictionary<string, MapSave> MapSaves_Dict = new Dictionary<string, MapSave>();
    //�������
    [ShowNonSerializedField]
    public Dictionary<string, PlayerData> PlayerData_Dict = new Dictionary<string, PlayerData>();

    public string leaveTime = "0";//�뿪ʱ��
}
