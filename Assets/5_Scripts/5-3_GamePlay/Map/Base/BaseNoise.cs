using Sirenix.OdinInspector;
using UnityEngine;

[System.Serializable]
public abstract class BaseNoise
{
    #region 通道基础配置
    [LabelText("环境通道")]
    [PropertyTooltip("该配置写入的环境通道；同类型配置有多个时会取平均值。")]
    public NoiseType noiseType = NoiseType.Land;

    [LabelText("通道坐标倍率")]
    [PropertyTooltip("只影响当前通道的坐标倍率。最终采样频率 = 世界坐标缩放 × 通道坐标倍率 × 基础采样频率。")]
    [MinValue(0f)]
    public float coordScale = 10f;

    [LabelText("基础采样频率")]
    [PropertyTooltip("当前通道的基础频率。越小地貌越舒展，越大细节越密集。")]
    [MinValue(0f)]
    public float frequency = 0.1f;

    [ShowInInspector, ReadOnly, LabelText("通道有效频率")]
    [PropertyTooltip("不含 PlanetData 世界坐标缩放的通道内频率，仅用于配置检查。")]
    private float EffectiveChannelFrequency =>
        IsFinite(coordScale) && IsFinite(frequency) ? coordScale * frequency : 0f;
    #endregion

    /// <summary>
    /// 在世界坐标采样噪声，返回 [0,1] 值
    /// </summary>
    public abstract float Sample(float x, float y, int seed);

    #region 参数保护
    protected static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    protected static float FiniteOrDefault(float value, float fallback)
    {
        return IsFinite(value) ? value : fallback;
    }

    protected static float Clamp01Finite(float value, float fallback = 0.5f)
    {
        return IsFinite(value) ? Mathf.Clamp01(value) : Mathf.Clamp01(fallback);
    }
    #endregion
}

/// <summary>
/// 噪声类型枚举（用于噪声配置与采样）
/// </summary>
[System.Serializable]
public enum NoiseType
{
    Land,
    Humidity,
    Precipitation,
    Temperature,
    River,
    Solidity
}
