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
   
    public string Name_tileBase;
    [MemoryPackIgnore]
    public TileBase tileBase;
    
    public string Name_ItemName;

    public string TileTag = "none";
    public Vector3Int position;

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
}