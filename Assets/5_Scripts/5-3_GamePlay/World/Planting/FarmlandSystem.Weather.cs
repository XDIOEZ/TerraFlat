using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using Unity.Profiling;
using UnityEngine;

public static partial class FarmlandSystem
{
    #region 耕地稀疏索引

    private static readonly ConditionalWeakTable<ChunkTerrainData, FarmlandCellIndex> farmlandCells = new();

    // 索引只随真实地块变化维护，降雨和水肥变化不重新扫描整个区块。
    private sealed class FarmlandCellIndex
    {
        private readonly ChunkTerrainData terrain;
        private readonly List<int> cells = new();
        private int tileId;

        internal FarmlandCellIndex(ChunkTerrainData terrain)
        {
            this.terrain = terrain;
            terrain.Changed += OnChanged;
        }

        internal IReadOnlyList<int> Get(int currentTileId)
        {
            if (tileId == currentTileId) return cells;
            tileId = currentTileId;
            cells.Clear();
            for (int index = 0; index < terrain.CellCount; index++)
                if (terrain.GetCell(index % terrain.Width, index / terrain.Width).GroundTileId == tileId)
                    cells.Add(index);
            return cells;
        }

        private void OnChanged(ChunkTerrainChanged change)
        {
            if (tileId <= 0 || change.Kind is not (TerrainChangeKind.Cell or TerrainChangeKind.TileStack))
                return;
            foreach (int index in change.GetCellIndices(terrain.Width))
            {
                int slot = cells.BinarySearch(index);
                bool matches = terrain.GetCell(index % terrain.Width, index / terrain.Width).GroundTileId == tileId;
                if (matches && slot < 0) cells.Insert(~slot, index);
                else if (!matches && slot >= 0) cells.RemoveAt(slot);
            }
        }
    }

    internal static IReadOnlyList<int> GetFarmlandCellIndices(ChunkTerrainData terrain) =>
        GameRes.ExistingInstance != null &&
        GameRes.ExistingInstance.TryGetTileDefinition(TileBlockId, out RuntimeTileDefinition farmland)
            ? GetFarmlandCellIndices(terrain, farmland.RuntimeTileId) : Array.Empty<int>();

    internal static IReadOnlyList<int> GetFarmlandCellIndices(ChunkTerrainData terrain, int tileId) =>
        terrain == null || terrain.IsDisposed || tileId <= 0 ? Array.Empty<int>() :
        farmlandCells.GetValue(terrain, value => new FarmlandCellIndex(value)).Get(tileId);

    #endregion

    #region 耕地地表补水

    // 来源方只提供水量，土壤统一维护容量、水肥层和持久化。
    public static bool SupplySurfaceWater(RuntimeTerrainTileSample sample, float waterPoints, bool persist = true)
    {
        if (!GameNetwork.HasStateAuthority || !float.IsFinite(waterPoints) || waterPoints <= 0f ||
            !IsFarmland(sample.Cell) || !CanUseSoilCell(sample))
            return false;

        bool hasSoil = HasSoilState(sample);
        bool hasMoisture = sample.Terrain.TryGetEnvironmentValue("moisture", sample.LocalCell.x,
            sample.LocalCell.y, out float moisture);
        var template = (TileData_Farmland)RequireFarmlandDefinition().TileDataTemplate;
        int water = hasSoil ? ReadWaterLevel(sample.Terrain, sample.LocalCell) :
            hasMoisture ? Mathf.RoundToInt(Mathf.Clamp01(moisture) * template.maxWater) : template.waterValue;
        float remainder = hasSoil ? Read(sample.Terrain, sample.LocalCell, WaterSupplyRemainderLayer) : 0f;
        if (!TryCalculateSurfaceWater(water, remainder, waterPoints, out int nextWater, out float nextRemainder))
            return false;

        float fertility = hasSoil ? Read(sample.Terrain, sample.LocalCell, FertilityLayer) : template.Fertility;
        if (!hasSoil && sample.Terrain.TryGetEnvironmentValue("fertility", sample.LocalCell.x,
                sample.LocalCell.y, out float generatedFertility))
            fertility = generatedFertility;

        int x = sample.LocalCell.x, y = sample.LocalCell.y;
        if (!hasSoil)
        {
            sample.Terrain.SetEnvironmentValue(SourceLayer, x, y, sample.Cell.GroundTileId);
            sample.Terrain.SetEnvironmentValue(FertilityLayer, x, y, fertility);
            sample.Terrain.SetEnvironmentValue(WaterConsumptionRemainderLayer, x, y, 0f);
        }
        sample.Terrain.SetEnvironmentValue(WaterLayer, x, y, nextWater);
        sample.Terrain.SetEnvironmentValue(WaterSupplyRemainderLayer, x, y, nextRemainder);
        if (persist)
            SaveDataMgr.Instance.RecordAgricultureCell(sample);
        return true;
    }

    #endregion

    #region 区块批量补水

    private static EnvironmentCellUpdate[] surfaceSourceUpdates = Array.Empty<EnvironmentCellUpdate>();
    private static EnvironmentCellUpdate[] surfaceFertilityUpdates = Array.Empty<EnvironmentCellUpdate>();
    private static EnvironmentCellUpdate[] surfaceWaterUpdates = Array.Empty<EnvironmentCellUpdate>();
    private static EnvironmentCellUpdate[] surfaceWaterProgressUpdates = Array.Empty<EnvironmentCellUpdate>();
    private static EnvironmentCellUpdate[] surfaceConsumptionProgressUpdates = Array.Empty<EnvironmentCellUpdate>();
    private static bool advancingSurfaceWaterBatch;
    private static readonly ProfilerMarker SurfaceWaterPrepareMarker = new("FlatWorld.Soil.SurfaceWater.Prepare");
    private static readonly ProfilerMarker SurfaceWaterComputeMarker = new("FlatWorld.Soil.SurfaceWater.Compute");
    private static readonly ProfilerMarker SurfaceWaterWriteMarker = new("FlatWorld.Soil.SurfaceWater.Write");
    private static readonly ProfilerMarker SurfaceWaterNotifyMarker = new("FlatWorld.Soil.SurfaceWater.Notify");

    // 区块地址本来就是规范坐标，批次共享目录和环境数组，先写完所有层再通知观察者。
    public static int SupplySurfaceWaterBatch(ChunkRuntime chunk, ReadOnlySpan<SurfaceWaterSupply> supplies,
        List<int> changedCells, bool deferNotification = false)
    {
        if (changedCells == null) throw new ArgumentNullException(nameof(changedCells));
        if (advancingSurfaceWaterBatch)
            throw new InvalidOperationException("地表批量补水不允许重入。");
        if (!GameNetwork.HasStateAuthority || supplies.IsEmpty || chunk?.Terrain == null ||
            chunk.Terrain.IsDisposed || chunk.DataStatus != ChunkDataStatus.Ready)
            return 0;

        ChunkTerrainData terrain = chunk.Terrain;
        int previousIndex = -1;
        foreach (SurfaceWaterSupply supply in supplies)
        {
            if ((uint)supply.CellIndex >= (uint)terrain.CellCount || supply.CellIndex <= previousIndex)
                throw new ArgumentException("地表补水格索引必须在区块范围内且严格递增。", nameof(supplies));
            previousIndex = supply.CellIndex;
        }

        advancingSurfaceWaterBatch = true;
        try
        {
            terrain.PublishEnvironmentBatches();
            RuntimeTileDefinition farmland;
            TileData_Farmland template;
            ReadOnlyMemory<float> sources, waters, waterProgress, moistures, fertilities, generatedFertilities;
            using (SurfaceWaterPrepareMarker.Auto())
            {
                farmland = RequireFarmlandDefinition();
                template = (TileData_Farmland)farmland.TileDataTemplate;
                if (surfaceWaterUpdates.Length < supplies.Length)
                {
                    int capacity = terrain.CellCount;
                    surfaceSourceUpdates = new EnvironmentCellUpdate[capacity];
                    surfaceFertilityUpdates = new EnvironmentCellUpdate[capacity];
                    surfaceWaterUpdates = new EnvironmentCellUpdate[capacity];
                    surfaceWaterProgressUpdates = new EnvironmentCellUpdate[capacity];
                    surfaceConsumptionProgressUpdates = new EnvironmentCellUpdate[capacity];
                }
                terrain.TryGetEnvironmentLayer(SourceLayer, out sources);
                terrain.TryGetEnvironmentLayer(WaterLayer, out waters);
                terrain.TryGetEnvironmentLayer(WaterSupplyRemainderLayer, out waterProgress);
                terrain.TryGetEnvironmentLayer("moisture", out moistures);
                terrain.TryGetEnvironmentLayer(FertilityLayer, out fertilities);
                terrain.TryGetEnvironmentLayer("fertility", out generatedFertilities);
            }

            int maximum = template.maxWater;
            int newSoilCount = 0, waterCount = 0;
            using (SurfaceWaterComputeMarker.Auto())
            foreach (SurfaceWaterSupply supply in supplies)
            {
                if (!float.IsFinite(supply.WaterPoints) || supply.WaterPoints <= 0f)
                    continue;
                int index = supply.CellIndex;
                int x = index % terrain.Width, y = index / terrain.Width;
                TerrainCell cell = terrain.GetCell(x, y);
                if (cell.GroundTileId != farmland.RuntimeTileId)
                    continue;
                bool hasSoil = !sources.IsEmpty && sources.Span[index] == farmland.RuntimeTileId;
                float moisture = moistures.IsEmpty ? 0f : moistures.Span[index];
                int water = Mathf.Clamp(Mathf.RoundToInt(hasSoil ? (waters.IsEmpty ? 0f : waters.Span[index]) :
                    moistures.IsEmpty ? template.waterValue : Mathf.Clamp01(moisture) * maximum), 0, maximum);
                float remainder = hasSoil && !waterProgress.IsEmpty ? waterProgress.Span[index] : 0f;
                if (!TryCalculateSurfaceWater(water, remainder, supply.WaterPoints,
                        out int nextWater, out float nextRemainder))
                    continue;

                var worldCell = new Vector2Int(chunk.Address.ChunkOrigin.X + x, chunk.Address.ChunkOrigin.Y + y);
                if (!CanUseSoilCell(terrain, x, y, cell, worldCell, true))
                    continue;

                float fertility = hasSoil ? (fertilities.IsEmpty ? 0f : fertilities.Span[index]) :
                    generatedFertilities.IsEmpty ? template.Fertility : generatedFertilities.Span[index];
                if (!float.IsFinite(nextRemainder) || !float.IsFinite(fertility))
                    throw new InvalidOperationException("地表水肥批次包含非有限数值。");
                if (!hasSoil)
                {
                    surfaceSourceUpdates[newSoilCount] = new EnvironmentCellUpdate(index, cell.GroundTileId);
                    surfaceConsumptionProgressUpdates[newSoilCount] = new EnvironmentCellUpdate(index, 0f);
                    surfaceFertilityUpdates[newSoilCount++] = new EnvironmentCellUpdate(index, fertility);
                }
                surfaceWaterUpdates[waterCount] = new EnvironmentCellUpdate(index, nextWater);
                surfaceWaterProgressUpdates[waterCount] = new EnvironmentCellUpdate(index, nextRemainder);
                waterCount++;
                changedCells.Add(index);
            }
            if (waterCount == 0) return 0;

            using (SurfaceWaterWriteMarker.Auto())
            {
                terrain.SetEnvironmentBatch(SourceLayer, surfaceSourceUpdates.AsSpan(0, newSoilCount), true);
                terrain.SetEnvironmentBatch(FertilityLayer, surfaceFertilityUpdates.AsSpan(0, newSoilCount), true);
                terrain.SetEnvironmentBatch(WaterConsumptionRemainderLayer,
                    surfaceConsumptionProgressUpdates.AsSpan(0, newSoilCount), true);
                terrain.SetEnvironmentBatch(WaterLayer, surfaceWaterUpdates.AsSpan(0, waterCount), true);
                terrain.SetEnvironmentBatch(WaterSupplyRemainderLayer, surfaceWaterProgressUpdates.AsSpan(0, waterCount), true);
            }
            if (!deferNotification) PublishSurfaceWaterBatch(terrain);
            return waterCount;
        }
        finally
        {
            advancingSurfaceWaterBatch = false;
        }
    }

    public static void PublishSurfaceWaterBatch(ChunkTerrainData terrain)
    {
        using (SurfaceWaterNotifyMarker.Auto())
            terrain.PublishEnvironmentBatches();
    }

    // 单格和批量来源共用容量规则，测试直接读取正式计算结果。
    public static bool TryCalculateSurfaceWater(int water, float remainder, float waterPoints,
        out int nextWater, out float nextRemainder) =>
        TileData_Farmland.TryAccumulateWaterSupply(water, remainder, waterPoints, out nextWater, out nextRemainder);

    #endregion
}

/// <summary>来源方给某个区块格子的水量，土壤补水服务统一处理资格和容量。</summary>
public readonly struct SurfaceWaterSupply
{
    #region 补水输入
    public readonly int CellIndex;
    public readonly float WaterPoints;
    public SurfaceWaterSupply(int cellIndex, float waterPoints)
    { CellIndex = cellIndex; WaterPoints = waterPoints; }
    #endregion
}
