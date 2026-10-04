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
        sample.Terrain != null && !sample.Terrain.IsDisposed &&
        TerrainSupportLayer.GetTileId(sample.Terrain, sample.LocalCell.x, sample.LocalCell.y) == 0 &&
        sample.Cell.GroundTileId != 0 && sample.Cell.BackTileId == 0 && sample.Cell.BlockingTileId == 0 &&
        (sample.Cell.Flags & (TerrainCellFlags.Blocking | TerrainCellFlags.Occupied)) == 0 &&
        sample.Terrain.GetTileLayerCount(sample.LocalCell.x, sample.LocalCell.y) == 1 &&
        !BuildingOccupancyRegistry.IsOccupied(sample.WorldCell);

    /// <summary>普通土地尚无农业差量时，读取生成环境；已有浇水、施肥或耕作则保留当前值。</summary>
    public static TileData_Farmland ReadSoilSnapshot(RuntimeTerrainTileSample sample)
    {
        var soil = (TileData_Farmland)RequireFarmlandDefinition().TileDataTemplate.Clone();
        soil.position = (Vector3Int)sample.WorldCell;
        if (HasSoilState(sample))
        {
            soil.waterValue = Read(sample.Terrain, sample.LocalCell, WaterLayer);
            soil.fertilityValue = new GameValue_float(Read(sample.Terrain, sample.LocalCell, FertilityLayer));
        }
        else
        {
            if (sample.Terrain.TryGetEnvironmentValue("moisture", sample.LocalCell.x, sample.LocalCell.y, out float moisture))
                soil.waterValue = Mathf.Clamp01(moisture) * soil.maxWater;
            if (sample.Terrain.TryGetEnvironmentValue("fertility", sample.LocalCell.x, sample.LocalCell.y, out float fertility))
                soil.fertilityValue = new GameValue_float(fertility);
        }
        soil.NormalizeValues();
        return soil;
    }

    /// <summary>只在实际耕作、浇水或播种时创建水肥差量；允许配置过的自然基质承载作物。</summary>
    public static void EnsureSoilState(RuntimeTerrainTileSample sample)
    {
        if (HasSoilState(sample)) return;
        TileData_Farmland soil = ReadSoilSnapshot(sample);
        int x = sample.LocalCell.x, y = sample.LocalCell.y;
        sample.Terrain.SetEnvironmentValue(SourceLayer, x, y, sample.Cell.GroundTileId);
        sample.Terrain.SetEnvironmentValue(WaterLayer, x, y, soil.waterValue);
        sample.Terrain.SetEnvironmentValue(FertilityLayer, x, y, soil.Fertility);
        SaveDataMgr.Instance.RecordAgricultureCell(sample);
    }

    /// <summary>普通土地的湿润度继续使用生成环境的 0～1 单位；农业水量为 0～100 点。</summary>
    public static void SyncSoilEnvironment(ChunkTerrainData terrain, int x, int y, float water, float fertility)
    {
        var template = (TileData_Farmland)RequireFarmlandDefinition().TileDataTemplate;
        terrain.SetEnvironmentValue("moisture", x, y, Mathf.Clamp01(water / Mathf.Max(0.01f, template.maxWater)));
        terrain.SetEnvironmentValue("fertility", x, y, Mathf.Clamp(fertility, 0f, template.maxFertility));
    }

    #endregion

    #region 九宫格液体灌溉

    /// <summary>农田从自身及周围八格的世界水体吸水，一份世界液体对应一点土壤水分。</summary>
    private static bool TryAbsorbNearbyLiquid(RuntimeTerrainTileSample farmland, TileData_Farmland soil)
    {
        if (!GameNetwork.HasStateAuthority || soil == null || ChunkMgr.ExistingInstance == null)
            return false;

        soil.NormalizeValues();
        float missingWater = Mathf.Max(0f, soil.maxWater - soil.waterValue);
        if (missingWater <= 0.0001f) return false;

        bool absorbedAny = false;
        Vector2Int center = WorldTopologyRuntime.NormalizeCell(farmland.WorldCell);
        for (int dy = -1; dy <= 1 && missingWater > 0.0001f; dy++)
        for (int dx = -1; dx <= 1 && missingWater > 0.0001f; dx++)
        {
            Vector2Int cell = WorldTopologyRuntime.NormalizeCell(center + new Vector2Int(dx, dy));
            if (!ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(
                    new Vector2(cell.x + 0.5f, cell.y + 0.5f), out RuntimeTerrainTileSample source) ||
                source.Terrain == null || source.Terrain.IsDisposed ||
                !WorldLiquidSystem.TryGetDefinition(source, out LiquidDefinition liquid) ||
                !string.Equals(liquid.Category, "water", StringComparison.OrdinalIgnoreCase))
                continue;

            float depth = source.Terrain.GetLiquidDepth(source.LocalCell.x, source.LocalCell.y);
            float depthPerServing = liquid.WorldWater.DepthPerServing;
            float servings = Mathf.Min(missingWater, depth / depthPerServing);
            if (servings <= 0.0001f ||
                !WorldLiquidSystem.TryPump(source, servings * depthPerServing, out _, out float removedDepth))
                continue;

            float absorbed = removedDepth / depthPerServing;
            if (absorbed <= 0f) continue;
            soil.AddWater(absorbed);
            missingWater = Mathf.Max(0f, soil.maxWater - soil.waterValue);
            absorbedAny = true;
        }

        return absorbedAny;
    }

    #endregion
}
