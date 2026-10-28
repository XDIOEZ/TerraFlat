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
    #region 地图生成管线与取消

    private static Map activeProceduralGenerationMap;

    [NonSerialized] private bool ownsProceduralGenerationSlot;

    [NonSerialized] private MapGenerationContext activeGenerationContext;

    [NonSerialized] private string lastGenerationFailure;

    // 组件负责生成生命周期，阶段数据仍由 MapGenerationContext 持有。
    private void CancelPendingGeneratorWork()
    {
        if (mapGenerators == null)
            return;

        for (int i = 0; i < mapGenerators.Count; i++)
            mapGenerators[i]?.CancelPendingWork();
    }

    private IEnumerator WaitForProceduralGenerationSlot()
    {
        while (activeProceduralGenerationMap != null &&
               activeProceduralGenerationMap != this)
        {
            yield return null;
        }

        activeProceduralGenerationMap = this;
        ownsProceduralGenerationSlot = true;
    }

    private void ReleaseProceduralGenerationSlot()
    {
        if (!ownsProceduralGenerationSlot)
            return;

        ownsProceduralGenerationSlot = false;
        if (activeProceduralGenerationMap == this)
            activeProceduralGenerationMap = null;
    }

    private void InitMapGenerators()
    {
        for (int i = 0; i < mapGenerators.Count; i++)
        {
            ChunkGeneratorBase generator = mapGenerators[i];
            if (generator == null)
            {
                Debug.LogError($"[Map.InitMapGenerators] mapGenerators[{i}] 为空", this);
                continue;
            }

            generator.Init(this);
        }

    }

    public T GetGenerator<T>() where T : ChunkGeneratorBase
    {
        if (mapGenerators == null)
            return null;

        for (int i = 0; i < mapGenerators.Count; i++)
        {
            if (mapGenerators[i] is T typed)
                return typed;
        }

        return null;
    }

    private IEnumerator GenerateByPipelineCoroutine(PlanetData planetData)
    {
        if (!MapGenerationPipelinePlan.TryCreateOrderedGenerators(mapGenerators, out List<ChunkGeneratorBase> orderedGenerators, out string validationError))
        {
            FailGeneration(null, validationError, null);
            yield break;
        }

        data ??= new Data_TileMap();
        Vector2 chunkSize = ChunkMgr.GetChunkSize();
        Data.EnsureTileStorage(
            Mathf.Max(1, Mathf.RoundToInt(chunkSize.x)),
            Mathf.Max(1, Mathf.RoundToInt(chunkSize.y)));
        Data.TileLoaded = false;

        int baseSeed = SaveDataMgr.Instance?.SaveData?.Seed ?? 1;
        DimensionManager dimensionManager = DimensionManager.Instance;
        int worldSeed = dimensionManager != null
            ? dimensionManager.GetActiveGenerationSeed(baseSeed)
            : baseSeed;
        WorldAddress worldAddress = dimensionManager != null
            ? dimensionManager.ActiveAddress
            : WorldAddress.FromWorldKey(SceneManager.GetActiveScene().name);
        var context = new MapGenerationContext(
            this,
            planetData,
            worldSeed,
            worldAddress,
            dimensionManager != null ? dimensionManager.ActiveDefinition : null,
            WrappedWorldGenerationDomain.Create(planetData));
        activeGenerationContext = context;

        GenerationStage? activeStage = null;
        for (int i = 0; i < orderedGenerators.Count; i++)
        {
            ChunkGeneratorBase generator = orderedGenerators[i];

            IEnumerator generatorRoutine;
            try
            {
                generator.Init(this);
                if (activeStage != generator.Stage)
                {
                    context.BeginStage(generator.Stage);
                    activeStage = generator.Stage;
                }
                generatorRoutine = generator.GenerateAsync(
                    context,
                    ProceduralGenerationCellsPerFrame);
            }
            catch (Exception exception)
            {
                FailGeneration(
                    context,
                    $"阶段 {generator.Stage} 初始化失败：{generator.GetType().Name}",
                    exception);
                yield break;
            }

            bool generatorFailed = false;
            Exception generatorException = null;
            while (generatorRoutine != null)
            {
                bool hasNext;
                object current = null;
                try
                {
                    hasNext = generatorRoutine.MoveNext();
                    if (hasNext)
                        current = generatorRoutine.Current;
                }
                catch (Exception exception)
                {
                    generatorFailed = true;
                    generatorException = exception;
                    break;
                }

                if (!hasNext)
                    break;

                yield return current;
            }

            try
            {
                (generatorRoutine as IDisposable)?.Dispose();
            }
            catch (Exception exception)
            {
                generatorFailed = true;
                generatorException ??= exception;
            }
            if (context.IsCancellationRequested)
                yield break;
            if (generatorFailed)
            {
                FailGeneration(
                    context,
                    $"阶段 {generator.Stage} 执行失败：{generator.GetType().Name}",
                    generatorException);
                yield break;
            }

            bool stageCompleted = i == orderedGenerators.Count - 1 ||
                                  orderedGenerators[i + 1].Stage != generator.Stage;
            if (!stageCompleted)
                continue;

            try
            {
                context.CompleteStage(generator.Stage);
                activeStage = null;
            }
            catch (Exception exception)
            {
                FailGeneration(
                    context,
                    $"阶段 {generator.Stage} 完成状态提交失败：{generator.GetType().Name}",
                    exception);
                yield break;
            }
            yield return null;
        }

        if (SaveDataMgr.Instance != null &&
            !SaveDataMgr.Instance.TryFinalizeProceduralChunk(chunk, out string persistenceFailure))
        {
            FailGeneration(context, $"程序化区块基线或差量应用失败：{persistenceFailure}", null);
            yield break;
        }

        // TileLoaded and Succeeded are committed only after batched Tilemap finalization succeeds.
    }

    private void FailGeneration(MapGenerationContext context, string reason, Exception exception)
    {
        string message = string.IsNullOrWhiteSpace(reason) ? "地图生成失败" : reason;
        context?.Fail(message, exception);
        if (Data != null)
            Data.TileLoaded = false;
        tilemapVisualReady = false;
        tileMap?.ClearAllTiles();
        TilemapPresentationChanged?.Invoke(this);
        BlockingTilemapLayer.ClearMap(this);
        SaveDataMgr.Instance?.DiscardProceduralChunkBaseline(chunk);
        lastGenerationFailure = exception == null ? message : $"{message}: {exception.Message}";
        Debug.LogError($"[Map.GenerateByPipeline] {lastGenerationFailure}\n{exception}", this);
        OnGenerationFailed?.Invoke(this, lastGenerationFailure);
        chunk?.MarkFailed(lastGenerationFailure);
    }

    private IEnumerator GenerateThenLoadTilemapCoroutine()
    {
        yield return WaitForProceduralGenerationSlot();
        try
        {
            try
            {
                OnMapGenerated_Start.Invoke();
            }
            catch (Exception exception)
            {
                FailGeneration(null, "地图生成开始回调失败", exception);
            }

            if (HasGenerationFailed)
                yield break;

            PlanetData planetData = SaveDataMgr.Instance != null
                ? SaveDataMgr.Instance.GetCurrentPlanetData()
                : null;
            yield return GenerateByPipelineCoroutine(planetData);
        }
        finally
        {
            ReleaseProceduralGenerationSlot();
        }

        bool generationSucceeded =
            activeGenerationContext != null &&
            !activeGenerationContext.HasFailed &&
            !activeGenerationContext.IsCancellationRequested &&
            !HasGenerationFailed;
        if (!generationSucceeded)
        {
            activeGenerationContext = null;
            loadOrGenerateCoroutine = null;
            yield break;
        }

        chunk?.NotifyItemsLoaded();

        tileMap.ClearAllTiles();
        loadOrGenerateCoroutine = null;
        LoadTileData_To_TileMap_Ansync();
        // NotifyChunkReady 由 LoadTileData_To_TileMapCoroutine 结束时的 FinalizeTilemapLoad 触发
    }

    #endregion
}
