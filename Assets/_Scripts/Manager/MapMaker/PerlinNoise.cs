using UnityEngine;

// 可在Unity编辑器中创建该噪声资产
[CreateAssetMenu(fileName = "PerlinNoise", menuName = "Noise/Perlin Noise")]
public class PerlinNoise : BaseNoise
{
    [Tooltip("噪声 octave 数量，影响细节丰富度")]
    public int octaves = 4;

    [Tooltip(" octave 间的频率倍数")]
    public float lacunarity = 2.0f;

    [Tooltip("octave 间的振幅衰减系数")]
    public float persistence = 0.5f;

    /// <summary>
    /// 实现Perlin噪声采样，支持多octave叠加
    /// </summary>
    public override float Sample(float x, float y, int seed)
    {
        seed = seed % 10000; // 保证 seed 在 0~9999

        float total = 0;
        float amplitude = 1;
        float frequency = this.frequency;
        float maxValue = 0; // 用于归一化结果到[0,1]

        // 叠加多个octave增强细节
        for (int i = 0; i < octaves; i++)
        {
            // 结合种子偏移和随机种子，实现不同种子的噪声变化
            float sampleX = x * frequency + (seed + seedOffset) * 0.1f;
            float sampleY = y * frequency + (seed + seedOffset) * 0.1f;

            // Perlin噪声原生返回值范围是[0,1]
            float noiseValue = Mathf.PerlinNoise(sampleX, sampleY);
            total += noiseValue * amplitude;

            maxValue += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }

        // 归一化结果到[0,1]
        return total / maxValue;
    }
}