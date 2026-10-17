using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using Unity.Profiling;
using UnityEngine;

public static partial class FarmlandSystem
{
    #region 通用地表补水

    // 来源方只提供水量，土壤统一维护容量、水肥层和持久化。
    public static bool SupplySurfaceWater(RuntimeTerrainTileSample sample, float waterPoints, bool persist = true)
    {
        if (!GameNetwork.HasStateAuthority || !float.IsFinite(waterPoints) || waterPoints <= 0f ||
            !CanUseSoilCell(sample))
            return false;

        bool hasSoil = HasSoilState(sample);
        bool hasMoisture = sample.Terrain.TryGetEnvironmentValue("moisture", sample.LocalCell.x,
            sample.LocalCell.y, out float moisture);
        if (!hasSoil)
        {
            GameRes resources = GameRes.ExistingInstance;
            if (resources == null || !resources.TryGetTileDefinition(sample.Cell.GroundTileId,
                    out RuntimeTileDefinition definition) || !definition.NaturalPlantable ||
                (!hasMoisture && !IsFarmland(sample.Cell)))
                return false;
        }

        var template = (TileData_Farmland)RequireFarmlandDefinition().TileDataTemplate;
        int water = hasSoil ? ReadWaterLevel(sample.Terrain, sample.LocalCell) :
            Mathf.RoundToInt(Mathf.Clamp01(moisture) * template.maxWater);
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
        SyncSoilEnvironment(sample.Terrain, x, y, nextWater, fertility);
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
    private static EnvironmentCellUpdate[] surfaceMoistureUpdates = Array.Empty<EnvironmentCellUpdate>();
    private static EnvironmentCellUpdate[] surfaceGeneratedFertilityUpdates = Array.Empty<EnvironmentCellUpdate>();
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
            GameRes resources;
            RuntimeTileDefinition farmland;
            TileData_Farmland template;
            ReadOnlyMemory<float> sources, waters, waterProgress, moistures, fertilities, generatedFertilities;
            using (SurfaceWaterPrepareMarker.Auto())
            {
                resources = GameRes.ExistingInstance;
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
                    surfaceMoistureUpdates = new EnvironmentCellUpdate[capacity];
                    surfaceGeneratedFertilityUpdates = new EnvironmentCellUpdate[capacity];
                }
                terrain.TryGetEnvironmentLayer(SourceLayer, out sources);
                terrain.TryGetEnvironmentLayer(WaterLayer, out waters);
                terrain.TryGetEnvironmentLayer(WaterSupplyRemainderLayer, out waterProgress);
                terrain.TryGetEnvironmentLayer("moisture", out moistures);
                terrain.TryGetEnvironmentLayer(FertilityLayer, out fertilities);
                terrain.TryGetEnvironmentLayer("fertility", out generatedFertilities);
            }

            int maximum = template.maxWater;
            int newSoilCount = 0, waterCount = 0, cachedTileId = -1;
            bool cachedPlantable = false;
            using (SurfaceWaterComputeMarker.Auto())
            foreach (SurfaceWaterSupply supply in supplies)
            {
                if (!float.IsFinite(supply.WaterPoints) || supply.WaterPoints <= 0f)
                    continue;
                int index = supply.CellIndex;
                bool hasSoil = !sources.IsEmpty && sources.Span[index] > 0f;
                float moisture = moistures.IsEmpty ? 0f : moistures.Span[index];
                int water = Mathf.Clamp(Mathf.RoundToInt(hasSoil ? (waters.IsEmpty ? 0f : waters.Span[index]) :
                    Mathf.Clamp01(moisture) * maximum), 0, maximum);
                float remainder = hasSoil && !waterProgress.IsEmpty ? waterProgress.Span[index] : 0f;
                if (!TryCalculateSurfaceWater(water, remainder, supply.WaterPoints,
                        out int nextWater, out float nextRemainder))
                    continue;

                int x = index % terrain.Width, y = index / terrain.Width;
                TerrainCell cell = terrain.GetCell(x, y);
                if (!hasSoil)
                {
                    if (cachedTileId != cell.GroundTileId)
                    {
                        cachedTileId = cell.GroundTileId;
                        cachedPlantable = resources.TryGetTileDefinition(cachedTileId,
                            out RuntimeTileDefinition definition) && definition.NaturalPlantable;
                    }
                    if (!cachedPlantable || (moistures.IsEmpty && cell.GroundTileId != farmland.RuntimeTileId))
                        continue;
                }
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
                surfaceMoistureUpdates[waterCount] = new EnvironmentCellUpdate(index, Mathf.Clamp01((float)nextWater / maximum));
                surfaceGeneratedFertilityUpdates[waterCount++] = new EnvironmentCellUpdate(index,
                    Mathf.Clamp(fertility, 0f, template.maxFertility));
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
                terrain.SetEnvironmentBatch("moisture", surfaceMoistureUpdates.AsSpan(0, waterCount), true);
                terrain.SetEnvironmentBatch("fertility", surfaceGeneratedFertilityUpdates.AsSpan(0, waterCount), true);
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
