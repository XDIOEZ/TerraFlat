using System.Collections.Generic;
using FlatWorld.WorldModel;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

namespace FlatWorld.GameplayMCP
{
    /// <summary>
    /// 只读核对当前相机可见区块窗口、WorldModel 数据、ChunkView 与基础地形 BRG 登记。
    /// 用于定位“区块已生成但没有渲染”和“区块尚未生成”两类问题，不修改任何游戏状态。
    /// </summary>
    [McpForUnityTool(
        "gameplay_chunk_render_debug",
        Description = "Read-only audit of the current streamed chunk window: camera size, load distance, data readiness, ChunkView binding and terrain BRG registration.",
        Group = "core")]
    public static class GameplayChunkRenderDebugTool
    {
        private const int MaxSamplesPerCategory = 32;

        public static object HandleCommand(JObject parameters)
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
            var missingBrgOwner = new JArray();
            var zeroBrgVisuals = new JArray();
            int dataReady = 0;
            int viewsStarted = 0;
            int viewsReady = 0;
            int brgRegistered = 0;
            int brgWithVisuals = 0;

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

                    if (chunkManager.TryGetChunkRuntime(address, out ChunkRuntime chunk) &&
                        chunk != null &&
                        chunk.DataStatus == ChunkDataStatus.Ready &&
                        chunk.Terrain != null)
                    {
                        dataReady++;
                    }
                    else
                    {
                        AddAddressSample(missingData, address);
                    }

                    if (!chunkManager.TryGetRuntimeChunkPresentationView(address, out ChunkView view) || view == null)
                    {
                        AddAddressSample(missingViews, address);
                        continue;
                    }

                    viewsStarted++;
                    if (view.IsBound)
                        viewsReady++;
                    if (!view.TryGetTerrainBatchDebugState(out bool registered, out int visualCount))
                    {
                        AddAddressSample(missingBrgOwner, address, "renderer_missing", visualCount);
                        continue;
                    }

                    if (!registered)
                    {
                        AddAddressSample(missingBrgOwner, address, "owner_not_registered", visualCount);
                        continue;
                    }

                    brgRegistered++;
                    if (visualCount <= 0)
                    {
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
                ["viewsStarted"] = viewsStarted,
                ["viewsReady"] = viewsReady,
                ["brgRegistered"] = brgRegistered,
                ["brgWithVisuals"] = brgWithVisuals,
                ["pendingPresentations"] = chunkManager.PendingRuntimeChunkPresentationCount,
                ["pendingPrefetch"] = chunkManager.PendingRuntimeChunkPrefetchCount,
                ["windowPresentationsReady"] = chunkManager.AreRuntimeWindowPresentationsReady,
                ["missingData"] = missingData,
                ["missingViews"] = missingViews,
                ["missingBrgOwner"] = missingBrgOwner,
                ["zeroBrgVisuals"] = zeroBrgVisuals
            };

            return new SuccessResponse("FlatWorld chunk render audit.", data);
        }

        /// <summary>限制异常样本数量，避免超大视距把 MCP 响应刷爆。</summary>
        private static void AddAddressSample(
            JArray target,
            RuntimeWorldAddress address,
            string reason = null,
            int visualCount = 0)
        {
            if (target.Count >= MaxSamplesPerCategory)
                return;

            var sample = new JObject
            {
                ["dimension"] = address.DimensionId,
                ["origin"] = new JArray(address.ChunkOrigin.X, address.ChunkOrigin.Y)
            };
            if (!string.IsNullOrEmpty(reason))
            {
                sample["reason"] = reason;
                sample["visualCount"] = visualCount;
            }
            target.Add(sample);
        }
    }
}
