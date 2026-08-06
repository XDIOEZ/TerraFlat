using UnityEngine;

public partial class ChunkMgr
{
    public const float MinChunkLoadSpeedMultiplier = 0.1f;
    public const float MaxChunkLoadSpeedMultiplier = 10f;

    [Header("Chunk加载速度")]
    [SerializeField, Range(MinChunkLoadSpeedMultiplier, MaxChunkLoadSpeedMultiplier)]
    private float chunkLoadSpeedMultiplier = 1f;

    /// <summary>
    /// 当前区块加载吞吐倍率。只影响分帧预算和并发上限，不改变加载距离与生成结果。
    /// </summary>
    public float ChunkLoadSpeedMultiplier => Mathf.Clamp(
        chunkLoadSpeedMultiplier,
        MinChunkLoadSpeedMultiplier,
        MaxChunkLoadSpeedMultiplier);

    public int EffectiveMaxChunkLoadPerFrame => ScalePositiveBudget(maxChunkLoadPerFrame);
    public int EffectiveMaxConcurrentChunkLoads => ScalePositiveBudget(maxConcurrentChunkLoads);

    /// <summary>
    /// GM、调试工具及其他上游系统统一使用的运行时调速入口。
    /// </summary>
    public bool TrySetChunkLoadSpeedMultiplier(float requestedMultiplier, out float appliedMultiplier)
    {
        if (float.IsNaN(requestedMultiplier) || float.IsInfinity(requestedMultiplier))
        {
            appliedMultiplier = ChunkLoadSpeedMultiplier;
            return false;
        }

        appliedMultiplier = Mathf.Clamp(
            requestedMultiplier,
            MinChunkLoadSpeedMultiplier,
            MaxChunkLoadSpeedMultiplier);
        chunkLoadSpeedMultiplier = appliedMultiplier;
        return true;
    }

    /// <summary>
    /// 不触发自动单例创建，供 Map 的热路径读取当前倍率。
    /// </summary>
    public static float CurrentChunkLoadSpeedMultiplier =>
        instance != null ? instance.ChunkLoadSpeedMultiplier : 1f;

    private int ScalePositiveBudget(int baseBudget)
    {
        return Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(1, baseBudget) * ChunkLoadSpeedMultiplier));
    }
}
