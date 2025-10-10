using UltEvents;
using UnityEngine;

[CreateAssetMenu(fileName = "TriFractalRiverNoise", menuName = "Noise/TriFractalRiverNoise")]
public class TriFractalRiverNoise : BaseNoise
{
    [Header("分形参数")]
    [Tooltip("迭代次数（越高细节越多，但性能更差）")]
    [Range(1, 12)]
    public int octaves = 5;

    [Tooltip("每次 octave 频率倍增")]
    public float lacunarity = 2f;

    [Tooltip("每次 octave 幅度衰减（常在 0.3 - 0.8）")]
    [Range(0.0f, 1.0f)]
    public float gain = 0.5f;

    [Header("河流条带控制")]
    [Tooltip("基础阈值，分形值靠近该值的区域会形成河带（0..1）")]
    [Range(0f, 1f)]
    public float threshold = 0.45f;

    [Tooltip("河道宽度（范围内的分形值会渐变为河流），越小越细窄")]
    [Range(0.01f, 0.5f)]
    public float riverWidth = 0.08f;

    [Tooltip("是否反转：当为 true 时，分形值远离阈值的地方为河道")]
    public bool invertBand = false;

    [Header("与陆地噪声交互（可选）")]
    [Tooltip("可选：传入你的陆地噪声 ScriptableObject，用于根据陆地噪声调节河流生成（不填则忽略）")]
    public BaseNoise landNoise = null;

    [Tooltip("陆地噪声影响强度（>0 增加阈值，<0 减少阈值）")]
    [Range(-1f, 1f)]
    public float landInfluence = 0.25f;

    [Tooltip("当使用 landNoise 时，将用此偏移采样陆地噪声，避免与本噪声完全重合")]
    public Vector2 landNoiseOffset = new Vector2(1000f, 1000f);

    /// <summary>
    /// 在世界坐标采样噪声，返回 [0,1] 值
    /// 生成逻辑：
    /// 1. 做 ridged / triangle 风格的 fBm（每 octave 取 Perlin -> ridge transform）
    /// 2. 归一化 fBm 到 [0,1]
    /// 3. 用 smoothstep 在 threshold ± riverWidth/2 区间构造条带（并支持反转）
    /// 4. 若提供 landNoise，则用 landNoise 调整 threshold，从而在不同地形上改变河流密度/数量
    /// </summary>
    public override float Sample(float x, float y, int seed)
    {
        seed = seed % 10000; // 保证 seed 在 0~9999

        // 把 seed 转成偏移，避免直接把 seed 加到频率上导致重复模式（更自然）
        float seedX = seed * 100.13f + seedOffset * 10.7f;
        float seedY = seed * 73.21f - seedOffset * 7.33f;

        // 1) tri / ridged fBm
        float amplitude = 1f;
        float frequencyLocal = frequency;
        float sum = 0f;
        float maxPossible = 0f; // 用于归一化
        for (int i = 0; i < Mathf.Max(1, octaves); i++)
        {
            float sx = (x + seedX) * frequencyLocal;
            float sy = (y + seedY) * frequencyLocal;
            float n = Mathf.PerlinNoise(sx, sy); // [0,1]

            // ridge transform (把噪声变成尖脊/三角形状)
            // 先把 [0,1] 映射为 [-1,1]，再取绝对值并反转，使得“沟/脊”更明显
            float ridge = 1f - Mathf.Abs(2f * n - 1f); // [0,1], 中央形成峰
            // 可选额外处理：抬升小值以避免完全为0（这里不做）
            sum += ridge * amplitude;

            maxPossible += amplitude;
            amplitude *= gain;
            frequencyLocal *= lacunarity;
        }

        // 2) 归一化 fBm 到 [0,1]
        float fbm = (maxPossible > 0f) ? (sum / maxPossible) : 0f;
        fbm = Mathf.Clamp01(fbm);

        // 3) 用 landNoise 调整阈值（可选）
        float appliedThreshold = threshold;
        if (landNoise != null)
        {
            // 采样陆地噪声（用自己的 seed + offset）
            float landSample = landNoise.Sample(x + landNoiseOffset.x, y + landNoiseOffset.y, seed);
            // landSample 在 [0,1]，我们根据它来移动阈值：例如地形越高/越陆地，可能更多或更少的河流
            // 公式：appliedThreshold = threshold + (1 - landSample) * landInfluence
            // 你可以根据需要反转 landSample 的影响
            appliedThreshold = threshold + (1f - landSample) * landInfluence;
            appliedThreshold = Mathf.Clamp01(appliedThreshold);
        }

        // 4) 构造条带：fbm 距离阈值越近，结果越高
        float halfWidth = Mathf.Max(0.0001f, riverWidth * 0.5f);
        float lower = appliedThreshold - halfWidth;
        float upper = appliedThreshold + halfWidth;

        // 使用 smoothstep 实现平滑带状（Unity 没有内置 smoothstep，手写）
        float t;
        if (!invertBand)
        {
            // 高度在 [lower,upper] 时为 1，区间外平滑过渡到 0
            t = SmoothStep(lower, appliedThreshold, fbm) * (1f - SmoothStep(appliedThreshold, upper, fbm));
            // 上面表达式会产生峰值 ~1 在 appliedThreshold 处，两侧衰减；但为了更简单和稳定我们改用距离法：
            float dist = Mathf.Abs(fbm - appliedThreshold);
            // dist 0 -> 0, 半宽 -> 1 -> 我们想要反过来：当 dist small -> 1
            float normalizedDist = Mathf.InverseLerp(halfWidth, 0f, dist); // halfWidth->0 maps to 0->1
            t = Mathf.Clamp01(normalizedDist);
        }
        else
        {
            // 反转：远离阈值处为河道
            float dist = Mathf.Abs(fbm - appliedThreshold);
            float normalized = Mathf.InverseLerp(0f, halfWidth, dist); // 0->halfWidth maps to 0->1
            t = Mathf.Clamp01(normalized);
        }

        // 最终再 clamp 并返回
        return Mathf.Clamp01(t);
    }

    /// <summary>
    /// 与 GLSL/Shader 的 smoothstep 同样行为：
    /// smoothstep(edge0, edge1, x) 平滑插值，x<=edge0 返回0，x>=edge1 返回1，中间平滑
    /// </summary>
    private float SmoothStep(float edge0, float edge1, float x)
    {
        if (edge0 >= edge1) return (x < edge0) ? 0f : 1f;
        float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
        // Hermite interpolation
        return t * t * (3f - 2f * t);
    }
}
