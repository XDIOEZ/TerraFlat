using System;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;

public static partial class FarmlandSystem
{
    #region 统一目标与原地表水肥

    /// <summary>白色格子提示和正式锄地共用同一只读校验，不因显示预览改变土地。</summary>
    public static bool TryGetTillingTarget(Vector3 pointer, Vector3 owner, float range,
        out RuntimeTerrainTileSample sample)
    {
        sample = default;
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (manager == null || !manager.TryGetRuntimeTerrainTile(pointer, out sample) ||
            !IsWithinReach(owner, sample.WorldCell, range) || !IsOpen(sample) ||
            !manager.TryGetRuntimeChunkView(sample.Address, out _) || HasWorldPlant(sample.WorldCell))
            return false;
        string source = GetBlockId(sample.Cell.GroundTileId);
        return source == "Tile_Grass" || source == "Tile_Soil";
    }

    private static bool HasSoilState(RuntimeTerrainTileSample sample) =>
        Read(sample.Terrain, sample.LocalCell, SourceLayer) > 0f;

    /// <summary>耕地已有液体覆盖时仍允许结算土壤，供九宫格灌溉吸收自身格液体。</summary>
    private static bool CanUseSoilCell(RuntimeTerrainTileSample sample) =>
        CanUseSoilCell(sample.Terrain, sample.LocalCell.x, sample.LocalCell.y, sample.Cell,
            sample.WorldCell, false);

    private static bool CanUseSoilCell(ChunkTerrainData terrain, int x, int y, TerrainCell cell,
        Vector2Int worldCell, bool normalizedWorldCell) =>
        terrain != null && !terrain.IsDisposed &&
        TerrainSupportLayer.GetTileId(terrain, x, y) == 0 &&
        cell.GroundTileId != 0 && cell.BackTileId == 0 && cell.BlockingTileId == 0 &&
        (cell.Flags & (TerrainCellFlags.Blocking | TerrainCellFlags.Occupied)) == 0 &&
        terrain.GetTileLayerCount(x, y) == 1 &&
        !(normalizedWorldCell ? BuildingOccupancyRegistry.IsOccupiedNormalized(worldCell) :
            BuildingOccupancyRegistry.IsOccupied(worldCell));

    /// <summary>普通土地尚无农业差量时，读取生成环境；已有浇水、施肥或耕作则保留当前值。</summary>
    public static TileData_Farmland ReadSoilSnapshot(RuntimeTerrainTileSample sample)
    {
        var soil = (TileData_Farmland)RequireFarmlandDefinition().TileDataTemplate.Clone();
        ReadSoilSnapshot(sample, soil);
        return soil;
    }

    // 同步批次复用自己的快照容器，数值仍逐次读取当前权威环境层。
    internal static void ReadSoilSnapshot(RuntimeTerrainTileSample sample, TileData_Farmland soil)
    {
        soil.position = (Vector3Int)sample.WorldCell;
        if (HasSoilState(sample))
        {
            soil.waterValue = ReadWaterLevel(sample.Terrain, sample.LocalCell);
            soil.waterSupplyRemainder = Read(sample.Terrain, sample.LocalCell, WaterSupplyRemainderLayer);
            soil.waterConsumptionRemainder = Read(sample.Terrain, sample.LocalCell, WaterConsumptionRemainderLayer);
            SetSnapshotFertility(soil, Read(sample.Terrain, sample.LocalCell, FertilityLayer));
        }
        else
        {
            if (sample.Terrain.TryGetEnvironmentValue("moisture", sample.LocalCell.x, sample.LocalCell.y, out float moisture))
                soil.waterValue = Mathf.RoundToInt(Mathf.Clamp01(moisture) * soil.maxWater);
            if (sample.Terrain.TryGetEnvironmentValue("fertility", sample.LocalCell.x, sample.LocalCell.y, out float fertility))
                SetSnapshotFertility(soil, fertility);
        }
        soil.NormalizeValues();
    }

    private static void SetSnapshotFertility(TileData_Farmland soil, float value)
    {
        soil.fertilityValue ??= new GameValue_float();
        soil.fertilityValue.BaseValue = value;
        soil.fertilityValue.BaseAdditive = 0f;
        soil.fertilityValue.AdditiveModifier = 0f;
        soil.fertilityValue.MultiplicativeModifier = 1f;
        soil.fertilityValue.FinalAdditive = 0f;
    }

    /// <summary>只在实际耕作、浇水或播种时创建水肥差量；允许配置过的自然基质承载作物。</summary>
    public static void EnsureSoilState(RuntimeTerrainTileSample sample)
    {
        if (HasSoilState(sample)) return;
        TileData_Farmland soil = ReadSoilSnapshot(sample);
        int x = sample.LocalCell.x, y = sample.LocalCell.y;
        sample.Terrain.SetEnvironmentValue(SourceLayer, x, y, sample.Cell.GroundTileId);
        sample.Terrain.SetEnvironmentValue(WaterLayer, x, y, soil.waterValue);
        sample.Terrain.SetEnvironmentValue(WaterSupplyRemainderLayer, x, y, soil.waterSupplyRemainder);
        sample.Terrain.SetEnvironmentValue(WaterConsumptionRemainderLayer, x, y, soil.waterConsumptionRemainder);
        sample.Terrain.SetEnvironmentValue(FertilityLayer, x, y, soil.Fertility);
        SaveDataMgr.Instance.RecordAgricultureCell(sample);
    }

    /// <summary>农业水分只读写 0～10 整级，生成湿润度继续使用 0～1 比例。</summary>
    public static int ReadWaterLevel(ChunkTerrainData terrain, Vector2Int local) =>
        Mathf.Clamp(Mathf.RoundToInt(Read(terrain, local, WaterLayer)), 0, TileData_Farmland.MaxWaterLevel);

    public static void SyncSoilEnvironment(ChunkTerrainData terrain, int x, int y, int water, float fertility)
    {
        var template = (TileData_Farmland)RequireFarmlandDefinition().TileDataTemplate;
        terrain.SetEnvironmentValue("moisture", x, y, Mathf.Clamp01((float)water / template.maxWater));
        terrain.SetEnvironmentValue("fertility", x, y, Mathf.Clamp(fertility, 0f, template.maxFertility));
    }

    #endregion

    #region 本格倒水与九宫格灌溉

    // 桶和机器只发布真实倒水位置，农业订阅后只处理这一格。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void BindSurfaceLiquidAdded()
    {
        WorldLiquidSystem.SurfaceLiquidAdded -= NotifySurfaceLiquidAdded;
        WorldLiquidSystem.SurfaceLiquidAdded += NotifySurfaceLiquidAdded;
    }

    public static void NotifySurfaceLiquidAdded(RuntimeTerrainTileSample source)
    {
        if (!GameNetwork.HasStateAuthority || source.Terrain == null || source.Terrain.IsDisposed)
            return;
        TerrainCell current = source.Terrain.GetCell(source.LocalCell.x, source.LocalCell.y);
        if (!IsFarmland(current) || !CanUseSoilCell(source.Terrain, source.LocalCell.x, source.LocalCell.y,
                current, source.WorldCell, false))
            return;
        var farmland = new RuntimeTerrainTileSample(source.Address, source.Terrain, source.WorldCell,
            source.LocalCell, current, source.TopTileId);
        TileData_Farmland soil = ReadSoilSnapshot(farmland);
        if (TryAbsorbLiquid(farmland, soil))
            CommitSoil(soil);
    }

    /// <summary>作物读取时保留九宫格灌溉，每份地表水只兑换一级耕地水分。</summary>
    private static bool TryAbsorbNearbyLiquid(RuntimeTerrainTileSample farmland, TileData_Farmland soil,
        SoilSamplingBatch batch = null)
    {
        if (!GameNetwork.HasStateAuthority || soil == null || ChunkMgr.ExistingInstance == null)
            return false;

        soil.NormalizeValues();
        if (soil.waterValue >= soil.maxWater) return false;

        bool absorbedAny = false;
        Vector2Int center = batch == null ? WorldTopologyRuntime.NormalizeCell(farmland.WorldCell) : farmland.WorldCell;
        for (int dy = -1; dy <= 1 && soil.waterValue < soil.maxWater; dy++)
        for (int dx = -1; dx <= 1 && soil.waterValue < soil.maxWater; dx++)
        {
            Vector2Int cell = center + new Vector2Int(dx, dy);
            Vector2 position = new(cell.x + 0.5f, cell.y + 0.5f);
            RuntimeTerrainTileSample source;
            bool found = batch != null ? batch.TrySample(position, out source) :
                ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(position, out source);
            if (!found ||
                source.Terrain == null || source.Terrain.IsDisposed)
                continue;
            absorbedAny |= TryAbsorbLiquid(source, soil);
        }

        return absorbedAny;
    }

    private static bool TryAbsorbLiquid(RuntimeTerrainTileSample source, TileData_Farmland soil)
    {
        int requested = Mathf.Min(soil.maxWater - soil.waterValue, source.LiquidLevel);
        if (requested <= 0 || !WorldLiquidSystem.TryGetDefinition(source, out LiquidDefinition liquid) ||
            !string.Equals(liquid.Category, "water", StringComparison.OrdinalIgnoreCase) ||
            !WorldLiquidSystem.TryPump(source, requested, out _, out int removed) || removed <= 0)
            return false;
        soil.AddWater(removed);
        return true;
    }

    #endregion
}
