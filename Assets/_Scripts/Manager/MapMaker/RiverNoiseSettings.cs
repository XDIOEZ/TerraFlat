using UnityEngine;

[CreateAssetMenu(fileName = "RiverNoiseSettings", menuName = "Configs/River Noise Settings")]
public class RiverNoiseSettings : ScriptableObject
{
    [Header("基础参数")]
    public float noiseFrequency = 0.01f;
    public float threshold = 0.5f;
    public float riverWidth = 0.1f;
    public int seed = 0;

    [Header("高级噪声参数")]
    public int octaves = 4;
    [Range(0f, 1f)] public float persistence = 0.5f;
    public float lacunarity = 2f;
    public float warpAmplitude = 20f;
    public float warpFrequency = 0.005f;

    /// <summary>
    /// 分形布朗噪声 (FBM)
    /// </summary>
    public float Noise2D(float x, float y, float frequency, int seedOffset = 0)
    {
        float noise = 0f;
        float amplitude = 1f;
        float maxAmplitude = 0f;
        float freq = frequency;

        for (int i = 0; i < octaves; i++)
        {
            float sampleX = x * freq + seed + seedOffset;
            float sampleY = y * freq + seed + seedOffset;
            noise += Mathf.PerlinNoise(sampleX, sampleY) * amplitude;

            maxAmplitude += amplitude;
            amplitude *= persistence;
            freq *= lacunarity;
        }

        return noise / maxAmplitude;
    }

    /// <summary>
    /// 计算河流掩码，返回 [0,1]
    /// </summary>
    public float GetRiverMask(float worldX, float worldY)
    {
        // 域扭曲
        float warpX = Noise2D(worldX, worldY, warpFrequency, 0) * warpAmplitude;
        float warpY = Noise2D(worldX + 1000f, worldY + 1000f, warpFrequency, 1) * warpAmplitude;

        // 主噪声
        float n = Noise2D(worldX + warpX, worldY + warpY, noiseFrequency, 2);

        float d = Mathf.Abs(n - threshold);
        float mask = d / riverWidth;
        return Mathf.Clamp01(mask);
    }
}
