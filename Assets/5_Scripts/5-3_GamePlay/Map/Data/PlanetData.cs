using MemoryPack;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public enum WeatherType
{
    Clear,
    Cloudy,
    Rain,
    Storm
}

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

    [LabelText("基础温度"), SuffixLabel("℃", true), PropertyTooltip("该星球不包含天气修正的基础环境温度。")]
    public float GlobalTemperature = 26f;

    [LabelText("当前天气"), PropertyTooltip("该星球当前天气类型。")]
    public WeatherType CurrentWeather = WeatherType.Clear;

    [LabelText("天气强度"), Range(0f, 1f), PropertyTooltip("当前天气的强度，0表示无影响，1表示满强度。")]
    public float WeatherIntensity = 0f;

    [LabelText("雨天降温"), SuffixLabel("℃", true), PropertyTooltip("雨天对环境温度的额外降温值。")]
    public float RainTemperatureOffset = -4f;

    [LabelText("阴天天气修正"), SuffixLabel("℃", true), PropertyTooltip("阴天对环境温度的额外修正值。")]
    public float CloudyTemperatureOffset = -1f;

    [LabelText("暴风天气修正"), SuffixLabel("℃", true), PropertyTooltip("暴风天气对环境温度的额外修正值。")]
    public float StormTemperatureOffset = -6f;

}