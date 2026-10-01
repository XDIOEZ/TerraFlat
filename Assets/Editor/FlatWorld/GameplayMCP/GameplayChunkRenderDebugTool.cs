using System.Collections.Generic;
using System.Threading.Tasks;
using FlatWorld.WorldModel;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

namespace FlatWorld.GameplayMCP
{
    /// <summary>
    /// 只读核对区块窗口的数据生成、主线程提交、ChunkView 绑定与基础地形 BRG 登记。
    /// 同时报告各队列积压和实际分帧预算，用于定位黑块停在哪一段；不修改游戏状态。
    /// </summary>
    [McpForUnityTool(
        "gameplay_chunk_render_debug",
        Description = "Read-only chunk loading diagnosis. status: queues, driver heartbeat, per-stage timings, oldest requests and BRG registration. sample: observe 1-20 real seconds without moving/resuming the game; report throughput, budget hits and save JSON.",
        Group = "core")]
    public static class GameplayChunkRenderDebugTool
    {
        #region 统一输入输出

        /// <summary>按需返回数据，错误与分页状态保持完整。</summary>
        public static async Task<object> HandleCommand(JObject parameters)
        {
            return GameplayMcpOutput.Finish(await ExecuteCommand(parameters), parameters, true);
        }

        #endregion

        #region 诊断采样

        /// <summary>每类异常最多返回的地址数，避免高视距产生过大的 MCP 响应。</summary>
        private const int MaxSamplesPerCategory = 32;

        public sealed class Parameters : GameplayMcpOutputParameters
        {
            [ToolParameter("status or sample", Required = false, DefaultValue = "status")]
            public string action { get; set; }
            [ToolParameter("Real-time sample duration, 1-20 seconds", Required = false, DefaultValue = "10")]
            public float seconds { get; set; }
        }

        private static async Task<object> ExecuteCommand(JObject parameters)
        {
            string action = parameters?["action"]?.ToString()?.Trim().ToLowerInvariant() ?? "status";
            if (action == "sample")
                return await GameplayChunkStreamingDiagnostics.Sample(parameters?["seconds"]?.Value<float>() ?? 10f);
            if (action != "status") return new ErrorResponse("Use status or sample.");
            return ReadStatus();
        }

        internal static object ReadStatus()
        {
            if (!GameplayMcpRuntime.TryGetPlayerContext(
                    out Player player,
                    out _,
                    out _,
                    out string error))
            {
                return new ErrorResponse(error);
            }

            ChunkMgr chunkManager = ChunkMgr.ExistingInstance;
            if (chunkManager == null)
                return new ErrorResponse("chunk_manager_missing: 当前世界没有可用的 ChunkMgr。");

            Mod_ChunkLoader loader = player.GetComponentInChildren<Mod_ChunkLoader>(true);
            Mod_Cam cameraModule = player.GetComponentInChildren<Mod_Cam>(true);
            if (loader == null || cameraModule == null)
                return new ErrorResponse("chunk_camera_missing: 当前玩家缺少 Mod_ChunkLoader 或 Mod_Cam。");

            Vector2 chunkSize = ChunkMgr.GetChunkSize();
            if (chunkSize.x <= 0.1f || chunkSize.y <= 0.1f)
                return new ErrorResponse("chunk_size_invalid: 当前区块尺寸无效。");

            Vector2Int loadDistance = loader.CurrentLoadChunkDistanceXY;
            Vector2Int prefetchDistance = loader.CurrentUnActiveChunkDistanceXY;
            Vector2Int destroyDistance = loader.CurrentDestroyChunkDistanceXY;
            Vector2Int centerOrigin = chunkManager.ResolveRuntimeChunkOrigin(player.transform.position);
            int radiusX = Mathf.Max(0, loadDistance.x - 1);
            int radiusY = Mathf.Max(0, loadDistance.y - 1);

            var visited = new HashSet<RuntimeWorldAddress>();
            var missingData = new JArray();
            var missingViews = new JArray();
            var readyDataMissingViews = new JArray();
            var pendingBaseTerrain = new JArray();
            var missingBrgOwner = new JArray();
            var zeroBrgVisuals = new JArray();
            int dataReady = 0;
            int failedData = 0;
            int viewsStarted = 0;
            int viewsReady = 0;
            int readyDataWithoutView = 0;
            int baseTerrainPresented = 0;
            int viewsWaitingBaseTerrain = 0;
            int brgRegistered = 0;
            int brgWithVisuals = 0;
            int brgOwnerMissing = 0;
            int brgZeroVisuals = 0;

            for (int dx = -radiusX; dx <= radiusX; dx++)
            {
                for (int dy = -radiusY; dy <= radiusY; dy++)
                {
                    Vector2 probe = new(
                        centerOrigin.x + dx * chunkSize.x + chunkSize.x * 0.5f,
                        centerOrigin.y + dy * chunkSize.y + chunkSize.y * 0.5f);
                    RuntimeWorldAddress address = chunkManager.ResolveWorldAddress(probe);
                    if (!visited.Add(address))
                        continue;

                    bool hasChunk = chunkManager.TryGetChunkRuntime(address, out ChunkRuntime chunk) &&
                                    chunk != null;
                    bool hasReadyData = hasChunk &&
                                        chunk.DataStatus == ChunkDataStatus.Ready &&
                                        chunk.Terrain != null;
                    if (hasReadyData)
                    {
                        dataReady++;
                    }
                    else
                    {
                        if (hasChunk && chunk.DataStatus == ChunkDataStatus.Failed)
                            failedData++;
                        AddAddressSample(missingData, address,
                            hasChunk ? chunk.DataStatus.ToString() : "no_chunk", null,
                            hasChunk ? chunk.FailureReason : null);
                    }

                    if (!chunkManager.TryGetRuntimeChunkPresentationView(address, out ChunkView view) || view == null)
                    {
                        AddAddressSample(missingViews, address);
                        if (hasReadyData)
                        {
                            readyDataWithoutView++;
                            AddAddressSample(readyDataMissingViews, address);
                        }
                        continue;
                    }

                    viewsStarted++;
                    if (view.IsBound)
                        viewsReady++;
                    if (!view.IsBaseTerrainPresented)
                    {
                        viewsWaitingBaseTerrain++;
                        AddAddressSample(pendingBaseTerrain, address,
                            view.IsBinding ? "binding" : "not_binding");
                        continue;
                    }

                    baseTerrainPresented++;
                    if (!view.TryGetTerrainBatchDebugState(out bool registered, out int visualCount))
                    {
                        brgOwnerMissing++;
                        AddAddressSample(missingBrgOwner, address, "renderer_missing", visualCount);
                        continue;
                    }

                    if (!registered)
                    {
                        brgOwnerMissing++;
                        AddAddressSample(missingBrgOwner, address, "owner_not_registered", visualCount);
                        continue;
                    }

                    brgRegistered++;
                    if (visualCount <= 0)
                    {
                        brgZeroVisuals++;
                        AddAddressSample(zeroBrgVisuals, address, "zero_visuals", visualCount);
                        continue;
                    }

                    brgWithVisuals++;
                }
            }

            int targetCount = visited.Count;
            Camera camera = cameraModule.ControllerCamera;
            var data = new JObject
            {
                ["cameraOrthographicSize"] = cameraModule.CurrentOrthographicSize,
                ["cameraAspect"] = camera != null ? camera.aspect : 0f,
                ["player"] = new JArray(player.transform.position.x, player.transform.position.y),
                ["centerChunkOrigin"] = new JArray(centerOrigin.x, centerOrigin.y),
                ["chunkSize"] = new JArray(chunkSize.x, chunkSize.y),
                ["loadDistance"] = new JArray(loadDistance.x, loadDistance.y),
                ["prefetchDistance"] = new JArray(prefetchDistance.x, prefetchDistance.y),
                ["destroyDistance"] = new JArray(destroyDistance.x, destroyDistance.y),
                ["targetCount"] = targetCount,
                ["dataReady"] = dataReady,
                ["dataNotReady"] = targetCount - dataReady,
                ["dataFailed"] = failedData,
                ["viewsStarted"] = viewsStarted,
                ["viewsReady"] = viewsReady,
                ["readyDataWithoutView"] = readyDataWithoutView,
                ["baseTerrainPresented"] = baseTerrainPresented,
                ["viewsWaitingBaseTerrain"] = viewsWaitingBaseTerrain,
                ["brgRegistered"] = brgRegistered,
                ["brgWithVisuals"] = brgWithVisuals,
                ["brgOwnerMissing"] = brgOwnerMissing,
                ["brgZeroVisuals"] = brgZeroVisuals,
                ["generationConcurrency"] = chunkManager.RuntimeChunks?.MaxGenerationConcurrency ?? 0,
                ["generationQueued"] = chunkManager.RuntimeChunks?.QueuedGenerationCount ?? 0,
                ["generationActive"] = chunkManager.RuntimeChunks?.ActiveGenerationCount ?? 0,
                ["pendingCommits"] = chunkManager.RuntimeChunks?.PendingCommitCount ?? 0,
                ["commitsPerFrame"] = chunkManager.RuntimeChunkCommitBudget,
                ["presentationStartsPerFrame"] = chunkManager.RuntimeChunkPresentationStartBudget,
                ["presentationContinuationStepsPerFrame"] =
                    chunkManager.RuntimeChunkPresentationContinuationBudget,
                ["pendingPresentations"] = chunkManager.PendingRuntimeChunkPresentationCount,
                ["pendingPrefetch"] = chunkManager.PendingRuntimeChunkPrefetchCount,
                ["windowPresentationsReady"] = chunkManager.AreRuntimeWindowPresentationsReady,
                ["missingData"] = missingData,
                ["missingViews"] = missingViews,
                ["readyDataMissingViews"] = readyDataMissingViews,
                ["pendingBaseTerrain"] = pendingBaseTerrain,
                ["missingBrgOwner"] = missingBrgOwner,
                ["zeroBrgVisuals"] = zeroBrgVisuals
            };
            data["streamingDiagnostics"] = GameplayChunkStreamingDiagnostics.Capture(chunkManager);

            return new SuccessResponse("FlatWorld chunk render audit.", data);
        }

        /// <summary>限制异常样本数量，避免超大视距把 MCP 响应刷爆。</summary>
        private static void AddAddressSample(
            JArray target,
            RuntimeWorldAddress address,
            string reason = null,
            int? visualCount = null,
            string detail = null)
        {
            if (target.Count >= MaxSamplesPerCategory)
                return;

            var sample = new JObject
            {
                ["dimension"] = address.DimensionId,
                ["origin"] = new JArray(address.ChunkOrigin.X, address.ChunkOrigin.Y)
            };
            if (!string.IsNullOrEmpty(reason))
                sample["reason"] = reason;
            if (visualCount.HasValue)
                sample["visualCount"] = visualCount.Value;
            if (!string.IsNullOrEmpty(detail))
                sample["detail"] = detail;
            target.Add(sample);
        }

        #endregion
    }
}
