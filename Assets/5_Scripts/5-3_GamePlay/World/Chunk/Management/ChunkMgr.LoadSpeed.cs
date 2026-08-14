using System;
using UnityEngine;

public partial class ChunkMgr
{
    public const float MinChunkLoadSpeedMultiplier = 0.1f;

    [Header("Chunk加载速度")]
    [SerializeField, Min(MinChunkLoadSpeedMultiplier)]
    private float chunkLoadSpeedMultiplier = 1f;

    [SerializeField]
    private bool unlimitedChunkLoadSpeed;

    /// <summary>
    /// 当前区块加载吞吐倍率。无限制时返回正无穷，影响旧管线分帧预算和后台并发上限；
    /// 新 WorldModel 的主线程表现队列仍保留独立的逐帧安全上限。
    /// </summary>
    public float ChunkLoadSpeedMultiplier => unlimitedChunkLoadSpeed
        ? float.PositiveInfinity
        : ResolveFiniteChunkLoadSpeedMultiplier();

    /// <summary>是否取消旧加载管线与后台生成的人工限制，不取消 Unity 主线程表现安全上限。</summary>
    public bool IsChunkLoadSpeedUnlimited => unlimitedChunkLoadSpeed;

    public int EffectiveMaxChunkLoadPerFrame => Mathf.Min(
        ScalePositiveBudget(maxChunkLoadPerFrame), 4);
    public int EffectiveMaxConcurrentChunkLoads => Mathf.Min(
        ScalePositiveBudget(maxConcurrentChunkLoads), SafeBackgroundGenerationCeiling);
    /// <summary>新 WorldModel 调度器实际使用的后台生成并发上限。</summary>
    public int EffectiveBackgroundGenerationConcurrency => Mathf.Min(
        ScalePositiveBudget(WorldStreamingPreferences.ResolveBaseGenerationConcurrency(
            backgroundGenerationConcurrency)),
        SafeBackgroundGenerationCeiling);

    /// <summary>为 Unity 主线程和渲染线程预留 CPU 后的后台生成安全上限。</summary>
    public static int SafeBackgroundGenerationCeiling
    {
        get
        {
            int logicalProcessors = Mathf.Max(1, SystemInfo.processorCount);
            return Mathf.Clamp(logicalProcessors / 3, 1, 4);
        }
    }

    /// <summary>
    /// GM、调试工具及其他上游系统统一使用的运行时调速入口。
    /// </summary>
    public bool TrySetChunkLoadSpeedMultiplier(float requestedMultiplier, out float appliedMultiplier)
    {
        if (float.IsNaN(requestedMultiplier) || float.IsNegativeInfinity(requestedMultiplier))
        {
            appliedMultiplier = ChunkLoadSpeedMultiplier;
            return false;
        }

        if (float.IsPositiveInfinity(requestedMultiplier))
        {
            unlimitedChunkLoadSpeed = true;
            appliedMultiplier = float.PositiveInfinity;
            ApplyChunkLoadSpeedToRuntimeScheduler();
            return true;
        }

        unlimitedChunkLoadSpeed = false;
        appliedMultiplier = Mathf.Max(requestedMultiplier, MinChunkLoadSpeedMultiplier);
        chunkLoadSpeedMultiplier = appliedMultiplier;
        ApplyChunkLoadSpeedToRuntimeScheduler();
        return true;
    }

    /// <summary>把 GM 调速状态立即同步给正在运行的新区块生成调度器。</summary>
    private void ApplyChunkLoadSpeedToRuntimeScheduler()
    {
        runtimeChunkManager?.SetMaxGenerationConcurrency(
            EffectiveBackgroundGenerationConcurrency);
    }

    /// <summary>
    /// 不触发自动单例创建，供 Map 的热路径读取当前倍率。
    /// </summary>
    public static float CurrentChunkLoadSpeedMultiplier =>
        instance != null ? instance.ChunkLoadSpeedMultiplier : 1f;

    /// <summary>不触发自动单例创建，供热路径判断是否取消分帧限制。</summary>
    public static bool CurrentChunkLoadSpeedUnlimited =>
        instance != null && instance.IsChunkLoadSpeedUnlimited;

    /// <summary>返回已存在的区块管理器，不在菜单场景自动创建空对象。</summary>
    public static ChunkMgr ExistingInstance => instance;

    /// <summary>缩放整数工作量；无限制仍保留四倍分帧安全上限，避免堵死主线程。</summary>
    public static int ScaleCurrentChunkLoadItemBudget(int baseBudget, int minimum)
    {
        minimum = Mathf.Max(1, minimum);
        if (CurrentChunkLoadSpeedUnlimited)
            return Mathf.Max(minimum, SaturatingScale(baseBudget, 4f));

        float scaled = Mathf.Max(minimum, baseBudget) * CurrentChunkLoadSpeedMultiplier;
        if (float.IsInfinity(scaled) || scaled >= int.MaxValue)
            return int.MaxValue;

        return Mathf.Max(minimum, Mathf.RoundToInt(scaled));
    }

    /// <summary>缩放毫秒预算；无限制仍最多使用两倍预算，避免单帧长时间不让出主线程。</summary>
    public static float ScaleCurrentChunkLoadFrameBudget(float baseBudget, float minimum)
    {
        if (CurrentChunkLoadSpeedUnlimited)
            return Mathf.Max(minimum, baseBudget * 2f);

        return Mathf.Max(minimum, baseBudget * CurrentChunkLoadSpeedMultiplier);
    }

    private int ScalePositiveBudget(int baseBudget)
    {
        if (unlimitedChunkLoadSpeed)
            return int.MaxValue;

        float scaled = Mathf.Max(1, baseBudget) * ResolveFiniteChunkLoadSpeedMultiplier();
        if (float.IsInfinity(scaled) || scaled >= int.MaxValue)
            return int.MaxValue;

        return Mathf.Max(1, Mathf.CeilToInt(scaled));
    }

    private float ResolveFiniteChunkLoadSpeedMultiplier()
    {
        return float.IsNaN(chunkLoadSpeedMultiplier) || float.IsInfinity(chunkLoadSpeedMultiplier)
            ? 1f
            : Mathf.Max(MinChunkLoadSpeedMultiplier, chunkLoadSpeedMultiplier);
    }

    /// <summary>安全缩放整数，防止倍率溢出。</summary>
    private static int SaturatingScale(int value, float multiplier)
    {
        double scaled = Math.Max(1, value) * (double)multiplier;
        return scaled >= int.MaxValue ? int.MaxValue : Mathf.Max(1, (int)Math.Ceiling(scaled));
    }
}
