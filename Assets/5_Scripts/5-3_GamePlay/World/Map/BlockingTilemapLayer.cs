using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[DisallowMultipleComponent]
public sealed class BlockingTilemapLayer : MonoBehaviour
{
    public const string BlockingTileTag = "Blocking";

    private const string LayerObjectName = "建筑阻挡层";
    private const int SortingOrderOffset = 2;

    private Tilemap blockingTilemap;
    private TilemapCollider2D blockingCollider;

    public Tilemap BlockingTilemap => blockingTilemap;
    public TilemapCollider2D BlockingCollider => blockingCollider;

    #region 地图同步

    public static TileData ResolveGroundTile(IReadOnlyList<TileData> tiles)
    {
        if (tiles == null || tiles.Count == 0)
            return null;

        TileData topTile = tiles[tiles.Count - 1];
        return IsBlockingTile(topTile) && tiles.Count > 1
            ? tiles[tiles.Count - 2]
            : topTile;
    }

    public static bool IsBlockingTile(TileData tile)
    {
        return tile != null &&
               !tile.IsWalkable &&
               string.Equals(tile.TileTag, BlockingTileTag, System.StringComparison.Ordinal);
    }

    public static void SyncMap(Map map)
    {
        if (map?.Data == null)
            return;

        BlockingTilemapLayer layer = BeginBatch(map);
        int width = map.Data.Width;
        int height = map.Data.Height;
        var row = new TileBase[width];
        for (int y = 0; y < height; y++)
        {
            System.Array.Clear(row, 0, row.Length);
            bool hasBlockingTileInRow = false;
            for (int x = 0; x < width; x++)
            {
                TileData top = map.Data.GetTileFromTop(map.Data.position + new Vector2Int(x, y), 0);
                if (!IsBlockingTile(top))
                    continue;

                row[x] = GameRes.Instance?.GetTileBase(top.ID);
                hasBlockingTileInRow = row[x] != null || hasBlockingTileInRow;
            }

            if (!hasBlockingTileInRow)
                continue;

            layer ??= EnsureBatchLayer(map);
            layer?.WriteBatchRow(map.Data.position, y, row);
        }

        layer?.CompleteBatch();
    }

    public static BlockingTilemapLayer BeginBatch(Map map)
    {
        if (map == null)
            return null;

        BlockingTilemapLayer layer = map.GetComponent<BlockingTilemapLayer>();
        if (layer == null)
            return null;

        layer.EnsureTilemap(map);
        layer.blockingTilemap?.ClearAllTiles();
        return layer;
    }

    public static BlockingTilemapLayer EnsureBatchLayer(Map map)
    {
        if (map == null)
            return null;

        BlockingTilemapLayer layer = map.GetComponent<BlockingTilemapLayer>();
        if (layer == null)
            layer = map.gameObject.AddComponent<BlockingTilemapLayer>();
        layer.EnsureTilemap(map);
        return layer;
    }

    public static void ClearMap(Map map)
    {
        map?.GetComponent<BlockingTilemapLayer>()?.Clear();
    }

    public static void RefreshMapCell(Map map, Vector2Int worldPosition)
    {
        if (map?.Data == null)
            return;

        TileData topTile = map.Data.GetTopTile(worldPosition);
        BlockingTilemapLayer layer = map.GetComponent<BlockingTilemapLayer>();
        if (IsBlockingTile(topTile))
        {
            if (layer == null)
                layer = map.gameObject.AddComponent<BlockingTilemapLayer>();
            layer.SetCell(map, worldPosition, topTile);
        }
        else
        {
            layer?.SetCell(map, worldPosition, null);
        }
    }

    internal void WriteBatchRow(Vector2Int mapOrigin, int localY, TileBase[] row)
    {
        if (blockingTilemap == null || row == null || row.Length == 0)
            return;

        blockingTilemap.SetTilesBlock(
            new BoundsInt(mapOrigin.x, mapOrigin.y + localY, 0, row.Length, 1, 1),
            row);
    }

    internal void CompleteBatch()
    {
        ProcessColliderChanges();
    }

    private void SetCell(Map map, Vector2Int worldPosition, TileData tileData)
    {
        EnsureTilemap(map);
        if (blockingTilemap == null)
            return;

        TileBase tileBase = tileData != null ? GameRes.Instance?.GetTileBase(tileData.ID) : null;
        blockingTilemap.SetTile(new Vector3Int(worldPosition.x, worldPosition.y, 0), tileBase);
    }

    private void Clear()
    {
        blockingTilemap?.ClearAllTiles();
        ProcessColliderChanges();
    }

    public void ProcessColliderChanges()
    {
        if (blockingCollider != null && blockingCollider.hasTilemapChanges)
            blockingCollider.ProcessTilemapChanges();
    }

    #endregion

    #region 层级创建

    private void EnsureTilemap(Map map)
    {
        if (map == null)
            return;

        GameObject layerObject;
        if (blockingTilemap != null)
        {
            layerObject = blockingTilemap.gameObject;
        }
        else
        {
            Transform existing = transform.Find(LayerObjectName);
            if (existing != null)
            {
                layerObject = existing.gameObject;
            }
            else
            {
                layerObject = new GameObject(LayerObjectName);
                layerObject.transform.SetParent(transform, false);
            }

            blockingTilemap = layerObject.GetComponent<Tilemap>();
            if (blockingTilemap == null)
                blockingTilemap = layerObject.AddComponent<Tilemap>();
        }

        int colliderLayer = LayerMask.NameToLayer("Collider");
        layerObject.layer = colliderLayer >= 0
            ? colliderLayer
            : map.tileMap != null ? map.tileMap.gameObject.layer : gameObject.layer;

        TilemapRenderer blockingRenderer = layerObject.GetComponent<TilemapRenderer>();
        if (blockingRenderer == null)
            blockingRenderer = layerObject.AddComponent<TilemapRenderer>();

        TilemapRenderer groundRenderer = map.tileMap != null
            ? map.tileMap.GetComponent<TilemapRenderer>()
            : null;
        if (groundRenderer != null)
        {
            blockingRenderer.sortingLayerID = groundRenderer.sortingLayerID;
            blockingRenderer.sortingOrder = groundRenderer.sortingOrder + SortingOrderOffset;
            blockingRenderer.sharedMaterial = groundRenderer.sharedMaterial;
        }

        blockingCollider = layerObject.GetComponent<TilemapCollider2D>();
        if (blockingCollider == null)
            blockingCollider = layerObject.AddComponent<TilemapCollider2D>();

        TilemapDamageReceiver damageReceiver = layerObject.GetComponent<TilemapDamageReceiver>();
        if (damageReceiver == null)
            damageReceiver = layerObject.AddComponent<TilemapDamageReceiver>();
        damageReceiver.Bind(map, blockingTilemap, blockingCollider);
    }

    #endregion
}
