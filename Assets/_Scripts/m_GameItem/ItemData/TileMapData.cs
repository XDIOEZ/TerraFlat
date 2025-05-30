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
    public string Name_tileBase = "TileBase_";             // ��Ӧ TileBase ����
    public string Name_ItemName;    //��Ӧ����Ʒ����
    
    public Vector3Int position;         // Tile ����

    public float workTime;              // ����� Tile ������ʱ


    public ItemValues itemValues;             // ��Ʒ����
}
