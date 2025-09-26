using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class Map_Pit : Map
{

    public override void Load()
    {
        chunk = GetComponentInParent<Chunk>();
        chunk.Map = this;

        // TileData已生成完成，开始加载
        LoadTileData_To_TileMap_Ansync();
    }
    public new void LoadTileData_To_TileMap_Ansync()
    {
        // 如果已有协程在运行，先停止它
        if (loadTileMapCoroutine != null)
        {
            StopCoroutine(loadTileMapCoroutine);
        }

        // 启动新的协程
        loadTileMapCoroutine = StartCoroutine(LoadTileData_To_TileMapCoroutine());
    }

    /// <summary>
    /// 使用协程优化的加载Tile数据到Tilemap的方法
    /// </summary>
    private  IEnumerator LoadTileData_To_TileMapCoroutine()
    {
        if (Data.TileData == null || Data.TileData.Count == 0)
        {
            Debug.LogWarning("TileData is empty. Nothing to load.");
            loadTileMapCoroutine = null;
            yield break;
        }

        // 分批处理Tile数据，避免长时间阻塞主线程
        const int batchSize = 500;
        int processedCount = 0;

        foreach (var kvp in Data.TileData)
        {
            Vector2Int position2D = kvp.Key;
            List<TileData> tileDataList = kvp.Value;

            // 获取最顶层 TileData（倒数第一个）
            TileData topTile = tileDataList[^1];

            TileBase tile = GameRes.Instance.GetTileBase(topTile.Name_TileBase);
            if (tile == null)
            {
                Debug.LogError($"无法加载 Tile: {topTile.Name_TileBase}");
                continue;
            }

            Vector3Int position3D = new Vector3Int(position2D.x, position2D.y, 0);

            tileMap.SetTile(position3D, tile);

            processedCount++;

            // 每处理一批就等待一帧，让出控制权给其他任务
            if (processedCount % batchSize == 0)
            {
                yield return null;
            }
        }

        // 等待一帧确保所有Tile设置完成
        yield return null;

        Debug.Log($"✅ 完成加载 {Data.TileData.Count} 个Tile到Tilemap");

        // 清理协程引用
        loadTileMapCoroutine = null;
    }
}
