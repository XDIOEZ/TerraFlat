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
}

public class Mod_Scene : Module
{
    public Ex_ModData_MemoryPackable _data;
    public override ModuleData _Data { get { return _data; } set { _data = (Ex_ModData_MemoryPackable)value; } }
  
    public TextAsset _sceneAsset;
    public SceneData Data;
    public Vector2 PlayerPosOffset;

    public MapSave MapSave
    {
        get
        {
            if (SaveDataMgr.Instance.SaveData.MapInScene.TryGetValue(Data.SceneName, out var mapSave))
            {
                return mapSave;
            }
            return null;
        }
        set
        {
            SaveDataMgr.Instance.SaveData.MapInScene[Data.SceneName] = value;
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

        if (mod_Building!= null)
        {
            mod_Building.StartUnInstall += UnInstall;
        }

        //检查是否已经初始化过 如果没有就初始化
        if (Data.IsInit == false && _sceneAsset!= null)
        {
            
            MapSave MapSave = MemoryPackSerializer.Deserialize<MapSave>(_sceneAsset.bytes);

            Data.SceneName += _sceneAsset.name;
            Data.SceneName += "_";
            Data.SceneName += Random.Range(1, 1000000).ToString();


            Data.IsInit = true;
            this.MapSave = MapSave;
            Debug.Log(Data.SceneName + "初始化完成");
          
        }
    }
    public void UnInstall()
    {
        if (Data.Encapsulation == true)
        {
            return;
        }
        if (MapSave == null)
        {
            Debug.LogWarning("MapSave 数据为空，无法卸载屋子物品。");
            return;
        }

        // 创建快照，防止遍历中修改字典
        foreach (var itemListKV in MapSave.items.ToList())
        {
            // 跳过不需要拆的物品类别
            if (itemListKV.Key == "MapEdge" || itemListKV.Key == "MapCore"|| itemListKV.Key == "墙壁"|| itemListKV.Key == "Door")
                continue;

            var itemList = itemListKV.Value;
            foreach (var mapItem in itemList)
            {
                // 生成物品实例（内部应该恢复位置、旋转等）
             Item item =   ItemMgr.Instance.InstantiateItem(mapItem,null);
                item.transform.position = transform.position;
            }

            // 从保存数据中移除该类别
            MapSave.items.Remove(itemListKV.Key);
           
        }
        SaveDataMgr.Instance.SaveData.MapInScene.Remove(Data.SceneName);
        Debug.Log("屋子内部物品已全部实例化并从 MapSave 移除");
    }

    [Button]
    public void AddScene()
    {
        // 创建一个新的临时场景，名字可以随便起
        Scene newScene = SceneManager.CreateScene("TempTentScene");

        // 切换到这个新场景（可选）
        SceneManager.SetActiveScene(newScene);

        Debug.Log("新场景已创建：" + newScene.name);
    }

    [Button]
    public void Interact(Item interacter)
    {
        Player player = interacter as Player;
        if (player == null)
        {
            Debug.LogError("Interact 调用失败：传入对象不是 Player");
            return;
        }

        Data_Player playerData = player.Data;
        Vector2 playerPos = player.transform.position;

        // ===== 进入房间 =====
        if (MapSave != null)
        {

            string lastSceneName = playerData.CurrentSceneName;

            // 设置初始进入房间的位置
            player.transform.position = this.Data.PlayerPos + PlayerPosOffset;
            //////////下面的操作都是在新场景中进行的//////////////

            // 切换场景
            GameManager.Instance.ChangeScene_ByPlayerData(lastSceneName, Data.SceneName,() =>
            {
                playerData.CurrentSceneName = this.MapSave.MapName;
                // lastScene 已经被销毁
                playerData.IsInRoom = true;

                // 重新加载玩家
                Player newPlayer = ItemMgr.Instance.LoadPlayer(playerData.Name_User);
                newPlayer.Load();
                newPlayer.LoadDataPosition();
                ItemMgr.Instance.Player_DIC[playerData.Name_User] = newPlayer;

                // 生成 Chunk
                ChunkMgr.Instance.CleanEmptyDicValues();
                Chunk chunk = ChunkMgr.Instance.CreateChunK_ByMapSave(MapSave);
                ChunkMgr.Instance.Chunk_Dic[MapSave.MapName] = chunk;
                ChunkMgr.Instance.Chunk_Dic_Active[MapSave.MapName] = chunk;
                // 遍历所有门，设置返回点
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
            });
        }
        // ===== 离开房间 =====
        else
        {
       
            player.transform.position = this.Data.PlayerPos + PlayerPosOffset;

            GameManager.Instance.ChangeScene_ByPlayerData(playerData.CurrentSceneName, this.Data.SceneName,() =>
            {
                playerData.CurrentSceneName = this.Data.SceneName;//更新玩家所处的场景名称
                playerData.IsInRoom = false;

                // 重新加载玩家
                Player newPlayer = ItemMgr.Instance.LoadPlayer(playerData.Name_User);
                newPlayer.Load();
                newPlayer.LoadDataPosition();
                ItemMgr.Instance.Player_DIC[playerData.Name_User] = newPlayer;
            });
        }
    }


    public override void Save()
    {
        _data.WriteData(Data);
    }
}