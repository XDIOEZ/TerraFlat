using Force.DeepCloner;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using System;
using UltEvents;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.Tilemaps;

public partial class Map
{
    #region 地图同步与分帧表现

    // 地图协调加载进度，逐行投影与缓冲由专用表现器维护。
    public void LoadTileData_To_TileMap_Sync()
    {
        tilemapVisualReady = false;
        TilemapPresentationChanged?.Invoke(this);
        tilemapPresenter.BeginBatch(this);
        if (Data == null || Data.CountNonEmptyCells() == 0)
        {
            Debug.LogWarning("TileData is empty. Nothing to load.");
            tilemapPresenter.CompleteBatch(this);
            FinalizeTilemapLoad();
            return;
        }

        for (int localY = 0; localY < Data.Height; localY++)
        {
            if (!tilemapPresenter.TryRenderRow(this, localY, out string failureReason))
            {
                FailGeneration(activeGenerationContext, failureReason, null);
                activeGenerationContext = null;
                return;
            }
        }

        tilemapPresenter.CompleteBatch(this);
        FinalizeTilemapLoad();
    }

    public void LoadTileData_To_TileMap_Ansync()
    {
        tilemapVisualReady = false;
        TilemapPresentationChanged?.Invoke(this);
        // 如果已有协程在运行，先停止它
        if (loadTileMapCoroutine != null)
        {
            StopCoroutine(loadTileMapCoroutine);
        }

        // 启动新的协程
        loadTileMapCoroutine = StartCoroutine(LoadTileData_To_TileMapCoroutine());
    }

    private IEnumerator LoadTileData_To_TileMapCoroutine()
    {
        tilemapPresenter.BeginBatch(this);
        if (Data == null || Data.CountNonEmptyCells() == 0)
        {
            Debug.LogWarning("TileData is empty. Nothing to load.");
            if (WorldNavigationManager.Instance?.EnableDebugLogs == true)
                Debug.LogWarning($"[WorldNav][Map] TileData为空，直接Finalize | Map={name} chunk={chunk?.name ?? "null"}");
            tilemapPresenter.CompleteBatch(this);
            loadTileMapCoroutine = null;
            FinalizeTilemapLoad();
            yield break;
        }

        int batchSize = ScaledTilemapLoadBatchSize;
        int processedCount = 0;
        int processedThisFrame = 0;
        double frameBudgetMilliseconds = TilemapLoadFrameBudgetMilliseconds;
        double ticksPerMillisecond = System.Diagnostics.Stopwatch.Frequency / 1000d;
        long frameStartTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();

        for (int localY = 0; localY < Data.Height; localY++)
        {
            int previousTileCount = tilemapPresenter.TileCount;
            if (!tilemapPresenter.TryRenderRow(this, localY, out string failureReason))
            {
                tilemapPresenter.CompleteBatch(this);
                loadTileMapCoroutine = null;
                FailGeneration(activeGenerationContext, failureReason, null);
                activeGenerationContext = null;
                yield break;
            }
            int renderedInRow = tilemapPresenter.TileCount - previousTileCount;
            processedCount += renderedInRow;
            processedThisFrame += Data.Width;

            double elapsedMilliseconds =
                (System.Diagnostics.Stopwatch.GetTimestamp() - frameStartTimestamp) / ticksPerMillisecond;
            if (processedThisFrame >= batchSize || elapsedMilliseconds >= frameBudgetMilliseconds)
            {
                processedThisFrame = 0;
                yield return null;
                frameStartTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            }
        }

        tilemapPresenter.CompleteBatch(this);
        yield return null;
        if (WorldNavigationManager.Instance?.EnableDebugLogs == true)
        {
            Debug.Log($"✅ 完成加载 {processedCount} 个Tile到Tilemap");
            Debug.Log($"[WorldNav][Map] Tilemap加载完成 | {processedCount} 个Tile | Ready={WorldNavigationManager.Instance?.IsNavigationReady} | Map={name}");
        }
        loadTileMapCoroutine = null;
        FinalizeTilemapLoad();
    }

    #endregion
}
