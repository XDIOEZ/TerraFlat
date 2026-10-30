using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FlatWorld.Networking;
using FlatWorld.NaturalEntities;
using FlatWorld.WorldModel;
using Unity.Profiling;
using UnityEngine;

/// <summary>按所属世界推进火场，燃料和生命副作用仍交给各自拥有者。</summary>
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

    public static bool TryIgniteWorldFireCell(string worldKey, Vector2 logicalPosition, float intensity = .25f)
    {
        if (!GameNetwork.HasStateAuthority || !float.IsFinite(intensity) || intensity <= 0f || intensity > 1f ||
            !GameplayWorldRegistry.TryGet(worldKey, out GameplayWorldRuntime world) || world.Chunks == null) return false;
        using var scope = GameplayWorldRegistry.UseWorld(worldKey);
        if (!world.Chunks.TryGetRuntimeTerrainTile(logicalPosition, out RuntimeTerrainTileSample sample) ||
            sample.Terrain.GetFire(sample.LocalCell.x, sample.LocalCell.y) != 0 || IsExposedToWater(sample) ||
            !ItemEnvironmentSources.CanSustainCombustion(worldKey, logicalPosition)) return false;
        WorldFireSimulationContext context = world.Chunks.GetWorldFireIgnitionContext();
        context.PrepareSingleIgnition(sample);
        CollectOwnerFuel(context);
        bool hasFuel = context.HasFuelAt(sample.WorldCell) || HasTerrainFuel(sample);
        context.ClearPreparedInputs();
        if (!hasFuel || IsNonCombustibleBarrier(sample)) return false;
        Span<WorldFireCellState> state = stackalloc WorldFireCellState[1];
        state[0] = new WorldFireCellState { CellIndex = sample.LocalCell.y * sample.Terrain.Width + sample.LocalCell.x, Intensity = intensity };
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

    public static int IgniteWorldFireArea(string worldKey, Vector2Int minimum, Vector2Int size, float intensity = .25f)
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
        if (intensity > 0f) CollectOwnerFuel(context);
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

    internal static bool HasTerrainFuel(in RuntimeTerrainTileSample sample)
    {
        if (HasExposedGrass(sample)) return true;
        return TryGetTileFuel(sample.TopTileId, out var top) && top.FireFuel.IsCombustible ||
            TryGetTileFuel(sample.Cell.BlockingTileId, out var wall) && wall.FireFuel.IsCombustible;
    }

    internal static bool HasExposedGrass(in RuntimeTerrainTileSample sample) => sample.Cell.BlockingTileId == 0 &&
        sample.Cell.BackTileId == 0 && TerrainSupportLayer.GetExposedTileId(sample.Terrain, sample.LocalCell.x, sample.LocalCell.y) == 0 &&
        sample.Terrain.GetGrass(sample.LocalCell.x, sample.LocalCell.y) == ChunkTerrainData.GrassPresent;

    internal static float SurfaceFuelDuration(in RuntimeTerrainTileSample sample)
    {
        if (HasExposedGrass(sample)) return WorldFireSimulationKernel.Parameters.Default.GrassBurnDurationSeconds;
        return sample.Cell.BlockingTileId == 0 && TryGetTileFuel(sample.TopTileId, out var definition) && definition.FireFuel.IsCombustible
            ? definition.FireFuel.BurnDurationSeconds : 0f;
    }

    internal static bool IsNonCombustibleBarrier(in RuntimeTerrainTileSample sample) =>
        sample.Cell.BlockingTileId != 0 &&
        (!TryGetTileFuel(sample.Cell.BlockingTileId, out var definition) || !definition.FireFuel.IsCombustible);

    internal static void CollectOwnerFuel(WorldFireSimulationContext context)
    {
        NaturalEntityEcsService.CollectWorldFireFuel(context);
        DroppedItemService.CollectWorldFireFuel(context);
        context.CollectFullItemFuel();
    }
    #endregion
}

/// <summary>同一世界复用连续缓冲、格索引和副作用候选，单轮不持有异步工作。</summary>
internal sealed class WorldFireSimulationContext
{
    #region 世界批次数据
    internal readonly ChunkMgr Manager;
    internal readonly WorldRuntime World;
    internal string WorldKey => World.WorldId;
    internal readonly Dictionary<Vector2Int, int> Indices = new();
    internal readonly List<RuntimeTerrainTileSample> Samples = new();
    internal readonly List<Item> Items = new();
    private readonly List<Item> itemQueryScratch = new();
    private readonly HashSet<Item> itemQueryDedupe = new();
    private readonly HashSet<Item> itemCandidates = new();
    private readonly HashSet<ChunkTerrainData> itemQueryChunks = new();
    private readonly Dictionary<Item, uint> itemGenerations = new();
    private readonly Dictionary<Item, HashSet<Vector2Int>> buildingFootprints = new();
    private GameRes fuelDefinitionOwner;
    private ulong fuelDefinitionRevision;
    private float maximumDefinitionFootprintReach;
    private readonly WorldTopologyDomain topology;
    private readonly uint worldSeed;
    private readonly Dictionary<Vector2Int, float> fuel = new();
    private readonly Dictionary<ChunkTerrainData, List<int>> chunkIndices = new();
    private readonly List<ChunkTerrainData> submittedChunks = new();
    private readonly Dictionary<ChunkTerrainData, (float Rain, Vector2 Wind)> weatherSamples = new();
    private readonly List<ChunkTerrainData> retiredChunks = new();
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
    {
        Manager = manager; World = manager.WorldRuntime;
        topology = WorldTopologyRuntime.GetActiveDomain();
        uint seed = unchecked((uint)(SaveDataMgr.Instance?.SaveData?.Seed ?? 1));
        foreach (char character in World.WorldId) seed = unchecked((seed ^ character) * 16777619u);
        worldSeed = seed;
    }

    internal void ClearPreparedInputs()
    { Indices.Clear(); Samples.Clear(); fuel.Clear(); Items.Clear(); itemGenerations.Clear(); buildingFootprints.Clear(); }

    internal void PrepareSingleIgnition(in RuntimeTerrainTileSample sample)
    { ClearPreparedInputs(); AddSample(sample); }

    private void EnsureCapacity(int capacity)
    {
        if (inputs.Length >= capacity) return;
        int length = Mathf.NextPowerOfTwo(Mathf.Max(256, capacity));
        Array.Resize(ref inputs, length); Array.Resize(ref outputs, length); Array.Resize(ref submit, length);
    }

    private void AddSample(in RuntimeTerrainTileSample sample)
    {
        if (Indices.ContainsKey(sample.WorldCell)) return;
        Indices.Add(sample.WorldCell, Samples.Count);
        Samples.Add(sample);
    }

    private void AddLogicalCell(Vector2Int cell)
    {
        cell = new Vector2Int(topology.NormalizeX(cell.x), topology.NormalizeY(cell.y));
        if (!Indices.ContainsKey(cell) && Manager.TryGetRuntimeTerrainTile((Vector2)cell + Vector2.one * .5f, out var sample))
            AddSample(sample);
    }

    internal void AddIgnitionSample(in RuntimeTerrainTileSample sample) => AddSample(sample);

    internal int CommitPreparedIgnitions(float intensity)
    {
        EnsureCapacity(Samples.Count);
        var batches = UnityEngine.Pool.DictionaryPool<ChunkTerrainData, List<WorldFireCellState>>.Get();
        int count = 0;
        try
        {
            foreach (var sample in Samples)
            {
                if (intensity > 0f)
                {
                    if (sample.Terrain.GetFire(sample.LocalCell.x, sample.LocalCell.y) != 0 ||
                        WorldFireSystem.IsExposedToWater(sample) || WorldFireSystem.IsNonCombustibleBarrier(sample) ||
                        !HasFuelAt(sample.WorldCell) && !WorldFireSystem.HasTerrainFuel(sample) ||
                        !ItemEnvironmentSources.CanSustainCombustion(WorldKey, (Vector2)sample.WorldCell + Vector2.one * .5f)) continue;
                }
                else if (sample.Terrain.GetFire(sample.LocalCell.x, sample.LocalCell.y) == 0) continue;
                if (!batches.TryGetValue(sample.Terrain, out var batch))
                {
                    batch = UnityEngine.Pool.ListPool<WorldFireCellState>.Get(); batches.Add(sample.Terrain, batch);
                }
                batch.Add(new WorldFireCellState
                { CellIndex = sample.LocalCell.y * sample.Terrain.Width + sample.LocalCell.x, Intensity = intensity });
                count++;
            }
            foreach (var batch in batches)
            {
                batch.Value.CopyTo(submit);
                batch.Key.SetFireStatesBatch(submit.AsSpan(0, batch.Value.Count));
            }
        }
        finally
        {
            foreach (var batch in batches.Values) UnityEngine.Pool.ListPool<WorldFireCellState>.Release(batch);
            UnityEngine.Pool.DictionaryPool<ChunkTerrainData, List<WorldFireCellState>>.Release(batches);
            ClearPreparedInputs();
        }
        return count;
    }

    internal bool AddFuel(Vector2 position, IReadOnlyList<GridCellOffset> occupiedCells, float flammability)
    {
        Vector2Int root = Vector2Int.FloorToInt(position);
        bool added = false;
        int count = occupiedCells?.Count ?? 0;
        for (int i = 0; i < Mathf.Max(1, count); i++)
        {
            Vector2Int cell = count == 0 ? root : root + new Vector2Int(occupiedCells[i].X, occupiedCells[i].Y);
            cell = WorldTopologyRuntime.NormalizeCell(cell);
            if (!Indices.ContainsKey(cell)) continue;
            if (!fuel.TryGetValue(cell, out float previous) || previous < flammability) fuel[cell] = flammability;
            added = true;
        }
        return added;
    }

    internal bool HasFuelAt(Vector2Int cell) => fuel.ContainsKey(cell);

    internal float ExposureAt(Vector2 position, IReadOnlyList<GridCellOffset> occupiedCells)
    {
        float exposure = 0f;
        Vector2Int root = Vector2Int.FloorToInt(position);
        int count = occupiedCells?.Count ?? 0;
        for (int i = 0; i < Mathf.Max(1, count); i++)
        {
            Vector2Int cell = count == 0 ? root : root + new Vector2Int(occupiedCells[i].X, occupiedCells[i].Y);
            if (Indices.TryGetValue(WorldTopologyRuntime.NormalizeCell(cell), out int index))
                exposure = Mathf.Max(exposure, outputs[index].ExposureSeconds);
        }
        return exposure;
    }

    internal void CollectFullItemFuel()
    {
        Items.Clear();
        itemQueryChunks.Clear(); itemCandidates.Clear();
        ItemMgr manager = ItemMgr.GetExistingInstance();
        if (manager == null) return;
        GameRes resources = GameRes.ExistingInstance;
        if (fuelDefinitionOwner != resources || fuelDefinitionRevision != resources.ItemDefinitionRevision)
        {
            fuelDefinitionOwner = resources; fuelDefinitionRevision = resources.ItemDefinitionRevision;
            maximumDefinitionFootprintReach = 0f;
            foreach (RuntimeItemDefinition definition in resources.ItemDefinitions.Values)
                for (int i = 0; i < definition.WorldGridOccupancy.Count; i++)
                {
                    GridCellOffset cell = definition.WorldGridOccupancy[i];
                    maximumDefinitionFootprintReach = Mathf.Max(maximumDefinitionFootprintReach,
                        new Vector2(cell.X, cell.Y).magnitude);
                }
        }
        foreach (var sample in Samples)
        {
            var occupants = BuildingOccupancyRegistry.GetOccupantsAtNormalizedCell(sample.WorldCell);
            if (occupants != null)
                foreach (Mod_Building building in occupants)
                    if (building != null && building.item != null && itemCandidates.Add(building.item))
                        Items.Add(building.item);
            if (!itemQueryChunks.Add(sample.Terrain)) continue;
            Vector2 center = new(sample.Address.ChunkOrigin.X + sample.Terrain.Width * .5f,
                sample.Address.ChunkOrigin.Y + sample.Terrain.Height * .5f);
            float radius = new Vector2(sample.Terrain.Width, sample.Terrain.Height).magnitude * .5f +
                maximumDefinitionFootprintReach + 1f;
            manager.QueryItemCentersInCircleNonAlloc(center, radius, ~0, null, itemQueryScratch, itemQueryDedupe);
            foreach (Item candidate in itemQueryScratch) if (itemCandidates.Add(candidate)) Items.Add(candidate);
        }
        for (int i = Items.Count - 1; i >= 0; i--)
        {
            Item item = Items[i];
            if (item == null || !item.IsInitialized || item.DestructionHandled || item.Owner != null || item.InHand ||
                item.IsInPool || item.itemData == null || !item.gameObject.activeInHierarchy) { Items.RemoveAt(i); continue; }
            GameRes.ExistingInstance.TryGetItemDefinition(item.itemData.IDName, out RuntimeItemDefinition definition);
            Mod_Building installed = item.itemMods.GetMod_ByID<Mod_Building>(ModText.Building);
            if (installed != null && BuildingOccupancyRegistry.GetRegisteredFootprintCells(installed) is { } footprint)
                buildingFootprints[item] = footprint;
            if (definition?.FireFuel.IsCombustible == true)
            {
                if (buildingFootprints.TryGetValue(item, out var cells) && cells != null)
                {
                    foreach (Vector2Int cell in cells)
                        if (Indices.ContainsKey(cell)) fuel[cell] = definition.FireFuel.Flammability;
                }
                else AddFuel(WorldLocalPresentation.ToLogical(item.transform.position), definition.WorldGridOccupancy, definition.FireFuel.Flammability);
            }
            itemGenerations[item] = item.RuntimeGeneration;
        }
    }
    #endregion

    #region 固定步采样与提交
    internal void Simulate(float fixedSeconds)
    {
        using var simulationScope = SimulationMarker.Auto();
        foreach (ChunkTerrainData terrain in submittedChunks)
            if (chunkIndices.TryGetValue(terrain, out var previous)) previous.Clear();
        submittedChunks.Clear();
        retiredChunks.Clear();
        foreach (ChunkTerrainData terrain in chunkIndices.Keys) if (terrain.IsDisposed) retiredChunks.Add(terrain);
        foreach (ChunkTerrainData terrain in retiredChunks) chunkIndices.Remove(terrain);
        double now = DayTimeSystem.GetInstance()?.TryGetResolvedTimeData(WorldKey, out _, out TimeData clock) == true
            ? clock.GetTotalGameTimeSeconds() : double.NaN;
        Seconds = double.IsNaN(now) || double.IsNaN(previousGameTime) ? fixedSeconds : (float)Math.Max(0d, now - previousGameTime);
        previousGameTime = now;
        ClearPreparedInputs();
        ActiveFireCount = 0;
        StepIndex++;
        using (PrepareMarker.Auto())
        {
            IReadOnlyList<ChunkRuntime> activeChunks = WorldFireSystem.GetActiveWorldFireChunks(WorldKey);
            for (int chunkIndex = 0; chunkIndex < (activeChunks?.Count ?? 0); chunkIndex++)
            {
                ChunkRuntime chunk = activeChunks[chunkIndex];
                ChunkTerrainData terrain = chunk.Terrain;
                if (chunk.DataStatus != ChunkDataStatus.Ready || terrain == null || terrain.IsDisposed) continue;
                IReadOnlyList<int> active = terrain.ActiveFireCellIndices;
                ActiveFireCount += active.Count;
                for (int i = 0; i < active.Count; i++)
                {
                    int index = active[i];
                    Vector2Int cell = new(chunk.Address.ChunkOrigin.X + index % terrain.Width,
                        chunk.Address.ChunkOrigin.Y + index / terrain.Width);
                    AddLogicalCell(cell);
                    foreach (Vector2Int offset in WorldFireSystem.Neighbours)
                        AddLogicalCell(cell + offset);
                }
            }
            if (Samples.Count == 0 || Seconds <= 0f) return;
            EnsureCapacity(Samples.Count);
            WorldFireSystem.CollectOwnerFuel(this);
            WeatherMgr weather = WeatherMgr.GetForWorld(WorldKey, false);
            weatherSamples.Clear();
            for (int i = 0; i < Samples.Count; i++)
            {
                RuntimeTerrainTileSample sample = Samples[i];
                Vector2 center = (Vector2)sample.WorldCell + Vector2.one * .5f;
                sample.Terrain.TryGetEnvironmentValue("moisture", sample.LocalCell.x, sample.LocalCell.y, out float moisture);
                if (!weatherSamples.TryGetValue(sample.Terrain, out var environment))
                {
                    float rain = 0f;
                    Vector2 wind = Vector2.zero;
                    if (weather != null)
                    {
                        weather.TryGetPrecipitationAt(center, out rain, out _);
                        if (weather.TryGetWindBlendAt(center, out var blend)) wind = new Vector2(blend.Wind.x, blend.Wind.y);
                    }
                    environment = (rain, wind); weatherSamples.Add(sample.Terrain, environment);
                }
                inputs[i] = new WorldFireSimulationKernel.Input
                {
                    State = sample.Terrain.GetFireState(sample.LocalCell.x, sample.LocalCell.y),
                    HasFuel = HasFuelAt(sample.WorldCell) || WorldFireSystem.HasTerrainFuel(sample) ? 1 : 0,
                    HasGrass = WorldFireSystem.HasExposedGrass(sample) ? 1 : 0,
                    SurfaceFuelDurationSeconds = WorldFireSystem.SurfaceFuelDuration(sample),
                    WaterContact = WorldFireSystem.IsExposedToWater(sample) ? 1 : 0,
                    SupportsCombustion = ItemEnvironmentSources.CanSustainCombustion(WorldKey, center) ? 1 : 0,
                    Moisture = moisture, Rain = environment.Rain,
                    RandomSeed = unchecked((uint)sample.WorldCell.x * 73856093u ^ (uint)sample.WorldCell.y * 19349663u ^ worldSeed ^
                        (double.IsNaN(now) ? (uint)StepIndex : (uint)(long)Math.Floor(now * 60d)) * 83492791u)
                };
            }
            for (int i = 0; i < Samples.Count; i++)
            {
                var source = inputs[i];
                if (source.State.Intensity <= 0f || source.HasFuel == 0 || source.WaterContact != 0 || source.SupportsCombustion == 0) continue;
                Vector2Int cell = Samples[i].WorldCell;
                Vector2 wind = weatherSamples[Samples[i].Terrain].Wind;
                foreach (Vector2Int offset in WorldFireSystem.Neighbours)
                {
                    Vector2Int adjacent = WorldTopologyRuntime.NormalizeCell(cell + offset);
                    if (!Indices.TryGetValue(adjacent, out int target) || WorldFireSystem.IsNonCombustibleBarrier(Samples[target])) continue;
                    if (offset.x != 0 && offset.y != 0 &&
                        (IsBarrier(cell + new Vector2Int(offset.x, 0)) || IsBarrier(cell + new Vector2Int(0, offset.y)))) continue;
                    float windFactor = Mathf.Clamp(1f + .5f * Vector2.Dot(wind, ((Vector2)offset).normalized), .5f, 1.5f);
                    inputs[target].IgnitionPressure += source.State.Intensity * windFactor;
                }
            }
        }
        using (KernelMarker.Auto())
            WorldFireSimulationKernel.Advance(WorldFireSimulationKernel.Parameters.Default, inputs.AsSpan(0, Samples.Count),
                outputs.AsSpan(0, Samples.Count), Seconds);
        using (CommitMarker.Auto())
        {
            NaturalEntityEcsService.ApplyWorldFireExposure(this);
            ApplyFullItemExposure();
            for (int i = 0; i < Samples.Count; i++)
            {
                var sample = Samples[i];
                var output = outputs[i];
                if (sample.Terrain.IsDisposed) continue;
                if (output.ConsumedSurfaceFuel != 0)
                {
                    if (inputs[i].HasGrass != 0 && sample.Terrain.TryConsumeGrass(sample.LocalCell.x, sample.LocalCell.y))
                        SaveDataMgr.Instance?.RecordRuntimeGrassRemoved(sample);
                    else if (inputs[i].HasGrass == 0) TileBuildingSystem.BurnOutExposedSurface(sample);
                }
                if (output.ExposureSeconds > 0f) TileBuildingSystem.ApplyWorldFireExposure(sample, output.ExposureSeconds);
                if (!chunkIndices.TryGetValue(sample.Terrain, out List<int> indices))
                {
                    indices = new List<int>(16); chunkIndices.Add(sample.Terrain, indices);
                }
                if (indices.Count == 0) submittedChunks.Add(sample.Terrain);
                indices.Add(i);
            }
            foreach (ChunkTerrainData terrain in submittedChunks)
            {
                List<int> indices = chunkIndices[terrain];
                int count = 0;
                for (int i = 0; i < indices.Count; i++)
                {
                    int index = indices[i];
                    var sample = Samples[index];
                    var live = terrain.GetFireState(sample.LocalCell.x, sample.LocalCell.y);
                    var frozen = inputs[index].State;
                    if (live.Intensity == frozen.Intensity && live.SurfaceFuelBurnSeconds == frozen.SurfaceFuelBurnSeconds)
                        submit[count++] = outputs[index].State;
                }
                terrain.SetFireStatesBatch(submit.AsSpan(0, count));
            }
        }
    }

    private bool IsBarrier(Vector2Int cell) => !Indices.TryGetValue(WorldTopologyRuntime.NormalizeCell(cell), out int index) ||
        WorldFireSystem.IsNonCombustibleBarrier(Samples[index]);

    private void ApplyFullItemExposure()
    {
        foreach (Item item in Items)
        {
            if (item == null || item.DestructionHandled || item.itemData == null || item.Owner != null || item.InHand ||
                !itemGenerations.TryGetValue(item, out uint generation) || generation != item.RuntimeGeneration ||
                GameplayWorldRegistry.GetItemWorldKey(item) != WorldKey) continue;
            GameRes.ExistingInstance.TryGetItemDefinition(item.itemData.IDName, out RuntimeItemDefinition definition);
            Vector2 position = WorldLocalPresentation.ToLogical(item.transform.position);
            float exposure = 0f;
            if (buildingFootprints.TryGetValue(item, out var cells) && cells != null)
            {
                foreach (Vector2Int cell in cells)
                    if (Indices.TryGetValue(cell, out int index)) exposure = Mathf.Max(exposure, outputs[index].ExposureSeconds);
            }
            else exposure = ExposureAt(position, definition?.WorldGridOccupancy);
            if (exposure <= 0f) continue;
            if (definition?.IsActor == true)
            {
                bool grounded = true;
                var contacts = item.itemMods.GetCapabilities<IWorldTerrainContactState>();
                for (int i = 0; i < contacts.Count; i++)
                    if (!contacts[i].HasTerrainContact) { grounded = false; break; }
                if (!grounded) continue;
                Mod_BuffManager buffs = item.itemMods.GetMod_ByID<Mod_BuffManager>(ModText.Mod_BuffManager);
                if (buffs == null) continue;
                int desired = Mathf.Clamp(Mathf.CeilToInt(exposure / Seconds * 10f), 1, 10);
                int current = buffs.GetBuffStacks(BurningBuffIds.Burning);
                if (desired > current) buffs.AddBuff(BurningBuffIds.Burning, desired - current);
                else buffs.TrySetBuffDuration(BurningBuffIds.Burning, 5f);
            }
            else if (definition?.FireFuel.IsCombustible == true && !definition.CanBePickedUp)
                item.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp)?.ApplyWorldFireExposure(exposure, definition.FireFuel.BurnDurationSeconds);
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
