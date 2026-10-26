using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FlatWorld.Networking;
using FlatWorld.Diagnostics;
using FlatWorld.WorldModel;
using UnityEngine;

public static class WorldWeatherSurfaceSystem
{
    #region 实际积雪索引

    private static readonly ConditionalWeakTable<ChunkTerrainData, SnowCellIndex> snowCells = new();

    // 雪层融化到零后立即移出候选，避免空数组让普通地块永久参与天气结算。
    private sealed class SnowCellIndex
    {
        private readonly ChunkTerrainData terrain;
        internal readonly List<int> Cells = new();
        internal SnowCellIndex(ChunkTerrainData terrain)
        {
            this.terrain = terrain;
            if (terrain.TryGetEnvironmentLayer(WorldSnowSystem.WeatherDepthLayer, out _) ||
                terrain.TryGetEnvironmentLayer(WorldSnowInteraction.DepthLayer, out _) ||
                terrain.TryGetEnvironmentLayer(SnowDepthLayer.LayerId, out _))
                for (int index = 0; index < terrain.CellCount; index++) Refresh(index);
            terrain.Changed += OnChanged;
        }

        private void OnChanged(ChunkTerrainChanged change)
        {
            if (change.Kind != TerrainChangeKind.Environment ||
                !(change.AffectsEnvironmentLayer(WorldSnowSystem.WeatherDepthLayer) ||
                  change.AffectsEnvironmentLayer(WorldSnowInteraction.DepthLayer) ||
                  change.AffectsEnvironmentLayer(WorldSnowInteraction.EditedLayer) ||
                  change.AffectsEnvironmentLayer(SnowDepthLayer.LayerId) ||
                  change.AffectsEnvironmentLayer(TerrainSupportLayer.TileIdLayer))) return;
            foreach (int index in change.GetCellIndices(terrain.Width)) Refresh(index);
        }

        private void Refresh(int index)
        {
            int x = index % terrain.Width, y = index / terrain.Width;
            bool edited = terrain.TryGetEnvironmentValue(WorldSnowInteraction.EditedLayer, x, y, out float flag) && flag > 0f;
            bool hasSnow = WorldSnowSystem.GetWeatherDepth(terrain, x, y) > 0f ||
                (edited ? terrain.TryGetEnvironmentValue(WorldSnowInteraction.DepthLayer, x, y, out float depth) && depth > 0f
                    : WorldSnowSystem.GetExposedNaturalDepth(terrain, x, y) > 0f);
            int slot = Cells.BinarySearch(index);
            if (hasSnow && slot < 0) Cells.Insert(~slot, index);
            else if (!hasSnow && slot >= 0) Cells.RemoveAt(slot);
        }
    }

    internal static IReadOnlyList<int> GetSnowCellIndices(ChunkTerrainData terrain) =>
        snowCells.GetValue(terrain, value => new SnowCellIndex(value)).Cells;

    #endregion

    #region 区块结算

    private static readonly List<int> SoilChanged = new();
    private static readonly List<int> SnowChanged = new();
    private static SnowCoverConfig snowConfig;
    private static float[] ambientTemperatures = Array.Empty<float>();
    private static int[] activeCells = Array.Empty<int>();
    private static SurfaceWaterSupply[] waterSupplies = Array.Empty<SurfaceWaterSupply>();
    private static EnvironmentCellUpdate[] weatherSnowUpdates = Array.Empty<EnvironmentCellUpdate>();
    private static EnvironmentCellUpdate[] playerSnowUpdates = Array.Empty<EnvironmentCellUpdate>();
    private static EnvironmentCellUpdate[] editedSnowUpdates = Array.Empty<EnvironmentCellUpdate>();
    private static EnvironmentCellUpdate[] seasonSnowUpdates = Array.Empty<EnvironmentCellUpdate>();
    private static EnvironmentCellUpdate[] iceProgressUpdates = Array.Empty<EnvironmentCellUpdate>();
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
        float freezingTemperature = weather.PrecipitationFreezingTemperature;
        TemperatureMgr temperature = temperatureSnapshot == null ? TemperatureMgr.Instance : null;
        bool maySnow = precipitationSeconds > 0f && (temperatureSnapshot != null
            ? temperatureSnapshot.MayFreeze(freezingTemperature)
            : temperature == null || temperature.MayFreezeAmbientChunk(chunk, freezingTemperature));
        if (!HasSurfaceWork(terrain, precipitationSeconds, maySnow: maySnow))
            return;

        SnowCoverConfig config = GetSnowConfig();
        if (temperatureSnapshot == null && temperature == null)
            return;
        if (ambientTemperatures.Length < terrain.CellCount)
        {
            ambientTemperatures = new float[terrain.CellCount];
            activeCells = new int[terrain.CellCount];
            waterSupplies = new SurfaceWaterSupply[terrain.CellCount];
            weatherSnowUpdates = new EnvironmentCellUpdate[terrain.CellCount];
            playerSnowUpdates = new EnvironmentCellUpdate[terrain.CellCount];
            editedSnowUpdates = new EnvironmentCellUpdate[terrain.CellCount];
            seasonSnowUpdates = new EnvironmentCellUpdate[terrain.CellCount];
            iceProgressUpdates = new EnvironmentCellUpdate[terrain.CellCount];
        }
        int activeCount;
        using (ExposureMarker.Measure(terrain.CellCount))
            activeCount = CollectActiveCells(chunk, precipitationSeconds, maySnow);
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
            int waterSupplyCount = 0, weatherSnowCount = 0, playerSnowCount = 0,
                editedSnowCount = 0, seasonSnowCount = 0, iceProgressCount = 0;
            weather.TryGetSnowCoverageContext(out SnowCoverState seasonalSnow, out float baselineOffset);
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

                    terrain.TryGetEnvironmentValue(WorldSnowInteraction.SeasonLayer, x, y, out float baseline);
                    float seasonalDepth = WorldSnowSystem.GetSeasonalDepth(terrain, x, y, seasonalSnow, baselineOffset);
                    WorldSurfaceThawRules.SnowStep snow = WorldSurfaceThawRules.AdvanceSnow(
                        WorldSnowSystem.GetExposedNaturalDepth(terrain, x, y), edited, playerDepth, baseline,
                        seasonalDepth, previousWeatherSnow, currentTemperature, freezingTemperature, seconds,
                        precipitationSeconds, config.AccumulationPerSecond, config.MeltPerDegreeSecond);
                    float waterPoints = currentTemperature > freezingTemperature
                        ? precipitationSeconds * Mathf.Max(0f, weather.SurfaceRainWaterPerSecond) +
                          snow.Melted * Mathf.Max(0f, weather.MeltWaterPerSnowDepth) : 0f;
                    bool snowChanged = false;
                    if (snow.Edited != edited)
                    {
                        editedSnowUpdates[editedSnowCount++] = new EnvironmentCellUpdate(index, snow.Edited ? 1f : 0f);
                        snowChanged = true;
                    }
                    if (snow.SeasonalBaseline != baseline)
                    {
                        seasonSnowUpdates[seasonSnowCount++] = new EnvironmentCellUpdate(index, snow.SeasonalBaseline);
                        snowChanged = true;
                    }
                    if (snow.Depth != playerDepth)
                    {
                        playerSnowUpdates[playerSnowCount++] = new EnvironmentCellUpdate(index, snow.Depth);
                        snowChanged = true;
                    }
                    if (snow.WeatherDepth != previousWeatherSnow)
                    {
                        weatherSnowUpdates[weatherSnowCount++] = new EnvironmentCellUpdate(index, snow.WeatherDepth);
                        snowChanged = true;
                    }
                    if (WorldFrozenSurfaceWaterThawSystem.TryGetThawableSource(terrain, x, y, out _))
                    {
                        terrain.TryGetEnvironmentValue(FrozenSurfaceWaterLayer.MeltProgressLayer, x, y, out float progress);
                        float next = WorldSurfaceThawRules.AdvanceIceMeltProgress(progress, currentTemperature,
                            seconds, config.IceMeltPerDegreeSecond);
                        if (next != progress && (next < 1f || WorldFrozenSurfaceWaterThawSystem.TryCompleteThaw(chunk, index)))
                        {
                            iceProgressUpdates[iceProgressCount++] = new EnvironmentCellUpdate(index, next);
                            snowChanged = true;
                        }
                    }
                    if (snowChanged)
                        SnowChanged.Add(index);
                    if (waterPoints > 0f && FarmlandSystem.IsFarmland(terrain.GetCell(x, y)))
                        waterSupplies[waterSupplyCount++] = new SurfaceWaterSupply(index, waterPoints);
                }
                // 雨雪结果保持原顺序，在土壤补水前集中提交雪层，免除逐格通知。
                CommitSnowChanges(terrain, weatherSnowUpdates.AsSpan(0, weatherSnowCount),
                    playerSnowUpdates.AsSpan(0, playerSnowCount), editedSnowUpdates.AsSpan(0, editedSnowCount),
                    seasonSnowUpdates.AsSpan(0, seasonSnowCount), iceProgressUpdates.AsSpan(0, iceProgressCount));
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
        => CommitSnowChanges(terrain, weather, player, default, default, default);

    internal static void CommitSnowChanges(ChunkTerrainData terrain,
        ReadOnlySpan<EnvironmentCellUpdate> weather, ReadOnlySpan<EnvironmentCellUpdate> player,
        ReadOnlySpan<EnvironmentCellUpdate> edited, ReadOnlySpan<EnvironmentCellUpdate> season,
        ReadOnlySpan<EnvironmentCellUpdate> iceProgress)
    {
        if (!edited.IsEmpty) terrain.SetEnvironmentBatch(WorldSnowInteraction.EditedLayer, edited, true);
        if (!season.IsEmpty) terrain.SetEnvironmentBatch(WorldSnowInteraction.SeasonLayer, season, true);
        if (!iceProgress.IsEmpty) terrain.SetEnvironmentBatch(FrozenSurfaceWaterLayer.MeltProgressLayer, iceProgress, true);
        if (!player.IsEmpty)
            terrain.SetEnvironmentBatch(WorldSnowInteraction.DepthLayer, player, true);
        if (!weather.IsEmpty)
            terrain.SetEnvironmentBatch(WorldSnowSystem.WeatherDepthLayer, weather, true);
        if (!player.IsEmpty || !weather.IsEmpty || !edited.IsEmpty || !season.IsEmpty || !iceProgress.IsEmpty)
            terrain.PublishEnvironmentBatches();
    }

    internal static bool HasSurfaceWork(ChunkTerrainData terrain, float precipitationSeconds,
        bool hasPending = false, bool maySnow = true) =>
        hasPending || precipitationSeconds > 0f && (maySnow || FarmlandSystem.GetFarmlandCellIndices(terrain).Count > 0) ||
        GetSnowCellIndices(terrain).Count > 0 ||
        WorldFrozenSurfaceWaterThawSystem.GetFrozenCellIndices(terrain).Count > 0;

    // 先筛选受本区块降水或已有积雪影响的地面格，不再逐格查询天气。
    private static int CollectActiveCells(ChunkRuntime chunk, float precipitationSeconds, bool maySnow)
    {
        ChunkTerrainData terrain = chunk.Terrain;
        int count = 0;
        // 暖雨和融雪只合并实际耕地、积雪格，可能落雪时才处理全地表。
        if (!maySnow)
        {
            IReadOnlyList<int> farmland = precipitationSeconds > 0f
                ? FarmlandSystem.GetFarmlandCellIndices(terrain) : Array.Empty<int>();
            IReadOnlyList<int> snow = GetSnowCellIndices(terrain);
            IReadOnlyList<int> ice = WorldFrozenSurfaceWaterThawSystem.GetFrozenCellIndices(terrain);
            int farmSlot = 0, snowSlot = 0, iceSlot = 0;
            while (farmSlot < farmland.Count || snowSlot < snow.Count || iceSlot < ice.Count)
            {
                int farmIndex = farmSlot < farmland.Count ? farmland[farmSlot] : int.MaxValue;
                int snowIndex = snowSlot < snow.Count ? snow[snowSlot] : int.MaxValue;
                int iceIndex = iceSlot < ice.Count ? ice[iceSlot] : int.MaxValue;
                int index = Math.Min(Math.Min(farmIndex, snowIndex), iceIndex);
                bool snowCandidate = index == snowIndex;
                if (index == farmIndex) farmSlot++;
                if (index == snowIndex) snowSlot++;
                if (index == iceIndex) iceSlot++;
                int x = index % terrain.Width, y = index / terrain.Width;
                if (!snowCandidate && TerrainSupportLayer.GetTileId(terrain, x, y) != 0) continue;
                if (TerrainSupportLayer.GetSurfaceCell(terrain, x, y).GroundTileId != 0 &&
                    WorldLiquidSystem.GetSurfaceDepth(terrain, x, y) <= 0f)
                    activeCells[count++] = index;
            }
            return count;
        }
        for (int y = 0; y < terrain.Height; y++)
        for (int x = 0; x < terrain.Width; x++)
        {
            float weatherSnow = WorldSnowSystem.GetWeatherDepth(terrain, x, y);
            bool edited = terrain.TryGetEnvironmentValue(WorldSnowInteraction.EditedLayer, x, y,
                out float editedValue) && editedValue > 0f;
            terrain.TryGetEnvironmentValue(WorldSnowInteraction.DepthLayer, x, y, out float playerDepth);
            bool hasSnow = weatherSnow > 0f || (edited ? playerDepth > 0f :
                WorldSnowSystem.GetExposedNaturalDepth(terrain, x, y) > 0f);
            if (precipitationSeconds <= 0f && !hasSnow &&
                !WorldFrozenSurfaceWaterThawSystem.TryGetThawableSource(terrain, x, y, out _))
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
