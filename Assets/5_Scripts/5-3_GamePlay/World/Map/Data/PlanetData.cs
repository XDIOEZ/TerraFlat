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

/// <summary>
/// Defines how world coordinates behave outside the generated map range.
/// Keep the numeric values stable because this enum is persisted by MemoryPack.
/// </summary>
[System.Serializable]
public enum WorldTopologyMode
{
    Infinite = 0,
    Wrapped = 1
}

[MemoryPackable]
[System.Serializable]
public partial class PlanetData
{
    #region 世界生成默认值
    public const int DefaultRadius = 1000;
    public const float DefaultNoiseScale = 0.01f;
    public const float MinNoiseScale = 0f;
    public const float MaxNoiseScale = 100f;

    /// <summary>判断世界坐标缩放是否可用于程序生成。</summary>
    public static bool IsValidNoiseScale(float value)
    {
        return !float.IsNaN(value) &&
               !float.IsInfinity(value) &&
               value >= MinNoiseScale &&
               value <= MaxNoiseScale;
    }

    /// <summary>非法世界坐标缩放统一回退到项目默认值。</summary>
    public static float NormalizeNoiseScale(float value)
    {
        return IsValidNoiseScale(value) ? value : DefaultNoiseScale;
    }
    #endregion

    #region 星球基础数据
    //星球名称
    public string Name;
    //星球半径
    [LabelText("星球半径"), MinValue(1), PropertyTooltip("星球可探索范围的基础半径。")]
    public int Radius = DefaultRadius;

    [LabelText("世界坐标缩放"), MinValue(MinNoiseScale), PropertyTooltip("所有地形噪声共享的世界级坐标倍率。越小地貌越舒展，越大地貌越密集；最终频率还会乘各通道的坐标倍率和基础频率。")]
    public float NoiseScale = DefaultNoiseScale;

    //星球地图大小
    public Vector2Int ChunkSize = new Vector2Int(16, 16);
    #endregion

    #region 地图与环境数据
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

    [LabelText("天气数据版本"), PropertyTooltip("天气事件运行时数据版本。")]
    public int WeatherDataVersion = 0;

    [LabelText("天气阶段"), PropertyTooltip("权威天气事件当前所处阶段。")]
    public WeatherPhase WeatherPhase = WeatherPhase.Clear;

    [LabelText("阶段开始总时间"), PropertyTooltip("当前天气阶段开始时的绝对世界时间。")]
    public float WeatherPhaseStartedTotalTime = 0f;

    [LabelText("阶段结束总时间"), PropertyTooltip("非晴朗阶段结束时的绝对世界时间。")]
    public float WeatherPhaseEndTotalTime = 0f;

    [LabelText("下次天气事件总时间"), PropertyTooltip("晴朗阶段下一次天气预兆开始时的绝对世界时间。")]
    public float NextWeatherEventTotalTime = 0f;

    [LabelText("天气随机游标"), PropertyTooltip("确定性天气随机序列的持久化游标。")]
    public int WeatherRandomCursor = 0;

    [LabelText("天气事件序号"), PropertyTooltip("已经开始过的降雨事件数量。")]
    public int WeatherEventSequence = 0;

    [LabelText("雨天降温"), SuffixLabel("℃", true), PropertyTooltip("雨天对环境温度的额外降温值。")]
    public float RainTemperatureOffset = -4f;

    [LabelText("阴天天气修正"), SuffixLabel("℃", true), PropertyTooltip("阴天对环境温度的额外修正值。")]
    public float CloudyTemperatureOffset = -1f;

    [LabelText("暴风天气修正"), SuffixLabel("℃", true), PropertyTooltip("暴风天气对环境温度的额外修正值。")]
    public float StormTemperatureOffset = -6f;

    // Keep this field at the end of the MemoryPack layout. Older saves omit it
    // and therefore retain the enum default: Infinite.
    [LabelText("World Topology")]
    public WorldTopologyMode TopologyMode = WorldTopologyMode.Infinite;

    // 世界级生态数据。
    [LabelText("生态世界数据")]
    public EcologyWorldSaveData Ecology = new();
    #endregion

}
