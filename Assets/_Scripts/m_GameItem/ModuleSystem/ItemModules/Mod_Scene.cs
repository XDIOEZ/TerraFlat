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
    public MapSave MapSave;
    public Vector2 PlayerPos;
    public bool Encapsulation;
}

public class Mod_Scene : Module
{
    public static MapSave CurrentMapSave;
    public Ex_ModData_MemoryPackable _data;
    public override ModuleData _Data { get { return _data; } set { _data = (Ex_ModData_MemoryPackable)value; } }
  
    public TextAsset _sceneAsset;
    public SceneData Data;
    public Vector2 PlayerPosOffset;

    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Space;
        }
    }

    public override void Load()
    {
        _data.ReadData(ref Data);


        var mod_Building = item.itemMods.GetMod_ByID(ModText.Building) as Mod_Building;

        if(mod_Building!= null)
        {
            mod_Building.StartUnInstall += UnInstall;
        }

        //检查是否已经初始化过 如果没有就初始化
        if (Data.IsInit == false)
        {
            Data.MapSave = MemoryPackSerializer.Deserialize<MapSave>(_sceneAsset.bytes);
            Debug.Log(Data.MapSave.MapName + "初始化完成");
            Data.MapSave.MapName += "_";
            Data.MapSave.MapName += Random.Range(1, 1000000).ToString();
            Data.IsInit = true;
        }

        if (CurrentMapSave != null && CurrentMapSave.MapName == Data.MapSave.MapName)
        {
            Data.MapSave = CurrentMapSave;
            CurrentMapSave = null;
        }
    }
    public void UnInstall()
    {
        SaveDataManager.Instance.SaveData.Active_PlanetData.MapData_Dict.Remove(Data.MapSave.MapName);

        if (Data.Encapsulation == true)
        {
            return;
        }
        if (Data?.MapSave == null)
        {
            Debug.LogWarning("MapSave 数据为空，无法卸载屋子物品。");
            return;
        }

        // 创建快照，防止遍历中修改字典
        foreach (var itemListKV in Data.MapSave.items.ToList())
        {
            // 跳过不需要拆的物品类别
            if (itemListKV.Key == "MapEdge" || itemListKV.Key == "MapCore"|| itemListKV.Key == "墙壁")
                continue;

            var itemList = itemListKV.Value;
            foreach (var mapItem in itemList)
            {
                // 生成物品实例（内部应该恢复位置、旋转等）
             Item item =   GameItemManager.Instance.InstantiateItem(mapItem,null);
                item.transform.position = transform.position;
            }

            // 从保存数据中移除该类别
            Data.MapSave.items.Remove(itemListKV.Key);
           
        }

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
    public void Test()
    {
        //创建一个临时场景
        Scene newScene = SceneManager.CreateScene(Data.SceneName);

        //TODO 0.保存旧场景内物品
        //1.卸载旧场景内物品


        //切换到这个新场景
        SceneManager.SetActiveScene(newScene);

        //TODO 通过Data.MapSave 还原场景内物品 放入上面的新临时场景

        GameChunkManager.Instance.CreateChun_ByMapSave(Data.MapSave);

        Data.MapSave.items["MapEdge"].ForEach(item =>
        {
            Data_Boundary boundary = item as Data_Boundary;
            boundary.TP_Position = transform.position;
            boundary.TP_SceneName = SaveDataManager.Instance.SaveData.Active_MapName;
            Data.PlayerPos = boundary._transform.Position;
        });

        if (SaveDataManager.Instance.SaveData.PlayerData_Dict.TryGetValue
         (SaveDataManager.Instance.CurrentContrrolPlayerName, out var savedData))
        {
            //Debug.Log($"成功加载已保存的玩家：{playerName}");
            Data_Player playerData = savedData;
            playerData._transform.Position = Data.PlayerPos + PlayerPosOffset;
        }


    }

    public override void Save()
    {
        _data.WriteData(Data);
    }
}