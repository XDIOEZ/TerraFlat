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
    //噪声缩放
    public float NoiseScale = 0.01f;
    //星球地图大小
    public Vector2Int ChunkSize = new Vector2Int(16, 16);

    //星球地图数据字典
    [ShowInInspector]
    public Dictionary<string, MapSave> MapData_Dict = new();

    [Tooltip("星球是否自动生成地图")]
    public bool AutoGenerateMap = true;

    [LabelText("全局温度"), SuffixLabel("℃", true), PropertyTooltip("该星球当前全局环境温度。")]
    public float GlobalTemperature = 26f;

}