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
    public float LightEfficiency;//光照效率
}

public class Mod_Scene : Module
{
    public Ex_ModData_MemoryPackable _data;
    public override ModuleData _Data { get { return _data; } set { _data = (Ex_ModData_MemoryPackable)value; } }

    [Tooltip("场景预制体列表，初始化时会从中随机选择一个")]
    public List<TextAsset> _sceneAssetList = new List<TextAsset>(); // 将单个字段改为列表
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

    // 检查是否已经初始化，通过在PlanetData_Dict中查找SceneName来判断
    if (!SaveDataMgr.Instance.SaveData.PlanetData_Dict.ContainsKey(Data.SceneName) && 
        _sceneAssetList != null && _sceneAssetList.Count > 0)
    {
        // 从列表中随机选择一个场景预制体
        TextAsset selectedSceneAsset = _sceneAssetList[Random.Range(0, _sceneAssetList.Count)];
        
        if (selectedSceneAsset != null)
        {
            MapSave MapSave = MemoryPackSerializer.Deserialize<MapSave>(selectedSceneAsset.bytes);

            Data.SceneName += selectedSceneAsset.name;
            Data.SceneName += "_";
            Data.SceneName += Random.Range(1, 1000000).ToString();

            planetData = new PlanetData();
            planetData.ChunkSize = new Vector2Int(200, 200);
            planetData.Name = Data.SceneName;
            // 存储(0,0)位置的地图数据
            planetData.MapData_Dict.Add(MapSave.Name, MapSave);
            planetData.AutoGenerateMap = false;
            SaveDataMgr.Instance.SaveData.PlanetData_Dict[Data.SceneName] = planetData;
            Debug.Log(Data.SceneName + "初始化完成，使用预制体: " + selectedSceneAsset.name);
        }
        else
        {
            Debug.LogError("场景预制体列表中包含空引用！");
        }
    }
    else if (!SaveDataMgr.Instance.SaveData.PlanetData_Dict.ContainsKey(Data.SceneName) && 
             (_sceneAssetList == null || _sceneAssetList.Count == 0))
    {
        Debug.LogWarning("场景预制体列表为空，无法初始化场景数据！");
    }
}
    
    public void Install()
    {
        TimeData timeData = new TimeData()
        {
            ReferenceScene = SceneManager.GetActiveScene().name,
        };

/*        SaveDataMgr.Instance.SaveData.DayTimeData.WorldTimeDict[Data.SceneName]
            = new SerializableTimeData(timeData);*/
        DayTimeSystem.Instance.WorldTimeDict[Data.SceneName] = timeData;

        //SaveDataMgr.Instance.SaveData.DayTimeData.SceneLightingRateDict[Data.SceneName]
        //    = Data.LightEfficiency;
        DayTimeSystem.Instance.SceneLightingRateDict[Data.SceneName]
            = Data.LightEfficiency;
    }

    public void UnInstall()
    {
        if (Data.Encapsulation == true || planetData == null)
        {
            return;
        }

        // 遍历planetData中所有的地图数据
        foreach (var mapSaveKV in planetData.MapData_Dict.ToList())
        {
            var mapSave = mapSaveKV.Value;

            // 遍历所有物品列表，防止在迭代时修改字典值
            foreach (var itemListKV in mapSave.items.ToList())
            {
                // 跳过不需要恢复的物品类型
                if (itemListKV.Key == "MapEdge" || itemListKV.Key == "MapCore" ||
                    itemListKV.Key == "墙壁" || itemListKV.Key == "Door")
                    continue;

                var itemList = itemListKV.Value;
                foreach (var mapItem in itemList)
                {
                    // 将物品实例化到当前位置，不应用用户恢复位置、旋转等
                    Item item = ItemMgr.Instance.InstantiateItem(mapItem, null);
                    item.transform.position = transform.position;
                }

                // 从保存中移除这些物品
                mapSave.items.Remove(itemListKV.Key);
            }

            // 从地图数据字典中移除当前地图
            planetData.MapData_Dict.Remove(mapSaveKV.Key);
        }

        SaveDataMgr.Instance.SaveData.PlanetData_Dict.Remove(Data.SceneName);
        Debug.Log("场景内物品已全部实例化，原始MapSave已移除");
    }

    [Button]
    public void AddScene()
    {
        // 创建一个新的临时场景名称，这样可以避免重复名称冲突
        Scene newScene = SceneManager.CreateScene("TempTentScene");

        // 切换到新创建的场景作为当前选中
        SceneManager.SetActiveScene(newScene);

        Debug.Log("新场景已创建" + newScene.name);
    }

    [Button]
    public void Interact(Item interacter)
    {
        Player player = interacter as Player;
        if (player == null)
        {
            Debug.LogError("Interact 调用失败，交互对象不是 Player");
            return;
        }

        Data_Player playerData = player.Data;
        Vector2 playerPos = player.transform.position;

        // 获取(0,0)位置的地图数据
        MapSave targetMapSave = null;
        if (planetData != null && planetData.MapData_Dict.TryGetValue(Vector2Int.zero.ToString(), out var mapSave))
        {
            targetMapSave = mapSave;
        }

        // ===== 进入房间 =====
        if (targetMapSave != null)
        {
            string lastSceneName = playerData.CurrentSceneName;

            // 设置初始进入房间的位置
            player.transform.position = this.Data.PlayerPos + PlayerPosOffset;

            //////////以下的操作将在新场景中进行//////////////

            // 切换场景
            GameManager.Instance.ChangeScene_ByPlayerData(lastSceneName, Data.SceneName, () =>
            {
                playerData.CurrentSceneName = Data.SceneName;

                // 重新加载玩家数据
                Player newPlayer = ItemMgr.Instance.LoadPlayer(playerData.Name_User);
                newPlayer.Load();
                newPlayer.LoadDataPosition();
                ItemMgr.Instance.Player_DIC[playerData.Name_User] = newPlayer;

                // 清理 Chunk
                ChunkMgr.Instance.CleanEmptyDicValues();

                Chunk chunk = ChunkMgr.Instance.CreateChunK_ByMapSave(targetMapSave);
                ChunkMgr.Instance.Chunk_Dic[targetMapSave.Name] = chunk;
                ChunkMgr.Instance.Chunk_Dic_Active[targetMapSave.Name] = chunk;

                if (_sceneAssetList != null && _sceneAssetList.Count > 0)
                {
                    // 遍历所有物品，找到返回点
                    if (chunk.RuntimeItemsGroup.TryGetValue("MapCore_Pit", out var MapCore_Pit))
                    {
                        foreach (var MapCore in MapCore_Pit)
                        {
                            MapCore.Act();
                        }
                    }
                    // 遍历所有物品，找到返回点
                    if (chunk.RuntimeItemsGroup.TryGetValue("Door", out var doors))
                    {
                        foreach (var door in doors)
                        {
                            if (door.itemMods.GetMod_ByID(ModText.Scene) is Mod_Scene sceneMod)
                            {
                                sceneMod.Data.SceneName = lastSceneName;
                                sceneMod.Data.PlayerPos = playerPos;

                                newPlayer.transform.position = (Vector2)door.transform.position + PlayerPosOffset;
                                this.Data.PlayerPos = door.transform.position;
                            }
                        }
                    }
                }

                AstarGameManager.Instance.UpdateMeshAsync();
            });
        }
        // ===== 离开房间 =====
        else
        {
            player.transform.position = this.Data.PlayerPos + PlayerPosOffset;

            GameManager.Instance.ChangeScene_ByPlayerData(playerData.CurrentSceneName, this.Data.SceneName, () =>
            {
                playerData.CurrentSceneName = this.Data.SceneName;//设置当前所在的场景名称

                // 重新加载玩家数据
                Player newPlayer = ItemMgr.Instance.LoadPlayer(playerData.Name_User);
                newPlayer.Load();
                newPlayer.LoadDataPosition();
                ItemMgr.Instance.Player_DIC[playerData.Name_User] = newPlayer;
                AstarGameManager.Instance.UpdateMeshAsync();
            });
        }
    }

    public override void Save()
    {
        _data.WriteData(Data);
    }
    
    /// <summary>
    /// 获取场景预制体列表中的随机一个
    /// </summary>
    /// <returns>随机选择的场景预制体，如果列表为空则返回null</returns>
    public TextAsset GetRandomSceneAsset()
    {
        if (_sceneAssetList == null || _sceneAssetList.Count == 0)
            return null;
            
        return _sceneAssetList[Random.Range(0, _sceneAssetList.Count)];
    }
    
    /// <summary>
    /// 添加场景预制体到列表
    /// </summary>
    /// <param name="sceneAsset">要添加的场景预制体</param>
    public void AddSceneAsset(TextAsset sceneAsset)
    {
        if (sceneAsset != null && !_sceneAssetList.Contains(sceneAsset))
        {
            _sceneAssetList.Add(sceneAsset);
        }
    }
    
    /// <summary>
    /// 从列表中移除场景预制体
    /// </summary>
    /// <param name="sceneAsset">要移除的场景预制体</param>
    /// <returns>移除成功返回true，否则返回false</returns>
    public bool RemoveSceneAsset(TextAsset sceneAsset)
    {
        return _sceneAssetList.Remove(sceneAsset);
    }
}