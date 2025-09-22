using MemoryPack;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine;

[MemoryPackable]
[System.Serializable]
public partial class PlanetData
{
    //星球名称
    public string Name;
    //星球半径
    public int Radius = 1000;
    //温度偏移值
    public int TemperatureOffset = 0;
    //降雨偏移
    public float RainOffset = 0;
    //海洋高度
    public float OceanHeight = -1;
    //噪声缩放
    public float NoiseScale = 0.01f;

    //星球地图大小
    public Vector2Int ChunkSize = new Vector2Int(100, 100);

    [ShowInInspector]
    //星球地图数据
    public Dictionary<string, MapSave> MapData_Dict = new();

    [Tooltip("星球是否自动生成地图")]
    public bool AutoGenerateMap = true;

}