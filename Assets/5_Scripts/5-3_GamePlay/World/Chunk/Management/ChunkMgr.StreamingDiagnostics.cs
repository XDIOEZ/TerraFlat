using System.Collections.Generic;
using System.Text;
using FlatWorld.WorldModel;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

public partial class ChunkMgr
{
    #region 流送诊断

    private long streamingAdvanceCount;
    private long lastStreamingAdvanceTimestamp;
    private int lastStreamingAdvanceFrame = -1;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 每 30 秒最多输出 12 条慢生成日志，保证后续传送仍能采样。
    private const int MaxSlowChunkLogsPerWindow = 12;
    private const float SlowChunkLogWindowSeconds = 30f;
    private int slowChunkLogCount;
    private float slowChunkLogWindowStart;

    /// <summary>后台计时完成后在主线程输出慢区块分段耗时。</summary>
    private void LogSlowChunkGeneration(ChunkGenerationDiagnostic diagnostic)
    {
        float now = Time.realtimeSinceStartup;
        if (now - slowChunkLogWindowStart >= SlowChunkLogWindowSeconds)
        {
            slowChunkLogWindowStart = now;
            slowChunkLogCount = 0;
        }
        if (slowChunkLogCount >= MaxSlowChunkLogsPerWindow)
            return;
        slowChunkLogCount++;
        var stages = new StringBuilder();
        foreach (KeyValuePair<string, double> stage in diagnostic.GenerationStagesMs)
        {
            if (stages.Length > 0) stages.Append(", ");
            stages.Append(stage.Key).Append('=').Append(stage.Value.ToString("F0")).Append("ms");
        }
        foreach (KeyValuePair<string, long> count in diagnostic.GenerationCounts)
            stages.Append(", ").Append(count.Key).Append('=').Append(count.Value);
        Debug.Log($"[ChunkGenSlow] {diagnostic.Dimension} " +
                  $"({diagnostic.Origin[0]},{diagnostic.Origin[1]}) " +
                  $"result={diagnostic.Stage} queue={diagnostic.GenerationQueueMs:F0}ms " +
                  $"work={diagnostic.GenerationWorkMs:F0}ms | {stages}", this);
    }
#endif

    /// <summary>只记录主线程实际推进，不从任何诊断读取入口提交区块或重绑宿主。</summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void RecordStreamingAdvance()
    {
        streamingAdvanceCount++;
        lastStreamingAdvanceTimestamp = Stopwatch.GetTimestamp();
        lastStreamingAdvanceFrame = Time.frameCount;
    }

    /// <summary>按需读取驱动心跳、真实预算、协程与等待表现的最长队龄。</summary>
    public object CaptureStreamingDriverDiagnostics()
    {
        // 热重载可清掉非序列化缓存；只查现有组件，不能为了诊断补组件或恢复 owner。
        WorldRuntimeHost host = GetComponent<WorldRuntimeHost>();
        var pending = new List<object>();
        double longestWait = 0d;
        foreach (var pair in activeRuntimeBindings)
        {
            RuntimeChunkBinding binding = pair.Value;
            if (!binding.PresentationQueued && !binding.PresentationInProgress && binding.FailedRestoreChunk == null)
                continue;
            double wait = ChunkStreamingDiagnostics.ElapsedMs(binding.DiagnosticQueuedAt);
            longestWait = System.Math.Max(longestWait, wait);
            if (pending.Count >= 16) continue;
            pending.Add(new
            {
                dimension = pair.Key.DimensionId,
                origin = new[] { pair.Key.ChunkOrigin.X, pair.Key.ChunkOrigin.Y },
                queued = binding.PresentationQueued, binding = binding.PresentationInProgress,
                failedRestore = binding.FailedRestoreChunk != null,
                renderer = binding.View != null ? binding.View.LastBindingRenderer : null,
                ageWallMs = wait,
                pauseAffected = WorldRuntime != null &&
                    binding.DiagnosticPauseRevision != WorldRuntime.StreamingDiagnostics.PauseRevision
            });
        }
        return new
        {
            frame = Time.frameCount, timeScale = Time.timeScale,
            windowTargetCount = runtimeWindowTargets.Count, presentationBindingCount = activeRuntimeBindings.Count,
            managerEnabled = isActiveAndEnabled, shuttingDown = IsWorldRuntimeShuttingDown,
            hostExists = host != null, hostEnabled = host != null && host.isActiveAndEnabled,
            hostHasOwner = host != null && host.HasOwner, hostBoundToManager = host != null && host.IsBoundTo(this),
            hostUpdateCount = host != null ? host.UpdateCount : 0,
            hostLastFrame = host != null ? host.LastUpdateFrame : -1,
            advanceCount = streamingAdvanceCount, lastAdvanceFrame = lastStreamingAdvanceFrame,
            advanceObserved = lastStreamingAdvanceTimestamp > 0,
            millisecondsSinceAdvance = ChunkStreamingDiagnostics.ElapsedMs(lastStreamingAdvanceTimestamp),
            framesSinceAdvance = lastStreamingAdvanceFrame < 0 ? -1 : Time.frameCount - lastStreamingAdvanceFrame,
            commitCountLimit = RuntimeChunkCommitBudget,
            presentationStartCountLimit = RuntimeChunkPresentationStartBudget,
            presentationStartMsLimit = maxChunkPresentationStartMillisecondsPerFrame,
            continuationCountLimit = RuntimeChunkPresentationContinuationBudget,
            continuationMsLimit = maxChunkPresentationContinuationMillisecondsPerFrame,
            presentationCoroutineRegistered = runtimePresentationCoroutine != null,
            prefetchCoroutineRegistered = runtimePrefetchCoroutine != null,
            prefetchBlockedByVisibleWork = HasUrgentRuntimeChunkWork(),
            prefetchConcurrencyLimit = MaxIdlePrefetchConcurrency,
            oldestPresentationAgeWallMs = longestWait, pendingPresentationSamples = pending
        };
    }

    #endregion
}
