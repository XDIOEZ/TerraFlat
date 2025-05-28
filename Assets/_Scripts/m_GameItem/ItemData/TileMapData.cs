using MemoryPack;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class Data_TileMap : ItemData
{
    // [ShowNonSerializedField]
    [ShowInInspector]
    public Dictionary<string, List<Vector2Int>> Data = new Dictionary<string, List<Vector2Int>>();
}