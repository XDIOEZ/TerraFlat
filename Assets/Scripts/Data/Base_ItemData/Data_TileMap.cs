using MemoryPack;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

[System.Serializable]
[MemoryPackable]
public partial class Data_TileMap : ItemData
{
    [HideInInspector]
    [SerializeField]
    public Dictionary<Vector2Int, List<TileData>> TileData = new();

    [Tooltip("µØÍ¼µÄÎ»ÖÃ")]
    public Vector2Int position = new Vector2Int(0,0);

    public bool TileLoaded = false;

    public EnvironmentFactors[,] EnvironmentData = new EnvironmentFactors[0, 0];
}