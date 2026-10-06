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
    Storm,
    Fog = 4 // 只追加枚举值，保持已有存档和联机天气编号不变。
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
    public SnowCoverState SeasonalSnow = new(); // 季节覆盖独立于天然雪地与地形身份。
    #region 世界生成默认值
    public const int DefaultRadius = 1000;
    public const int DefaultChunkDimension = 16;
    public const int MinChunkDimension = 1;
    public const int MaxChunkDimension = 256;
    public const float DefaultSpatialDistanceScale = 1f;
    public const float MinSpatialDistanceScale = 0.25f;
    public const float MaxSpatialDistanceScale = 4f;
    public const float DefaultNoiseScale = 0.01f; // 仅作为底层程序生成坐标频率基准。
    public const float DefaultWindStrength = 0.35f;

    /// <summary>判断玩家设置的自然地理空间距离倍率是否有效。</summary>
    public static bool IsValidSpatialDistanceScale(float value)
    {
        return !float.IsNaN(value) &&
               !float.IsInfinity(value) &&
               value >= MinSpatialDistanceScale &&
               value <= MaxSpatialDistanceScale;
    }

    /// <summary>非法空间距离倍率统一回退到 1x。</summary>
    public static float NormalizeSpatialDistanceScale(float value)
    {
        return IsValidSpatialDistanceScale(value) ? value : DefaultSpatialDistanceScale;
    }

    /// <summary>空间距离倍率与底层噪声坐标频率互为倒数：2x 地理尺度使用 0.005 坐标倍率。</summary>
    public static float ResolveCoordinateScale(float spatialDistanceScale) =>
        DefaultNoiseScale / NormalizeSpatialDistanceScale(spatialDistanceScale);

    /// <summary>区块单边尺寸允许玩家按世界配置，联机协议与生成器共用同一范围。</summary>
    public static bool IsValidChunkDimension(int value)
    {
        return value >= MinChunkDimension && value <= MaxChunkDimension;
    }

    /// <summary>非法区块尺寸回退到默认 16×16，避免不同系统各自维护兜底值。</summary>
    public static Vector2Int NormalizeChunkSize(Vector2Int value)
    {
        return new Vector2Int(
            IsValidChunkDimension(value.x) ? value.x : DefaultChunkDimension,
            IsValidChunkDimension(value.y) ? value.y : DefaultChunkDimension);
    }
    #endregion

    #region 星球基础数据
    //星球名称
    public string Name;
    //星球半径
    [LabelText("星球半径"), MinValue(1), PropertyTooltip("星球可探索范围的基础半径。")]
    public int Radius = DefaultRadius;

    [LabelText("空间距离倍率"), MinValue(MinSpatialDistanceScale), MaxValue(MaxSpatialDistanceScale), PropertyTooltip("自然地理结构的统一空间尺度。1x 为基准；2x 会让地貌、气候区、河流、湖泊、雪区与洞穴等空间距离约扩大到两倍，并反向降低底层噪声坐标频率。")]
    public float SpatialDistanceScale = DefaultSpatialDistanceScale;

    //星球地图大小
    public Vector2Int ChunkSize = new Vector2Int(DefaultChunkDimension, DefaultChunkDimension);
    #endregion

    #region 地图与环境数据
    //星球地图数据字典
    [ShowInInspector]
    public Dictionary<string, MapSave> MapData_Dict = new();

    [Tooltip("星球是否自动生成地图")]
    public bool AutoGenerateMap = true;

    public const float DefaultGlobalTemperature = 26f; // 生成气候的星球温度基准
    [LabelText("基础温度"), SuffixLabel("℃", true), PropertyTooltip("默认 26℃ 保留生成地块温度；调高或调低时，整张地图叠加相对于 26℃ 的差值。")]
    public float GlobalTemperature = DefaultGlobalTemperature;

    [LabelText("当前天气"), PropertyTooltip("该星球当前天气类型。")]
    public WeatherType CurrentWeather = WeatherType.Clear;

    [LabelText("天气强度"), Range(0f, 1f), PropertyTooltip("当前天气的强度，0表示无影响，1表示满强度。")]
    public float WeatherIntensity = 0f;

    [LabelText("全局风力"), Range(0f, 1f), PropertyTooltip("星球当前的全局风力，统一控制草木摆动幅度。")]
    public float WindStrength = DefaultWindStrength;

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
