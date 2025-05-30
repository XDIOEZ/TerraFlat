using MemoryPack;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class Data_TileMap : ItemData
{
    public Dictionary<Vector2Int,List<TileData>> TileData = new();
}

[System.Serializable]
[MemoryPackable]
public partial class TileData
{
    public string Name_tileBase = "TileBase_";             // 对应 TileBase 名称
    public string Name_ItemName;    //对应的物品名称
    
    public Vector3Int position;         // Tile 坐标

    public float workTime;              // 玩家与 Tile 交互耗时


    public ItemValues itemValues;             // 物品属性
}
