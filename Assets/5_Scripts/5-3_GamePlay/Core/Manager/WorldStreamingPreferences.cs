// AI-Context: 区块流送性能模式的全局 PlayerPrefs；只控制客户端生成吞吐，不写入世界存档。
using System;
using UnityEngine;

/// <summary>
/// 区块流送的玩家性能偏好。平滑模式只使用一个后台生成线程，并继续通过协程逐帧绘制；
/// 高吞吐模式使用 CPU 安全上限内的多个线程；自动模式采用项目默认值。
/// </summary>
public enum WorldStreamingPerformanceMode
{
    Automatic = 0,
    Smooth = 1,
    Throughput = 2
}

/// <summary>保存并广播区块流送性能偏好。</summary>
public static class WorldStreamingPreferences
{
    private const string ModeKey = "FlatWorld.WorldStreaming.PerformanceMode.v1";

    public static event Action Changed;

    public static WorldStreamingPerformanceMode Mode
    {
        get
        {
            int value = PlayerPrefs.GetInt(ModeKey,
                (int)WorldStreamingPerformanceMode.Automatic);
            return Enum.IsDefined(typeof(WorldStreamingPerformanceMode), value)
                ? (WorldStreamingPerformanceMode)value
                : WorldStreamingPerformanceMode.Automatic;
        }
    }

    /// <summary>保存模式并让正在运行的 ChunkMgr 立即更新调度器。</summary>
    public static void SetMode(WorldStreamingPerformanceMode mode)
    {
        if (!Enum.IsDefined(typeof(WorldStreamingPerformanceMode), mode))
            mode = WorldStreamingPerformanceMode.Automatic;
        if (Mode == mode)
            return;

        PlayerPrefs.SetInt(ModeKey, (int)mode);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    /// <summary>把玩家模式转换为实际后台生成基础并发。</summary>
    public static int ResolveBaseGenerationConcurrency(int automaticValue)
    {
        return Mode switch
        {
            WorldStreamingPerformanceMode.Smooth => 1,
            WorldStreamingPerformanceMode.Throughput =>
                ChunkMgr.SafeBackgroundGenerationCeiling,
            _ => Mathf.Clamp(automaticValue, 1,
                ChunkMgr.SafeBackgroundGenerationCeiling)
        };
    }
}
