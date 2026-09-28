using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Profiling;
using UnityEngine;

public partial class MonsterSpawnerManager
{
    #region 性能边界

    /// <summary>只读取得已存在的生成器，诊断与退出回调不得隐式创建单例。</summary>
    public static MonsterSpawnerManager ExistingInstance => instance;

    // 固定名称标记不构造逐帧字符串；创建与管理 Tick 分开，避免把实例初始化当成稳定态。
    private static readonly ProfilerMarker TickMarker = new("FlatWorld.MonsterSpawner.Tick");
    private static readonly ProfilerMarker CreationMarker = new("FlatWorld.MonsterSpawner.Create");
    private static readonly ProfilerMarker DormancyMarker = new("FlatWorld.MonsterSpawner.Dormancy");

    private void Update()
    {
        if (Time.unscaledTime < _nextEcologyTickTime)
            return;
        using (TickMarker.Auto())
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            bool sampling = _samplingPerformance;
            long bytes = sampling ? GC.GetAllocatedBytesForCurrentThread() : 0;
            long ticks = sampling ? Stopwatch.GetTimestamp() : 0;
            int creationsBefore = _creationSamples.Samples;
            try { TickSpawner(); }
            finally
            {
                if (sampling)
                {
                    long elapsed = Stopwatch.GetTimestamp() - ticks;
                    long allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
                    if (_creationSamples.Samples == creationsBefore)
                        _steadySamples.Add(elapsed, allocated);
                    else
                        _spawningSamples.Add(elapsed, allocated);
                }
            }
#else
            TickSpawner();
#endif
        }
    }

    /// <summary>保留具名生成入口；实际资源与实体初始化单独计量。</summary>
    private bool TrySpawnMonster(SpawnerConfig.SpawnEntry entry, Vector3 position)
    {
        using (CreationMarker.Auto())
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            bool sampling = _samplingPerformance;
            long bytes = sampling ? GC.GetAllocatedBytesForCurrentThread() : 0;
            long ticks = sampling ? Stopwatch.GetTimestamp() : 0;
            try
            {
                bool success = CreateMonster(entry, position);
                if (sampling && success) _sampleSpawnSuccesses++;
                return success;
            }
            finally
            {
                if (sampling)
                    _creationSamples.Add(Stopwatch.GetTimestamp() - ticks,
                        GC.GetAllocatedBytesForCurrentThread() - bytes);
            }
#else
            return CreateMonster(entry, position);
#endif
        }
    }

    #endregion

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    #region 按需只读诊断

    /// <summary>采样期内只累计值类型；不保存逐帧数组、不输出日志。</summary>
    private struct PerformanceBucket
    {
        public int Samples;
        public long TotalTicks, MaxTicks, TotalBytes, MaxBytes;

        public void Add(long ticks, long bytes)
        {
            Samples++;
            TotalTicks += ticks;
            MaxTicks = Math.Max(MaxTicks, ticks);
            TotalBytes += bytes;
            MaxBytes = Math.Max(MaxBytes, bytes);
        }

        public object Report(bool gcSupported) => new
        {
            samples = Samples,
            meanMs = Samples == 0 ? 0 : TotalTicks * 1000d / Stopwatch.Frequency / Samples,
            maxMs = MaxTicks * 1000d / Stopwatch.Frequency,
            totalGcBytes = gcSupported ? (long?)TotalBytes : null,
            meanGcBytes = gcSupported ? (double?)(Samples == 0 ? 0 : (double)TotalBytes / Samples) : null,
            maxGcBytes = gcSupported ? (long?)MaxBytes : null
        };
    }

    private bool _samplingPerformance;
    private bool _allocationCounterSupported;
    private PerformanceBucket _steadySamples, _spawningSamples, _creationSamples, _dormancySamples;
    private int _sampleSpawnSuccesses;

    /// <summary>仅启用有界诊断，不改变时间、数量、随机数或生成条件。</summary>
    public void BeginPerformanceSample()
    {
        if (_samplingPerformance) throw new InvalidOperationException("已有刷怪性能采样正在运行。");
        // 某些 Unity Mono 运行时此 API 恒为零；必须先自检，不能把不支持误报成零分配。
        long before = GC.GetAllocatedBytesForCurrentThread();
        var probe = new byte[128];
        _allocationCounterSupported = GC.GetAllocatedBytesForCurrentThread() > before;
        GC.KeepAlive(probe);
        _steadySamples = _spawningSamples = _creationSamples = _dormancySamples = default;
        _sampleSpawnSuccesses = 0;
        _samplingPerformance = true;
    }

    /// <summary>结束计量后才分配序列化报告；所有时间均为主线程包含子调用的耗时。</summary>
    public object EndPerformanceSample()
    {
        _samplingPerformance = false;
        return new
        {
            allocationCounterSupported = _allocationCounterSupported,
            noCreationFrames = _steadySamples.Report(_allocationCounterSupported),
            creationFrames = _spawningSamples.Report(_allocationCounterSupported),
            creationOperations = _creationSamples.Report(_allocationCounterSupported),
            dormancyCallbacks = _dormancySamples.Report(_allocationCounterSupported),
            successfulSpawns = _sampleSpawnSuccesses
        };
    }

    /// <summary>只读报告正式 ECS 居民与生态进度，不再统计旧 Item 怪物注册表。</summary>
    public object CaptureSpawnerDiagnostics()
    {
        IAiEcologyBackend backend = AiRuntimeBackendService.Ecology;
        var configs = new List<object>();
        foreach (SpawnerConfig config in _spawnerConfigs)
        {
            if (config == null) continue;
            var species = new List<object>();
            foreach (var entry in config.SpawnEntries)
            {
                if (entry == null) continue;
                species.Add(new { id = entry.PrefabName,
                    active = backend?.GetSpeciesCount(entry.PrefabName) ?? 0 });
            }
            _runtimeStates.TryGetValue(GetConfigKey(config), out var state);
            configs.Add(new
            {
                id = GetConfigKey(config), active = backend?.GetGroupCount(config) ?? 0, species,
                effectiveLimit = GetEffectiveGroupLimit(config),
                ignoreLimits = config.IgnorePopulationLimits, unbounded = config.UnboundedDailyGrowth,
                pending = state?.PendingSpawnCount ?? 0, replacements = state?.PendingReplacementCount ?? 0,
                budget = state?.AvailableBudget ?? -1, lifetimeSpawns = state?.LifetimeSpawnCount ?? 0
            });
        }
        return new
        {
            ready = _gameManager != null && _gameManager.IsGameplayReady,
            enabled, backendReady = backend?.IsReady == true,
            registered = backend?.ResidentCount ?? 0,
            activePopulationLimited = backend?.PopulationLimitedCount ?? 0,
            maxLoaded = _settings.MaxLoadedEntityActors,
            retryTimers = _nextSpawnRetryTime.Count, recoveryTimers = _nextRecoveryCheckTime.Count,
            runtimeStates = _runtimeStates.Count, configs
        };
    }

    #endregion
#endif
}
