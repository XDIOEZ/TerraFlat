using MemoryPack;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[System.Serializable]
[MemoryPackable]
public partial class Data_TileMap : ItemData
{
    [HideInInspector]
    [SerializeField]
    public Dictionary<Vector2Int, List<TileData>> TileData = new();

}

[System.Serializable]
[MemoryPackable]
public partial class TileData
{
    //物品的绘制物块 用于实现
    public string Name_TileBase;
    //对应的物品名字--用于获取物品中的方法
    public string Name_ItemName;
    //地块的Tag
    public string TileTag = "";
    //地块所在位置
    public Vector3Int position;
    //拆除所需时间
    public float DemolitionTime;
    //当前拆除的时间
    public float workTime;
}
[System.Serializable]
[MemoryPackable]
public partial class TileData_Grass : TileData
{
    public GameValue_float FertileValue = new GameValue_float();

}
[System.Serializable]
[MemoryPackable]
public partial class TileData_Water : TileData
{
    public GameValue_float DeepValue = new GameValue_float();
}