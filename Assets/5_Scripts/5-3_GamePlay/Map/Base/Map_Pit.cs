using System.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;

public class Map_Pit : Map
{
    public override void Load()
    {
        chunk = GetComponentInParent<Chunk>();
        chunk.Map = this;
        chunk.ResetLifecycleState();
        Data.TileLoaded = true;
        LoadTileData_To_TileMap_Ansync();
    }

    protected override int TilemapLoadBatchSize => 500;

    public new void LoadTileData_To_TileMap_Ansync()
    {
        if (loadTileMapCoroutine != null)
            StopCoroutine(loadTileMapCoroutine);

        loadTileMapCoroutine = StartCoroutine(LoadTileData_To_TileMapCoroutine());
    }

    private IEnumerator LoadTileData_To_TileMapCoroutine()
    {
        if (Data == null || Data.CountNonEmptyCells() == 0)
        {
            Debug.LogWarning("TileData is empty. Nothing to load.");
            Debug.LogWarning($"[WorldNav][Map_Pit] TileData为空，直接Finalize | Map={name} chunk={chunk?.name ?? "null"}");
            loadTileMapCoroutine = null;
            FinalizeTilemapLoad();
            yield break;
        }

        const int batchSize = 500;
        int processedCount = 0;

        foreach (var (worldPos, tileDataList) in Data.EnumerateNonEmptyTiles())
        {
            TileData topTile = tileDataList[^1];
            TileBase tile = GameRes.Instance.GetTileBase(topTile.ID);
            if (tile == null)
            {
                Debug.LogError($"无法加载 Tile: {topTile.ID}");
                continue;
            }

            tileMap.SetTile(new Vector3Int(worldPos.x, worldPos.y, 0), tile);
            if (++processedCount % batchSize == 0)
                yield return null;
        }

        yield return null;
        Debug.Log($"✅ 完成加载 {Data.CountNonEmptyCells()} 个Tile到Tilemap");
        Debug.Log($"[WorldNav][Map_Pit] Tilemap加载完成 | {processedCount} 个Tile | Ready={WorldNavigationManager.Instance?.IsNavigationReady} | Map={name}");
        loadTileMapCoroutine = null;
        FinalizeTilemapLoad();
    }
}
