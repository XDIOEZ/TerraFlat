using MemoryPack;
using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class TileMapData : ItemData
{
    // [ShowNonSerializedField]
    public Dictionary<string, List<Vector2Int>> Data = new Dictionary<string, List<Vector2Int>>();
//    public Dictionary<string, string> Data_MapEdge = new Dictionary<string, string>();
    public List<WorldEdgeData> WorldEdgeDatas = new List<WorldEdgeData>();
}