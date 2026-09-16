using System;
using System.Collections.Generic;
using FlatWorld.DroppedItems;
using FlatWorld.WorldModel;
using UnityEngine;
using static WorldItemWaterRules;

internal sealed partial class DroppedItemRuntime
{
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
        Vector2Int cell = watch.Origin + new Vector2Int(change.LocalCell.X, change.LocalCell.Y);
        if (!spatial.TryGetValue(SpatialCell((Vector2)cell + Vector2.one * 0.5f), out HashSet<int> bucket)) return;
        foreach (int id in bucket)
        {
            DroppedBody body = simulation.Get(id);
            if (Mathf.FloorToInt(body.Position.x) == cell.x && Mathf.FloorToInt(body.Position.y) == cell.y)
                QueueEnvironment(id);
        }
    }

    private void TickWater(float deltaTime)
    {
        waterClock += deltaTime;
        if (waterClock < WaterInterval) return;
        float step = waterClock; waterClock = 0f;
        terrainScratch.Clear(); terrainScratch.AddRange(terrainWatches.Values);
        foreach (TerrainWatch watch in terrainScratch)
        {
            if (!watch.Terrain.IsDisposed) continue;
            foreach (int id in watch.Residents) { QueueEnvironment(id); itemTerrain.Remove(id); }
            watch.Terrain.Changed -= watch.Handler; terrainWatches.Remove(watch.Terrain);
        }
        // 未加载地形只排队重试，严格限制每轮成本，绝不触发同步 Chunk 加载。
        int budget = Mathf.Min(256, pendingEnvironment.Count);
        while (budget-- > 0)
        {
            int id = pendingEnvironment.Dequeue();
            if (!pendingEnvironmentSet.Remove(id) || !simulation.Contains(id)) continue;
            CheckEnvironment(id);
        }
        waterScratch.Clear(); waterScratch.AddRange(wetItems);
        foreach (int id in waterScratch)
        {
            if (!simulation.Contains(id)) continue;
            CheckEnvironment(id);
            DroppedBody body = simulation.Get(id);
            ChunkMgr chunks = ChunkMgr.ExistingInstance;
            if (body.WaterKind == 0 || chunks == null || !chunks.TryGetRuntimeWaterCurrent(body.Position, out RuntimeWaterCurrentSample current)) continue;
            float speed = current.Kind == RuntimeWaterCurrentKind.River ? RiverDriftSpeed :
                current.Kind == RuntimeWaterCurrentKind.Ocean ? OceanDriftSpeed : 0f;
            if (speed <= 0f || current.Direction.sqrMagnitude <= 0.000001f) continue;
            Vector2 destination = domain.Normalize((Vector2)body.Position + current.Direction * (speed * Mathf.Min(step, 1f)));
            if (!WorldItemWaterSystem.TryResolveWaterAt(destination, out bool isWater) || !isWater) continue;
            body.Position = destination; simulation.Set(body); UpdatePlacement(id); CheckEnvironment(id);
        }
    }

    #endregion

    #region 浮沉与入水转换

    private void CheckEnvironment(int id)
    {
        if (!simulation.Contains(id) || simulation.TryGetFlight(id, out _)) return;
        DroppedBody body = simulation.Get(id);
        ChunkMgr chunks = ChunkMgr.ExistingInstance;
        if (chunks == null || !chunks.TryGetRuntimeTerrainTile(body.Position, out RuntimeTerrainTileSample sample))
        {
            QueueEnvironment(id);
            return;
        }
        WatchTerrain(id, sample);
        bool changed = body.Pickable == 0;
        bool isWater = (sample.Cell.Flags & TerrainCellFlags.Water) != 0;
        if (!isWater)
        {
            if (body.WaterKind != 0)
            {
                body.WaterKind = 0; body.WaterDepth = 0f; wetItems.Remove(id); simulation.SetWater(id, null);
                changed = true;
            }
            if (changed) { body.Pickable = 1; simulation.Set(body); UpdatePlacement(id); }
            return;
        }

        bool entering = body.WaterKind == 0;
        if (entering)
        {
            try { TransformOnWaterEntry(id, ref body); }
            catch (Exception exception) { Debug.LogError($"[DroppedItems] 入水转换失败，原物品已保留：{exception}"); }
        }
        ItemData data = payloads[id];
        float ratio = ResolveWeightVolumeRatio(data.Stack);
        float threshold = ResolveSinkRatioThreshold((Vector2)body.Position);
        byte kind = (byte)(ShouldSink(ratio, threshold) ? 2 : 1);
        float target = kind == 1 ? ResolveFloatingDepth(ratio) : 1f;
        if (entering || body.WaterKind != kind)
        {
            changed = true;
            float start = entering ? (kind == 1 ? Mathf.Max(FloatingEntryDepth, target) : 0f) : body.WaterDepth;
            float duration = kind == 1 ? FloatingRiseDuration : ResolveSinkDuration(ratio);
            body.WaterKind = kind; body.WaterDepth = start;
            simulation.SetWater(id, new DroppedWaterTransition
            { StartDepth = start, TargetDepth = target, Duration = duration });
            if (entering)
                WorldItemWaterEntrySplashEffect.Play(new Vector3(body.Position.x, body.Position.y, 0f),
                    kind == 1 ? ResolveSplashIntensity(target) : 1f);
        }
        wetItems.Add(id);
        if (changed) { body.Pickable = 1; simulation.Set(body); UpdatePlacement(id); }
    }

    /// <summary>火把等规则从定义读取；先准备替代载荷与视觉，成功后保留同一实体及整组数量。</summary>
    private void TransformOnWaterEntry(int id, ref DroppedBody body)
    {
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
