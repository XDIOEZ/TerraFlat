using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.Diagnostics;
using FlatWorld.WorldModel;
using UnityEngine;

public static class WorldWeatherSurfaceSystem
{
    #region 区块结算

    private static readonly List<int> SoilChanged = new();
    private static readonly List<int> SnowChanged = new();
    private static SnowCoverConfig snowConfig;
    private static float[] ambientTemperatures = Array.Empty<float>();
    private static int[] activeCells = Array.Empty<int>();
    private static SurfaceWaterSupply[] waterSupplies = Array.Empty<SurfaceWaterSupply>();
    private static EnvironmentCellUpdate[] weatherSnowUpdates = Array.Empty<EnvironmentCellUpdate>();
    private static EnvironmentCellUpdate[] playerSnowUpdates = Array.Empty<EnvironmentCellUpdate>();
    #region 性能分段
    // 工作量按实际批次汇总，保留已有 Profiler 名称便于前后对照。
    private static readonly PerformanceDebugPoint AdvanceChunkMarker = new("FlatWorld.Weather.SurfaceChunk");
    private static readonly PerformanceDebugPoint ExposureMarker = new("FlatWorld.Weather.SurfaceExposure");
    private static readonly PerformanceDebugPoint TemperatureMarker = new("FlatWorld.Weather.SurfaceTemperatures");
    private static readonly PerformanceDebugPoint WaterMarker = new("FlatWorld.Weather.SurfaceWater");
    private static readonly PerformanceDebugPoint PersistMarker = new("FlatWorld.Weather.PersistSurfaceChunk");
    private static readonly PerformanceDebugPoint CommitMarker = new("FlatWorld.Weather.SurfaceCommit");
    private static readonly PerformanceDebugPoint NotifyMarker = new("FlatWorld.Weather.SurfaceNotify");
    #endregion

    // 整个区块共享降水积分，地块只负责实际浇水、积雪和融雪。
    public static void AdvanceChunk(ChunkRuntime chunk, WeatherMgr weather, float seconds,
        float precipitationSeconds)
        => AdvanceChunk(chunk, weather, seconds, precipitationSeconds, null);

    internal static void AdvanceChunk(ChunkRuntime chunk, WeatherMgr weather, float seconds,
        float precipitationSeconds, TemperatureMgr.AmbientChunkSnapshot temperatureSnapshot)
    {
        if (!GameNetwork.HasStateAuthority || chunk == null || weather == null ||
            !float.IsFinite(seconds) || seconds <= 0f ||
            !float.IsFinite(precipitationSeconds) || precipitationSeconds < 0f ||
            chunk.DataStatus != ChunkDataStatus.Ready ||
            chunk.Terrain == null || chunk.Terrain.IsDisposed ||
            ChunkMgr.ExistingInstance?.IsTerrainRestoredForEnvironment(chunk) != true)
            return;

        using var profilerScope = AdvanceChunkMarker.Measure(1);
        ChunkTerrainData terrain = chunk.Terrain;
        // 未产生过天气雪或玩家雪的晴天区块无需逐格结算。
        if (!HasSurfaceWork(terrain, precipitationSeconds))
            return;

        SnowCoverConfig config = GetSnowConfig();
        float freezingTemperature = weather.PrecipitationFreezingTemperature;
        TemperatureMgr temperature = temperatureSnapshot == null ? TemperatureMgr.Instance : null;
        if (temperatureSnapshot == null && temperature == null)
            return;
        if (ambientTemperatures.Length < terrain.CellCount)
        {
            ambientTemperatures = new float[terrain.CellCount];
            activeCells = new int[terrain.CellCount];
            waterSupplies = new SurfaceWaterSupply[terrain.CellCount];
            weatherSnowUpdates = new EnvironmentCellUpdate[terrain.CellCount];
            playerSnowUpdates = new EnvironmentCellUpdate[terrain.CellCount];
        }
        int activeCount;
        using (ExposureMarker.Measure(terrain.CellCount))
            activeCount = CollectActiveCells(chunk, precipitationSeconds);
        if (activeCount == 0)
            return;
        // 修改土壤和雪层前统一冻结当地温度，邻区热源依赖每块只检查一次。
        using (TemperatureMarker.Measure(activeCount))
            if (!(temperatureSnapshot != null
                ? temperatureSnapshot.TrySample(chunk, activeCells.AsSpan(0, activeCount), ambientTemperatures)
                : temperature.TrySampleAmbientChunk(chunk, activeCells.AsSpan(0, activeCount), ambientTemperatures)))
                return;
        SoilChanged.Clear();
        SnowChanged.Clear();
        try
        {
            int waterSupplyCount = 0, weatherSnowCount = 0, playerSnowCount = 0;
            using (CommitMarker.Measure(activeCount))
            {
                for (int i = 0; i < activeCount; i++)
                {
                    int index = activeCells[i];
                    int x = index % terrain.Width;
                    int y = index / terrain.Width;
                    float previousWeatherSnow = WorldSnowSystem.GetWeatherDepth(terrain, x, y);
                    bool edited = terrain.TryGetEnvironmentValue(WorldSnowInteraction.EditedLayer, x, y,
                        out float editedValue) && editedValue > 0f;
                    terrain.TryGetEnvironmentValue(WorldSnowInteraction.DepthLayer, x, y, out float playerDepth);
                    float currentTemperature = ambientTemperatures[index];
                    if (!float.IsFinite(currentTemperature))
                        continue;

                    float nextWeatherSnow = previousWeatherSnow;
                    float waterPoints = 0f;
                    bool snowChanged = false;
                    if (currentTemperature <= freezingTemperature)
                    {
                        nextWeatherSnow = Mathf.Clamp01(previousWeatherSnow +
                            precipitationSeconds * Mathf.Max(0f, config.AccumulationPerSecond));
                    }
                    else
                    {
                        waterPoints = precipitationSeconds * Mathf.Max(0f, weather.SurfaceRainWaterPerSecond);
                        float meltBudget = (currentTemperature - freezingTemperature) *
                            Mathf.Max(0f, config.MeltPerDegreeSecond) * seconds;
                        nextWeatherSnow = Mathf.Max(0f, previousWeatherSnow - meltBudget);
                        float melted = previousWeatherSnow - nextWeatherSnow;
                        if (edited)
                        {
                            terrain.TryGetEnvironmentValue(WorldSnowInteraction.SeasonLayer, x, y, out float baseline);
                            float before = Mathf.Max(0f, playerDepth + previousWeatherSnow - baseline);
                            float after = Mathf.Max(0f, playerDepth + nextWeatherSnow - baseline);
                            melted = before - after;
                            float additional = Mathf.Min(after, Mathf.Max(0f, meltBudget - melted));
                            if (additional > 0f)
                            {
                                playerDepth = Mathf.Max(0f, playerDepth - additional);
                                playerSnowUpdates[playerSnowCount++] = new EnvironmentCellUpdate(index, playerDepth);
                                snowChanged = true;
                                melted += additional;
                            }
                        }
                        waterPoints += melted * Mathf.Max(0f, weather.MeltWaterPerSnowDepth);
                    }
                    if (nextWeatherSnow != previousWeatherSnow)
                    {
                        weatherSnowUpdates[weatherSnowCount++] = new EnvironmentCellUpdate(index, nextWeatherSnow);
                        snowChanged = true;
                    }
                    if (snowChanged)
                        SnowChanged.Add(index);
                    if (waterPoints > 0f)
                        waterSupplies[waterSupplyCount++] = new SurfaceWaterSupply(index, waterPoints);
                }
                // 雨雪结果保持原顺序，在土壤补水前集中提交雪层，免除逐格通知。
                CommitSnowChanges(terrain, weatherSnowUpdates.AsSpan(0, weatherSnowCount),
                    playerSnowUpdates.AsSpan(0, playerSnowCount));
            }
            if (waterSupplyCount > 0)
                using (WaterMarker.Measure(waterSupplyCount))
                    FarmlandSystem.SupplySurfaceWaterBatch(chunk, waterSupplies.AsSpan(0, waterSupplyCount), SoilChanged, true);
            if (SoilChanged.Count > 0 || SnowChanged.Count > 0)
                using (PersistMarker.Measure(SoilChanged.Count + SnowChanged.Count))
                    SaveDataMgr.Instance.RecordWeatherSurfaceChunk(chunk, SoilChanged, SnowChanged);
            if (waterSupplyCount > 0)
                using (NotifyMarker.Measure(waterSupplyCount))
                    FarmlandSystem.PublishSurfaceWaterBatch(terrain);
        }
        finally
        {
            SoilChanged.Clear();
            SnowChanged.Clear();
        }
    }

    internal static void CommitSnowChanges(ChunkTerrainData terrain,
        ReadOnlySpan<EnvironmentCellUpdate> weather, ReadOnlySpan<EnvironmentCellUpdate> player)
    {
        if (!player.IsEmpty)
            terrain.SetEnvironmentBatch(WorldSnowInteraction.DepthLayer, player, true);
        if (!weather.IsEmpty)
            terrain.SetEnvironmentBatch(WorldSnowSystem.WeatherDepthLayer, weather, true);
        if (!player.IsEmpty || !weather.IsEmpty)
            terrain.PublishEnvironmentBatches();
    }

    internal static bool HasSurfaceWork(ChunkTerrainData terrain, float precipitationSeconds, bool hasPending = false) =>
        hasPending || precipitationSeconds > 0f ||
        terrain.TryGetEnvironmentValue(WorldSnowSystem.WeatherDepthLayer, 0, 0, out _) ||
        terrain.TryGetEnvironmentValue(WorldSnowInteraction.DepthLayer, 0, 0, out _);

    // 先筛选受本区块降水或已有积雪影响的地面格，不再逐格查询天气。
    private static int CollectActiveCells(ChunkRuntime chunk, float precipitationSeconds)
    {
        ChunkTerrainData terrain = chunk.Terrain;
        int count = 0;
        for (int y = 0; y < terrain.Height; y++)
        for (int x = 0; x < terrain.Width; x++)
        {
            float weatherSnow = WorldSnowSystem.GetWeatherDepth(terrain, x, y);
            bool edited = terrain.TryGetEnvironmentValue(WorldSnowInteraction.EditedLayer, x, y,
                out float editedValue) && editedValue > 0f;
            terrain.TryGetEnvironmentValue(WorldSnowInteraction.DepthLayer, x, y, out float playerDepth);
            bool hasSnow = weatherSnow > 0f || edited && playerDepth > 0f;
            if (precipitationSeconds <= 0f && !hasSnow)
                continue;
            TerrainCell surface = TerrainSupportLayer.GetSurfaceCell(terrain, x, y);
            if (surface.GroundTileId == 0 || WorldLiquidSystem.GetSurfaceDepth(terrain, x, y) > 0f)
                continue;
            int index = y * terrain.Width + x;
            activeCells[count++] = index;
        }
        return count;
    }

    #endregion

    #region 共享积雪配置

    private static SnowCoverConfig GetSnowConfig()
    {
        if (snowConfig == null)
            snowConfig = Resources.Load<SnowCoverConfig>("Weather/SnowCoverConfig");
        if (snowConfig == null)
            throw new MissingReferenceException("缺少 Weather/SnowCoverConfig 积雪配置。");
        return snowConfig;
    }

    #endregion
}

internal sealed class WeatherSurfaceWorkQueue : IDisposable
{
    #region 地表结算队列

    private readonly LinkedList<Work> pending = new();
    private readonly Dictionary<ChunkRuntime, int> pendingChunks = new();
    private bool applying;
    private int advanceBarriers;
    public int Count => pending.Count;
    public long LastSequence { get; private set; }
    public bool IsAdvanceBlocked => advanceBarriers > 0;
    public bool HasPending(ChunkRuntime chunk) => chunk != null && pendingChunks.ContainsKey(chunk);

    private sealed class AdvanceBarrier : IDisposable
    {
        private WeatherSurfaceWorkQueue owner;
        public AdvanceBarrier(WeatherSurfaceWorkQueue owner)
        {
            this.owner = owner;
            owner.advanceBarriers++;
        }
        public void Dispose()
        {
            if (owner == null) return;
            owner.advanceBarriers--;
            owner = null;
        }
    }

    public IDisposable BeginAdvanceBarrier() => new AdvanceBarrier(this);

    // 每笔保留独立的游戏时间和降水积分，不能合并跨越冷热变化的结算。
    private sealed class Work : IDisposable
    {
        public readonly ChunkRuntime Chunk;
        public readonly ChunkTerrainData Terrain;
        public readonly float Seconds;
        public readonly float PrecipitationSeconds;
        public readonly long Sequence;
        public readonly TemperatureMgr.AmbientChunkSnapshot Temperature;
        private readonly ChunkLease lease;

        public Work(ChunkRuntime chunk, float seconds, float precipitationSeconds, long sequence,
            TemperatureMgr.AmbientChunkSnapshot temperature)
        {
            Chunk = chunk;
            Terrain = chunk.Terrain;
            Seconds = seconds;
            PrecipitationSeconds = precipitationSeconds;
            Sequence = sequence;
            Temperature = temperature;
            lease = chunk.AcquireLease(ChunkLeaseKind.Navigation);
        }

        public void Dispose() => lease.Dispose();
    }

    public void Enqueue(ChunkRuntime chunk, float seconds, float precipitationSeconds,
        TemperatureMgr.AmbientChunkSnapshot temperature = null)
    {
        if (chunk?.Terrain == null || chunk.Terrain.IsDisposed || chunk.DataStatus != ChunkDataStatus.Ready)
            throw new ArgumentException("地表结算只能登记已就绪区块。", nameof(chunk));
        if (!float.IsFinite(seconds) || seconds <= 0f || !float.IsFinite(precipitationSeconds) ||
            precipitationSeconds < 0f || precipitationSeconds > seconds)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        pending.AddLast(new Work(chunk, seconds, precipitationSeconds, ++LastSequence, temperature));
        pendingChunks.TryGetValue(chunk, out int count);
        pendingChunks[chunk] = count + 1;
    }

    public bool DrainNext(Action<ChunkRuntime, float, float, TemperatureMgr.AmbientChunkSnapshot> apply,
        long throughSequence = long.MaxValue)
    {
        if (applying || pending.First == null || pending.First.Value.Sequence > throughSequence)
            return false;
        Apply(pending.First, apply);
        return true;
    }

    public void DrainSlice(Action<ChunkRuntime, float, float, TemperatureMgr.AmbientChunkSnapshot> apply, double milliseconds)
    {
        if (!double.IsFinite(milliseconds) || milliseconds < 0d)
            throw new ArgumentOutOfRangeException(nameof(milliseconds));
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        double budgetTicks = milliseconds * System.Diagnostics.Stopwatch.Frequency / 1000d;
        while (DrainNext(apply))
            if (System.Diagnostics.Stopwatch.GetTimestamp() - started >= budgetTicks)
                break;
    }

    public void DrainChunk(ChunkRuntime chunk, Action<ChunkRuntime, float, float, TemperatureMgr.AmbientChunkSnapshot> apply)
    {
        if (applying)
            return;
        LinkedListNode<Work> node = pending.First;
        while (node != null)
        {
            LinkedListNode<Work> next = node.Next;
            if (ReferenceEquals(node.Value.Chunk, chunk))
                Apply(node, apply);
            node = next?.List == pending ? next : pending.First;
            if (next == null)
                break;
        }
    }

    private void Apply(LinkedListNode<Work> node, Action<ChunkRuntime, float, float, TemperatureMgr.AmbientChunkSnapshot> apply)
    {
        Work work = node.Value;
        pending.Remove(node);
        int remaining = pendingChunks[work.Chunk] - 1;
        if (remaining == 0) pendingChunks.Remove(work.Chunk);
        else pendingChunks[work.Chunk] = remaining;
        applying = true;
        try
        {
            // 旧地形不得写入同地址的新区块，短租约只保护待结算数据而不激活游戏逻辑。
            if (work.Chunk.DataStatus == ChunkDataStatus.Ready && !work.Terrain.IsDisposed &&
                ReferenceEquals(work.Chunk.Terrain, work.Terrain))
                apply(work.Chunk, work.Seconds, work.PrecipitationSeconds, work.Temperature);
        }
        finally
        {
            applying = false;
            work.Dispose();
        }
    }

    public void Dispose()
    {
        pendingChunks.Clear();
        while (pending.First != null)
        {
            Work work = pending.First.Value;
            pending.RemoveFirst();
            work.Dispose();
        }
    }

    #endregion
}
