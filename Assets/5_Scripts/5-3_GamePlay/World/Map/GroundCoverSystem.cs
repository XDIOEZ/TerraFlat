using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>一个可采集的地表植被生成点；只引用世界数据与当前物品定义，不需要 Item 实例。</summary>
public readonly struct GroundCoverTarget
{
    /// <summary>保留本次选中的区块、稳定生成点和采集物定义。</summary>
    public GroundCoverTarget(ChunkRuntime chunk, NaturalItemPlacement placement, RuntimeItemDefinition definition)
    {
        Chunk = chunk;
        Placement = placement;
        Definition = definition;
    }

    public ChunkRuntime Chunk { get; } // 所属权威区块，用于拒绝过期目标。
    public NaturalItemPlacement Placement { get; } // 确定性生成点和 GUID。
    public RuntimeItemDefinition Definition { get; } // 采集后使用的当前物品定义。
    public Vector2Int WorldCell => new(Chunk.Address.ChunkOrigin.X + Placement.LocalX,
        Chunk.Address.ChunkOrigin.Y + Placement.LocalY);
    public Vector3 WorldPosition => new(WorldCell.x + 0.5f + Placement.OffsetX,
        WorldCell.y + 0.5f + Placement.OffsetY, 0f);
}

/// <summary>
/// 地表植被的坐标查询与采集服务。自然状态由确定性生态生成点和现有删除差量共同决定，
/// 图层不保存第二份采集状态；只有成功准备好掉落物后才删除生成点，一次采集获得一个物品。
/// groundCover 是物品定义声明，不按花朵或剪刀的具体 ID 写特判。
/// </summary>
public static class GroundCoverSystem
{
    #region 目标查询

    /// <summary>按准心所在规范化地格查找当前显示的植被，不进行物理查询或扫描其他区块。</summary>
    public static bool TryResolve(Vector2 worldPosition, out GroundCoverTarget target)
    {
        target = default;
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (manager == null || !manager.TryGetRuntimeTerrainTile(worldPosition, out RuntimeTerrainTileSample sample) ||
            !manager.TryGetChunkRuntime(sample.Address, out ChunkRuntime chunk))
            return false;

        return TryFindAt(chunk, sample.LocalCell.x, sample.LocalCell.y, manager, out target);
    }

    /// <summary>与图层一致地选择同格第一个尚未采集的生成点。</summary>
    public static bool TryFindAt(ChunkRuntime chunk, int x, int y, ChunkMgr manager, out GroundCoverTarget target)
    {
        target = default;
        IReadOnlyList<NaturalItemPlacement> placements = chunk?.Ecology?.Placements;
        GameRes resources = GameRes.ExistingInstance;
        if (placements == null || resources == null || manager == null || !CanShowAt(chunk, x, y))
            return false;

        for (int i = 0; i < placements.Count; i++)
        {
            NaturalItemPlacement placement = placements[i];
            if (placement.LocalX == x && placement.LocalY == y &&
                resources.TryGetItemDefinition(placement.ItemId, out RuntimeItemDefinition definition) &&
                definition.IsGroundCover && !manager.IsNaturalItemRemoved(chunk.Address, placement.Guid))
            {
                target = new GroundCoverTarget(chunk, placement, definition);
                return true;
            }
        }
        return false;
    }

    /// <summary>植被只在仍可见的自然地面显示，耕地、平台、水面和阻挡格不提供采集目标。</summary>
    public static bool CanShowAt(ChunkRuntime chunk, int x, int y)
    {
        ChunkTerrainData terrain = chunk?.Terrain;
        if (terrain == null || terrain.IsDisposed || (uint)x >= (uint)terrain.Width || (uint)y >= (uint)terrain.Height)
            return false;
        TerrainCell cell = terrain.GetCell(x, y);
        return (cell.Flags & (TerrainCellFlags.Water | TerrainCellFlags.Blocking | TerrainCellFlags.Occupied)) == 0 &&
            cell.BlockingTileId == 0 && !FarmlandSystem.IsFarmland(cell) &&
            TerrainSupportLayer.GetTileId(terrain, x, y) == 0;
    }

    #endregion

    #region 采集提交

    /// <summary>重新核实生成点，准备真实掉落物后一次性消费；失败不会让花朵消失或重复出货。</summary>
    public static bool TryHarvest(GroundCoverTarget target)
    {
        if (!GameNetwork.HasStateAuthority || target.Chunk == null ||
            !TryResolve((Vector2)target.WorldCell + Vector2.one * 0.5f, out GroundCoverTarget current) ||
            !ReferenceEquals(current.Chunk, target.Chunk) || current.Placement.Guid != target.Placement.Guid)
            return false;

        ChunkMgr manager = ChunkMgr.ExistingInstance;
        ItemMgr items = ItemMgr.Instance;
        if (items == null || !manager.TryGetRuntimeDropParent(current.WorldPosition, out ChunkNaturalItemRenderer parent))
            return false;

        Item product = null;
        try
        {
            product = items.InstantiateItem(current.Definition.Id, current.WorldPosition,
                Quaternion.identity, Vector3.one, parent.gameObject);
            if (product == null)
                throw new InvalidOperationException($"无法创建地表植被采集物：{current.Definition.Id}");
            product.Load();
            product.SetInHand(false);
            product.itemData.Stack.Amount = 1;
            product.DropInRange();
        }
        catch
        {
            if (product != null && !product.DestructionHandled)
                items.DespawnItem(product, saveData: false, detachFromChunk: false);
            throw;
        }

        if (manager.TryRemoveNaturalItem(current.Chunk.Address, current.Placement.Guid))
            return true;
        items.DespawnItem(product, saveData: false, detachFromChunk: false);
        return false;
    }

    #endregion
}
