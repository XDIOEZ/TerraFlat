using UnityEngine;

[System.Serializable]
public class LandNoise : BaseNoise
{
    #region 地形噪声参数
    [Header("额外噪声参数")]
    [Tooltip("噪声叠加的八度数（越大细节越多，但更慢）")]
    [Range(1, 8)]
    public int octaves = 4;

    [Tooltip("每个八度的频率倍率（>1，越大细节增长越快）")]
    [Range(1f, 4f)]
    public float lacunarity = 2f;

    [Tooltip("每个八度的振幅衰减（0~1，越小高频细节越弱）")]
    [Range(0f, 1f)]
    public float persistence = 0.5f;

    [Tooltip("输出强度：在归一化后再缩放（仍会 Clamp 到 0~1）")]
    [Range(0.1f, 3f)]
    public float outputScale = 1f;

    [Tooltip("额外坐标偏移：用于微调噪声域位置")]
    public Vector2 coordOffset = Vector2.zero;
    #endregion

    /// <summary>
    /// 生成地形高度噪声值，返回 [0,1]
    /// </summary>
    public override float Sample(float x, float y, int seed)
    {
        seed = seed % 10000; // 保证 seed 在 0~9999

        // 基础位置计算（结合种子偏移），确保不同 seed 有不同噪声域
        float scaledX = x * coordScale;
        float scaledY = y * coordScale;

        float baseX = scaledX * frequency + (seed) * 0.123f + coordOffset.x;
        float baseY = scaledY * frequency + (seed) * 0.321f + coordOffset.y;

        // 分形 Perlin：叠加多个八度并做归一化到 [0,1]
        int safeOctaves = Mathf.Clamp(octaves, 1, 8);
        float safeLacunarity = Mathf.Max(1f, lacunarity);
        float safePersistence = Mathf.Clamp01(persistence);

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
        return Mathf.Clamp01(normalized);
    }
}
