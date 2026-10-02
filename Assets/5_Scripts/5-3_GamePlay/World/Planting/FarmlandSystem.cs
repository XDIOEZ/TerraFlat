using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>
/// 农业地形服务：未完成耕作只保留当前会话的临时视觉进度，正式完成后才写入耕地 Tile。
/// 水分和肥力继续写入权威区块环境层；TileData 仅作为成长计算快照，修改后必须提交。
/// </summary>
public static partial class FarmlandSystem
{
    #region 地块契约

    public const string TileBlockId = "Tile_Farmland";
    public const string SourceLayer = "flatworld.agriculture.source";
    public const string WaterLayer = "flatworld.agriculture.water";
    public const string FertilityLayer = "flatworld.agriculture.fertility";
    public const float TillingRecoveryDelaySeconds = 10f;
    public const float TillingRecoveryDurationSeconds = 2.5f;
    private static readonly List<Item> occupancyBuffer = new(); // 主线程同步查询缓存
    private static readonly HashSet<Item> occupancyDedupe = new();

    public static float Read(ChunkTerrainData terrain, Vector2Int local, string layer) =>
        terrain.TryGetEnvironmentValue(layer, local.x, local.y, out float value) ? value : 0f;

    /// <summary>统一从地块目录按数字 ID 解析稳定地块 ID。</summary>
    public static string GetBlockId(int tileId)
    {
        GameRes resources = GameRes.ExistingInstance;
        return tileId > 0 && resources != null &&
               resources.TryGetTileDefinition(tileId, out RuntimeTileDefinition definition)
            ? definition.Id
            : null;
    }

    public static bool IsFarmland(TerrainCell cell) => GetBlockId(cell.GroundTileId) == TileBlockId;

    private static RuntimeTileDefinition RequireFarmlandDefinition()
    {
        GameRes resources = GameRes.ExistingInstance;
        if (resources == null || !resources.TryGetTileDefinition(TileBlockId, out RuntimeTileDefinition definition) ||
            definition.RuntimeTileId <= 0 || definition.TileDataTemplate is not TileData_Farmland)
            throw new InvalidOperationException("地块目录未登记有效的 Tile_Farmland。");
        return definition;
    }

    /// <summary>按格子最近边缘计算可操作距离，允许手机准线在最远端吸附。</summary>
    public static bool IsWithinReach(Vector3 owner, Vector2Int cell, float range)
    {
        Vector2 offset = WorldTopologyRuntime.ShortestDelta((Vector2)owner,
            new Vector2(cell.x + 0.5f, cell.y + 0.5f));
        return new Vector2(Mathf.Max(0f, Mathf.Abs(offset.x) - 0.5f),
            Mathf.Max(0f, Mathf.Abs(offset.y) - 0.5f)).sqrMagnitude <= range * range;
    }

    public static bool IsOpen(RuntimeTerrainTileSample sample) =>
        TerrainSupportLayer.GetTileId(sample.Terrain, sample.LocalCell.x, sample.LocalCell.y) == 0 &&
        sample.Cell.GroundTileId != 0 && sample.Cell.BackTileId == 0 && sample.Cell.BlockingTileId == 0 &&
        sample.LiquidDepth <= 0f &&
        (sample.Cell.Flags & (TerrainCellFlags.Blocking | TerrainCellFlags.Occupied)) == 0 &&
        sample.Terrain.GetTileLayerCount(sample.LocalCell.x, sample.LocalCell.y) == 1 &&
        !BuildingOccupancyRegistry.IsOccupied(sample.WorldCell);

    /// <summary>活着的植株和资源节点占格；普通掉落物与玩家不阻止农业操作。</summary>
    public static bool HasWorldPlant(Vector2Int cell)
    {
        cell = WorldTopologyRuntime.NormalizeCell(cell);
        if (FlatWorld.NaturalEntities.NaturalEntityEcsService.HasResourceAtCell(cell)) return true;
        if (ItemMgr.Instance == null) return false;
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

    #region 临时耕作表现

    private readonly struct TillingKey : IEquatable<TillingKey>
    {
        public readonly RuntimeWorldAddress Address;
        public readonly Vector2Int LocalCell;

        public TillingKey(RuntimeWorldAddress address, Vector2Int localCell)
        {
            Address = address;
            LocalCell = localCell;
        }

        public bool Equals(TillingKey other) => Address.Equals(other.Address) && LocalCell == other.LocalCell;
        public override bool Equals(object obj) => obj is TillingKey other && Equals(other);
        public override int GetHashCode() => unchecked((Address.GetHashCode() * 397) ^ LocalCell.GetHashCode());
    }

    private sealed class TillingRuntimeState
    {
        public int SourceGroundTileId;
        public float Progress;
        public float LastWorkedAt;
    }

    private static readonly Dictionary<TillingKey, TillingRuntimeState> tillingStates = new();
    private static readonly List<TillingKey> tillingRemoveBuffer = new();

    /// <summary>只通知表现层刷新指定格；事件本身不保存任何地形数据。</summary>
    public static event Action<RuntimeWorldAddress, Vector2Int> TillingVisualChanged;

    public static float GetTillingVisualProgress(RuntimeWorldAddress address, Vector2Int localCell, int currentGroundTileId)
    {
        var key = new TillingKey(address, localCell);
        if (!tillingStates.TryGetValue(key, out TillingRuntimeState state) ||
            state.SourceGroundTileId != currentGroundTileId)
        {
            tillingStates.Remove(key);
            return 0f;
        }

        float progress = EvaluateTillingProgress(state, Time.time);
        if (progress <= 0f)
            tillingStates.Remove(key);
        return progress;
    }

    /// <summary>区块表现解绑时丢弃所有未完成耕作，保证它不会跟随存档或重新加载恢复。</summary>
    public static void ClearTillingProgress(RuntimeWorldAddress address)
    {
        tillingRemoveBuffer.Clear();
        foreach (KeyValuePair<TillingKey, TillingRuntimeState> pair in tillingStates)
            if (pair.Key.Address.Equals(address))
                tillingRemoveBuffer.Add(pair.Key);
        for (int i = 0; i < tillingRemoveBuffer.Count; i++)
            tillingStates.Remove(tillingRemoveBuffer[i]);
        tillingRemoveBuffer.Clear();
    }

    private static float EvaluateTillingProgress(TillingRuntimeState state, float now)
    {
        float idle = Mathf.Max(0f, now - state.LastWorkedAt);
        if (idle <= TillingRecoveryDelaySeconds)
            return Mathf.Clamp01(state.Progress);
        float recovery = (idle - TillingRecoveryDelaySeconds) /
                         Mathf.Max(0.01f, TillingRecoveryDurationSeconds);
        return Mathf.Clamp01(state.Progress * (1f - recovery));
    }

    #endregion

    #region 耕作事务

    public static bool TryTill(Vector3 pointer, Vector3 owner, float range, float work, out bool completed)
    {
        completed = false;
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (float.IsNaN(work) || float.IsInfinity(work) || work <= 0f ||
            !TryGetTillingTarget(pointer, owner, range, out var sample))
            return false;

        RuntimeTileDefinition farmland = RequireFarmlandDefinition();
        Vector2Int local = sample.LocalCell;
        var key = new TillingKey(sample.Address, local);
        float now = Time.time;
        float progress = 0f;
        if (tillingStates.TryGetValue(key, out TillingRuntimeState state) &&
            state.SourceGroundTileId == sample.Cell.GroundTileId)
            progress = EvaluateTillingProgress(state, now);

        progress = Mathf.Min(1f, progress + work);
        completed = progress >= 0.9999f;
        if (!completed)
        {
            state ??= new TillingRuntimeState();
            state.SourceGroundTileId = sample.Cell.GroundTileId;
            state.Progress = progress;
            state.LastWorkedAt = now;
            tillingStates[key] = state;
            TillingVisualChanged?.Invoke(sample.Address, local);
            return true;
        }

        // 完成前只取原地表水肥快照；真正完成时才清草并把 Ground Tile 改成正式耕地。
        TileData_Farmland soil = ReadSoilSnapshot(sample);
        tillingStates.Remove(key);
        TillingVisualChanged?.Invoke(sample.Address, local);
        RuntimeGrassClearing.Clear(sample);
        sample.Terrain.SetCell(local.x, local.y, new TerrainCell(farmland.RuntimeTileId, 0, 0,
            sample.Cell.BiomeId, sample.Cell.NavigationCost, sample.Cell.Flags));
        if (!manager.TryGetChunkRuntime(sample.Address, out ChunkRuntime chunk))
            throw new InvalidOperationException($"耕地提交时找不到区块：{sample.Address}");
        SaveDataMgr.Instance.RecordRuntimeTerrainChange(
            new TileBuildingCell(chunk, sample.WorldCell, local, farmland.RuntimeTileId, farmland.Id));
        soil.position = (Vector3Int)sample.WorldCell;
        CommitSoil(soil);
        return true;
    }

    #endregion

    #region 水肥快照

    public static bool TryReadSoil(Vector2Int worldCell, out TileData_Farmland soil)
        => TryReadSoil(worldCell, out soil, out _);

    /// <summary>同时返回已加载的地块，供旧作物识别区分“无耕地”和“区块未就绪”。</summary>
    public static bool TryReadSoil(Vector2Int worldCell, out TileData_Farmland soil,
        out RuntimeTerrainTileSample sample)
    {
        soil = null;
        sample = default;
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (manager == null || !manager.TryGetRuntimeTerrainTile(
                new Vector2(worldCell.x + 0.5f, worldCell.y + 0.5f), out sample) ||
            !IsOpen(sample) || (!IsFarmland(sample.Cell) && !HasSoilState(sample)))
            return false;
        soil = ReadSoilSnapshot(sample);
        return true;
    }

    /// <summary>成长或施肥完成后提交水肥快照。</summary>
    public static void CommitSoil(TileData_Farmland soil)
    {
        if (!ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(
                new Vector2(soil.position.x + 0.5f, soil.position.y + 0.5f), out var sample) ||
            !IsOpen(sample) || (!IsFarmland(sample.Cell) && !HasSoilState(sample)))
            return;
        soil.NormalizeValues();
        if (Read(sample.Terrain, sample.LocalCell, SourceLayer) <= 0f)
            sample.Terrain.SetEnvironmentValue(SourceLayer, sample.LocalCell.x, sample.LocalCell.y,
                sample.Cell.GroundTileId);
        sample.Terrain.SetEnvironmentValue(WaterLayer, sample.LocalCell.x, sample.LocalCell.y, soil.waterValue);
        sample.Terrain.SetEnvironmentValue(FertilityLayer, sample.LocalCell.x, sample.LocalCell.y, soil.Fertility);
        SyncSoilEnvironment(sample.Terrain, sample.LocalCell.x, sample.LocalCell.y, soil.waterValue, soil.Fertility);
        SaveDataMgr.Instance.RecordAgricultureCell(sample);
    }

    #endregion
}
