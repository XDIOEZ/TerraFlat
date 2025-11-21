using MemoryPack;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

[System.Serializable]
[MemoryPackable]
public partial class SceneData
{
    public string SceneName;
    public bool IsInit = false;
    public Vector2 PlayerPos;
    public bool Encapsulation;
    public float LightEfficiency;//����Ч��
}

public class Mod_Scene : Module
{
    #region �ֶκ�����
    public Ex_ModData_MemoryPackable _data;
    public override ModuleData _Data { get { return _data; } set { _data = (Ex_ModData_MemoryPackable)value; } }

    [Tooltip("����Ԥ�����б�����ʼ��ʱ��������ѡ��һ��")]
    public List<TextAsset> _sceneAssetList = new List<TextAsset>(); // �������ֶθ�Ϊ�б�
    public SceneData Data;
    public Vector2 PlayerPosOffset;

    public PlanetData planetData
    {
        get
        {
            if (SaveDataMgr.Instance.SaveData.PlanetData_Dict.TryGetValue(Data.SceneName, out PlanetData planetData))
            {
                return planetData;
            }
            return null;
        }
        set
        {
            SaveDataMgr.Instance.SaveData.PlanetData_Dict[Data.SceneName] = value;
        }
    }
    #endregion

    #region ��������
    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Scene;
        }
    }

    public override void Load()
    {
        _data.ReadData(ref Data);

        var mod_Building = item.itemMods.GetMod_ByID(ModText.Building) as Mod_Building;
        var Interacter = item.itemMods.GetMod_ByID(ModText.Interact) as Mod_Interaction;

        Interacter.OnAction_Start += Interact;

        if (mod_Building != null)
        {
            mod_Building.StartUnInstall += UnInstall;
            mod_Building.StartInstall += Install;
        }

        // ע�⣺��Դ��ʼ���߼�������Interact������ִ��
    }

    public override void Save()
    {
        _data.WriteData(Data);
    }
    #endregion

    #region ������װ��ж��
    public void Install()
    {
        TimeData timeData = new TimeData()
        {
            ReferenceScene = SceneManager.GetActiveScene().name,
        };

        DayTimeSystem.Instance.WorldTimeDict[Data.SceneName] = timeData;

        DayTimeSystem.Instance.SceneLightingRateDict[Data.SceneName]
            = Data.LightEfficiency;
    }

    public void UnInstall()
    {
        if (Data.Encapsulation == true || planetData == null)
        {
            return;
        }

        // ����planetData�����еĵ�ͼ����
        foreach (var mapSaveKV in planetData.MapData_Dict.ToList())
        {
            var mapSave = mapSaveKV.Value;

            // ����������Ʒ�б�����ֹ�ڵ���ʱ�޸��ֵ�ֵ
            foreach (var itemListKV in mapSave.items.ToList())
            {
                // ��������Ҫ�ָ�����Ʒ����
                if (itemListKV.Key == "MapEdge" || itemListKV.Key == "MapCore" ||
                    itemListKV.Key == "ǽ��" || itemListKV.Key == "Door")
                    continue;

                var itemList = itemListKV.Value;
                foreach (var mapItem in itemList)
                {
                    // ����Ʒʵ��������ǰλ�ã���Ӧ���û��ָ�λ�á���ת��
                    Item item = ItemMgr.Instance.InstantiateItem(mapItem, null);
                    item.transform.position = transform.position;
                }

                // �ӱ������Ƴ���Щ��Ʒ
                mapSave.items.Remove(itemListKV.Key);
            }

            // �ӵ�ͼ�����ֵ����Ƴ���ǰ��ͼ
            planetData.MapData_Dict.Remove(mapSaveKV.Key);
        }

        SaveDataMgr.Instance.SaveData.PlanetData_Dict.Remove(Data.SceneName);
        Debug.Log("��������Ʒ��ȫ��ʵ������ԭʼMapSave���Ƴ�");
    }
    #endregion

    #region ��������
    [Button]
    public void AddScene()
    {
        // ����һ���µ���ʱ�������ƣ��������Ա����ظ����Ƴ�ͻ
        Scene newScene = SceneManager.CreateScene("TempTentScene");

        // �л����´����ĳ�����Ϊ��ǰѡ��
        SceneManager.SetActiveScene(newScene);

        Debug.Log("�³����Ѵ���" + newScene.name);
    }

    [Button]
    public virtual void Interact(Item interacter)
    {
        Player player = interacter as Player;
        if (player == null)
        {
            Debug.LogError("Interact ����ʧ�ܣ����������� Player");
            return;
        }

        // ===== ��Դ��ʼ���߼��Ƶ��˴� =====
        // ����Ƿ��Ѿ���ʼ����ͨ����PlanetData_Dict�в���SceneName���ж�
        if (!SaveDataMgr.Instance.SaveData.PlanetData_Dict.ContainsKey(Data.SceneName) && 
            _sceneAssetList != null && _sceneAssetList.Count > 0)
        {
            // ���б������ѡ��һ������Ԥ����
            TextAsset selectedSceneAsset = _sceneAssetList[Random.Range(0, _sceneAssetList.Count)];
            
            if (selectedSceneAsset != null)
            {
                MapSave MapSave = MemoryPackSerializer.Deserialize<MapSave>(selectedSceneAsset.bytes);

                Data.SceneName += selectedSceneAsset.name;
                Data.SceneName += "_";
                Data.SceneName += Random.Range(1, 1000000).ToString();

                planetData = new PlanetData();
                planetData.ChunkSize = new Vector2Int(100, 100);
                planetData.Name = Data.SceneName;
                // �洢(0,0)λ�õĵ�ͼ����
                planetData.MapData_Dict.Add(MapSave.Name, MapSave);
                planetData.AutoGenerateMap = false;
                SaveDataMgr.Instance.SaveData.PlanetData_Dict[Data.SceneName] = planetData;
                Debug.Log(Data.SceneName + "��ʼ����ɣ�ʹ��Ԥ����: " + selectedSceneAsset.name);
            }
            else
            {
                Debug.LogError("����Ԥ�����б��а��������ã�");
            }
        }
        else if (!SaveDataMgr.Instance.SaveData.PlanetData_Dict.ContainsKey(Data.SceneName) && 
                 (_sceneAssetList == null || _sceneAssetList.Count == 0))
        {
            Debug.LogWarning("����Ԥ�����б�Ϊ�գ��޷���ʼ���������ݣ�");
        }

        Data_Player playerData = player.Data;
        Vector2 playerPos = player.transform.position;

        // ===== ���뷿�� =====
        if (_sceneAssetList.Count > 0)
        {
            string lastSceneName = playerData.CurrentSceneName;

            // ���ó�ʼ���뷿���λ��
            player.transform.position = this.Data.PlayerPos + PlayerPosOffset;

            //////////���µĲ��������³����н���//////////////

            // �л�����
            GameManager.Instance.ChangeScene_By_SceneNames(lastSceneName, Data.SceneName, () =>
            {
                playerData.CurrentSceneName = Data.SceneName;

                // ���� Chunk
                ChunkMgr.Instance.CleanEmptyDicValues();

                // ���¼������
                Player newPlayer = ItemMgr.Instance.CreatePlayer(playerData.Name_User);
                ItemMgr.Instance.Player_DIC[playerData.Name_User] = newPlayer;

                //������ Chunk
                foreach (var targetMapSave in planetData.MapData_Dict.Values)
                {
                    Chunk chunk = ChunkMgr.Instance.CreateChunk_ByMapSave(targetMapSave);

                    ChunkMgr.Instance.AddActiveChunk(chunk);//���ӵ����� Chunk �б���

                    chunk.LoadChunk_By_MapSaveData_Sync();

                    //������ҷ��ص�ͷ��س���
                    if (_sceneAssetList != null && _sceneAssetList.Count > 0)
                    {
                        // ����������Ʒ���ҵ����ص�
                        if (chunk.RuntimeItemsGroup.TryGetValue("MapCore_Pit", out var MapCore_Pit))
                        {
                            foreach (var MapCore in MapCore_Pit)
                            {
                                MapCore.Act();
                            }
                        }
                        // ����������Ʒ���ҵ����ص�
                        if (chunk.RuntimeItemsGroup.TryGetValue("Door", out var doors))
                        {
                            foreach (var door in doors)
                            {
                                if (door.itemMods.GetMod_ByID(ModText.Scene) is Mod_Scene sceneMod)
                                {
                                    sceneMod.Data.SceneName = lastSceneName;
                                    sceneMod.Data.PlayerPos = playerPos;

                                    newPlayer.Data.transform.position = (Vector2)door.transform.position + PlayerPosOffset;
                                    this.Data.PlayerPos = door.transform.position;
                                }
                            }
                        }
                    }
                }
                //�ȴ�Chunk���ݴ�����Ϻ��ٳ�ʼ����� ��Ϊ��������м�������CHunk���ݵ����
                newPlayer.Load();
                newPlayer.LoadDataPosition();
            });
        }
        // ===== �뿪���� =====
        else
        {
            player.transform.position = this.Data.PlayerPos + PlayerPosOffset;

            GameManager.Instance.ChangeScene_By_SceneNames(playerData.CurrentSceneName, this.Data.SceneName, () =>
            {
                playerData.CurrentSceneName = this.Data.SceneName;//���õ�ǰ���ڵĳ�������


                // ���¼����������
                Player newPlayer = ItemMgr.Instance.LoadPlayer(playerData.Name_User);
                newPlayer.Load();
                newPlayer.LoadDataPosition();
                ItemMgr.Instance.Player_DIC[playerData.Name_User] = newPlayer;
            });
        }
    }
    #endregion

    #region ����Ԥ�������
    /// <summary>
    /// ��ȡ����Ԥ�����б��е����һ��
    /// </summary>
    /// <returns>���ѡ��ĳ���Ԥ���壬����б�Ϊ���򷵻�null</returns>
    public TextAsset GetRandomSceneAsset()
    {
        if (_sceneAssetList == null || _sceneAssetList.Count == 0)
            return null;
            
        return _sceneAssetList[Random.Range(0, _sceneAssetList.Count)];
    }
    
    /// <summary>
    /// ���ӳ���Ԥ���嵽�б�
    /// </summary>
    /// <param name="sceneAsset">Ҫ���ӵĳ���Ԥ����</param>
    public void AddSceneAsset(TextAsset sceneAsset)
    {
        if (sceneAsset != null && !_sceneAssetList.Contains(sceneAsset))
        {
            _sceneAssetList.Add(sceneAsset);
        }
    }
    
    /// <summary>
    /// ���б����Ƴ�����Ԥ����
    /// </summary>
    /// <param name="sceneAsset">Ҫ�Ƴ��ĳ���Ԥ����</param>
    /// <returns>�Ƴ��ɹ�����true�����򷵻�false</returns>
    public bool RemoveSceneAsset(TextAsset sceneAsset)
    {
        return _sceneAssetList.Remove(sceneAsset);
    }
    #endregion
}