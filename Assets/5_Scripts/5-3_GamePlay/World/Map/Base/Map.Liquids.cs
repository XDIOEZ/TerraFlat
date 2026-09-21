using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.Tilemaps;

public partial class Map
{
    #region 生成期液体层
    // MapCore 生成链的临时液体数据；Ground 与 Liquid 从这里开始就保持独立。
    // 正式世界、抽水和差量存档始终走 ChunkMgr 权威 Terrain。
    private ChunkTerrainData generatedLiquidTerrain;
    private readonly List<Tile> generatedLiquidTiles = new();
    private GameObject generatedLiquidVisual;
    public ChunkTerrainData GeneratedLiquidTerrain => generatedLiquidTerrain;

    /// <summary>重建 MapCore 生成期独立液体格，不向 Ground 栈写入任何液体地块。</summary>
    public void ResetGeneratedLiquids()
    {
        DisposeGeneratedLiquids();
        using var buffer = new ChunkTerrainBuffer(Data.Width, Data.Height, GameRes.ExistingInstance?.LiquidTypes);
        generatedLiquidTerrain = buffer.Seal();
    }
    public void SetGeneratedLiquid(Vector2Int worldCell, string liquidId, float depth)
    {
        if (generatedLiquidTerrain == null || generatedLiquidTerrain.Width != Data.Width || generatedLiquidTerrain.Height != Data.Height)
            ResetGeneratedLiquids();
        Vector2Int local = worldCell - Data.position;
        generatedLiquidTerrain.SetLiquid(local.x, local.y, generatedLiquidTerrain.LiquidTypes.GetIndex(liquidId), depth);
    }
    public float GetGeneratedLiquidDepth(Vector2Int worldCell)
    {
        Vector2Int local = worldCell - Data.position;
        return generatedLiquidTerrain != null && (uint)local.x < (uint)Data.Width && (uint)local.y < (uint)Data.Height
            ? generatedLiquidTerrain.GetLiquidDepth(local.x, local.y) : 0f;
    }
    public string GetGeneratedLiquidId(Vector2Int worldCell)
    {
        if (GetGeneratedLiquidDepth(worldCell) <= 0f) return null;
        Vector2Int local = worldCell - Data.position;
        return generatedLiquidTerrain.GetLiquidId(local.x, local.y);
    }

    /// <summary>MapCore 调试 Tilemap 分开画 Ground 与 Liquid；BRG 正式表现不经过这里。</summary>
    private void RebuildGeneratedLiquidVisuals()
    {
        ClearGeneratedLiquidVisuals();
        if (generatedLiquidTerrain == null || tileMap == null || GameRes.ExistingInstance == null) return;
        generatedLiquidVisual = new GameObject("Liquid");
        generatedLiquidVisual.transform.SetParent(tileMap.transform, false);
        var layers = new Dictionary<string, Tilemap>();
        var tiles = new Dictionary<string, Tile>();
        TilemapRenderer ground = tileMap.GetComponent<TilemapRenderer>();
        for (int y = 0; y < Data.Height; y++)
        for (int x = 0; x < Data.Width; x++)
        {
            if (generatedLiquidTerrain.GetLiquidDepth(x, y) <= 0f) continue;
            string id = generatedLiquidTerrain.GetLiquidId(x, y);
            if (!layers.TryGetValue(id, out Tilemap layer))
            {
                if (!GameRes.ExistingInstance.TryGetLiquidDefinition(id, out var definition) || definition.WorldWater?.Sprite == null) continue;
                var child = new GameObject(id, typeof(Tilemap), typeof(TilemapRenderer));
                child.transform.SetParent(generatedLiquidVisual.transform, false);
                layer = child.GetComponent<Tilemap>();
                TilemapRenderer renderer = child.GetComponent<TilemapRenderer>();
                renderer.sharedMaterial = definition.WorldWater.Material;
                if (ground != null) { renderer.sortingLayerID = ground.sortingLayerID; renderer.sortingOrder = ground.sortingOrder + 1; }
                var tile = ScriptableObject.CreateInstance<Tile>();
                tile.sprite = definition.WorldWater.Sprite;
                tile.colliderType = Tile.ColliderType.None;
                generatedLiquidTiles.Add(tile); tiles.Add(id, tile); layers.Add(id, layer);
            }
            layer.SetTile(new Vector3Int(x, y, 0), tiles[id]);
        }
    }
    private void ClearGeneratedLiquidVisuals()
    {
        if (generatedLiquidVisual != null) Destroy(generatedLiquidVisual);
        foreach (Tile tile in generatedLiquidTiles) if (tile != null) Destroy(tile);
        generatedLiquidTiles.Clear(); generatedLiquidVisual = null;
    }
    private void DisposeGeneratedLiquids()
    {
        ClearGeneratedLiquidVisuals();
        generatedLiquidTerrain?.Dispose(); generatedLiquidTerrain = null;
    }
    #endregion
}
