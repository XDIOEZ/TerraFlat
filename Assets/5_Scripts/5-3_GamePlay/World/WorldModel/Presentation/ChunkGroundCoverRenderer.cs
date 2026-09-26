using System;
using System.Collections;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>
/// 草层中的花朵等地表植被以全局 BatchRendererGroup 批量表现，不创建 Tile 或常驻 Item。
/// 每区块最多提交每格一株；采集差量仍是唯一权威状态，解绑只清理表现实例。
/// </summary>
public sealed class ChunkGroundCoverRenderer : MonoBehaviour, IIncrementalChunkViewRenderer
{
    #region 表现缓存

    private const int PlacementsPerStep = 128;

    [SerializeField] private Material groundCoverMaterial; // 与草共用 Default/0 排序域的地表材质。
    private readonly Dictionary<int, Vector3Int> placementCells = new(); // GUID 到表现格的索引。
    private readonly HashSet<Vector3Int> coverCells = new(); // 跳过没有植被的格子变化通知。
    private readonly HashSet<Vector3Int> visibleCells = new(); // 仅跟踪当前 BRG 实例，不保存采集状态。
    private ChunkRuntime boundChunk; // 当前绑定的权威区块。
    private ChunkTilemapRenderer ownerRenderer; // 共享区块边界裁剪与 BRG 生命周期。
    private ChunkMgr manager; // 绑定时获取并在解绑时解除观察。

    #endregion

    #region 绑定与解绑

    /// <summary>同步入口复用同一批生成点提交逻辑。</summary>
    public void Bind(ChunkRuntime chunk)
    {
        if (!PrepareBinding(chunk))
            return;
        try
        {
            IReadOnlyList<NaturalItemPlacement> placements = chunk.Ecology?.Placements;
            if (placements != null)
                RenderPlacements(placements, 0, placements.Count);
        }
        catch
        {
            Unbind();
            throw;
        }
    }

    /// <summary>每次最多检查 128 个生态放置点，避免密集花层阻塞视距加载。</summary>
    public IEnumerator BindIncremental(ChunkRuntime chunk)
    {
        if (!PrepareBinding(chunk))
            yield break;

        bool completed = false;
        try
        {
            IReadOnlyList<NaturalItemPlacement> placements = chunk.Ecology?.Placements;
            if (placements != null)
            {
                for (int start = 0; start < placements.Count; start += PlacementsPerStep)
                {
                    RenderPlacements(placements, start,
                        Mathf.Min(PlacementsPerStep, placements.Count - start));
                    if (start + PlacementsPerStep < placements.Count)
                        yield return null;
                }
            }
            completed = true;
        }
        finally
        {
            if (!completed)
                Unbind();
        }
    }

    /// <summary>确认基础地形 Owner 与物品目录就绪后订阅权威变化。</summary>
    private bool PrepareBinding(ChunkRuntime chunk)
    {
        if (chunk?.Terrain == null)
            throw new InvalidOperationException("地表植被绑定前必须完成区块数据。");
        if (ReferenceEquals(boundChunk, chunk))
            return false;

        Unbind();
        if (groundCoverMaterial == null)
            throw new InvalidOperationException("ChunkGroundCoverRenderer 缺少 BRG 地表材质。");
        ownerRenderer = GetComponent<ChunkTilemapRenderer>();
        if (ownerRenderer == null || !ownerRenderer.IsBatchPresentationComplete)
            throw new InvalidOperationException("地表植被必须在基础地形 BRG Owner 注册后绑定。");
        manager = ChunkMgr.ExistingInstance;
        if (manager == null || GameRes.ExistingInstance == null)
            throw new InvalidOperationException("地表植被绑定前必须完成世界和物品目录初始化。");

        boundChunk = chunk;
        manager.NaturalItemRemoved += HandleRemoved;
        boundChunk.Terrain.Changed += HandleTerrainChanged;
        boundChunk.Terrain.LiquidBatchChanged += HandleLiquidBatchChanged;
        ownerRenderer.BatchPresentationRebuilt += HandleOwnerPresentationRebuilt;
        return true;
    }

    /// <summary>清除本图层实例和事件订阅，不修改区块生态或采集差量。</summary>
    public void Unbind()
    {
        if (manager != null)
            manager.NaturalItemRemoved -= HandleRemoved;
        if (boundChunk?.Terrain != null)
        {
            boundChunk.Terrain.Changed -= HandleTerrainChanged;
            boundChunk.Terrain.LiquidBatchChanged -= HandleLiquidBatchChanged;
        }
        ClearVisibleInstances();
        if (ownerRenderer != null)
            ownerRenderer.BatchPresentationRebuilt -= HandleOwnerPresentationRebuilt;

        placementCells.Clear();
        coverCells.Clear();
        visibleCells.Clear();
        boundChunk = null;
        ownerRenderer = null;
        manager = null;
    }

    /// <summary>场景销毁时解除所有外部事件订阅。</summary>
    private void OnDestroy() => Unbind();

    #endregion

    #region 增量刷新

    /// <summary>按范围读取放置点，只处理有效地表植被并抑制同格重复提交。</summary>
    private void RenderPlacements(IReadOnlyList<NaturalItemPlacement> placements, int start, int count)
    {
        GameRes resources = GameRes.ExistingInstance;
        for (int i = start; i < start + count; i++)
        {
            NaturalItemPlacement placement = placements[i];
            if (!resources.TryGetItemDefinition(placement.ItemId, out RuntimeItemDefinition definition) ||
                !definition.IsGroundCover)
                continue;

            Vector3Int cell = new(placement.LocalX, placement.LocalY, 0);
            placementCells[placement.Guid] = cell;
            coverCells.Add(cell);
            if (visibleCells.Contains(cell) || manager.IsNaturalItemRemoved(boundChunk.Address, placement.Guid) ||
                !GroundCoverSystem.CanShowAt(boundChunk, cell.x, cell.y))
                continue;

            Render(new GroundCoverTarget(boundChunk, placement, definition));
        }
    }

    /// <summary>仅刷新本区块被采集生成点所在的格子。</summary>
    private void HandleRemoved(RuntimeWorldAddress address, int guid)
    {
        if (boundChunk != null && boundChunk.Address.Equals(address) &&
            placementCells.TryGetValue(guid, out Vector3Int cell))
            RefreshCell(cell);
    }

    /// <summary>地块、支撑、环境或单格液体变化时重查该格植被可见性。</summary>
    private void HandleTerrainChanged(ChunkTerrainChanged changed)
    {
        if (changed.Kind == TerrainChangeKind.Cell || changed.Kind == TerrainChangeKind.TileStack ||
            changed.Kind == TerrainChangeKind.Environment || changed.Kind == TerrainChangeKind.Liquid)
            RefreshCell(new Vector3Int(changed.LocalCell.X, changed.LocalCell.Y, 0));
    }

    /// <summary>批量液体变化结束后再刷新受影响格，不逐格监听流动过程。</summary>
    private void HandleLiquidBatchChanged(ChunkLiquidBatchChanged changed)
    {
        foreach (int index in changed.CellIndices.Span)
            RefreshCell(new Vector3Int(index % changed.Terrain.Width, index / changed.Terrain.Width, 0));
    }

    /// <summary>基础 BRG Owner 修复后按权威生态和采集差量重提花层实例。</summary>
    private void HandleOwnerPresentationRebuilt()
    {
        ClearVisibleInstances();
        placementCells.Clear();
        coverCells.Clear();
        IReadOnlyList<NaturalItemPlacement> placements = boundChunk?.Ecology?.Placements;
        if (placements == null)
            return;

        RenderPlacements(placements, 0, placements.Count);
    }

    /// <summary>用与工具相同的查询选取此格剩余植被，全部采完后清除实例。</summary>
    private void RefreshCell(Vector3Int cell)
    {
        if (!coverCells.Contains(cell))
            return;
        if (GroundCoverSystem.TryFindAt(boundChunk, cell.x, cell.y, manager, out GroundCoverTarget target))
            Render(target);
        else
            ClearCell(cell);
    }

    #endregion

    #region BRG 单格提交

    /// <summary>提交目录 Sprite、地表材质、物品色调和确定性局部矩阵。</summary>
    private void Render(GroundCoverTarget target)
    {
        RuntimeItemDefinition definition = target.Definition;
        NaturalItemPlacement placement = target.Placement;
        Vector3Int cell = new(placement.LocalX, placement.LocalY, 0);
        Vector3 offset = new(placement.OffsetX, placement.OffsetY, 0f);
        offset += definition.Visual?.RendererLocalPosition ?? Vector3.zero;
        Vector3 scale = definition.Visual?.RendererLocalScale ?? Vector3.one;
        if (definition.Visual?.FlipX == true) scale.x = -scale.x;
        if (definition.Visual?.FlipY == true) scale.y = -scale.y;

        Int2 origin = boundChunk.Address.ChunkOrigin;
        Matrix4x4 localToWorld = Matrix4x4.Translate(
                                     new Vector3(origin.X + cell.x + 0.5f, origin.Y + cell.y + 0.5f, 0f)) *
                                 Matrix4x4.TRS(offset,
                                     Quaternion.Euler(definition.Visual?.RendererLocalEulerAngles ?? Vector3.zero),
                                     scale);
        ownerRenderer.SetLayerVisual(ChunkBatchRendererGroupService.VisualLayer.GroundCover,
            cell.x, cell.y, definition.Sprite, groundCoverMaterial, localToWorld,
            definition.Visual?.Color ?? Color.white);
        visibleCells.Add(cell);
    }

    /// <summary>花朵采尽或地表被遮蔽时移除唯一的同格 BRG 实例。</summary>
    private void ClearCell(Vector3Int cell)
    {
        if (!visibleCells.Remove(cell))
            return;
        ownerRenderer.ClearLayerVisual(ChunkBatchRendererGroupService.VisualLayer.GroundCover, cell.x, cell.y);
    }

    /// <summary>释放当前花实例；解绑和 Owner 全量重建共用此批量清理入口。</summary>
    private void ClearVisibleInstances()
    {
        if (ownerRenderer != null)
            foreach (Vector3Int cell in visibleCells)
                ownerRenderer.ClearLayerVisual(
                    ChunkBatchRendererGroupService.VisualLayer.GroundCover, cell.x, cell.y);
        visibleCells.Clear();
    }

    #endregion
}
