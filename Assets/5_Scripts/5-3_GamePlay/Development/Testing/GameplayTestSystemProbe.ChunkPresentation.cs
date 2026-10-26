using System;
using System.Collections;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 基础地形分步绑定与取消

    private static void RegisterChunkPresentationOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("world_streaming/base_presentation_steps", ServiceBasePresentationSteps);
        operations.Add("world_streaming/collision_dirty_queue", ServiceCollisionDirtyQueue);
        operations.Add("rendering/building_liquid_surface", ServiceBuildingLiquidSurface);
    }

    // 使用正式液体与支撑写入采样变化，Lua负责判断水下、水上及批量排水的结果。
    private static JObject ServiceBuildingLiquidSurface(JObject args)
    {
        using var buffer = new ChunkTerrainBuffer(2, 1, LiquidTypeCatalog.BuiltIn);
        using ChunkTerrainData terrain = buffer.Seal();
        var samples = new JArray();
        var notifications = new JArray();
        void Sample(string stage) => samples.Add(new JObject
        {
            ["stage"] = stage,
            ["submerged"] = ChunkTilemapRenderer.IsBuildingSubmergedByUnsupportedLiquid(terrain, 0, 0),
            ["depth"] = terrain.GetLiquidDepth(0, 0),
            ["support"] = TerrainSupportLayer.GetTileId(terrain, 0, 0)
        });
        terrain.Changed += changed =>
        {
            if (changed.Kind == TerrainChangeKind.Liquid || changed.AffectsEnvironmentLayer(TerrainSupportLayer.TileIdLayer))
                notifications.Add(new JObject { ["kind"] = "single", ["submerged"] =
                    ChunkTilemapRenderer.IsBuildingSubmergedByUnsupportedLiquid(terrain, 0, 0) });
        };
        terrain.LiquidBatchChanged += changed =>
        {
            foreach (int index in changed.CellIndices.Span)
                notifications.Add(new JObject { ["kind"] = "batch", ["index"] = index, ["submerged"] =
                    ChunkTilemapRenderer.IsBuildingSubmergedByUnsupportedLiquid(terrain, index % terrain.Width, index / terrain.Width) });
        };
        int water = LiquidTypeCatalog.BuiltIn.GetIndex(LiquidTypeCatalog.DirtyWaterId);
        Sample("dry");
        terrain.SetLiquid(0, 0, water, 50); Sample("water");
        TerrainSupportLayer.Set(terrain, 0, 0, 23, 1); Sample("platform");
        TerrainSupportLayer.Set(terrain, 0, 0, 0, 0); Sample("removed");
        terrain.SetLiquidBatch(new[] { new LiquidCellUpdate(0, 0, 0) }); Sample("drained");
        terrain.SetLiquidBatch(new[] { new LiquidCellUpdate(0, water, 1), new LiquidCellUpdate(1, water, 10) }); Sample("shallow");
        TerrainSupportLayer.Set(terrain, 1, 0, 23, 1); Sample("neighbour_platform");
        var sorting = WorldRenderingConfigCatalog.Default.sorting;
        int groundQueue = 0, waterQueue = 0;
        foreach (var profile in sorting.batchProfiles)
        {
            if (profile.visualLayer == "Ground") groundQueue = sorting.batchRenderQueueBase + profile.renderQueueOffset;
            if (profile.visualLayer == "Water") waterQueue = sorting.batchRenderQueueBase + profile.renderQueueOffset;
        }
        return new JObject
        {
            ["samples"] = samples, ["notifications"] = notifications,
            ["groundQueue"] = groundQueue, ["waterQueue"] = waterQueue,
            ["buildingQueue"] = ChunkTilemapRenderer.ResolveSubmergedBuildingRenderQueue(),
            ["outsideSubmerged"] = ChunkTilemapRenderer.IsBuildingSubmergedByUnsupportedLiquid(terrain, -1, 0),
            ["missingSubmerged"] = ChunkTilemapRenderer.IsBuildingSubmergedByUnsupportedLiquid(null, 0, 0)
        };
    }

    // 独立空地形实走正式绑定器，检查预算与所有权，不伪造可见地块或替代画面验收。
    private static JObject ServiceBasePresentationSteps(JObject args)
    {
        using var world = new WorldRuntime("probe:presentation", 1);
        var profile = new ChunkGenerationProfileSnapshot("probe:presentation", 1, 16, 8);
        ChunkRuntime Commit(Int2 origin)
        {
            var address = new FlatWorld.WorldModel.WorldAddress("probe:presentation", origin);
            ChunkGenerationRequest request = world.BeginChunkGeneration(address, 1, profile);
            using var buffer = new ChunkTerrainBuffer(profile.Width, profile.Height);
            using var result = new ChunkGenerationResult(request, buffer);
            if (!world.TryCommit(result, out string reason)) throw new InvalidOperationException(reason);
            world.TryGetChunk(address, out ChunkRuntime committed);
            return committed;
        }
        Vector2Int origin = WorldTopologyRuntime.NormalizeCell(new Vector2Int(1000000, 1000000));
        ChunkRuntime chunk = Commit(new Int2(origin.x, origin.y));
        var root = new GameObject("GameplayTest Independent Base Presentation") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        root.transform.position = new Vector3(origin.x, origin.y, 0f);
        ChunkTilemapRenderer renderer = root.AddComponent<ChunkTilemapRenderer>();
        renderer.SetWorld(world);
        int events = 0, maxStepWrites = 0;
        renderer.BatchPresentationRebuilt += () => events++;
        bool uploadInvalidated = false, uploadEarlyReady = false;
        int Drain(IEnumerator steps, bool injectUploadChange = false)
        {
            int count = 0;
            bool more;
            do
            {
                int before = renderer.BaseTerrainBindingCellWrites;
                more = steps.MoveNext();
                maxStepWrites = Math.Max(maxStepWrites, renderer.BaseTerrainBindingCellWrites - before);
                if (injectUploadChange && !uploadInvalidated && more && before >= 192 &&
                    renderer.BaseTerrainBindingCellWrites == before)
                {
                    uploadEarlyReady = renderer.IsBatchPresentationComplete;
                    uploadInvalidated = renderer.InvalidateBindingVisuals();
                    ChunkBatchRendererGroupService.UnregisterOwner(renderer);
                    chunk.Terrain.SetCell(1, 0, new TerrainCell(0, 0, 0, 1, 3, TerrainCellFlags.Walkable));
                }
                if (++count > 128) throw new InvalidOperationException("基础地形绑定没有在有界步数内完成。");
            } while (more);
            return count;
        }
        IEnumerator binding = null, cancelled = null, stale = null, replacement = null;
        try
        {
            binding = renderer.BindIncremental(chunk);
            bool firstYield = binding.MoveNext();
            bool initReady = renderer.IsBatchPresentationComplete;
            bool initRegistered = renderer.IsBatchPresentationRegistered;
            bool secondYield = binding.MoveNext();
            int firstSliceWrites = renderer.BaseTerrainBindingCellWrites;
            bool partialReady = renderer.IsBatchPresentationComplete;
            Vector2Int neighbourOrigin = WorldTopologyRuntime.NormalizeCell(origin + new Vector2Int(profile.Width, 0));
            ChunkRuntime neighbour = Commit(new Int2(neighbourOrigin.x, neighbourOrigin.y));
            chunk.Terrain.SetCell(0, 0, new TerrainCell(0, 0, 0, 1, 2, TerrainCellFlags.Walkable));
            bool neighbourEvicted = world.EvictChunk(neighbour.Address);
            bool invalidated = renderer.InvalidateBindingVisuals();
            int earlyEvents = events;
            bool repairWhileBinding = renderer.RepairBatchPresentationIfNeeded();
            int steps = Drain(binding, injectUploadChange: true);
            bool completed = renderer.IsBatchPresentationComplete;
            int completedEvents = events;
            bool completedInvalidation = renderer.InvalidateBindingVisuals();
            int fullWrites = renderer.BaseTerrainBindingCellWrites;
            renderer.Unbind();
            bool unbound = !renderer.IsBatchPresentationRegistered && !renderer.IsBatchPresentationComplete;

            cancelled = renderer.BindIncremental(chunk);
            cancelled.MoveNext();
            cancelled.MoveNext();
            (cancelled as IDisposable)?.Dispose();
            bool cancelledReleased = !renderer.IsBatchPresentationRegistered && !renderer.IsBatchPresentationComplete;
            int cancelledEvents = events;

            stale = renderer.BindIncremental(chunk);
            stale.MoveNext();
            renderer.Unbind();
            replacement = renderer.BindIncremental(chunk);
            replacement.MoveNext();
            (stale as IDisposable)?.Dispose();
            bool replacementSurvived = renderer.IsBatchPresentationRegistered && !renderer.IsBatchPresentationComplete;
            Drain(replacement);

            // 按正式 LiquidBatchChanged 入口制造一次加水再排水，帧末只需刷新水面。
            // 两个批次在下一次表现提交前完成，最终地面依然干燥，不依赖测试 GameRes 水材质。
            long fullWritesBeforeLiquid = renderer.IncrementalFullTerrainCellWrites;
            int visualCountBeforeLiquid = renderer.BaseTerrainVisualCount;
            int waterIndex = chunk.Terrain.LiquidTypes.GetIndex(LiquidTypeCatalog.DirtyWaterId);
            chunk.Terrain.SetLiquidBatch(new[] { new LiquidCellUpdate(5, waterIndex, 5) });
            chunk.Terrain.SetLiquidBatch(new[] { new LiquidCellUpdate(5, 0, 0) });
            bool waterSkippedLavaScan = !renderer.PendingLiquidGlowRefresh;
            int queuedLiquidCells = renderer.PendingLiquidVisualCellCount;
            renderer.FlushPendingLiquidVisuals();
            bool liquidOnlyRefresh = queuedLiquidCells > 0 &&
                renderer.PendingLiquidVisualCellCount == 0 &&
                renderer.IncrementalFullTerrainCellWrites == fullWritesBeforeLiquid &&
                renderer.BaseTerrainVisualCount == visualCountBeforeLiquid &&
                chunk.Terrain.GetLiquidLevel(5, 0) == 0;

            int lavaIndex = chunk.Terrain.LiquidTypes.GetIndex(LiquidTypeCatalog.LavaId);
            chunk.Terrain.SetLiquidBatch(new[] { new LiquidCellUpdate(5, lavaIndex, 4) });
            bool lavaRequestedGlow = renderer.PendingLiquidGlowRefresh;
            chunk.Terrain.SetLiquidBatch(new[] { new LiquidCellUpdate(5, 0, 0) });
            renderer.FlushPendingLiquidVisuals();
            bool lavaRemovalPreserved = lavaRequestedGlow && renderer.PendingLiquidGlowRefresh &&
                renderer.IncrementalFullTerrainCellWrites == fullWritesBeforeLiquid;

            // 非液体的正式 Cell 修改仍必须走四层完整重提路径。
            chunk.Terrain.SetCell(5, 4, new TerrainCell(0, 0, 0, 1, 7, TerrainCellFlags.Walkable));
            bool terrainRefreshPreserved = renderer.IncrementalFullTerrainCellWrites > fullWritesBeforeLiquid;
            return new JObject
            {
                ["firstYield"] = firstYield, ["secondYield"] = secondYield,
                ["initRegistered"] = initRegistered, ["initReady"] = initReady,
                ["firstSliceWrites"] = firstSliceWrites, ["partialReady"] = partialReady,
                ["neighbourEvicted"] = neighbourEvicted, ["invalidated"] = invalidated,
                ["earlyEvents"] = earlyEvents, ["repairWhileBinding"] = repairWhileBinding,
                ["steps"] = steps, ["completed"] = completed, ["completedEvents"] = completedEvents,
                ["completedInvalidation"] = completedInvalidation, ["fullWrites"] = fullWrites,
                ["uploadInvalidated"] = uploadInvalidated, ["uploadEarlyReady"] = uploadEarlyReady,
                ["maxStepWrites"] = maxStepWrites, ["unbound"] = unbound,
                ["cancelledReleased"] = cancelledReleased, ["cancelledEvents"] = cancelledEvents,
                ["replacementSurvived"] = replacementSurvived,
                ["replacementCompleted"] = renderer.IsBatchPresentationComplete, ["finalEvents"] = events,
                ["liquidOnlyRefresh"] = liquidOnlyRefresh, ["queuedLiquidCells"] = queuedLiquidCells,
                ["terrainRefreshPreserved"] = terrainRefreshPreserved,
                ["waterSkippedLavaScan"] = waterSkippedLavaScan,
                ["lavaRemovalPreserved"] = lavaRemovalPreserved
            };
        }
        finally
        {
            (binding as IDisposable)?.Dispose();
            (cancelled as IDisposable)?.Dispose();
            (stale as IDisposable)?.Dispose();
            (replacement as IDisposable)?.Dispose();
            renderer.Unbind();
            renderer.SetWorld(null);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    #endregion
}
