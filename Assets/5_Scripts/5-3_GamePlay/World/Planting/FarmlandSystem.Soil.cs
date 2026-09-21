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

    /// <summary>普通土地尚无农业差量时，读取生成环境；已有浇水、施肥或耕作则保留当前值。</summary>
    public static TileData_Farmland ReadSoilSnapshot(RuntimeTerrainTileSample sample)
    {
        var soil = (TileData_Farmland)GameRes.Instance.GetTileBlock(TileBlockId).tileDataTemplate.Clone();
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
        var template = (TileData_Farmland)GameRes.Instance.GetTileBlock(TileBlockId).tileDataTemplate;
        terrain.SetEnvironmentValue("moisture", x, y, Mathf.Clamp01(water / Mathf.Max(0.01f, template.maxWater)));
        terrain.SetEnvironmentValue("fertility", x, y, Mathf.Clamp(fertility, 0f, template.maxFertility));
    }

    #endregion

    #region 倾倒液体的世界提交

    /// <summary>一份水增加一点地面水分；水格增加原水深。只更新已有格子，不生成水面或替换地形。</summary>
    public static bool TryAddGroundWater(Vector2 worldPosition, float amount)
    {
        if (!FlatWorld.Networking.GameNetwork.HasStateAuthority || amount <= 0f ||
            float.IsNaN(amount) || float.IsInfinity(amount) || ChunkMgr.ExistingInstance == null ||
            !ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(worldPosition, out var sample))
            return false;
        int x = sample.LocalCell.x, y = sample.LocalCell.y;
        float liquidDepth = sample.Terrain.GetLiquidDepth(x, y);
        if (liquidDepth > 0f)
            return WorldLiquidSystem.TrySet(sample, sample.Terrain.GetLiquidId(x, y), liquidDepth + amount);
        if (!IsOpen(sample)) return false;
        EnsureSoilState(sample);
        TileData_Farmland soil = ReadSoilSnapshot(sample);
        soil.AddWater(amount);
        CommitSoil(soil);
        return true;
    }

    #endregion
}
