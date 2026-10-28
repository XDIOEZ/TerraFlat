using System;
using UnityEngine;
using UnityEngine.Tilemaps;

// 逐行表现器独立维护地面与阻挡缓冲，地图组件只协调加载和生成状态。
internal sealed class MapTilemapRowPresenter
{
    #region 地图表现批次
    private TileBase[] groundRow;
    private TileBase[] blockingRow;
    private BlockingTilemapLayer blockingLayer;

    public int BatchCount { get; private set; }
    public int TileCount { get; private set; }

    public void BeginBatch(Map map)
    {
        BatchCount = TileCount = 0;
        map.tileMap.ClearAllTiles();
        blockingLayer = BlockingTilemapLayer.BeginBatch(map);
        int width = Mathf.Max(1, map.Data?.Width ?? 0);
        if (groundRow == null || groundRow.Length != width)
            groundRow = new TileBase[width];
        if (blockingRow == null || blockingRow.Length != width)
            blockingRow = new TileBase[width];
    }

    public bool TryRenderRow(Map map, int localY, out string failureReason)
    {
        failureReason = null;
        Array.Clear(groundRow, 0, groundRow.Length);
        Array.Clear(blockingRow, 0, blockingRow.Length);
        bool hasBlockingTile = false;
        Data_TileMap data = map.Data;
        GameRes resources = GameRes.Instance;

        for (int localX = 0; localX < data.Width; localX++)
        {
            Vector2Int worldPosition = data.position + new Vector2Int(localX, localY);
            if (!data.TryGetStackView(worldPosition, out TileStackView stack) || stack.Count == 0)
                continue;

            TileData groundData = BlockingTilemapLayer.ResolveGroundTile(stack);
            if (groundData != null)
            {
                TileBase groundTile = resources?.GetTileBase(groundData.ID);
                if (groundTile == null)
                {
                    failureReason = $"Tilemap 最终化失败：找不到地面 TileBase '{groundData.ID}'，位置 {worldPosition}。";
                    return false;
                }
                groundRow[localX] = groundTile;
                TileCount++;
            }

            TileData topData = stack[^1];
            if (!BlockingTilemapLayer.IsBlockingTile(topData))
                continue;

            TileBase blockingTile = resources?.GetTileBase(topData.ID);
            if (blockingTile == null)
            {
                failureReason = $"Tilemap 最终化失败：找不到阻挡 TileBase '{topData.ID}'，位置 {worldPosition}。";
                return false;
            }
            blockingRow[localX] = blockingTile;
            hasBlockingTile = true;
        }

        map.tileMap.SetTilesBlock(
            new BoundsInt(data.position.x, data.position.y + localY, 0, data.Width, 1, 1), groundRow);
        BatchCount++;
        if (!hasBlockingTile)
            return true;

        blockingLayer ??= BlockingTilemapLayer.EnsureBatchLayer(map);
        if (blockingLayer == null)
        {
            failureReason = "Tilemap 最终化失败：无法创建顶部阻挡层。";
            return false;
        }
        blockingLayer.WriteBatchRow(data.position, localY, blockingRow);
        BatchCount++;
        return true;
    }

    public void CompleteBatch(Map map)
    {
        map.tileMap?.RefreshAllTiles();
        blockingLayer?.BlockingTilemap?.RefreshAllTiles();
        TilemapCollider2D groundCollider = map.tileMap != null
            ? map.tileMap.GetComponent<TilemapCollider2D>()
            : null;
        if (groundCollider != null && groundCollider.hasTilemapChanges)
            groundCollider.ProcessTilemapChanges();
        blockingLayer?.CompleteBatch();
    }
    #endregion
}
