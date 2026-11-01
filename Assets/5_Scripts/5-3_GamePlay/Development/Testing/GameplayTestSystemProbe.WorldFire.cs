using System;
using System.Collections.Generic;
using System.Diagnostics;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 火场数值与区块批次规则
    private static void RegisterWorldFireOperations(Dictionary<string, Func<JObject, JObject>> result)
        => result.Add("environment/world_fire", CaptureWorldFireRules);

    private static JObject CaptureWorldFireRules(JObject args)
    {
        var input = new WorldFireSimulationKernel.Input[5];
        for (int i = 0; i < input.Length; i++) input[i] = new WorldFireSimulationKernel.Input
        {
            State = new WorldFireCellState { CellIndex = i, Intensity = 1f },
            Combustibility = new WorldCombustibilityValue(.5f, 100f), HasBurningNeighbour = 1, RandomSeed = 1
        };
        input[1].Combustibility = new WorldCombustibilityValue(1f, .5f);
        input[2].State.Intensity = 0f; input[2].Combustibility = new WorldCombustibilityValue(0f, 100f);
        input[3].State.Intensity = 0f; input[3].Combustibility = new WorldCombustibilityValue(1f, 100f);
        input[4].State.Intensity = 0f; input[4].HasBurningNeighbour = 0;
        var output = new WorldFireSimulationKernel.Output[input.Length];
        WorldFireSimulationKernel.Advance(input, output, 1f);
        var data = new JObject
        {
            ["steadyFire"] = output[0].State.Intensity, ["consumedSeconds"] = output[0].ConsumedFuelSeconds,
            ["exhaustedFire"] = output[1].State.Intensity, ["exhaustedConsumption"] = output[1].ConsumedFuelSeconds,
            ["zeroProbability"] = output[2].State.Intensity, ["certainProbability"] = output[3].State.Intensity,
            ["newConsumption"] = output[3].ConsumedFuelSeconds, ["noNeighbour"] = output[4].State.Intensity
        };
        WorldFireSimulationKernel.Advance(input, output, 100f);
        data["hundredSecondsFire"] = output[0].State.Intensity;
        data["hundredSecondsConsumed"] = output[0].ConsumedFuelSeconds;
        var one = new[] { new WorldFireSimulationKernel.Input
        { Combustibility = new WorldCombustibilityValue(.5f, 100f), HasBurningNeighbour = 1, RandomSeed = 1 } };
        var oneOutput = new WorldFireSimulationKernel.Output[1];
        WorldFireSimulationKernel.Advance(one, oneOutput, 1f / 60f);
        float originalCycle = oneOutput[0].State.Intensity;
        WorldFireSimulationKernel.Advance(one, oneOutput, .1f);
        float environmentCycle = oneOutput[0].State.Intensity;
        data["probabilityTimeScaled"] = originalCycle == 0f && environmentCycle == 1f;

        using var world = new WorldRuntime("fire_rules", 1);
        var address = new FlatWorld.WorldModel.WorldAddress("surface", new Int2(0, 0));
        var profile = new ChunkGenerationProfileSnapshot("fire_rules", 1, 100, 100);
        ChunkGenerationRequest request = world.BeginChunkGeneration(address, 1, profile);
        using var buffer = new ChunkTerrainBuffer(100, 100);
        using var generation = new ChunkGenerationResult(request, buffer);
        if (!world.TryCommit(generation, out string reason) || !world.TryGetChunk(address, out ChunkRuntime chunk))
            throw new InvalidOperationException(reason);
        int notifications = 0, changedCells = 0;
        chunk.Terrain.Changed += change =>
        {
            if (change.Kind != TerrainChangeKind.Fire) return;
            notifications++; changedCells = 0;
            foreach (int index in change.GetCellIndices(100)) changedCells++;
        };
        var states = new WorldFireCellState[10000];
        for (int i = 0; i < states.Length; i++) states[i] = new WorldFireCellState
        { CellIndex = i, Intensity = .5f, SurfaceFuelBurnSeconds = 3.5f };
        chunk.Terrain.SetFireStatesBatch(states);
        data["batchNotifications"] = notifications; data["batchChangedCells"] = changedCells;
        data["activeCount"] = chunk.Terrain.ActiveFireCellIndices.Count;
        WorldFireCellState[] frozen = chunk.Terrain.CopyFireStates();
        chunk.Terrain.SetFire(99, 99, 0);
        data["removedCount"] = chunk.Terrain.ActiveFireCellIndices.Count;
        chunk.Terrain.RestoreFireStates(frozen);
        data["restoredCount"] = chunk.Terrain.ActiveFireCellIndices.Count;
        data["restoredProgress"] = chunk.Terrain.GetFireState(99, 99).SurfaceFuelBurnSeconds;
        chunk.Terrain.SetFire(17, 29, 255);
        data["singleWriteCorrectCell"] = chunk.Terrain.GetFire(17, 29);
        data["otherCellPreserved"] = chunk.Terrain.GetFire(0, 0);

        var stressInput = new WorldFireSimulationKernel.Input[10000];
        var stressOutput = new WorldFireSimulationKernel.Output[10000];
        for (int i = 0; i < stressInput.Length; i++) stressInput[i] = new WorldFireSimulationKernel.Input
        { State = states[i], Combustibility = new WorldCombustibilityValue(.1f, 100f) };
        WorldFireSimulationKernel.Advance( stressInput, stressOutput, 1f / 60f);
        long allocations = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        for (int i = 0; i < 60; i++) WorldFireSimulationKernel.Advance( stressInput, stressOutput, 1f / 60f);
        data["kernelAllocatedBytes"] = GC.GetAllocatedBytesForCurrentThread() - allocations;
        data["kernelMeanMilliseconds"] = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency / 60d;
        data["kernelCells"] = stressOutput.Length;
        CaptureCombustibilityLayerRules(data);
        CaptureWorldFireSimulationPipelineRules(data);
        CaptureWorldFireDirtyFrontierRules(data);
        CaptureWorldFireCrossChunkHeatRules(data);
        return data;
    }

    private static void CaptureCombustibilityLayerRules(JObject data)
    {
        using var buffer = new ChunkTerrainBuffer(2, 1);
        using var terrain = buffer.Seal();
        terrain.CacheCombustibilityBase(0, new WorldCombustibilityValue(.1f, 100f));
        terrain.ConsumeCombustibilityFuel(0, 40f);
        data["remainingFuel"] = terrain.GetCombustibility(0, 0).FuelSeconds;
        var field = new WorldCombustibilitySourceField();
        field.Changed += (cell, value) => terrain.SetCombustibilitySourceContribution(cell.X, value);
        object first = new(), second = new();
        var cells = new[] { new Int2(0, 0) };
        field.ReplaceSource(first, cells, new WorldCombustibilityValue(.4f, 50f));
        field.ReplaceSource(second, cells, new WorldCombustibilityValue(.2f, 20f));
        data["combinedProbability"] = terrain.GetCombustibility(0, 0).IgnitionProbability;
        data["combinedFuel"] = terrain.GetCombustibility(0, 0).FuelSeconds;
        field.ReplaceSource(first, cells, new WorldCombustibilityValue(.4f, 50f));
        data["repeatedSourceFuel"] = terrain.GetCombustibility(0, 0).FuelSeconds;
        field.RemoveSource(first);
        data["removedSourceFuel"] = terrain.GetCombustibility(0, 0).FuelSeconds;
        terrain.SetCombustibility(1, 0, new WorldCombustibilityValue(.001f, 1000f));
        data["editedProbability"] = terrain.GetCombustibility(1, 0).IgnitionProbability;
        data["editedFuel"] = terrain.GetCombustibility(1, 0).FuelSeconds;
        terrain.ConsumeCombustibilityFuel(1, 1000f);
        var saved = terrain.CopyCombustibilityChanges();
        using var restoredBuffer = new ChunkTerrainBuffer(2, 1);
        using var restored = restoredBuffer.Seal();
        restored.RestoreCombustibilityChanges(saved);
        data["exhaustedSavedFuel"] = restored.GetCombustibility(1, 0).FuelSeconds;
        data["exhaustedSavedCells"] = saved.Length;
        var encoded = MemoryPack.MemoryPackSerializer.Serialize(CombustibilityCellSaveData.Capture(saved[1]));
        data["fuelSaveRoundTrip"] = MemoryPack.MemoryPackSerializer.Deserialize<CombustibilityCellSaveData>(encoded).ConsumedFuelSeconds;
        var legacy = new ChunkSaveRecord();
        legacy.FireCells.Add(new FireCellSaveData { CellIndex = 0, Intensity = 1f, SurfaceFuelBurnSeconds = 42f });
        var envelope = new SqliteChunkSnapshot { Terrain = MemoryPack.MemoryPackSerializer.Serialize(legacy) };
        var upgraded = SaveDataMgr.ArchiveSchemas.MigrateRecord(new FlatWorld.Persistence.SaveArchiveRecord(
            "chunk", "rules", "chunk:0,0", 0, 0, "flatworld.chunk", 1, MemoryPack.MemoryPackSerializer.Serialize(envelope)));
        var migrated = MemoryPack.MemoryPackSerializer.Deserialize<ChunkSaveRecord>(
            MemoryPack.MemoryPackSerializer.Deserialize<SqliteChunkSnapshot>(upgraded.Payload).Terrain);
        data["migratedConsumedFuel"] = migrated.CombustibilityCells[0].ConsumedFuelSeconds;
        data["migratedChunkSchema"] = upgraded.SchemaVersion;
        using var oilBuffer = new ChunkTerrainBuffer(1, 1);
        using var oil = oilBuffer.Seal();
        oil.CacheCombustibilityBase(0, new WorldCombustibilityValue(.001f, 1000f), 17);
        oil.ConsumeCombustibilityFuel(0, 100f);
        oil.CacheCombustibilityBase(0, new WorldCombustibilityValue(.001f, 900f), 17);
        data["sameMaterialRemainingFuel"] = oil.GetCombustibility(0, 0).FuelSeconds;
        int changes = 0;
        oil.Changed += change => { if (change.Kind == TerrainChangeKind.Combustibility) changes++; };
        oil.CacheCombustibilityBase(0, new WorldCombustibilityValue(.1f, 60f), 18);
        data["newMaterialRemainingFuel"] = oil.GetCombustibility(0, 0).FuelSeconds;
        data["newMaterialConsumptionNotifications"] = changes;
        using var sourceBuffer = new ChunkTerrainBuffer(1, 1);
        using var sourceTerrain = sourceBuffer.Seal();
        sourceTerrain.SetCombustibilitySourceContribution(0, new WorldCombustibilityValue(.1f, 100f));
        sourceTerrain.ConsumeCombustibilityFuel(0, 40f);
        sourceTerrain.SetCombustibilitySourceContribution(0, default, preserveFuelConsumption: true);
        sourceTerrain.SetCombustibilitySourceContribution(0, new WorldCombustibilityValue(.1f, 100f));
        data["rehydratedSourceFuel"] = sourceTerrain.GetCombustibility(0, 0).FuelSeconds;
        sourceTerrain.SetCombustibility(0, 0, new WorldCombustibilityValue(0f, 10f));
        data["absoluteEditedFuel"] = sourceTerrain.GetCombustibility(0, 0).FuelSeconds;
        data["absoluteEditedProbability"] = sourceTerrain.GetCombustibility(0, 0).IgnitionProbability;
    }

    private static void CaptureWorldFireSimulationPipelineRules(JObject data)
    {
        string key = "__lua_fire_pipeline_" + Guid.NewGuid().ToString("N");
        WorldRuntime world = GameplayWorldRegistry.GetOrCreateOwnedWorldModel(key, 1);
        try
        {
            var address = new FlatWorld.WorldModel.WorldAddress("surface", new Int2(0, 0));
            var profile = new ChunkGenerationProfileSnapshot("fire_pipeline", 1, 100, 100);
            var request = world.BeginChunkGeneration(address, 1, profile);
            using var buffer = new ChunkTerrainBuffer(100, 100);
            using var generation = new ChunkGenerationResult(request, buffer);
            if (!world.TryCommit(generation, out string reason) || !world.TryGetChunk(address, out var chunk))
                throw new InvalidOperationException(reason);
            var fires = new WorldFireCellState[10000];
            for (int i = 0; i < fires.Length; i++)
            {
                chunk.Terrain.SetCombustibility(i % 100, i / 100, new WorldCombustibilityValue(.1f, 100f));
                fires[i] = new WorldFireCellState { CellIndex = i, Intensity = 1f };
            }
            chunk.Terrain.SetFireStatesBatch(fires);
            var simulation = new WorldFireSimulationContext(world, 1);
            simulation.Simulate(1f / 60f, sampleWorldClock: false);
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            for (int i = 0; i < 10; i++) simulation.Simulate(1f / 60f, sampleWorldClock: false);
            data["pipelineAllocatedBytes"] = GC.GetAllocatedBytesForCurrentThread() - allocated;
            data["pipelineMeanMilliseconds"] = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency / 10d;
            data["pipelineCells"] = simulation.ActiveFireCount;
            data["pipelineRemainingFuel"] = chunk.Terrain.GetCombustibility(50, 50).FuelSeconds;
        }
        finally { GameplayWorldRegistry.ReleaseOwnedWorldModel(key, world); }
    }

    private static void CaptureWorldFireDirtyFrontierRules(JObject data)
    {
        string key = "__lua_fire_frontier_" + Guid.NewGuid().ToString("N");
        WorldRuntime world = GameplayWorldRegistry.GetOrCreateOwnedWorldModel(key, 1);
        try
        {
            var left = CreateWorldFireHeatRuleChunk(world, Vector2Int.zero);
            var right = CreateWorldFireHeatRuleChunk(world, new Vector2Int(16, 0));
            left.Terrain.SetCombustibility(15, 5, new WorldCombustibilityValue(1f, 10f));
            right.Terrain.SetCombustibility(0, 5, new WorldCombustibilityValue(1f, 10f));
            left.Terrain.SetFire(15, 5, 255);
            var simulation = new WorldFireSimulationContext(world, 1);
            ulong startingTopology = left.Terrain.ActiveFireTopologyRevision;
            simulation.Simulate(.1f, sampleWorldClock: false);
            data["frontierCrossChunkIgnited"] = right.Terrain.GetFireState(0, 5).Intensity;
            simulation.Simulate(.1f, sampleWorldClock: false);
            data["frontierCrossChunkFuel"] = right.Terrain.GetCombustibility(0, 5).FuelSeconds;
            data["frontierStableBurningTopology"] = left.Terrain.ActiveFireTopologyRevision == startingTopology;

            // 外部把燃料清零后，缓存邻居不能继续点燃新火。
            left.Terrain.SetCombustibility(15, 5, new WorldCombustibilityValue(1f, 0f));
            right.Terrain.SetFire(0, 5, 0);
            simulation.Simulate(.1f, sampleWorldClock: false);
            data["frontierExhaustedSource"] = left.Terrain.GetFireState(15, 5).Intensity;
            data["frontierNoIgnitionWithoutFuel"] = right.Terrain.GetFireState(0, 5).Intensity;

            left.Terrain.SetCombustibility(2, 2, new WorldCombustibilityValue(1f, 10f));
            left.Terrain.SetCombustibility(10, 9, new WorldCombustibilityValue(1f, 10f));
            left.Terrain.SetFire(2, 2, 255);
            simulation.Simulate(.1f, sampleWorldClock: false);
            ulong beforeSwap = left.Terrain.ActiveFireTopologyRevision;
            left.Terrain.SetFireStatesBatch(new[]
            {
                new WorldFireCellState { CellIndex = 2 + 2 * 16 },
                new WorldFireCellState { CellIndex = 10 + 9 * 16, Intensity = 1f }
            });
            data["frontierSwapSameCount"] = left.Terrain.ActiveFireCellIndices.Count;
            data["frontierSwapTopologyChanged"] = left.Terrain.ActiveFireTopologyRevision > beforeSwap;
            simulation.Simulate(.1f, sampleWorldClock: false);
            data["frontierSwapNewFuel"] = left.Terrain.GetCombustibility(10, 9).FuelSeconds;
            data["frontierSwapOldFuel"] = left.Terrain.GetCombustibility(2, 2).FuelSeconds;
            ulong afterSimulation = left.Terrain.ActiveFireTopologyRevision;
            int noChanges = left.Terrain.SetFireStatesBatch(new[] { left.Terrain.GetFireState(10, 9) });
            data["frontierNoopTopologyStable"] = noChanges == 0 &&
                left.Terrain.ActiveFireTopologyRevision == afterSimulation;
        }
        finally { GameplayWorldRegistry.ReleaseOwnedWorldModel(key, world); }
    }

    private static void CaptureWorldFireCrossChunkHeatRules(JObject data)
    {
        string key = "__lua_fire_heat_" + Guid.NewGuid().ToString("N");
        string otherKey = key + "_other";
        WorldRuntime world = GameplayWorldRegistry.GetOrCreateOwnedWorldModel(key, 1);
        WorldRuntime other = GameplayWorldRegistry.GetOrCreateOwnedWorldModel(otherKey, 1);
        try
        {
            var right = CreateWorldFireHeatRuleChunk(world, new Vector2Int(16, 0));
            var upper = CreateWorldFireHeatRuleChunk(world, new Vector2Int(0, 16));
            right.Terrain.SetFire(0, 5, 255);
            upper.Terrain.SetFire(5, 0, 255);
            float[] frozen = WorldFireSystem.CaptureWorldFireHeatWeights(key, Vector2Int.zero, 16, 16);
            data["rightChunkHeating"] = frozen?[5 * 16 + 15] ?? 0f;
            data["upperChunkHeating"] = frozen?[15 * 16 + 5] ?? 0f;
            data["heatActiveChunks"] = WorldFireSystem.GetActiveWorldFireChunks(key).Count;
            data["otherWorldHeating"] = WorldFireSystem.GetWorldFireHeating(otherKey, new Vector2(15.5f, 5.5f), 20f);
            right.Terrain.SetFire(0, 5, 0); upper.Terrain.SetFire(5, 0, 0);
            data["heatAfterExtinguish"] = WorldFireSystem.GetWorldFireHeating(key, new Vector2(15.5f, 5.5f), 20f);
            data["heatActiveChunksAfterExtinguish"] = WorldFireSystem.GetActiveWorldFireChunks(key).Count;
            data["frozenHeatPreserved"] = frozen?[5 * 16 + 15] ?? 0f;
        }
        finally
        {
            GameplayWorldRegistry.ReleaseOwnedWorldModel(key, world);
            GameplayWorldRegistry.ReleaseOwnedWorldModel(otherKey, other);
        }
    }

    private static ChunkRuntime CreateWorldFireHeatRuleChunk(WorldRuntime world, Vector2Int origin)
    {
        var address = new FlatWorld.WorldModel.WorldAddress("surface", new Int2(origin.x, origin.y));
        var profile = new ChunkGenerationProfileSnapshot("fire_heat", 1, 16, 16);
        var request = world.BeginChunkGeneration(address, 1, profile);
        using var generation = new ChunkGenerationResult(request, new ChunkTerrainBuffer(16, 16));
        if (!world.TryCommit(generation, out string reason) || !world.TryGetChunk(address, out ChunkRuntime chunk))
            throw new InvalidOperationException(reason);
        return chunk;
    }
    #endregion
}
