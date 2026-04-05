
using MemoryPack;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class EnvironmentFactors
{
    [Tooltip("温度归一化值，范围：0~1（用于生物群系匹配）")]
    public float TemperatureNormalized; // 归一化温度（0~1）
    [Tooltip("温度，单位：°C（用于显示和玩法计算）")]
    public float Temperature;     // 温度，单位：℃
    [Tooltip("湿度，单位：%")]
    public float Humidity;        // 湿度，单位：%
    [Tooltip("降水量，单位：mm")]
    public float Precipitation;   // 降水量，单位：mm
    [Tooltip("固体化程度，单位：°C")]
    public float Solidity;        // 固体程度（0=水，1=陆）
    [Tooltip("海拔高度，单位：m")]
    public float Hight;
    [Tooltip("污染程度，单位：%")]
    public float Pollution;       // 污染程度，单位：%
}
