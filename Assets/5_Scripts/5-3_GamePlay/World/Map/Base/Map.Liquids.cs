using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.Tilemaps;

public partial class Map
{
    #region 旧地图生成适配
    // 仅供 Legacy Map 生成与预览；正式世界、抽水和差量存档始终走 ChunkMgr 权威 Terrain。
    private ChunkTerrainData legacyLiquidTerrain;
    private readonly List<Tile> legacyLiquidTiles = new();
    private GameObject legacyLiquidVisual;
    public ChunkTerrainData LegacyLiquidTerrain => legacyLiquidTerrain;

    /// <summary>重建旧地图的独立液体格，不向 Ground 栈写入 Water Tile。</summary>
    public void ResetLegacyLiquids()
    {
        DisposeLegacyLiquids();
        using var buffer = new ChunkTerrainBuffer(Data.Width, Data.Height, GameRes.ExistingInstance?.LiquidTypes);
        legacyLiquidTerrain = buffer.Seal();
    }
    public void SetGeneratedLiquid(Vector2Int worldCell, string liquidId, float depth)
    {
        if (legacyLiquidTerrain == null || legacyLiquidTerrain.Width != Data.Width || legacyLiquidTerrain.Height != Data.Height)
            ResetLegacyLiquids();
        Vector2Int local = worldCell - Data.position;
        legacyLiquidTerrain.SetLiquid(local.x, local.y, legacyLiquidTerrain.LiquidTypes.GetIndex(liquidId), depth);
    }
    public float GetGeneratedLiquidDepth(Vector2Int worldCell)
    {
        Vector2Int local = worldCell - Data.position;
        return legacyLiquidTerrain != null && (uint)local.x < (uint)Data.Width && (uint)local.y < (uint)Data.Height
            ? legacyLiquidTerrain.GetLiquidDepth(local.x, local.y) : 0f;
    }
    public string GetGeneratedLiquidId(Vector2Int worldCell)
    {
        if (GetGeneratedLiquidDepth(worldCell) <= 0f) return null;
        Vector2Int local = worldCell - Data.position;
        return legacyLiquidTerrain.GetLiquidId(local.x, local.y);
    }

    /// <summary>旧调试 Tilemap 也分开画底和液体；BRG 正式表现不经过此适配器。</summary>
    private void RebuildLegacyLiquidVisuals()
    {
        ClearLegacyLiquidVisuals();
        if (legacyLiquidTerrain == null || tileMap == null || GameRes.ExistingInstance == null) return;
        legacyLiquidVisual = new GameObject("Liquid");
        legacyLiquidVisual.transform.SetParent(tileMap.transform, false);
        var layers = new Dictionary<string, Tilemap>();
        var tiles = new Dictionary<string, Tile>();
        TilemapRenderer ground = tileMap.GetComponent<TilemapRenderer>();
        for (int y = 0; y < Data.Height; y++)
        for (int x = 0; x < Data.Width; x++)
        {
            if (legacyLiquidTerrain.GetLiquidDepth(x, y) <= 0f) continue;
            string id = legacyLiquidTerrain.GetLiquidId(x, y);
            if (!layers.TryGetValue(id, out Tilemap layer))
            {
                if (!GameRes.ExistingInstance.TryGetLiquidDefinition(id, out var definition) || definition.WorldWater?.Sprite == null) continue;
                var child = new GameObject(id, typeof(Tilemap), typeof(TilemapRenderer));
                child.transform.SetParent(legacyLiquidVisual.transform, false);
                layer = child.GetComponent<Tilemap>();
                TilemapRenderer renderer = child.GetComponent<TilemapRenderer>();
                renderer.sharedMaterial = definition.WorldWater.Material;
                if (ground != null) { renderer.sortingLayerID = ground.sortingLayerID; renderer.sortingOrder = ground.sortingOrder + 1; }
                var tile = ScriptableObject.CreateInstance<Tile>();
                tile.sprite = definition.WorldWater.Sprite;
                tile.colliderType = Tile.ColliderType.None;
                legacyLiquidTiles.Add(tile); tiles.Add(id, tile); layers.Add(id, layer);
            }
            layer.SetTile(new Vector3Int(x, y, 0), tiles[id]);
        }
    }
    private void ClearLegacyLiquidVisuals()
    {
        if (legacyLiquidVisual != null) Destroy(legacyLiquidVisual);
        foreach (Tile tile in legacyLiquidTiles) if (tile != null) Destroy(tile);
        legacyLiquidTiles.Clear(); legacyLiquidVisual = null;
    }
    private void DisposeLegacyLiquids()
    {
        ClearLegacyLiquidVisuals();
        legacyLiquidTerrain?.Dispose(); legacyLiquidTerrain = null;
    }
    #endregion
}
