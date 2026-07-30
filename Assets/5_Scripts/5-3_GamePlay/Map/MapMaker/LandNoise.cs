using Sirenix.OdinInspector;
using UnityEngine;

[System.Serializable]
public class LandNoise : BaseNoise
{
    #region 地形噪声参数
    [Title("分形细节")]
    [LabelText("八度数量")]
    [PropertyTooltip("噪声叠加次数。越大细节越多，同时生成成本越高。")]
    [Range(1, 8)]
    public int octaves = 4;

    [LabelText("频率增长")]
    [PropertyTooltip("每个八度的频率倍率。数值越大，高频细节增长越快。")]
    [Range(1f, 4f)]
    public float lacunarity = 2f;

    [LabelText("振幅保留")]
    [PropertyTooltip("每个八度保留的振幅比例。越小，高频细节越弱。")]
    [Range(0f, 1f)]
    public float persistence = 0.5f;

    // 旧字段未进入采样公式，隐藏但保留序列化数据，避免破坏已有 Prefab。
    [HideInInspector]
    public float outputScale = 1f;

    [LabelText("噪声域偏移")]
    [PropertyTooltip("额外移动当前通道的噪声域，用于避免多个通道图案重合。")]
    public Vector2 coordOffset = Vector2.zero;
    #endregion

    /// <summary>
    /// 生成地形高度噪声值，返回 [0,1]
    /// </summary>
    public override float Sample(float x, float y, int seed)
    {
        seed = seed % 10000; // 保证 seed 在 0~9999

        // 基础位置计算（结合种子偏移），确保不同 seed 有不同噪声域
        float safeCoordScale = FiniteOrDefault(coordScale, 10f);
        float safeFrequency = FiniteOrDefault(frequency, 0.1f);
        float offsetX = FiniteOrDefault(coordOffset.x, 0f);
        float offsetY = FiniteOrDefault(coordOffset.y, 0f);
        float scaledX = x * safeCoordScale;
        float scaledY = y * safeCoordScale;

        float baseX = scaledX * safeFrequency + seed * 0.123f + offsetX;
        float baseY = scaledY * safeFrequency + seed * 0.321f + offsetY;

        // 分形 Perlin：叠加多个八度并做归一化到 [0,1]
        int safeOctaves = Mathf.Clamp(octaves, 1, 8);
        float safeLacunarity = Mathf.Max(1f, FiniteOrDefault(lacunarity, 2f));
        float safePersistence = Mathf.Clamp01(FiniteOrDefault(persistence, 0.5f));

        float sum = 0f;
        float amplitude = 1f;
        float freq = 1f;
        float maxSum = 0f;

        for (int i = 0; i < safeOctaves; i++)
        {
            float n = Mathf.PerlinNoise(baseX * freq, baseY * freq); // [0,1]
            sum += n * amplitude;
            maxSum += amplitude;

            amplitude *= safePersistence;
            freq *= safeLacunarity;
        }

        float normalized = maxSum > 0f ? (sum / maxSum) : 0.5f;
        return Clamp01Finite(normalized);
    }
}
