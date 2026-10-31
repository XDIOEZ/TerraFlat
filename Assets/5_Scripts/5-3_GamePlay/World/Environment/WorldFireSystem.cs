using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using Unity.Profiling;
using UnityEngine;

/// <summary>按所属世界推进火焰传播，燃料只读取可燃性层。</summary>
public static class WorldFireSystem
{
    #region 世界火场公共入口
    public const float AmbientTargetTemperature = 180f;
    public const float MaterialContactTemperature = 700f;
    public const int HeatRadius = 3;
    internal static readonly Vector2Int[] Neighbours =
    {
        new(-1, -1), new(0, -1), new(1, -1), new(-1, 0),
        new(1, 0), new(-1, 1), new(0, 1), new(1, 1)
    };
    private static readonly ConditionalWeakTable<WorldRuntime, FireWorldReader> readers = new();

    private sealed class FireWorldReader
    {
        internal readonly WorldRuntime World;
        private readonly WorldTopologyDomain topology;
        private readonly Dictionary<Vector2Int, ChunkRuntime> chunks = new();
        private readonly Dictionary<Vector2Int, FireChunkWatch> watches = new();
        internal readonly List<ChunkRuntime> ActiveChunks = new();
        internal int ActiveFireCount { get; private set; }
        private int width, height;
        private sealed class FireChunkWatch
        {
            internal ChunkRuntime Chunk;
            internal ChunkTerrainData Terrain;
            internal int Count;
            internal Action<ChunkTerrainChanged> Handler;
        }
        internal FireWorldReader(WorldRuntime world)
        {
            World = world; topology = WorldTopologyRuntime.GetActiveDomain();
            foreach (ChunkRuntime chunk in world.Chunks.Values) Observe(chunk);
            world.Events.Subscribe<ChunkCommitted>(change => { if (world.TryGetChunk(change.Address, out var chunk)) Observe(chunk); });
            world.Events.Subscribe<ChunkDataStatusChanged>(change =>
            {
                if (change.Current == ChunkDataStatus.Evicting || change.Current == ChunkDataStatus.Absent)
                    Remove(new Vector2Int(change.Address.ChunkOrigin.X, change.Address.ChunkOrigin.Y));
                else if (world.TryGetChunk(change.Address, out var chunk)) Observe(chunk);
            });
        }
        private void Observe(ChunkRuntime chunk)
        {
            if (chunk.DataStatus != ChunkDataStatus.Ready || chunk.Terrain == null || chunk.Terrain.IsDisposed) return;
            width = chunk.Terrain.Width; height = chunk.Terrain.Height;
            Vector2Int origin = new(chunk.Address.ChunkOrigin.X, chunk.Address.ChunkOrigin.Y);
            if (watches.TryGetValue(origin, out var existing) && ReferenceEquals(existing.Terrain, chunk.Terrain)) return;
            Remove(origin);
            chunks[origin] = chunk;
            var watch = new FireChunkWatch { Chunk = chunk, Terrain = chunk.Terrain, Count = chunk.Terrain.ActiveFireCellIndices.Count };
            watch.Handler = change =>
            {
                if (change.Kind != TerrainChangeKind.Fire) return;
                int count = watch.Terrain.ActiveFireCellIndices.Count;
                if (watch.Count == 0 && count > 0) ActiveChunks.Add(watch.Chunk);
                else if (watch.Count > 0 && count == 0) ActiveChunks.Remove(watch.Chunk);
                ActiveFireCount += count - watch.Count; watch.Count = count;
            };
            watches.Add(origin, watch); ActiveFireCount += watch.Count;
            if (watch.Count > 0) ActiveChunks.Add(chunk);
            watch.Terrain.Changed += watch.Handler;
        }
        private void Remove(Vector2Int origin)
        {
            chunks.Remove(origin);
            if (!watches.TryGetValue(origin, out var watch)) return;
            watch.Terrain.Changed -= watch.Handler;
            if (watch.Count > 0) ActiveChunks.Remove(watch.Chunk);
            ActiveFireCount -= watch.Count; watches.Remove(origin);
        }
        internal bool MayHeatChunk(Vector2Int origin, int chunkWidth, int chunkHeight)
        {
            if (ActiveFireCount == 0 || width == 0) return false;
            int minimumX = Mathf.FloorToInt((float)(origin.x - HeatRadius) / width) * width;
            int minimumY = Mathf.FloorToInt((float)(origin.y - HeatRadius) / height) * height;
            for (int y = minimumY; y <= origin.y + chunkHeight - 1 + HeatRadius; y += height)
            for (int x = minimumX; x <= origin.x + chunkWidth - 1 + HeatRadius; x += width)
            {
                int px = topology.NormalizeX(x), py = topology.NormalizeY(y);
                Vector2Int key = new(Mathf.FloorToInt((float)px / width) * width, Mathf.FloorToInt((float)py / height) * height);
                if (watches.TryGetValue(key, out var watch) && watch.Count > 0) return true;
            }
            return false;
        }
        internal float Read(Vector2Int cell)
        {
            if (ActiveFireCount == 0 || width == 0) return 0f;
            cell = new Vector2Int(topology.NormalizeX(cell.x), topology.NormalizeY(cell.y));
            Vector2Int origin = new(Mathf.FloorToInt((float)cell.x / width) * width, Mathf.FloorToInt((float)cell.y / height) * height);
            if (!chunks.TryGetValue(origin, out var chunk) || chunk.DataStatus != ChunkDataStatus.Ready ||
                chunk.Terrain == null || chunk.Terrain.IsDisposed || chunk.Terrain.ActiveFireCellIndices.Count == 0) return 0f;
            return chunk.Terrain.GetFireState(cell.x - origin.x, cell.y - origin.y).Intensity;
        }
    }

    private static FireWorldReader GetWorldReader(string worldKey)
    {
        if (string.IsNullOrEmpty(worldKey) || !GameplayWorldRegistry.TryGetOwnedWorldModel(worldKey, out WorldRuntime model)) return null;
        if (readers.TryGetValue(model, out FireWorldReader reader)) return reader;
        using var scope = GameplayWorldRegistry.TryGet(worldKey, out _) ? GameplayWorldRegistry.UseWorld(worldKey) : default;
        reader = new FireWorldReader(model); readers.Add(model, reader);
        return reader;
    }

    public static bool TryGetWorldFireIntensity(string worldKey, Vector2 logicalPosition, out float intensity)
    {
        intensity = 0f;
        FireWorldReader reader = GetWorldReader(worldKey);
        if (reader == null) return false;
        intensity = reader.Read(Vector2Int.FloorToInt(logicalPosition));
        return intensity > 0f;
    }

    public static bool HasActiveWorldFire(string worldKey) => GetWorldReader(worldKey)?.ActiveFireCount > 0;

    internal static IReadOnlyList<ChunkRuntime> GetActiveWorldFireChunks(string worldKey)
        => GetWorldReader(worldKey)?.ActiveChunks;

    public static bool TryIgniteWorldFireCell(string worldKey, Vector2 logicalPosition, float intensity = 1f)
    {
        if (!GameNetwork.HasStateAuthority || !float.IsFinite(intensity) || intensity <= 0f || intensity > 1f ||
            !GameplayWorldRegistry.TryGet(worldKey, out GameplayWorldRuntime world) || world.Chunks == null) return false;
        using var scope = GameplayWorldRegistry.UseWorld(worldKey);
        if (!WorldCombustibilitySystem.TryGetPreparedSample(world.Chunks.WorldRuntime,
                Vector2Int.FloorToInt(logicalPosition), out var sample) ||
            sample.Terrain.GetFire(sample.LocalCell.x, sample.LocalCell.y) != 0 ||
            sample.Terrain.GetCombustibility(sample.LocalCell.x, sample.LocalCell.y).FuelSeconds <= 0f) return false;
        Span<WorldFireCellState> state = stackalloc WorldFireCellState[1];
        state[0] = new WorldFireCellState { CellIndex = sample.LocalCell.y * sample.Terrain.Width + sample.LocalCell.x, Intensity = 1f };
        sample.Terrain.SetFireStatesBatch(state);
        return true;
    }

    public static bool ExtinguishWorldFireCell(string worldKey, Vector2 logicalPosition)
    {
        if (!GameNetwork.HasStateAuthority || !GameplayWorldRegistry.TryGet(worldKey, out GameplayWorldRuntime world) || world.Chunks == null)
            return false;
        using var scope = GameplayWorldRegistry.UseWorld(worldKey);
        if (!world.Chunks.TryGetRuntimeTerrainTile(logicalPosition, out RuntimeTerrainTileSample sample) ||
            sample.Terrain.GetFire(sample.LocalCell.x, sample.LocalCell.y) == 0) return false;
        sample.Terrain.SetFire(sample.LocalCell.x, sample.LocalCell.y, 0);
        return true;
    }

    public static int IgniteWorldFireArea(string worldKey, Vector2Int minimum, Vector2Int size, float intensity = 1f)
    {
        if (intensity <= 0f) return 0;
        return ChangeWorldFireArea(worldKey, minimum, size, intensity);
    }

    public static int ExtinguishWorldFireArea(string worldKey, Vector2Int minimum, Vector2Int size)
        => ChangeWorldFireArea(worldKey, minimum, size, 0f);

    private static int ChangeWorldFireArea(string worldKey, Vector2Int minimum, Vector2Int size, float intensity)
    {
        if (!GameNetwork.HasStateAuthority || !float.IsFinite(intensity) || intensity < 0f || intensity > 1f ||
            !GameplayWorldRegistry.TryGet(worldKey, out GameplayWorldRuntime world) || world.Chunks == null) return 0;
        using var scope = GameplayWorldRegistry.UseWorld(worldKey);
        WorldFireSimulationContext context = world.Chunks.GetWorldFireIgnitionContext();
        context.ClearPreparedInputs();
        for (int y = 0; y < size.y; y++)
        for (int x = 0; x < size.x; x++)
        {
            Vector2 point = new(minimum.x + x + .5f, minimum.y + y + .5f);
            if (world.Chunks.TryGetRuntimeTerrainTile(point, out var sample)) context.AddIgnitionSample(sample);
        }
        return context.CommitPreparedIgnitions(intensity);
    }

    // 热场查询固定七乘七邻域，不把一万团火注册成一万个局部温度源。
    public static float GetWorldFireHeating(string worldKey, Vector2 position, float ambient)
    {
        if (ambient >= AmbientTargetTemperature) return 0f;
        return (AmbientTargetTemperature - ambient) * GetWorldFireHeatWeight(worldKey, Vector2Int.FloorToInt(position));
    }

    internal static float GetWorldFireHeatWeight(string worldKey, Vector2Int center)
    {
        FireWorldReader reader = GetWorldReader(worldKey);
        if (reader == null || reader.ActiveFireCount == 0) return 0f;
        float strongest = 0f;
        for (int y = -HeatRadius; y <= HeatRadius; y++)
        for (int x = -HeatRadius; x <= HeatRadius; x++)
        {
            int distanceSquared = x * x + y * y;
            if (distanceSquared >= HeatRadius * HeatRadius) continue;
            float intensity = reader.Read(center + new Vector2Int(x, y));
            float weight = ThermalInfluenceRuntime.DistanceWeight(distanceSquared, HeatRadius * HeatRadius) * intensity;
            strongest = Mathf.Max(strongest, weight);
        }
        return strongest;
    }

    internal static float[] CaptureWorldFireHeatWeights(string worldKey, Vector2Int origin, int width, int height)
    {
        FireWorldReader reader = GetWorldReader(worldKey);
        if (reader == null || !reader.MayHeatChunk(origin, width, height)) return null;
        float[] weights = null;
        for (int index = 0; index < width * height; index++)
        {
            float weight = GetWorldFireHeatWeight(worldKey, origin + new Vector2Int(index % width, index / width));
            if (weight <= 0f) continue;
            weights ??= new float[width * height]; weights[index] = weight;
        }
        return weights;
    }

    internal static bool IsExposedToWater(in RuntimeTerrainTileSample sample) => sample.LiquidDepth > 0f &&
        GameRes.ExistingInstance != null && GameRes.ExistingInstance.TryGetLiquidDefinition(sample.LiquidId, out var liquid) &&
        liquid.WorldWater?.WaterContact == true;

    internal static bool TryGetTileFuel(int tileId, out RuntimeTileDefinition definition)
    {
        definition = null;
        return GameRes.ExistingInstance != null && GameRes.ExistingInstance.TryGetTileDefinition(tileId, out definition);
    }

    internal static bool HasExposedGrass(in RuntimeTerrainTileSample sample) => sample.Cell.BlockingTileId == 0 &&
        sample.Cell.BackTileId == 0 && TerrainSupportLayer.GetExposedTileId(sample.Terrain, sample.LocalCell.x, sample.LocalCell.y) == 0 &&
        sample.Terrain.GetGrass(sample.LocalCell.x, sample.LocalCell.y) == ChunkTerrainData.GrassPresent;
    #endregion
}

/// <summary>火场只采集活火与八邻格的可燃性层，不查询世界实体。</summary>
internal sealed class WorldFireSimulationContext
{
    #region 复用输入与区块批次
    internal readonly ChunkMgr Manager;
    internal readonly WorldRuntime World;
    private readonly uint worldSeed;
    private readonly List<WorldCombustibilityCellSample> samples = new();
    private sealed class ChunkPreparation
    {
        internal readonly ChunkTerrainData Terrain;
        internal readonly int[] SampleSlots;
        internal readonly ulong[] SlotVersions;
        internal readonly List<int> Samples = new();
        internal ChunkPreparation(ChunkTerrainData terrain)
        { Terrain = terrain; SampleSlots = new int[terrain.CellCount]; SlotVersions = new ulong[terrain.CellCount]; }
    }
    private readonly Dictionary<ChunkTerrainData, ChunkPreparation> chunkPreparations = new();
    private readonly List<ChunkPreparation> preparedChunks = new();
    private readonly List<ChunkTerrainData> retiredChunks = new();
    private ulong preparationVersion;
    private WorldFireSimulationKernel.Input[] inputs = Array.Empty<WorldFireSimulationKernel.Input>();
    private WorldFireSimulationKernel.Output[] outputs = Array.Empty<WorldFireSimulationKernel.Output>();
    private WorldFireCellState[] submit = Array.Empty<WorldFireCellState>();
    private double previousGameTime = double.NaN;
    internal float Seconds { get; private set; }
    internal int ActiveFireCount { get; private set; }
    internal ulong StepIndex { get; private set; }
    private static readonly ProfilerMarker SimulationMarker = new("FlatWorld.Fire.Simulate");
    private static readonly ProfilerMarker PrepareMarker = new("FlatWorld.Fire.Prepare");
    private static readonly ProfilerMarker KernelMarker = new("FlatWorld.Fire.Burst");
    private static readonly ProfilerMarker CommitMarker = new("FlatWorld.Fire.Commit");

    internal WorldFireSimulationContext(ChunkMgr manager)
        : this(manager.WorldRuntime, unchecked((uint)(SaveDataMgr.Instance?.SaveData?.Seed ?? 1)))
    { Manager = manager; }

    internal WorldFireSimulationContext(WorldRuntime world, uint seed)
    {
        World = world;
        foreach (char character in world.WorldId) seed = unchecked((seed ^ character) * 16777619u);
        worldSeed = seed;
    }

    internal void ClearPreparedInputs()
    {
        foreach (var batch in preparedChunks) batch.Samples.Clear();
        preparedChunks.Clear(); samples.Clear(); retiredChunks.Clear(); preparationVersion++;
        foreach (var terrain in chunkPreparations.Keys) if (terrain.IsDisposed) retiredChunks.Add(terrain);
        foreach (var terrain in retiredChunks) chunkPreparations.Remove(terrain);
    }

    private ChunkPreparation GetChunkPreparation(ChunkTerrainData terrain)
    {
        if (!chunkPreparations.TryGetValue(terrain, out var batch))
            chunkPreparations.Add(terrain, batch = new ChunkPreparation(terrain));
        return batch;
    }

    internal void AddIgnitionSample(in RuntimeTerrainTileSample sample)
        => AddIgnitionSample(new WorldCombustibilityCellSample(sample.Terrain, sample.WorldCell, sample.LocalCell));

    private void AddIgnitionSample(in WorldCombustibilityCellSample sample)
    {
        var batch = GetChunkPreparation(sample.Terrain);
        int cell = sample.LocalCell.y * sample.Terrain.Width + sample.LocalCell.x;
        if (batch.SlotVersions[cell] == preparationVersion) return;
        if (batch.Samples.Count == 0) preparedChunks.Add(batch);
        batch.SlotVersions[cell] = preparationVersion;
        batch.SampleSlots[cell] = samples.Count;
        batch.Samples.Add(samples.Count); samples.Add(sample);
    }

    private void EnsureCapacity(int capacity)
    {
        if (inputs.Length >= capacity) return;
        int length = Mathf.NextPowerOfTwo(Mathf.Max(256, capacity));
        Array.Resize(ref inputs, length); Array.Resize(ref outputs, length); Array.Resize(ref submit, length);
    }

    private void AddChunkCell(ChunkRuntime chunk, ChunkPreparation batch, int x, int y)
    {
        Vector2Int cell = new(chunk.Address.ChunkOrigin.X + x, chunk.Address.ChunkOrigin.Y + y);
        if ((uint)x < (uint)batch.Terrain.Width && (uint)y < (uint)batch.Terrain.Height &&
            batch.SlotVersions[y * batch.Terrain.Width + x] == preparationVersion) return;
        if (WorldCombustibilitySystem.TryGetPreparedSample(World, cell, out var sample)) AddIgnitionSample(sample);
    }

    private int GetNeighbourSample(ChunkPreparation batch, in WorldCombustibilityCellSample sample, Vector2Int offset)
    {
        int x = sample.LocalCell.x + offset.x, y = sample.LocalCell.y + offset.y;
        if ((uint)x < (uint)batch.Terrain.Width && (uint)y < (uint)batch.Terrain.Height)
        {
            int index = y * batch.Terrain.Width + x;
            return batch.SlotVersions[index] == preparationVersion ? batch.SampleSlots[index] : -1;
        }
        if (!WorldCombustibilitySystem.TryGetPreparedSample(World, sample.WorldCell + offset, out var neighbour)) return -1;
        var adjacent = GetChunkPreparation(neighbour.Terrain);
        int cell = neighbour.LocalCell.y * neighbour.Terrain.Width + neighbour.LocalCell.x;
        return adjacent.SlotVersions[cell] == preparationVersion ? adjacent.SampleSlots[cell] : -1;
    }

    internal int CommitPreparedIgnitions(float intensity)
    {
        EnsureCapacity(samples.Count);
        int changed = 0;
        foreach (var batch in preparedChunks)
        {
            var terrain = batch.Terrain;
            int count = 0;
            foreach (int i in batch.Samples)
            {
                var sample = samples[i];
                if (intensity > 0f && (!WorldCombustibilitySystem.TryGetPreparedSample(World, sample.WorldCell, out _) ||
                    terrain.GetCombustibility(sample.LocalCell.x, sample.LocalCell.y).FuelSeconds <= 0f)) continue;
                var state = terrain.GetFireState(sample.LocalCell.x, sample.LocalCell.y);
                if ((state.Intensity > 0f) == (intensity > 0f)) continue;
                state.Intensity = intensity > 0f ? 1f : 0f;
                submit[count++] = state;
            }
            changed += terrain.SetFireStatesBatch(submit.AsSpan(0, count));
        }
        ClearPreparedInputs();
        return changed;
    }
    #endregion

    #region 固定步传播与扣料
    internal void Simulate(float fixedSeconds, bool sampleWorldClock = true)
    {
        using var simulationScope = SimulationMarker.Auto();
        double now = sampleWorldClock && DayTimeSystem.GetInstance()?.TryGetResolvedTimeData(World.WorldId, out _, out TimeData clock) == true
            ? clock.GetTotalGameTimeSeconds() : double.NaN;
        Seconds = double.IsNaN(now) || double.IsNaN(previousGameTime) ? fixedSeconds : (float)Math.Max(0d, now - previousGameTime);
        previousGameTime = now;
        ClearPreparedInputs(); ActiveFireCount = 0; StepIndex++;
        using (PrepareMarker.Auto())
        {
            var activeChunks = WorldFireSystem.GetActiveWorldFireChunks(World.WorldId);
            for (int chunkIndex = 0; chunkIndex < (activeChunks?.Count ?? 0); chunkIndex++)
            {
                ChunkRuntime chunk = activeChunks[chunkIndex];
                ChunkTerrainData terrain = chunk.Terrain;
                if (chunk.DataStatus != ChunkDataStatus.Ready || terrain == null || terrain.IsDisposed) continue;
                var active = terrain.ActiveFireCellIndices;
                var batch = GetChunkPreparation(terrain);
                ActiveFireCount += active.Count;
                for (int i = 0; i < active.Count; i++)
                {
                    int index = active[i];
                    int x = index % terrain.Width, y = index / terrain.Width;
                    AddChunkCell(chunk, batch, x, y);
                    foreach (Vector2Int offset in WorldFireSystem.Neighbours) AddChunkCell(chunk, batch, x + offset.x, y + offset.y);
                }
            }
            if (samples.Count == 0 || Seconds <= 0f) return;
            EnsureCapacity(samples.Count);
            for (int i = 0; i < samples.Count; i++)
            {
                var sample = samples[i];
                inputs[i] = new WorldFireSimulationKernel.Input
                {
                    State = sample.Terrain.GetFireState(sample.LocalCell.x, sample.LocalCell.y),
                    Combustibility = sample.Terrain.GetCombustibility(sample.LocalCell.x, sample.LocalCell.y),
                    RandomSeed = unchecked((uint)sample.WorldCell.x * 73856093u ^ (uint)sample.WorldCell.y * 19349663u ^
                        worldSeed ^ (uint)StepIndex * 83492791u)
                };
            }
            foreach (var batch in preparedChunks)
                foreach (int i in batch.Samples)
                {
                    if (inputs[i].State.Intensity <= 0f || inputs[i].Combustibility.FuelSeconds <= 0f) continue;
                    foreach (Vector2Int offset in WorldFireSystem.Neighbours)
                    {
                        int target = GetNeighbourSample(batch, samples[i], offset);
                        if (target >= 0) inputs[target].HasBurningNeighbour = 1;
                    }
                }
        }
        using (KernelMarker.Auto())
            WorldFireSimulationKernel.Advance(inputs.AsSpan(0, samples.Count), outputs.AsSpan(0, samples.Count), Seconds);
        using (CommitMarker.Auto())
        {
            foreach (var batch in preparedChunks)
            {
                var terrain = batch.Terrain;
                if (terrain.IsDisposed) continue;
                int count = 0;
                foreach (int i in batch.Samples)
                {
                    var sample = samples[i];
                    var live = terrain.GetFireState(sample.LocalCell.x, sample.LocalCell.y);
                    if (live.Intensity != inputs[i].State.Intensity ||
                        live.SurfaceFuelBurnSeconds != inputs[i].State.SurfaceFuelBurnSeconds) continue;
                    float remaining = terrain.GetCombustibility(sample.LocalCell.x, sample.LocalCell.y).FuelSeconds;
                    var output = outputs[i];
                    terrain.ConsumeCombustibilityFuel(output.State.CellIndex, Mathf.Min(output.ConsumedFuelSeconds, remaining));
                    if (remaining <= output.ConsumedFuelSeconds) output.State.Intensity = 0f;
                    submit[count++] = output.State;
                }
                terrain.SetFireStatesBatch(submit.AsSpan(0, count));
            }
        }
    }
    #endregion
}

public partial class ChunkMgr
{
    #region 所属世界火场驱动
    private WorldFireSimulationContext worldFireSimulation;
    private WorldFireSimulationContext worldFireIgnition;
    internal WorldFireSimulationContext GetWorldFireIgnitionContext()
    {
        if (worldFireIgnition == null || !ReferenceEquals(worldFireIgnition.World, WorldRuntime))
            worldFireIgnition = new WorldFireSimulationContext(this);
        return worldFireIgnition;
    }
    internal WorldFireSimulationContext GetWorldFireSimulationContext()
    {
        if (worldFireSimulation == null || !ReferenceEquals(worldFireSimulation.World, WorldRuntime))
            worldFireSimulation = new WorldFireSimulationContext(this);
        return worldFireSimulation;
    }
    internal void SimulateWorldFireFixedStep(float seconds)
    {
        if (WorldRuntime != null && GameNetwork.HasStateAuthority) GetWorldFireSimulationContext().Simulate(seconds);
    }
    private void ResetWorldFireSimulationContexts()
    { worldFireSimulation = null; worldFireIgnition = null; }
    #endregion
}
