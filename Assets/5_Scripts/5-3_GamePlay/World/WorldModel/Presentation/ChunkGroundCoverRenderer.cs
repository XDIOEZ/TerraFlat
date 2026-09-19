using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.Tilemaps;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>
/// 草层中的花朵等地表植被的批量表现。一个区块共用 Tilemap，按物品 groundCover 声明选择生成点，
/// 每格显示首个未采集的生成点；采集差量是唯一状态，解绑只清理表现，不把区块卸载当成采集。
/// </summary>
public sealed class ChunkGroundCoverRenderer : MonoBehaviour, IChunkViewRenderer
{
    #region 表现缓存

    [SerializeField, Tooltip("不挂碰撞体的地表植被 Tilemap。")]
    private Tilemap tilemap;
    private readonly Dictionary<string, Tile> tiles = new(StringComparer.Ordinal); // 每种植被一份共享 Tile。
    private readonly Dictionary<int, Vector3Int> placementCells = new(); // GUID 到表现格的索引。
    private readonly HashSet<Vector3Int> coverCells = new(); // 跳过没有植被的格子变化通知。
    private readonly HashSet<Vector3Int> visibleCells = new(); // 仅绘制顺序缓存，不保存采集状态。
    private ChunkRuntime boundChunk; // 当前绑定的权威区块。
    private ChunkMgr manager; // 绑定时获取并在解绑时解除观察。

    #endregion

    #region 绑定与解绑

    /// <summary>读取已有生态数据和删除差量，不为植被创建 Item。</summary>
    public void Bind(ChunkRuntime chunk)
    {
        if (chunk?.Terrain == null || tilemap == null)
            throw new InvalidOperationException("地表植被图层缺少区块数据或 Tilemap 配置。");
        if (ReferenceEquals(boundChunk, chunk)) return;
        Unbind();
        ApplyGroundCoverSorting();
        boundChunk = chunk;
        manager = ChunkMgr.ExistingInstance;
        if (manager == null || GameRes.ExistingInstance == null)
            throw new InvalidOperationException("地表植被图层绑定前必须完成世界和物品目录初始化。");
        manager.NaturalItemRemoved += HandleRemoved;
        boundChunk.Terrain.Changed += HandleTerrainChanged;

        IReadOnlyList<NaturalItemPlacement> placements = chunk.Ecology?.Placements;
        if (placements == null) return;
        for (int i = 0; i < placements.Count; i++)
        {
            NaturalItemPlacement placement = placements[i];
            if (!GameRes.ExistingInstance.TryGetItemDefinition(placement.ItemId, out RuntimeItemDefinition definition) ||
                !definition.IsGroundCover)
                continue;
            Vector3Int cell = new(placement.LocalX, placement.LocalY, 0);
            placementCells.Add(placement.Guid, cell);
            coverCells.Add(cell);
            if (visibleCells.Contains(cell) || manager.IsNaturalItemRemoved(chunk.Address, placement.Guid) ||
                !GroundCoverSystem.CanShowAt(chunk, cell.x, cell.y))
                continue;
            Render(new GroundCoverTarget(chunk, placement, definition));
            visibleCells.Add(cell);
        }
    }

    /// <summary>解除订阅并销毁自有 Tile；不销毁目录持有的 Sprite，也不改世界状态。</summary>
    public void Unbind()
    {
        if (manager != null) manager.NaturalItemRemoved -= HandleRemoved;
        if (boundChunk?.Terrain != null) boundChunk.Terrain.Changed -= HandleTerrainChanged;
        if (tilemap != null) tilemap.ClearAllTiles();
        foreach (Tile tile in tiles.Values)
        {
            if (Application.isPlaying) Destroy(tile);
            else DestroyImmediate(tile);
        }
        tiles.Clear();
        placementCells.Clear();
        coverCells.Clear();
        visibleCells.Clear();
        boundChunk = null;
        manager = null;
    }

    /// <summary>场景销毁时也清理外部事件订阅。</summary>
    private void OnDestroy() => Unbind();

    #endregion

    #region 增量绘制

    /// <summary>草与 BRG 地表共用 Default 排序域；花层明确在草之后，异步绑定次序不能改变遮挡关系。</summary>
    private void ApplyGroundCoverSorting()
    {
        TilemapRenderer renderer = tilemap.GetComponent<TilemapRenderer>();
        if (renderer == null) throw new MissingComponentException("花层缺少 TilemapRenderer。");
        renderer.sortingLayerName = "Default";
        renderer.sortingOrder = 1;
    }

    /// <summary>仅刷新本区块被采集生成点所在的格子。</summary>
    private void HandleRemoved(RuntimeWorldAddress address, int guid)
    {
        if (boundChunk != null && boundChunk.Address.Equals(address) && placementCells.TryGetValue(guid, out Vector3Int cell))
            RefreshCell(cell);
    }

    /// <summary>地形变化后重新判断植被可见性，草本身被吃掉不会消费花朵。</summary>
    private void HandleTerrainChanged(ChunkTerrainChanged changed)
    {
        if (changed.Kind == TerrainChangeKind.Cell || changed.Kind == TerrainChangeKind.TileStack ||
            changed.Kind == TerrainChangeKind.Environment)
            RefreshCell(new Vector3Int(changed.LocalCell.X, changed.LocalCell.Y, 0));
    }

    /// <summary>用与工具相同的查询选取此格剩余植被，全部采完后清空图像。</summary>
    private void RefreshCell(Vector3Int cell)
    {
        if (!coverCells.Contains(cell)) return;
        if (GroundCoverSystem.TryFindAt(boundChunk, cell.x, cell.y, manager, out GroundCoverTarget target))
            Render(target);
        else
            tilemap.SetTile(cell, null);
    }

    /// <summary>复用目录 Sprite 和草层材质，只写 Tile 与确定性局部变换。</summary>
    private void Render(GroundCoverTarget target)
    {
        RuntimeItemDefinition definition = target.Definition;
        if (!tiles.TryGetValue(definition.Id, out Tile tile))
        {
            tile = ScriptableObject.CreateInstance<Tile>();
            tile.name = "GroundCover_" + definition.Id;
            tile.sprite = definition.Sprite;
            tile.colliderType = Tile.ColliderType.None;
            tile.flags = TileFlags.None;
            tile.color = definition.Visual?.Color ?? Color.white;
            tiles.Add(definition.Id, tile);
        }
        NaturalItemPlacement placement = target.Placement;
        Vector3Int cell = new(placement.LocalX, placement.LocalY, 0);
        Vector3 offset = new(placement.OffsetX, placement.OffsetY, 0f);
        offset += definition.Visual?.RendererLocalPosition ?? Vector3.zero;
        Vector3 scale = definition.Visual?.RendererLocalScale ?? Vector3.one;
        if (definition.Visual?.FlipX == true) scale.x = -scale.x;
        if (definition.Visual?.FlipY == true) scale.y = -scale.y;
        tilemap.SetTile(cell, tile);
        tilemap.SetTransformMatrix(cell, Matrix4x4.TRS(offset,
            Quaternion.Euler(definition.Visual?.RendererLocalEulerAngles ?? Vector3.zero), scale));
    }

    #endregion
}
