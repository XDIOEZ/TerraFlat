using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>玩家填土事务及旧二维地层存档的单次导入。</summary>
public static class GroundTileLayerSystem
{
    #region 填土地层事务
    public static bool TryApplyPlayerCover(RuntimeTerrainTileSample sample, RuntimeTileDefinition target, out string reason)
    {
        reason = null;
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (manager == null || !manager.TryGetRuntimeTerrainTile(
                (Vector2)sample.WorldCell + Vector2.one * .5f, out RuntimeTerrainTileSample current) ||
            !ReferenceEquals(current.Terrain, sample.Terrain))
        {
            reason = "目标地格已经卸载或变更。";
            return false;
        }
        sample = current;
        if (!GameNetwork.HasStateAuthority || SaveDataMgr.Instance?.SaveData == null ||
            target == null || target.RuntimeTileId <= 0 || !target.TileDataTemplate.IsWalkable ||
            BlockingTilemapLayer.IsBlockingTile(target.TileDataTemplate) ||
            !FarmlandSystem.IsOpen(sample, allowCoveringSupport: true) || FarmlandSystem.HasWorldPlant(sample.WorldCell) ||
            !manager.TryGetChunkRuntime(sample.Address, out ChunkRuntime chunk))
        {
            reason = "当前地格无法覆盖地表。";
            return false;
        }
        Vector2Int local = sample.LocalCell;
        GroundLayerCellSaveData previous = Capture(sample.Terrain, local);
        try
        {
            sample.Terrain.PushGroundStratum(local.x, local.y, target.RuntimeTileId, sample.Cell.NavigationCost);
            SaveDataMgr.Instance.RecordGroundLayerCell(sample);
            SaveDataMgr.Instance.RecordRuntimeTerrainChange(new TileBuildingCell(chunk, sample.WorldCell, local,
                target.RuntimeTileId, target.Id));
            return true;
        }
        catch (Exception exception)
        {
            Restore(sample.Terrain, previous);
            SaveDataMgr.Instance.RecordGroundLayerCell(sample);
            SaveDataMgr.Instance.RecordRuntimeTerrainChange(new TileBuildingCell(chunk, sample.WorldCell, local,
                sample.Cell.GroundTileId, FarmlandSystem.GetBlockId(sample.Cell.GroundTileId)));
            Debug.LogError($"[地表覆盖] 提交失败：{exception}");
            reason = "地表覆盖保存失败。";
            return false;
        }
    }
    #endregion

    #region 列快照与旧地层导入
    internal static GroundLayerCellSaveData Capture(ChunkTerrainData terrain, Vector2Int local) =>
        GroundLayerCellSaveData.CaptureColumn(terrain, local);

    internal static void Restore(ChunkTerrainData terrain, GroundLayerCellSaveData snapshot)
    {
        Vector2Int local = snapshot.LocalPosition;
        if (snapshot.BottomZ.HasValue)
        {
            terrain.RestoreGroundColumn(local.x, local.y, snapshot.ToGroundColumn());
            return;
        }
        ImportLegacyGroundColumn(terrain, snapshot);
    }

    // 旧档仅有计数和覆盖前快照，在生成基线就绪后一次物化实际地层。
    private static void ImportLegacyGroundColumn(ChunkTerrainData terrain, GroundLayerCellSaveData snapshot)
    {
        Vector2Int local = snapshot.LocalPosition;
        if (snapshot.ExcavatedLayers < 0 || snapshot.ExcavatedLayers > 100 || snapshot.ExcavatedSurfaceLevels < 0)
            throw new InvalidOperationException("旧地层采挖记录无效。");
        int count = snapshot.Covers?.Count ?? 0;
        TerrainCell surface = terrain.GetCell(local.x, local.y);
        int naturalId = count > 0 ? snapshot.Covers[0].UnderlyingCell.GroundTileId : surface.GroundTileId;
        TerrainCell naturalCell = count > 0 ? snapshot.Covers[0].UnderlyingCell.ToTerrainCell() : surface;
        var down = new List<GroundStratum>();
        IReadOnlyList<int> natural = terrain.GetNaturalGroundTileIds(naturalId);
        int skip = natural.Count > 1 && natural[0] == natural[1] ? snapshot.ExcavatedLayers : 0;
        for (int i = Math.Min(skip, Math.Max(0, natural.Count - 1)); i < natural.Count; i++)
            down.Add(new GroundStratum { TileId = natural[i], Grass = ChunkTerrainData.GrassEmpty,
                NavigationCost = naturalCell.NavigationCost, Walkable = (naturalCell.Flags & TerrainCellFlags.Walkable) != 0 });
        if (down.Count == 0 && naturalId > 0) down.Add(new GroundStratum { TileId = naturalId, NavigationCost = naturalCell.NavigationCost,
            Walkable = (naturalCell.Flags & TerrainCellFlags.Walkable) != 0 });
        if (down.Count > 0)
        {
            GroundStratum naturalTop = down[0];
            naturalTop.TileId = naturalId;
            naturalTop.Grass = count > 0 ? snapshot.Covers[0].UnderlyingGrass : snapshot.SurfaceGrass;
            naturalTop.HarvestProgress = count > 0 ? NormalizeLegacyProgress(terrain, naturalId, snapshot.Covers[0].UnderlyingCell.AccumulatedDamage) : ReadLegacyProgress(terrain, local);
            down[0] = naturalTop;
        }
        down.Reverse();
        for (int i = 1; i <= count; i++)
        {
            GroundCoverLayerSaveData hidden = i < count ? snapshot.Covers[i] : null;
            TerrainCell coveredCell = hidden?.UnderlyingCell.ToTerrainCell() ?? surface;
            down.Add(new GroundStratum { TileId = hidden?.UnderlyingCell.GroundTileId ?? surface.GroundTileId,
                Grass = hidden?.UnderlyingGrass ?? snapshot.SurfaceGrass,
                HarvestProgress = hidden == null ? ReadLegacyProgress(terrain, local) : NormalizeLegacyProgress(terrain, hidden.UnderlyingCell.GroundTileId, hidden.UnderlyingCell.AccumulatedDamage), PlayerPlaced = true, NavigationCost = coveredCell.NavigationCost,
                Walkable = (coveredCell.Flags & TerrainCellFlags.Walkable) != 0 });
        }
        int minimumDepth = GameRes.ExistingInstance != null && GameRes.ExistingInstance.TryGetTileDefinition(
            surface.GroundTileId, out RuntimeTileDefinition definition) ? definition.MinimumSurfaceDepth : 0;
        int top = TerrainSurfaceHeightLayer.GetBaseLevel(terrain, local.x, local.y,
                terrain.GenerationMode == ChunkGenerationMode.Surface) - TerrainSurfaceHeightLayer.GetNaturalDepthLevels(terrain, local.x, local.y) -
            Math.Max(snapshot.ExcavatedSurfaceLevels, minimumDepth) + count;
        terrain.RestoreGroundColumn(local.x, local.y, new GroundColumnSnapshot { BottomZ = top - down.Count + 1, Layers = down.ToArray() });
    }
    private static float ReadLegacyProgress(ChunkTerrainData terrain, Vector2Int local) =>
        terrain.GetCell(local.x, local.y).BlockingTileId == 0 && terrain.TryGetEnvironmentValue(
            TileBuildingSystem.RuntimeDamageLayerId, local.x, local.y, out float value) ?
            NormalizeLegacyProgress(terrain, terrain.GetCell(local.x, local.y).GroundTileId, value) : 0f;
    private static float NormalizeLegacyProgress(ChunkTerrainData terrain, int tileId, float progress) =>
        terrain.IsGroundTileHarvestable(tileId) ? Mathf.Clamp01(progress) : 0f;
    #endregion
}
