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
        WorldFireSimulationKernel.Parameters settings = WorldFireSimulationKernel.Parameters.Default;
        var input = new WorldFireSimulationKernel.Input[6];
        for (int i = 0; i < input.Length; i++) input[i] = new WorldFireSimulationKernel.Input
        {
            State = new WorldFireCellState { CellIndex = i, Intensity = .25f }, HasFuel = 1, HasGrass = 1,
            SurfaceFuelDurationSeconds = settings.GrassBurnDurationSeconds, SupportsCombustion = 1, RandomSeed = 1
        };
        input[1].HasFuel = 0;
        input[2].Rain = 1f;
        input[3].WaterContact = 1;
        input[4].SupportsCombustion = 0;
        input[5].State.Intensity = 0f; input[5].IgnitionPressure = 1000000f;
        var output = new WorldFireSimulationKernel.Output[input.Length];
        WorldFireSimulationKernel.Advance(settings, input, output, 1f);
        var data = new JObject
        {
            ["growth"] = output[0].State.Intensity, ["decay"] = output[1].State.Intensity,
            ["rain"] = output[2].State.Intensity, ["water"] = output[3].State.Intensity,
            ["oxygen"] = output[4].State.Intensity, ["newFire"] = output[5].State.Intensity,
            ["newExposure"] = output[5].ExposureSeconds, ["waterExposure"] = output[3].ExposureSeconds
        };
        input[0].State.SurfaceFuelBurnSeconds = 9.9f;
        input[1] = input[0]; input[1].State.SurfaceFuelBurnSeconds = 59.9f; input[1].SurfaceFuelDurationSeconds = 60f;
        WorldFireSimulationKernel.Advance(settings, input, output, 1f);
        data["grassConsumed"] = output[0].ConsumedSurfaceFuel;
        data["floorConsumed"] = output[1].ConsumedSurfaceFuel;

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
        { State = states[i], HasFuel = 1, SupportsCombustion = 1, SurfaceFuelDurationSeconds = 10f };
        WorldFireSimulationKernel.Advance(settings, stressInput, stressOutput, 1f / 60f);
        long allocations = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        for (int i = 0; i < 60; i++) WorldFireSimulationKernel.Advance(settings, stressInput, stressOutput, 1f / 60f);
        data["kernelAllocatedBytes"] = GC.GetAllocatedBytesForCurrentThread() - allocations;
        data["kernelMeanMilliseconds"] = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency / 60d;
        data["kernelCells"] = stressOutput.Length;
        CaptureWorldFireCrossChunkHeatRules(data);
        return data;
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
