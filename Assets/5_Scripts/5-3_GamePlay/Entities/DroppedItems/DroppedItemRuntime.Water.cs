using System;
using System.Collections.Generic;
using FlatWorld.DroppedItems;
using FlatWorld.Diagnostics;
using FlatWorld.WorldModel;
using Unity.Profiling;
using UnityEngine;
using static WorldItemWaterRules;

internal sealed partial class DroppedItemRuntime
{
    #region 水态诊断与状态

    private static readonly PerformanceDebugPoint WaterCleanupPoint = new("DroppedItems.Water.CleanupTerrainWatches");
    private static readonly PerformanceDebugPoint WaterPendingPoint = new("DroppedItems.Water.PendingEnvironment");
    private static readonly PerformanceDebugPoint WaterWetPoint = new("DroppedItems.Water.WetBatch");
    private static readonly PerformanceDebugPoint WaterMovedPoint = new("DroppedItems.Water.MovedItems");
    private static readonly PerformanceDebugPoint WaterDeferredWetPoint = new("DroppedItems.Water.PendingWetChecksMerged");
    private static readonly ProfilerMarker WaterEnvironmentMarker = new("DroppedItems.Water.CheckEnvironment");
    private static readonly ProfilerMarker WaterTerrainSampleMarker = new("DroppedItems.Water.TerrainSample");
    private static readonly ProfilerMarker WaterEntryMarker = new("DroppedItems.Water.EntryTransform");
    private static readonly ProfilerMarker WaterBuoyancyMarker = new("DroppedItems.Water.ResolveBuoyancy");
    private static readonly ProfilerMarker WaterCurrentMarker = new("DroppedItems.Water.SampleCurrent");
    private static readonly ProfilerMarker WaterDestinationMarker = new("DroppedItems.Water.ResolveDestination");
    private static readonly ProfilerMarker WaterCommitMarker = new("DroppedItems.Water.CommitDrift");
    private const float WaterInterval = SettledTickInterval;
    private sealed class TerrainWatch
    {
        public ChunkTerrainData Terrain;
        public Vector2Int Origin;
        public Action<ChunkTerrainChanged> Handler;
        public readonly HashSet<int> Residents = new();
    }
    private readonly HashSet<int> wetItems = new();
    private readonly Queue<int> pendingEnvironment = new();
    private readonly HashSet<int> pendingEnvironmentSet = new();
    private readonly Dictionary<ChunkTerrainData, TerrainWatch> terrainWatches = new();
    private readonly Dictionary<int, TerrainWatch> itemTerrain = new();
    private readonly List<TerrainWatch> terrainScratch = new();
    private readonly List<int> waterScratch = new();
    private float waterClock;
    private ChunkMgr terrainSamplingManager;
    private ChunkMgr.RuntimeTerrainPresenceQuery terrainSamplingQuery;
    private bool terrainSamplingActive;
    private bool terrainSamplingAvailable;

    #endregion

    #region 同步环境寻址

    private readonly struct TerrainSamplingScope : IDisposable
    {
        private readonly DroppedItemRuntime owner;
        private readonly ChunkMgr previousManager;
        private readonly ChunkMgr.RuntimeTerrainPresenceQuery previousQuery;
        private readonly bool previousActive;
        private readonly bool previousAvailable;

        internal TerrainSamplingScope(DroppedItemRuntime owner)
        {
            this.owner = owner;
            previousManager = owner.terrainSamplingManager;
            previousQuery = owner.terrainSamplingQuery;
            previousActive = owner.terrainSamplingActive;
            previousAvailable = owner.terrainSamplingAvailable;
            owner.terrainSamplingActive = true;
            owner.terrainSamplingManager = null;
            owner.terrainSamplingAvailable = false;
            owner.terrainSamplingQuery = default;
        }

        public void Dispose()
        {
            owner.terrainSamplingManager = previousManager;
            owner.terrainSamplingQuery = previousQuery;
            owner.terrainSamplingActive = previousActive;
            owner.terrainSamplingAvailable = previousAvailable;
        }
    }

    // 只复用本次同步驱动的世界寻址，每次仍读最新地形；切世界和换代立即重新建立。
    private bool TrySampleEnvironmentTerrain(Vector2 position, out RuntimeTerrainTileSample sample)
    {
        sample = default;
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (manager == null) return false;
        if (!terrainSamplingActive) return manager.TryGetRuntimeTerrainTile(position, out sample);
        if (!terrainSamplingAvailable || !ReferenceEquals(manager, terrainSamplingManager) ||
            !terrainSamplingQuery.IsForWorld(manager.WorldRuntime))
        {
            terrainSamplingManager = manager;
            terrainSamplingAvailable = manager.TryCreateTerrainPresenceQuery(out terrainSamplingQuery);
        }
        return terrainSamplingAvailable && terrainSamplingQuery.TrySample(position, out sample);
    }

    #endregion

    #region 地形事件与低频调度

    private void QueueEnvironment(int id)
    {
        if (simulation.Contains(id) && pendingEnvironmentSet.Add(id)) pendingEnvironment.Enqueue(id);
    }

    private void WatchTerrain(int id, RuntimeTerrainTileSample sample)
    {
        if (itemTerrain.TryGetValue(id, out TerrainWatch previous) && ReferenceEquals(previous.Terrain, sample.Terrain)) return;
        DetachTerrain(id);
        if (!terrainWatches.TryGetValue(sample.Terrain, out TerrainWatch watch))
        {
            watch = new TerrainWatch { Terrain = sample.Terrain, Origin = sample.WorldCell - sample.LocalCell };
            watch.Handler = change => OnTerrainChanged(watch, change);
            watch.Terrain.Changed += watch.Handler;
            terrainWatches.Add(watch.Terrain, watch);
        }
        watch.Residents.Add(id); itemTerrain[id] = watch;
    }

    private void DetachTerrain(int id)
    {
        if (!itemTerrain.TryGetValue(id, out TerrainWatch watch)) return;
        itemTerrain.Remove(id); watch.Residents.Remove(id);
        if (watch.Residents.Count != 0) return;
        watch.Terrain.Changed -= watch.Handler; terrainWatches.Remove(watch.Terrain);
    }

    /// <summary>陆地静止实体只响应所在格变化，不为每个实体逐帧查询地形。</summary>
    private void OnTerrainChanged(TerrainWatch watch, ChunkTerrainChanged change)
    {
        // 土壤含水和雪厚不改变液体接触，静止掉落物只被实际地表变化唤醒。
        if (change.Kind == TerrainChangeKind.Environment &&
            !change.AffectsEnvironmentLayer(TerrainSupportLayer.TileIdLayer))
            return;
        int width = watch.Terrain.Width;
        foreach (int index in change.GetCellIndices(width))
        {
            Vector2Int cell = watch.Origin + new Vector2Int(index % width, index / width);
            if (!spatial.TryGetValue(SpatialCell((Vector2)cell + Vector2.one * 0.5f), out HashSet<int> bucket)) continue;
            foreach (int id in bucket)
            {
                LightweightDroppedBody body = simulation.Get(id);
                if (Mathf.FloorToInt(body.Position.x) == cell.x && Mathf.FloorToInt(body.Position.y) == cell.y)
                    QueueEnvironment(id);
            }
        }
    }

    private void TickWater(float deltaTime)
    {
        waterClock += deltaTime;
        if (waterClock < WaterInterval) return;
        float step = waterClock; waterClock = 0f;
        using (var cleanup = WaterCleanupPoint.Measure(terrainWatches.Count))
        {
            cleanup.SetGauge(terrainWatches.Count);
            terrainScratch.Clear(); terrainScratch.AddRange(terrainWatches.Values);
            foreach (TerrainWatch watch in terrainScratch)
            {
                if (!watch.Terrain.IsDisposed) continue;
                foreach (int id in watch.Residents) { QueueEnvironment(id); itemTerrain.Remove(id); }
                watch.Terrain.Changed -= watch.Handler; terrainWatches.Remove(watch.Terrain);
            }
        }
        // 未加载地形只排队重试，严格限制每轮成本，绝不触发同步 Chunk 加载。
        int budget = Mathf.Min(256, pendingEnvironment.Count);
        using (var pending = WaterPendingPoint.Measure(budget))
        {
            pending.SetGauge(pendingEnvironment.Count);
            int mergedWet = 0;
            while (budget-- > 0)
            {
                int id = pendingEnvironment.Dequeue();
                if (!pendingEnvironmentSet.Remove(id) || !simulation.Contains(id)) continue;
                // 湿物件紧接着会统一检查，合并本轮同一位置的重复地形查询。
                if (wetItems.Contains(id)) { mergedWet++; continue; }
                CheckEnvironment(id);
            }
            WaterDeferredWetPoint.AddWork(mergedWet);
        }
        using (var wet = WaterWetPoint.Measure(wetItems.Count))
        {
            wet.SetGauge(wetItems.Count);
            int moved = 0;
            waterScratch.Clear(); waterScratch.AddRange(wetItems);
            foreach (int id in waterScratch)
            {
                if (!simulation.Contains(id)) continue;
                CheckEnvironment(id);
                LightweightDroppedBody body = simulation.Get(id);
                ChunkMgr chunks = ChunkMgr.ExistingInstance;
                RuntimeWaterCurrentSample current;
                using (WaterCurrentMarker.Auto())
                    if (body.WaterKind == 0 || chunks == null || !chunks.TryGetRuntimeWaterCurrent(body.Position, out current)) continue;
                float speed = ResolveDriftSpeed(current.Kind, current.Flow, payloads[id].Stack);
                if (speed <= 0f || current.Direction.sqrMagnitude <= 0.000001f) continue;
                float driftSeconds = Mathf.Min(step, 1f);
                Vector2 destination;
                using (WaterDestinationMarker.Auto())
                {
                    destination = domain.Normalize((Vector2)body.Position + current.Direction * (speed * driftSeconds));
                    if (!WorldItemWaterSystem.TryResolveWaterAt(destination, out bool isWater) || !isWater) continue;
                }
                using (WaterCommitMarker.Auto())
                {
                    presentation.RecordWaterDrift(id, domain.ShortestDelta(body.Position, destination), driftSeconds);
                    body.Position = destination; simulation.Set(body); UpdatePlacement(id); CheckEnvironment(id);
                    moved++;
                }
            }
            WaterMovedPoint.AddWork(moved);
        }
    }

    #endregion

    #region 浮沉与入水转换

    private void CheckEnvironment(int id)
    {
        using (WaterEnvironmentMarker.Auto()) CheckEnvironmentCore(id);
    }

    private void CheckEnvironmentCore(int id)
    {
        if (!simulation.Contains(id) || simulation.TryGetFlight(id, out _)) return;
        LightweightDroppedBody body = simulation.Get(id);
        RuntimeTerrainTileSample sample;
        using (WaterTerrainSampleMarker.Auto())
        {
            if (!TrySampleEnvironmentTerrain(body.Position, out sample))
            {
                QueueEnvironment(id);
                return;
            }
        }
        WatchTerrain(id, sample);
        bool changed = body.Pickable == 0;
        bool isWater = sample.LiquidDepth > 0f;
        if (!isWater)
        {
            if (body.WaterKind != 0)
            {
                body.WaterKind = 0; body.LiquidDepth = 0f; body.SubmergedProgress = 0f;
                wetItems.Remove(id); simulation.SetWater(id, null);
                changed = true;
            }
            if (changed) { body.Pickable = 1; simulation.Set(body); UpdatePlacement(id); }
            return;
        }

        bool entering = body.WaterKind == 0;
        bool hasLiquid = WorldLiquidSourceResolver.TryResolve(sample, out WorldLiquidSourceTarget source);
        if (entering)
        {
            try { using (WaterEntryMarker.Auto()) TransformOnWaterEntry(id, ref body, source); }
            catch (Exception exception) { Debug.LogError($"[DroppedItems] 入水转换失败，原物品已保留：{exception}"); }
        }
        ItemData data = payloads[id];
        float ratio, threshold;
        using (WaterBuoyancyMarker.Auto())
        {
            ratio = ResolveWeightVolumeRatio(data.Stack);
            threshold = hasLiquid ? ResolveSinkRatioThreshold(source.Liquid.BuoyancyThresholdMultiplier) : SinkRatioThreshold;
        }
        byte kind = (byte)(ShouldSink(ratio, threshold) ? 2 : 1);
        float target = kind == 1 ? ResolveFloatingDepth(ratio) : 1f;
        if (entering || body.WaterKind != kind)
        {
            changed = true;
            float start = entering ? (kind == 1 ? Mathf.Max(FloatingEntryDepth, target) : 0f) : body.LiquidDepth;
            float duration = kind == 1 ? FloatingRiseDuration : ResolveSinkDuration(ratio);
            body.WaterKind = kind; body.LiquidDepth = start; body.SubmergedProgress = 0f;
            simulation.SetWater(id, new LightweightDroppedWaterTransition
            { StartDepth = start, TargetDepth = target, Duration = duration,
                RecedeDuration = kind == 2 ? SubmergedRecedeDuration : 0f });
            if (entering)
                WorldItemWaterEntrySplashEffect.Play(new Vector3(body.Position.x, body.Position.y, 0f),
                    kind == 1 ? ResolveSplashIntensity(target) : 1f);
        }
        wetItems.Add(id);
        if (changed) { body.Pickable = 1; simulation.Set(body); UpdatePlacement(id); }
    }

    /// <summary>火把等规则从定义读取；先准备替代载荷与视觉，成功后保留同一实体及整组数量。</summary>
    private void TransformOnWaterEntry(int id, ref LightweightDroppedBody body, WorldLiquidSourceTarget source)
    {
        // 入水熄灭转换只适用于真实水接触，岩浆等熔体不能把火把变成木棍。
        if (source.Liquid?.WorldWater?.WaterContact != true) return;
        GameRes resources = GameRes.ExistingInstance;
        ItemData original = payloads[id];
        if (!resources.TryGetItemDefinition(original.IDName, out RuntimeItemDefinition definition) ||
            string.IsNullOrWhiteSpace(definition.WaterEntryTransformItemId)) return;
        ItemData replacement = resources.CreateItemData(definition.WaterEntryTransformItemId);
        if (replacement?.Stack == null || !resources.TryGetItemDefinition(replacement.IDName, out RuntimeItemDefinition target) || target.IsActor)
            throw new InvalidOperationException($"无效的入水掉落物转换：{original.IDName}");
        replacement.Guid = id; replacement.inHand = false;
        replacement.Stack.Amount = body.Amount; replacement.Stack.CanBePickedUp = true;
        DroppedItemVisual visual = ResolveVisual(replacement);
        payloads[id] = replacement; visuals[id] = visual;
        body.Rotation = 0f;
    }

    private void ClearTerrainSubscriptions()
    {
        foreach (TerrainWatch watch in terrainWatches.Values) watch.Terrain.Changed -= watch.Handler;
        terrainWatches.Clear(); itemTerrain.Clear(); wetItems.Clear();
        pendingEnvironment.Clear(); pendingEnvironmentSet.Clear();
    }

    #endregion
}
