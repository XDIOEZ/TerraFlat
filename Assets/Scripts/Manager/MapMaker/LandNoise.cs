using UnityEngine;

[CreateAssetMenu(fileName = "LandNoise", menuName = "Noise/LandNoise")]
public class LandNoise : BaseNoise
{
    [Header("河流主河道设置")]
    [Tooltip("主河道强度，值越高河道越明显")]
    [Range(0.1f, 5f)] public float mainRiverStrength = 2f;

    [Tooltip("主河道平滑度，值越高河道越连续流畅")]
    [Range(1, 8)] public int mainSmoothness = 3;

    [Header("支流设置")]
    [Tooltip("是否启用支流")]
    public bool enableTributaries = true;

    [Tooltip("支流强度，值越高支流越明显")]
    [Range(0.1f, 2f)] public float tributaryStrength = 0.8f;

    [Tooltip("支流数量，值越高支流越多")]
    [Range(1, 4)] public int tributaryCount = 2;

    [Header("河道宽度控制")]
    [Tooltip("基础河道宽度")]
    [Range(0.5f, 5f)] public float baseWidth = 1.5f;

    [Tooltip("宽度变化程度，值越高宽度变化越明显")]
    [Range(0f, 1f)] public float widthVariation = 0.3f;

    /// <summary>
    /// 生成适合河流的噪声值，低值区域表示河流
    /// </summary>
    public override float Sample(float x, float y, int seed)
    {
        seed = seed % 10000; // 保证 seed 在 0~9999

        // 基础位置计算（结合种子偏移）
        float baseX = x * frequency + (seed + seedOffset) * 0.123f;
        float baseY = y * frequency + (seed + seedOffset) * 0.321f;

        // 生成主河道噪声（使用多个八度叠加实现流畅效果）
        float mainRiverNoise = 0;
        float mainAmplitude = 1;
        float mainFrequency = 1;

        for (int i = 0; i < mainSmoothness; i++)
        {
            float sampleX = baseX * mainFrequency;
            float sampleY = baseY * mainFrequency;

            // 使用Perlin噪声生成基础河道形状
            mainRiverNoise += Mathf.PerlinNoise(sampleX, sampleY) * mainAmplitude;

            mainAmplitude *= 0.5f;
            mainFrequency *= 2f;
        }

        // 归一化主河道噪声
        mainRiverNoise = mainRiverNoise * 0.666f; // 简单归一化多个八度的总和
        mainRiverNoise = 1 - (mainRiverNoise * mainRiverStrength); // 反转并应用强度

        // 生成支流噪声
        float tributaryNoise = 0;
        if (enableTributaries)
        {
            for (int i = 0; i < tributaryCount; i++)
            {
                // 为每个支流使用不同的偏移，创建不同的路径
                float offsetX = i * 123.45f;
                float offsetY = i * 543.21f;

                float tribX = baseX * (2 + i) + offsetX;
                float tribY = baseY * (2 + i) + offsetY;

                tributaryNoise += Mathf.PerlinNoise(tribX, tribY) * tributaryStrength;
            }

            // 归一化支流噪声
            tributaryNoise /= tributaryCount;
            tributaryNoise = 1 - tributaryNoise; // 反转使低值表示河流
        }

        // 组合主河道和支流，取最低值（最可能成为河流的区域）
        float combinedNoise = Mathf.Min(mainRiverNoise, 1 - tributaryNoise);

        // 应用宽度变化
        float widthNoise = Mathf.PerlinNoise(baseX * 3, baseY * 3) * widthVariation;
        float widthFactor = baseWidth - widthNoise;

        // 调整噪声值，使河道区域呈现更低的值
        float riverNoise = combinedNoise * (1 + widthFactor);

        // 确保结果在[0,1]范围内
        return Mathf.Clamp01(riverNoise);
    }
}
