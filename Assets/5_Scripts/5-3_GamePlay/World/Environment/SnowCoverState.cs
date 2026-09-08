using System;
using MemoryPack;
using UnityEngine;

/// <summary>独立季节积雪状态：按自然基础气温分档保存覆盖量，未加载区块同样经历降雪与融化，最多 161 个数值。</summary>
[Serializable, MemoryPackable]
public partial class SnowCoverState
{
    public const int MinimumTemperature = -80;
    public const int BandCount = 161;
    public float[] Coverage = new float[BandCount]; // -80～80℃ 基础气候的持续覆盖。
    public bool Initialized; // 是否建立世界时间游标。
    public float LastTotalTime; // 在其他维度度过的时间也需按原天气边界补算。

    /// <summary>从相邻气候档平滑读取覆盖量，边界使用最冷或最热档。</summary>
    public float Sample(float baselineTemperature)
    {
        float band = Mathf.Clamp(baselineTemperature - MinimumTemperature, 0f, BandCount - 1);
        int lower = Mathf.FloorToInt(band);
        return Mathf.Lerp(Coverage[lower], Coverage[Mathf.Min(lower + 1, BandCount - 1)], band - lower);
    }
}

/// <summary>纯积雪模拟；基础气候叠加季节和当前天气，冻结温度以下降水积雪，零度以上按温差融化。</summary>
public static class SnowCoverSimulation
{
    /// <summary>只推进本次真实经历的天气时段，不根据最终天气补写整个离线区间。</summary>
    public static void Advance(SnowCoverState state, SnowCoverConfig config, float temperatureOffset, float precipitation, float seconds)
    {
        if (state.Coverage == null || state.Coverage.Length != SnowCoverState.BandCount)
            throw new InvalidOperationException("积雪数据格式不兼容。");
        for (int i = 0; i < state.Coverage.Length; i++)
        {
            float temperature = i + SnowCoverState.MinimumTemperature + temperatureOffset;
            float change = temperature <= config.FreezingTemperature
                ? precipitation * config.AccumulationPerSecond
                : -(temperature - config.FreezingTemperature) * config.MeltPerDegreeSecond;
            state.Coverage[i] = Mathf.Clamp01(state.Coverage[i] + change * seconds);
        }
    }
}
