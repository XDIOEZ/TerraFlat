using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>
/// 农业地形服务：进度、水分和肥力只写入权威区块的独立环境层。
/// 完成耕作才替换地表；TileData 仅作为成长计算快照，修改后必须提交。
/// </summary>
public static class FarmlandSystem
{
    #region 地块契约

    public const string TileBlockId = "Tile_Farmland";
    public const string ProgressLayer = "flatworld.agriculture.progress";
    public const string SourceLayer = "flatworld.agriculture.source";
    public const string WaterLayer = "flatworld.agriculture.water";
    public const string FertilityLayer = "flatworld.agriculture.fertility";
    private static readonly List<Item> occupancyBuffer = new(); // 主线程同步查询缓存
    private static readonly HashSet<Item> occupancyDedupe = new();

    public static float Read(ChunkTerrainData terrain, Vector2Int local, string layer) =>
        terrain.TryGetEnvironmentValue(layer, local.x, local.y, out float value) ? value : 0f;

    /// <summary>通过当前世界映射解析地块。</summary>
    public static string GetBlockId(int tileId)
    {
        var profile = ChunkMgr.ExistingInstance?.ActiveGenerationProfile;
        return profile != null && profile.TextParameters.TryGetValue($"tile.block.{tileId}", out string id)
            ? id : null;
    }

    public static bool IsFarmland(TerrainCell cell) => GetBlockId(cell.GroundTileId) == TileBlockId;

    /// <summary>按格子最近边缘计算可操作距离，允许手机准线在最远端吸附。</summary>
    public static bool IsWithinReach(Vector3 owner, Vector2Int cell, float range)
    {
        Vector2 offset = WorldTopologyRuntime.ShortestDelta((Vector2)owner,
            new Vector2(cell.x + 0.5f, cell.y + 0.5f));
        return new Vector2(Mathf.Max(0f, Mathf.Abs(offset.x) - 0.5f),
            Mathf.Max(0f, Mathf.Abs(offset.y) - 0.5f)).sqrMagnitude <= range * range;
    }

    public static bool IsOpen(RuntimeTerrainTileSample sample) =>
        sample.Cell.GroundTileId != 0 && sample.Cell.BackTileId == 0 && sample.Cell.BlockingTileId == 0 &&
        (sample.Cell.Flags & (TerrainCellFlags.Water | TerrainCellFlags.Blocking | TerrainCellFlags.Occupied)) == 0 &&
        sample.Terrain.GetTileLayerCount(sample.LocalCell.x, sample.LocalCell.y) == 1 &&
        !BuildingOccupancyRegistry.IsOccupied(sample.WorldCell);

    /// <summary>活着的植株和资源节点占格；普通掉落物与玩家不阻止农业操作。</summary>
    public static bool HasWorldPlant(Vector2Int cell)
    {
        ItemMgr.Instance.QueryItemsInCircleNonAlloc(new Vector2(cell.x + 0.5f, cell.y + 0.5f),
            2f, ~0, null, occupancyBuffer, occupancyDedupe);
        try
        {
            foreach (Item candidate in occupancyBuffer)
            {
                if (candidate == null || candidate is Player || candidate is Map || candidate.InHand ||
                    candidate.Owner != null || candidate.DestructionHandled || candidate.itemData?.Stack == null ||
                    candidate.itemData.Stack.CanBePickedUp)
                    continue;
                Vector3 pos = WorldTopologyRuntime.NormalizePosition(candidate.transform.position);
                if (new Vector2Int(Mathf.FloorToInt(pos.x), Mathf.FloorToInt(pos.y)) == cell)
                    return true;
            }
            return false;
        }
        finally
        {
            occupancyBuffer.Clear();
            occupancyDedupe.Clear();
        }
    }

    #endregion

    #region 耕作事务

    public static bool TryTill(Vector3 pointer, Vector3 owner, float range, float work, out bool completed)
    {
        completed = false;
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (manager == null || !manager.TryGetRuntimeTerrainTile(pointer, out var sample) ||
            !IsWithinReach(owner, sample.WorldCell, range) || !IsOpen(sample))
            return false;
        string sourceId = GetBlockId(sample.Cell.GroundTileId);
        if (sourceId != "Tile_Grass" && sourceId != "Tile_Soil")
            return false;
        if (!manager.TryGetRuntimeChunkView(sample.Address, out _) || HasWorldPlant(sample.WorldCell))
            return false;

        int farmlandId = ResolveFarmlandId();
        var template = (TileData_Farmland)GameRes.Instance.GetTileBlock(TileBlockId).tileDataTemplate;
        var local = sample.LocalCell;
        float progress = Read(sample.Terrain, local, SourceLayer) == sample.Cell.GroundTileId
            ? Read(sample.Terrain, local, ProgressLayer) : 0f;
        progress = Mathf.Min(1f, progress + work);
        completed = progress >= 0.9999f;
        sample.Terrain.SetGrass(local.x, local.y, 0);
        sample.Terrain.SetEnvironmentValue(SourceLayer, local.x, local.y, sample.Cell.GroundTileId);
        sample.Terrain.SetEnvironmentValue(ProgressLayer, local.x, local.y, completed ? 1f : progress);
        if (completed)
        {
            sample.Terrain.SetEnvironmentValue(WaterLayer, local.x, local.y, template.waterValue);
            sample.Terrain.SetEnvironmentValue(FertilityLayer, local.x, local.y, template.Fertility);
            sample.Terrain.SetCell(local.x, local.y, new TerrainCell(farmlandId, 0, 0,
                sample.Cell.BiomeId, sample.Cell.NavigationCost, sample.Cell.Flags));
            manager.TryGetChunkRuntime(sample.Address, out ChunkRuntime chunk);
            SaveDataMgr.Instance.RecordRuntimeTerrainChange(
                new TileBuildingCell(chunk, sample.WorldCell, local, farmlandId, TileBlockId));
        }
        SaveDataMgr.Instance.RecordAgricultureCell(sample);
        return true;
    }

    private static int ResolveFarmlandId()
    {
        foreach (var entry in ChunkMgr.ExistingInstance.ActiveGenerationProfile.TextParameters)
        {
            if (entry.Value == TileBlockId && entry.Key.StartsWith("tile.block.", StringComparison.Ordinal))
                return int.Parse(entry.Key.Substring("tile.block.".Length));
        }
        throw new InvalidOperationException("当前世界未登记耕地，请使用包含耕地映射的新世界。");
    }

    #endregion

    #region 水肥快照

    public static bool TryReadSoil(Vector2Int worldCell, out TileData_Farmland soil)
    {
        soil = null;
        if (ChunkMgr.ExistingInstance == null || !ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(
                new Vector2(worldCell.x + 0.5f, worldCell.y + 0.5f), out var sample) || !IsFarmland(sample.Cell))
            return false;
        soil = (TileData_Farmland)GameRes.Instance.GetTileBlock(TileBlockId).tileDataTemplate.Clone();
        soil.position = (Vector3Int)worldCell;
        soil.waterValue = Read(sample.Terrain, sample.LocalCell, WaterLayer);
        soil.fertilityValue = new GameValue_float(Read(sample.Terrain, sample.LocalCell, FertilityLayer));
        return true;
    }

    /// <summary>成长或施肥完成后提交水肥快照。</summary>
    public static void CommitSoil(TileData_Farmland soil)
    {
        if (!ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(
                new Vector2(soil.position.x + 0.5f, soil.position.y + 0.5f), out var sample) || !IsFarmland(sample.Cell))
            return;
        soil.NormalizeValues();
        sample.Terrain.SetEnvironmentValue(WaterLayer, sample.LocalCell.x, sample.LocalCell.y, soil.waterValue);
        sample.Terrain.SetEnvironmentValue(FertilityLayer, sample.LocalCell.x, sample.LocalCell.y, soil.Fertility);
        SaveDataMgr.Instance.RecordAgricultureCell(sample);
    }

    #endregion
}
