using MemoryPack;
using Meryel.UnityCodeAssist.YamlDotNet.Core;
using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
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

    public GameSaveData GetSaveData()
    {
        SaveData ??= new GameSaveData();
        GameObject_False.Clear();
        SavePlayer();
        // ���浱ǰ����ĵ�ͼ
        MapSave mapSave = SaveActiveScene_Map();
        SaveData.MapSaves_Dict[mapSave.MapName] = mapSave;
        return SaveData;
    }

    #endregion

    #region ����

    [Button("�������")]
    public void LoadPlayer(string playerName)
    {
        if (SaveData.PlayerData_Dict.ContainsKey(playerName))
        {
            PlayerData _data = SaveData.PlayerData_Dict[playerName];
            Debug.Log("�ɹ�������ң�" + playerName);
            CreatePlayer(_data);
        }
        else
        {
            LoadAssetByLabelAndName<WorldSaveSO>("Template_SaveData", "���ģ��", result =>
            {
                PlayerData _data;

                if (result == null)
                {
                    Debug.LogWarning("����ʧ�ܣ�WorldSaveSO ����Ϊ�գ�");
                    _data = new PlayerData();
                }
                else if (result.SaveData == null)
                {
                    Debug.LogWarning("����ʧ�ܣ�SaveData Ϊ�գ�");
                    _data = new PlayerData();
                }
                else if (!result.SaveData.PlayerData_Dict.ContainsKey("Ĭ��"))
                {
                    Debug.LogWarning("���سɹ�����δ������Ĭ�ϡ�������ݣ�");
                    _data = new PlayerData();
                }
                else
                {
                    _data = result.SaveData.PlayerData_Dict["Ĭ��"];
                    Debug.Log("�ɹ�����Ĭ��ģ����ң�" + playerName);
                }

                CreatePlayer(_data);
            });
        }
    }

    private void CreatePlayer(PlayerData data)
    {
        GameObject newPlayerObj = GameRes.Instance.InstantiatePrefab("Player");
        Player newPlayer = newPlayerObj.GetComponentInChildren<Player>();
        newPlayer.Data = data;
        newPlayer.Load();
        Debug.Log("��Ҽ��سɹ���");
    }


    [Button("���ش浵")]
    public void Load()
    {
        LoadByDisk(SaveName);
    }

    // Fix for CS7036: Provide the required "onComplete" parameter when calling LoadOnDefaultTemplateMap.

    [Button("����ָ����ͼ")]
    public void LoadMap(string mapName)
    {
        MapSave mapSave;

        if (!SaveData.MapSaves_Dict.ContainsKey(mapName))
        {
            Debug.LogWarning($"δ�ҵ���ͼ���ݣ����ƣ�{mapName}������Ĭ��ģ��");

            LoadAssetByLabelAndName<WorldSaveSO>("Template_SaveData", mapName, result =>
            {
                if (result != null && result.SaveData != null && result.SaveData.MapSaves_Dict.ContainsKey(mapName))
                {
                    mapSave = result.SaveData.MapSaves_Dict[mapName];

                    foreach (var kvp in mapSave.items)
                    {
                        string itemName = kvp.Key;
                        List<ItemData> itemDataList = kvp.Value;

                        foreach (ItemData forLoadItemData in itemDataList)
                        {
                            GameObject itemPrefab;
                            if (!GameRes.Instance.AllPrefabs.TryGetValue(forLoadItemData.Name, out itemPrefab))
                            {
                                Debug.LogWarning($"δ�ҵ�Ԥ���壬���ƣ�{forLoadItemData.Name}");
                                continue;
                            }

                            GameObject newItemObj = Instantiate(itemPrefab, Vector3.zero, Quaternion.identity);
                            newItemObj.GetComponent<Item>().Item_Data = forLoadItemData;
                            newItemObj.transform.position = forLoadItemData._transform.Position;
                            newItemObj.transform.rotation = forLoadItemData._transform.Rotation;
                            newItemObj.transform.localScale = forLoadItemData._transform.Scale;
                            newItemObj.SetActive(true);

                            if (newItemObj.GetComponent<Item>() is ISave_Load save_Load)
                            {
                                save_Load.Load();
                            }
                        }
                    }

                    LoadPlayer(playerName);
                }
                else
                {
                    Debug.LogError($"����Ĭ��ģ���ͼʧ�ܣ����ƣ�{mapName}");
                }
            });
        }
        else
        {
            mapSave = SaveData.MapSaves_Dict[mapName];
            Debug.Log("�ɹ����ص�ͼ��" + mapName);

            foreach (var kvp in mapSave.items)
            {
                string itemName = kvp.Key;
                List<ItemData> itemDataList = kvp.Value;

                foreach (ItemData forLoadItemData in itemDataList)
                {
                    GameObject itemPrefab;
                    if (!GameRes.Instance.AllPrefabs.TryGetValue(forLoadItemData.Name, out itemPrefab))
                    {
                        Debug.LogWarning($"δ�ҵ�Ԥ���壬���ƣ�{forLoadItemData.Name}");
                        continue;
                    }

                    GameObject newItemObj = Instantiate(itemPrefab, Vector3.zero, Quaternion.identity);
                    newItemObj.GetComponent<Item>().Item_Data = forLoadItemData;
                    newItemObj.transform.position = forLoadItemData._transform.Position;
                    newItemObj.transform.rotation = forLoadItemData._transform.Rotation;
                    newItemObj.transform.localScale = forLoadItemData._transform.Scale;
                    newItemObj.SetActive(true);

                    if (newItemObj.GetComponent<Item>() is ISave_Load save_Load)
                    {
                        save_Load.Load();
                    }
                }
            }

            LoadPlayer(playerName);
        }
    }

    public void LoadAssetByLabelAndName<T>(string label, string name, Action<T> onComplete) where T : UnityEngine.Object
    {
        Addressables.LoadResourceLocationsAsync(label).Completed += locationHandle =>
        {
            if (locationHandle.Status != AsyncOperationStatus.Succeeded)
            {
                Debug.LogError($"��ǩ����ʧ�ܣ�{label}");
                onComplete?.Invoke(null);
                return;
            }

            var locations = locationHandle.Result;
            var targetLocation = locations.FirstOrDefault(loc => loc.PrimaryKey.Contains(name));

            if (targetLocation == null)
            {
                Debug.LogWarning($"δ�ҵ���Դ����ǩ��{label}�����ư�����{name}");
                onComplete?.Invoke(null);
                return;
            }

            Addressables.LoadAssetAsync<T>(targetLocation).Completed += assetHandle =>
            {
                if (assetHandle.Status == AsyncOperationStatus.Succeeded)
                {
                    onComplete?.Invoke(assetHandle.Result);
                }
                else
                {
                    Debug.LogError($"����ʧ�ܣ�{name}����ǩ��{label}��");
                    onComplete?.Invoke(null);
                }
            };
        };
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