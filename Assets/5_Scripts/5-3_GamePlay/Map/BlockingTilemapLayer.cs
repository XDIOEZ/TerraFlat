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

        BlockingTilemapLayer layer = map.GetComponent<BlockingTilemapLayer>();
        bool hasBlockingTiles = false;
        foreach (var (_, tiles) in map.Data.EnumerateNonEmptyTiles())
        {
            if (tiles != null && tiles.Count > 0 && IsBlockingTile(tiles[^1]))
            {
                hasBlockingTiles = true;
                break;
            }
        }

        if (!hasBlockingTiles)
        {
            layer?.Clear();
            return;
        }

        if (layer == null)
            layer = map.gameObject.AddComponent<BlockingTilemapLayer>();
        layer.Rebuild(map);
    }

    public static void RefreshMapCell(Map map, Vector2Int worldPosition)
    {
        if (map?.Data == null)
            return;

        TileData topTile = map.Data.GetTileDataAt(worldPosition);
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

    private void Rebuild(Map map)
    {
        EnsureTilemap(map);
        if (blockingTilemap == null)
            return;

        blockingTilemap.ClearAllTiles();
        foreach (var (worldPosition, tiles) in map.Data.EnumerateNonEmptyTiles())
        {
            if (tiles == null || tiles.Count == 0)
                continue;

            TileData topTile = tiles[^1];
            if (IsBlockingTile(topTile))
                SetCell(map, worldPosition, topTile);
        }
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
    }

    #endregion

    #region 层级创建

    private void EnsureTilemap(Map map)
    {
        if (blockingTilemap != null || map == null)
            return;

        Transform existing = transform.Find(LayerObjectName);
        GameObject layerObject;
        if (existing != null)
        {
            layerObject = existing.gameObject;
        }
        else
        {
            layerObject = new GameObject(LayerObjectName);
            layerObject.layer = map.tileMap != null ? map.tileMap.gameObject.layer : gameObject.layer;
            layerObject.transform.SetParent(transform, false);
        }

        blockingTilemap = layerObject.GetComponent<Tilemap>();
        if (blockingTilemap == null)
            blockingTilemap = layerObject.AddComponent<Tilemap>();

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

        if (layerObject.GetComponent<TilemapCollider2D>() == null)
            layerObject.AddComponent<TilemapCollider2D>();
    }

    #endregion
}
