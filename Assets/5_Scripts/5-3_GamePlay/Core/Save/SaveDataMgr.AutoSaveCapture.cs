using System;
using System.Collections;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>自动保存的世界身份冻结与分段采集协调。</summary>
public partial class SaveDataMgr
{
    #region 自动保存的世界身份与分段协调
    internal readonly struct AutoSaveWorldScope
    {
        private readonly SaveDataMgr manager;
        private readonly GameSaveData save;
        private readonly int sceneHandle;
        private readonly WorldRuntime world;
        private readonly long worldEpoch;

        internal AutoSaveWorldScope(SaveDataMgr manager)
        {
            this.manager = manager;
            save = manager.SaveData;
            sceneHandle = GameplayWorldRegistry.CurrentScene.handle;
            world = ChunkMgr.ExistingInstance?.WorldRuntime;
            worldEpoch = world?.Epoch ?? long.MinValue;
        }

        internal bool IsCurrent => manager != null &&
            ReferenceEquals(manager.SaveData, save) &&
            GameplayWorldRegistry.CurrentScene.handle == sceneHandle &&
            ReferenceEquals(ChunkMgr.ExistingInstance?.WorldRuntime, world) &&
            (world?.Epoch ?? long.MinValue) == worldEpoch;
    }

    // 维度切换沿用同一存档对象，因此同时冻结场景和世界运行体身份。
    internal AutoSaveWorldScope CaptureAutoSaveWorldScope() => new(this);


    /// <summary>自动保存将自然物、旧 Chunk、差异扫描和 AI 快照按单帧预算分散执行。</summary>
    private IEnumerator PrepareLoadedChunksForAutoSaveCoroutine(Action<Exception> onFailure, AutoSaveWorkBudget budget)
    {
        DroppedItemService.PrepareForSave();
        yield return budget.WaitForNextSlice();
        IEnumerator terrainRoutine = CaptureRuntimeTerrainStatesCoroutine(budget);
        while (terrainRoutine.MoveNext())
            yield return terrainRoutine.Current;
        yield return budget.WaitForNextSlice();
        ChunkMgr chunks = ChunkMgr.ExistingInstance;
        if (chunks != null)
        {
            // 自然物也要分帧克隆，避免表现窗口内大量 Item 在同一帧序列化。
            Exception naturalFailure = null;
            IEnumerator naturalRoutine = ForwardAutoSaveRoutine(
                chunks.CaptureRuntimeNaturalItemStatesCoroutine(budget),
                exception => naturalFailure = exception);
            while (naturalRoutine.MoveNext())
                yield return naturalRoutine.Current;
            if (naturalFailure != null)
            {
                onFailure?.Invoke(naturalFailure);
                yield break;
            }
        }

        if (chunks != null && chunks.Chunk_Dic_ByPos != null)
        {
            List<Chunk> loadedChunks = new List<Chunk>(chunks.Chunk_Dic_ByPos.Values);
            for (int i = 0; i < loadedChunks.Count; i++)
            {
                Chunk chunk = loadedChunks[i];
                if (chunk != null)
                {
                    Exception chunkSaveFailure = null;
                    IEnumerator chunkRoutine = ForwardAutoSaveRoutine(
                        chunk.SaveChunkCoroutine(exception => chunkSaveFailure = exception, budget),
                        exception =>
                        {
                            if (chunkSaveFailure == null)
                                chunkSaveFailure = exception;
                        });
                    while (chunkRoutine.MoveNext())
                        yield return chunkRoutine.Current;

                    if (chunkSaveFailure != null)
                    {
                        onFailure?.Invoke(chunkSaveFailure);
                        yield break;
                    }
                }

                // 即使当前 Chunk 很小，也让区块之间错开一帧，给输入与物理留出更新机会。
                yield return budget.WaitForNextSlice();
            }
        }

        // 旧 Chunk 写入结束后再合并新版建筑和 AI，避免旧差量记录覆盖新版实体快照。
        IEnumerator buildingRoutine = CaptureRuntimeBuildingStatesCoroutine(budget);
        while (buildingRoutine.MoveNext())
            yield return buildingRoutine.Current;
        yield return budget.WaitForNextSlice();
        Exception aiFailure = null;
        IEnumerator aiRoutine = ForwardAutoSaveRoutine(
            CaptureRuntimeAiEntityStatesCoroutine(budget),
            exception => aiFailure = exception);
        while (aiRoutine.MoveNext())
            yield return aiRoutine.Current;
        if (aiFailure != null)
            onFailure?.Invoke(aiFailure);
    }

    /// <summary>安全转发自动保存子协程，避免异常中断后让保存状态永久停留。</summary>
    private static IEnumerator ForwardAutoSaveRoutine(
        IEnumerator routine,
        Action<Exception> onFailure)
    {
        if (routine == null)
            yield break;

        try
        {
            while (true)
            {
                bool moved = false;
                Exception failure = null;
                try
                {
                    moved = routine.MoveNext();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }

                if (failure != null)
                {
                    onFailure?.Invoke(failure);
                    yield break;
                }

                if (!moved)
                    yield break;

                yield return routine.Current;
            }
        }
        finally { (routine as IDisposable)?.Dispose(); }
    }
    #endregion
}
