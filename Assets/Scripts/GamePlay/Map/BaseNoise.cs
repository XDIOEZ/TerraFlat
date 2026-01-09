using UnityEngine;

[System.Serializable]
public abstract class BaseNoise
{
    [Header("基础地形噪声设置")]
    [Tooltip("该噪声实例对应的类型（用于地图生成器选择/采样）")]
    public NoiseType noiseType = NoiseType.Land;

    [Tooltip("噪声坐标缩放：在 frequency 之前先缩放输入坐标（>1 更碎更密，<1 更平缓更大块）")]
    public float coordScale = 10f;

    [Tooltip("噪声采样频率")]
    public float frequency = 0.1f;

    /// <summary>
    /// 在世界坐标采样噪声，返回 [0,1] 值
    /// </summary>
    public abstract float Sample(float x, float y, int seed);
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
