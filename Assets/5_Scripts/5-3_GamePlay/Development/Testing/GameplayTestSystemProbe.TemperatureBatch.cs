using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 单点温度批次正式接口采样

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterTemperatureBatchRules()
        => Register("environment", "thermal_point_batch", TemperaturePointBatch);

    private static LiquidDefinition TemperatureBatchLava(float target) =>
        new(LiquidTypeCatalog.LavaId, "probe", "", "", "", Color.white,
            1f, false, 0f, null, null, worldWater: new WorldLiquidSettings
            { RadiantHeatRadius = 4f, RadiantTargetTemperature = target });

    private static Dictionary<string, LiquidDefinition> TemperatureBatchDefinitions() =>
        new(StringComparer.Ordinal) { [LiquidTypeCatalog.LavaId] = TemperatureBatchLava(100f) };

    private static ChunkRuntime TemperatureBatchChunk(WorldRuntime world, string dimension, Vector2Int origin,
        params Vector2Int[] liquids)
    {
        var address = new FlatWorld.WorldModel.WorldAddress(dimension, new Int2(origin.x, origin.y));
        var profile = new ChunkGenerationProfileSnapshot("thermal_batch", 1, 16, 16);
        ChunkGenerationRequest request = world.BeginChunkGeneration(address, 1, profile);
        using var buffer = new ChunkTerrainBuffer(16, 16);
        int lava = buffer.LiquidTypes.GetIndex(LiquidTypeCatalog.LavaId);
        foreach (Vector2Int liquid in liquids) buffer.SetLiquid(liquid.x, liquid.y, lava, 60);
        using var result = new ChunkGenerationResult(request, buffer);
        if (!world.TryCommit(result, out string reason) || !world.TryGetChunk(address, out ChunkRuntime chunk))
            throw new InvalidOperationException("单点温度批次夹具创建失败：" + reason);
        return chunk;
    }

    private static float TemperatureBatchPoint(LiquidTemperatureField.PointSamplingBatch batch, Vector2Int cell)
    {
        float heating = 0f, cooling = 0f;
        batch.AccumulateAtPoint(cell, 20f, ref heating, ref cooling);
        return ThermalInfluenceRuntime.Compose(20f, heating, cooling);
    }

    private static float TemperatureBatchFreshPoint(LiquidTemperatureField field,
        IReadOnlyDictionary<string, LiquidDefinition> definitions, Vector2Int cell, int revision = 0)
    {
        using var batch = field.BeginPointSamplingBatch(definitions, revision);
        return TemperatureBatchPoint(batch, cell);
    }

    private static string TemperatureBatchRejected(Action sample)
    {
        try { sample(); return string.Empty; }
        catch (Exception exception) { return exception.GetType().Name; }
    }

    // 独立世界复用正式热源计算，只采原始结果和邻区读取次数，Lua 持有全部玩法断言。
    private static JObject TemperaturePointBatch(JObject args)
    {
        var definitions = TemperatureBatchDefinitions();
        using var world = new WorldRuntime("thermal_batch", 1);
        ChunkRuntime first = TemperatureBatchChunk(world, "surface", Vector2Int.zero,
            Vector2Int.zero, new Vector2Int(2, 0));
        ChunkRuntime neighbor = TemperatureBatchChunk(world, "surface", new Vector2Int(16, 0), Vector2Int.zero);
        var field = new LiquidTemperatureField(world, "surface", 16, 16, default);
        var result = new JObject();
        var points = new JArray();
        var reference = new JArray();
        var firstSources = new LiquidTemperatureField.SourceChunk();
        var neighborSources = new LiquidTemperatureField.SourceChunk();
        firstSources.Refresh(first.Terrain, definitions);
        neighborSources.Refresh(neighbor.Terrain, definitions);
        using (var outer = field.BeginPointSamplingBatch(definitions, 0))
        {
            for (int i = 0; i < 64; i++)
            {
                Vector2Int cell = new(i % 16, i / 16);
                points.Add(TemperatureBatchPoint(outer, cell));
                float heating = 0f, cooling = 0f;
                Vector2 center = (Vector2)cell + Vector2.one * 0.5f;
                firstSources.AccumulateAtPoint(Vector2Int.zero, center, default, 20f, ref heating, ref cooling);
                neighborSources.AccumulateAtPoint(new Vector2Int(16, 0), center, default,
                    20f, ref heating, ref cooling);
                reference.Add(ThermalInfluenceRuntime.Compose(20f, heating, cooling));
            }
            result["preparedChunks"] = outer.PreparedChunkCount;
            result["dependencyLookups"] = outer.DependencyLookups;
            using (var inner = field.BeginPointSamplingBatch(definitions, 0))
                result["nested"] = TemperatureBatchPoint(inner, Vector2Int.zero);
            result["outerAfterNested"] = TemperatureBatchPoint(outer, Vector2Int.zero);
            result["outerLookupsAfterNested"] = outer.DependencyLookups;
            outer.Dispose();
            result["releasedRejected"] = TemperatureBatchRejected(() => TemperatureBatchPoint(outer, Vector2Int.zero));
        }
        result["points"] = points;
        result["reference"] = reference;
        long liquidRevision = first.Terrain.SurfaceLiquidRevision;
        first.Terrain.SetEnvironmentValue("moisture", 0, 0, 0.8f);
        result["soilRevisionUnchanged"] = first.Terrain.SurfaceLiquidRevision == liquidRevision;
        result["soil"] = TemperatureBatchFreshPoint(field, definitions, Vector2Int.zero);
        TerrainSupportLayer.Set(first.Terrain, 0, 0, 90, 2);
        result["covered"] = TemperatureBatchFreshPoint(field, definitions, Vector2Int.zero);
        TerrainSupportLayer.Set(first.Terrain, 0, 0, 0, 2);
        result["uncovered"] = TemperatureBatchFreshPoint(field, definitions, Vector2Int.zero);
        first.Terrain.SetLiquid(0, 0, 0, 0);
        first.Terrain.SetLiquid(2, 0, 0, 0);
        result["drained"] = TemperatureBatchFreshPoint(field, definitions, Vector2Int.zero);
        first.Terrain.SetLiquid(0, 0, first.Terrain.LiquidTypes.GetIndex(LiquidTypeCatalog.LavaId), 60);
        using (var beforeReload = field.BeginPointSamplingBatch(definitions, 0))
        {
            result["beforeReload"] = TemperatureBatchPoint(beforeReload, Vector2Int.zero);
            definitions[LiquidTypeCatalog.LavaId] = TemperatureBatchLava(180f);
            using (var afterReload = field.BeginPointSamplingBatch(definitions, 1))
                result["afterReload"] = TemperatureBatchPoint(afterReload, Vector2Int.zero);
            result["reloadRejectedOldScope"] = TemperatureBatchRejected(
                () => TemperatureBatchPoint(beforeReload, Vector2Int.zero));
        }
        result["identity"] = TemperatureBatchIdentity();
        result["topology"] = TemperatureBatchTopology();
        result["lifecycle"] = TemperatureBatchLifecycle();
        return result;
    }

    private static JObject TemperatureBatchIdentity()
    {
        var definitions = TemperatureBatchDefinitions();
        using var world = new WorldRuntime("thermal_identity", 1);
        ChunkRuntime first = TemperatureBatchChunk(world, "surface", Vector2Int.zero, Vector2Int.zero);
        ChunkTerrainData old = first.Terrain;
        long oldRevision = old.SurfaceLiquidRevision;
        var field = new LiquidTemperatureField(world, "surface", 16, 16, default);
        var result = new JObject { ["initial"] = TemperatureBatchFreshPoint(field, definitions, Vector2Int.zero) };
        ChunkRuntime replacement = TemperatureBatchChunk(world, "surface", Vector2Int.zero, new Vector2Int(15, 15));
        result["sameRevision"] = oldRevision == replacement.Terrain.SurfaceLiquidRevision;
        result["oldDisposed"] = old.IsDisposed;
        result["rebound"] = TemperatureBatchFreshPoint(field, definitions, Vector2Int.zero);
        var cave = new LiquidTemperatureField(world, "cave", 16, 16, default);
        result["missingDimension"] = TemperatureBatchFreshPoint(cave, definitions, Vector2Int.zero);
        TemperatureBatchChunk(world, "cave", Vector2Int.zero, Vector2Int.zero);
        result["newDimension"] = TemperatureBatchFreshPoint(cave, definitions, Vector2Int.zero);
        result["surfaceAfterCave"] = TemperatureBatchFreshPoint(field, definitions, Vector2Int.zero);
        result["evicted"] = world.EvictChunk(replacement.Address);
        result["unloaded"] = TemperatureBatchFreshPoint(field, definitions, new Vector2Int(15, 15));
        TemperatureBatchChunk(world, "surface", Vector2Int.zero, Vector2Int.zero);
        result["reloaded"] = TemperatureBatchFreshPoint(field, definitions, Vector2Int.zero);
        return result;
    }

    private static JObject TemperatureBatchTopology()
    {
        var definitions = TemperatureBatchDefinitions();
        using var world = new WorldRuntime("thermal_topology", 1);
        var planet = new PlanetData { TopologyMode = WorldTopologyMode.Wrapped,
            Radius = 16, ChunkSize = new Vector2Int(16, 16) };
        bool wrappedReady = WorldTopologyBounds.TryCreate(planet, out WorldTopologyBounds bounds);
        TemperatureBatchChunk(world, "surface", new Vector2Int(-16, 0), Vector2Int.zero);
        var wrapped = new LiquidTemperatureField(world, "surface", 16, 16, bounds);
        var flat = new LiquidTemperatureField(world, "surface", 16, 16, default);
        using var batch = wrapped.BeginPointSamplingBatch(definitions, 0);
        return new JObject
        {
            ["ready"] = wrappedReady,
            ["seam"] = TemperatureBatchPoint(batch, new Vector2Int(15, 0)),
            ["equivalent"] = TemperatureBatchPoint(batch, new Vector2Int(47, 0)),
            ["preparedChunks"] = batch.PreparedChunkCount,
            ["dependencyLookups"] = batch.DependencyLookups,
            ["unwrapped"] = TemperatureBatchFreshPoint(flat, definitions, new Vector2Int(15, 0))
        };
    }

    private static JObject TemperatureBatchLifecycle()
    {
        var definitions = TemperatureBatchDefinitions();
        using var world = new WorldRuntime("thermal_lifecycle", 1);
        TemperatureBatchChunk(world, "surface", Vector2Int.zero, Vector2Int.zero);
        var old = new LiquidTemperatureField(world, "surface", 16, 16, default);
        using var oldBatch = old.BeginPointSamplingBatch(definitions, 0);
        var result = new JObject { ["beforeEpoch"] = TemperatureBatchPoint(oldBatch, Vector2Int.zero) };
        world.BeginNewEpoch(2);
        result["oldScopeAfterEpoch"] = TemperatureBatchPoint(oldBatch, Vector2Int.zero);
        using var rejected = old.BeginPointSamplingBatch(definitions, 0);
        result["oldFieldReopened"] = rejected.IsCreated;
        var current = new LiquidTemperatureField(world, "surface", 16, 16, default);
        using var currentBatch = current.BeginPointSamplingBatch(definitions, 0);
        result["newEpoch"] = TemperatureBatchPoint(currentBatch, Vector2Int.zero);
        world.Dispose();
        result["afterDispose"] = TemperatureBatchPoint(currentBatch, Vector2Int.zero);
        result["remainingChunks"] = world.Chunks.Count;
        return result;
    }

    #endregion
}
