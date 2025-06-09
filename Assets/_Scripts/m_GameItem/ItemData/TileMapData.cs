using MemoryPack;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine;

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
   
    public string Name_tileBase = "TileBase_";
    
    public string Name_ItemName;

    public Vector3Int position;

    public float workTime;
 
    public ItemValues itemValues;
}