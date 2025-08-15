using MemoryPack;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
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
    public override void Load()
    {
        _data.ReadData(ref Data);


        var mod_Building = item.itemMods.GetMod_ByID(ModText.Building) as Mod_Building;

        if(mod_Building!= null)
        {
            mod_Building.StartUnInstall += UnInstall;
        }
        if (CurrentMapSave != null)
        {
            Data.MapSave = CurrentMapSave;
            CurrentMapSave = null;
        }
    }
    public void UnInstall()
    {
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
             Item item =   GameItemManager.Instance.InstantiateItem(mapItem);
                item.transform.position = transform.position;
            }

            // 从保存数据中移除该类别
            Data.MapSave.items.Remove(itemListKV.Key);
        }

        Debug.Log("屋子内部物品已全部实例化并从 MapSave 移除");
    }



    [Button]
    public void Test()
    {
            if(Data.IsInit == false) 
            {
                Data.MapSave = MemoryPackSerializer.Deserialize<MapSave>(_sceneAsset.bytes);
                Debug.Log(Data.MapSave.MapName+"初始化完成");
             Data.IsInit = true;
            }

        Data.MapSave.items["MapEdge"].ForEach(item =>
        {
            Data_Boundary boundary = item as Data_Boundary;
            boundary.TP_Position = transform.position;
            boundary.TP_SceneName = SaveLoadManager.Instance.SaveData.Active_MapName;
            Data.PlayerPos = boundary._transform.Position;
        });

        if(CurrentMapSave!= null)
        {
            Data.MapSave = CurrentMapSave;
            CurrentMapSave = null;
        }

        SaveLoadManager.Instance.SaveActiveMapToSaveData();
        SaveLoadManager.Instance.ChangeTOMapByMapSave(Data.MapSave);

        if (SaveLoadManager.Instance.SaveData.PlayerData_Dict.TryGetValue
         (SaveLoadManager.Instance.CurrentContrrolPlayerName, out var savedData))
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