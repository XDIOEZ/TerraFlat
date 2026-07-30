using Sirenix.OdinInspector;
using UnityEngine;

[System.Serializable]
public class PerlinNoise : BaseNoise
{
    #region 分形细节
    [Title("分形细节")]
    [LabelText("八度数量")]
    [PropertyTooltip("噪声叠加次数。越大细节越多，同时生成成本越高。")]
    [Range(1, 8)]
    public int octaves = 4;

    [LabelText("频率增长")]
    [PropertyTooltip("每个八度的频率倍率。数值越大，高频细节增长越快。")]
    [Range(1f, 4f)]
    public float lacunarity = 2.0f;

    [LabelText("振幅保留")]
    [PropertyTooltip("每个八度保留的振幅比例。越小，高频细节越弱。")]
    [Range(0f, 1f)]
    public float persistence = 0.5f;
    #endregion

    /// <summary>
    /// 实现Perlin噪声采样，支持多octave叠加
    /// </summary>
    public override float Sample(float x, float y, int seed)
    {
        seed = seed % 10000; // 保证 seed 在 0~9999

        int safeOctaves = Mathf.Clamp(octaves, 1, 8);
        float safeLacunarity = Mathf.Max(1f, FiniteOrDefault(lacunarity, 2f));
        float safePersistence = Mathf.Clamp01(FiniteOrDefault(persistence, 0.5f));
        float safeFrequency = FiniteOrDefault(frequency, 0.1f);
        float safeCoordScale = FiniteOrDefault(coordScale, 10f);

        float total = 0f;
        float amplitude = 1f;
        float octaveFrequency = safeFrequency;
        float maxValue = 0f;
        float scaledX = x * safeCoordScale;
        float scaledY = y * safeCoordScale;

        // 叠加多个octave增强细节
        for (int i = 0; i < safeOctaves; i++)
        {
            // 结合种子偏移和随机种子，实现不同种子的噪声变化
            float sampleX = scaledX * octaveFrequency + seed * 0.1f;
            float sampleY = scaledY * octaveFrequency + seed * 0.1f;

            // Perlin噪声原生返回值范围是[0,1]
            float noiseValue = Mathf.PerlinNoise(sampleX, sampleY);
            total += noiseValue * amplitude;

            maxValue += amplitude;
            amplitude *= safePersistence;
            octaveFrequency *= safeLacunarity;
        }

        // 归一化结果到[0,1]
        float normalized = maxValue > 0f ? total / maxValue : 0.5f;
        return Clamp01Finite(normalized);
    }
}