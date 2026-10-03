using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>伪 Z 轴只记录采掉的天然层数和玩家覆盖前的快照，不移动世界坐标。</summary>
public static class GroundTileLayerSystem
{
    #region 地层状态
    public const string DepthLayerId = "flatworld.ground.excavatedLayers";
    private static readonly ConditionalWeakTable<ChunkTerrainData, Dictionary<int, List<GroundCoverLayerSaveData>>> covers = new();

    public static int ReadDepth(ChunkTerrainData terrain, Vector2Int local) =>
        terrain.TryGetEnvironmentValue(DepthLayerId, local.x, local.y, out float value) ? (int)value : 0;

    internal static void WriteDepth(ChunkTerrainData terrain, Vector2Int local, int value)
    {
        if (ReadDepth(terrain, local) != value)
            terrain.SetEnvironmentValue(DepthLayerId, local.x, local.y, value);
    }

    private static int CellKey(ChunkTerrainData terrain, Vector2Int local) => local.y * terrain.Width + local.x;

    private static List<GroundCoverLayerSaveData> GetCovers(ChunkTerrainData terrain, Vector2Int local, bool create)
    {
        if (!covers.TryGetValue(terrain, out var cells))
        {
            if (!create) return null;
            cells = covers.GetOrCreateValue(terrain);
        }
        int key = CellKey(terrain, local);
        if (!cells.TryGetValue(key, out var layers) && create)
            cells.Add(key, layers = new List<GroundCoverLayerSaveData>());
        return layers;
    }

    internal static bool TryGetUnderlying(ChunkTerrainData terrain, Vector2Int local, out GroundCoverLayerSaveData layer)
    {
        var layers = GetCovers(terrain, local, false);
        layer = layers?.Count > 0 ? layers[layers.Count - 1] : null;
        return layer != null;
    }

    internal static void PopCover(ChunkTerrainData terrain, Vector2Int local)
    {
        var layers = GetCovers(terrain, local, false);
        if (layers == null || layers.Count == 0)
            throw new InvalidOperationException("目标地格没有玩家覆盖层。");
        layers.RemoveAt(layers.Count - 1);
        if (layers.Count == 0 && covers.TryGetValue(terrain, out var cells))
            cells.Remove(CellKey(terrain, local));
    }
    #endregion

    #region 后续地形工具入口
    /// <summary>后续玩家改地形工具必须走此入口，先压栈原地表再覆盖，挖掉时才能准确还原。</summary>
    public static bool TryApplyPlayerCover(RuntimeTerrainTileSample sample, RuntimeTileDefinition target, out string reason)
    {
        reason = null;
        // 提交前重新采样，不能让缓存的旧地格绕过当前占用和液体约束。
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (manager == null || !manager.TryGetRuntimeTerrainTile(
                (Vector2)sample.WorldCell + Vector2.one * 0.5f, out RuntimeTerrainTileSample current) ||
            !ReferenceEquals(current.Terrain, sample.Terrain))
        {
            reason = "目标地格已经卸载或变更。";
            return false;
        }
        sample = current;
        if (!GameNetwork.HasStateAuthority || SaveDataMgr.Instance?.SaveData == null ||
            sample.Terrain == null || sample.Terrain.IsDisposed || target == null || target.RuntimeTileId <= 0 ||
            !target.TileDataTemplate.IsWalkable || BlockingTilemapLayer.IsBlockingTile(target.TileDataTemplate) ||
            !FarmlandSystem.IsOpen(sample) || FarmlandSystem.HasWorldPlant(sample.WorldCell) ||
            ChunkMgr.ExistingInstance == null ||
            !ChunkMgr.ExistingInstance.TryGetChunkRuntime(sample.Address, out ChunkRuntime chunk))
        {
            reason = "当前地格无法覆盖地表。";
            return false;
        }

        Vector2Int local = sample.LocalCell;
        TerrainCell previous = sample.Terrain.GetCell(local.x, local.y);
        float previousProgress = GroundTileHarvestSystem.ReadProgress(sample);
        GroundLayerCellSaveData previousLayers = Capture(sample.Terrain, local);
        GetCovers(sample.Terrain, local, true).Add(new GroundCoverLayerSaveData
        {
            UnderlyingCell = RuntimeTileCellSaveDelta.Capture(local, previous, previousProgress),
            UnderlyingGrass = sample.Terrain.GetGrass(local.x, local.y)
        });
        try
        {
            sample.Terrain.SetGrass(local.x, local.y, 0);
            sample.Terrain.SetEnvironmentValue(TileBuildingSystem.RuntimeDamageLayerId, local.x, local.y, 0f);
            sample.Terrain.SetCell(local.x, local.y, new TerrainCell(target.RuntimeTileId, 0, 0,
                previous.BiomeId, previous.NavigationCost, previous.Flags));
            SaveDataMgr.Instance.RecordGroundLayerCell(sample);
            SaveDataMgr.Instance.RecordRuntimeTerrainChange(new TileBuildingCell(chunk, sample.WorldCell, local,
                target.RuntimeTileId, target.Id));
            return true;
        }
        catch (Exception exception)
        {
            Restore(sample.Terrain, previousLayers);
            sample.Terrain.SetCell(local.x, local.y, previous);
            sample.Terrain.SetEnvironmentValue(TileBuildingSystem.RuntimeDamageLayerId, local.x, local.y,
                previousProgress);
            // 回滚持久化快照，防止一次失败的覆盖在重进后生效。
            SaveDataMgr.Instance.RecordGroundLayerCell(sample);
            SaveDataMgr.Instance.RecordRuntimeTerrainChange(new TileBuildingCell(chunk, sample.WorldCell, local,
                previous.GroundTileId, FarmlandSystem.GetBlockId(previous.GroundTileId)));
            Debug.LogError($"[地表覆盖] 提交失败：{exception}");
            reason = "地表覆盖保存失败。";
            return false;
        }
    }
    #endregion

    #region 快照与恢复
    internal static GroundLayerCellSaveData Capture(ChunkTerrainData terrain, Vector2Int local)
    {
        var snapshot = new GroundLayerCellSaveData
        {
            LocalPosition = local,
            ExcavatedLayers = ReadDepth(terrain, local),
            SurfaceGrass = terrain.GetGrass(local.x, local.y)
        };
        var layers = GetCovers(terrain, local, false);
        if (layers != null)
            foreach (var layer in layers) snapshot.Covers.Add(Clone(layer));
        return snapshot;
    }

    internal static void Restore(ChunkTerrainData terrain, GroundLayerCellSaveData snapshot)
    {
        Vector2Int local = snapshot.LocalPosition;
        if ((uint)local.x >= (uint)terrain.Width || (uint)local.y >= (uint)terrain.Height ||
            snapshot.ExcavatedLayers < 0 || snapshot.ExcavatedLayers > 100)
            throw new InvalidOperationException("地层快照的坐标或天然采挖层数无效。");
        // 未覆盖的天然地格不创建覆盖栈；先校验全部下层，再替换现有运行态。
        if (snapshot.Covers != null)
        {
            foreach (var layer in snapshot.Covers)
                if (layer?.UnderlyingCell == null || layer.UnderlyingCell.LocalPosition != local ||
                    !GameRes.ExistingInstance.TryGetTileDefinition(layer.UnderlyingCell.GroundTileId, out _))
                    throw new InvalidOperationException("覆盖层下方的地块快照缺失或未登记。");
        }
        var layers = GetCovers(terrain, local, snapshot.Covers?.Count > 0);
        layers?.Clear();
        if (snapshot.Covers != null && layers != null)
            foreach (var layer in snapshot.Covers) layers.Add(Clone(layer));
        if ((layers == null || layers.Count == 0) && covers.TryGetValue(terrain, out var cells))
            cells.Remove(CellKey(terrain, local));
        WriteDepth(terrain, local, snapshot.ExcavatedLayers);
        terrain.SetGrass(local.x, local.y, snapshot.SurfaceGrass);
    }

    private static GroundCoverLayerSaveData Clone(GroundCoverLayerSaveData layer) => new()
    {
        UnderlyingCell = RuntimeTileCellSaveDelta.Capture(layer.UnderlyingCell.LocalPosition,
            layer.UnderlyingCell.ToTerrainCell(), layer.UnderlyingCell.AccumulatedDamage),
        UnderlyingGrass = layer.UnderlyingGrass
    };
    #endregion
}
