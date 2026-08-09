using System;
using FlatWorld.WorldModel;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

namespace FlatWorld.Automation
{
    /// <summary>真实世界黄金路径中的生态物品生成与删除持久化验证。</summary>
    internal static partial class FlatWorldGoldenPathScenarios
    {
        private static RuntimeWorldAddress ecologyAddress;
        private static int ecologyGuid;
        private static string ecologyItemId;
        private static bool ecologyFound;
        private static bool ecologyPersistenceVerified;

        private static void ResetEcologyScenario()
        {
            ecologyAddress = default;
            ecologyGuid = 0;
            ecologyItemId = null;
            ecologyFound = false;
            ecologyPersistenceVerified = false;
        }

        private static void BeginEcologyScenario(FlatWorldGoldenPathScenarioContext context)
        {
            ChunkMgr manager = ChunkMgr.Instance;
            if (manager?.ActiveGenerationProfile == null ||
                manager.ActiveGenerationProfile.EcologyRules.Count == 0)
            {
                throw new InvalidOperationException("黄金路径进入世界后没有生态生成规则。");
            }

            ObserveEcologyItems(manager);
        }

        private static void VerifyEcologyAtChunkReady(FlatWorldGoldenPathScenarioContext context)
        {
            if (ecologyPersistenceVerified)
                return;

            ObserveEcologyItems(ChunkMgr.Instance);
            if (!ecologyFound)
                return;

            Item item = ItemMgr.Instance?.GetItemByGuid(ecologyGuid);
            if (item == null)
                throw new InvalidOperationException(
                    $"生态物品已生成记录但 ItemMgr 中找不到：{ecologyItemId}/{ecologyGuid}。");

            ChunkMgr manager = ChunkMgr.Instance;
            if (!manager.TryGetChunkRuntime(ecologyAddress, out ChunkRuntime chunk) ||
                !manager.TryGetRuntimeChunkView(ecologyAddress, out ChunkView view))
            {
                throw new InvalidOperationException("生态物品所在 Chunk 没有可重绑的 ChunkView。");
            }

            ItemMgr.Instance.DespawnItem(item, saveData: false, detachFromChunk: false);
            PlanetData planet = SaveDataMgr.Instance?.GetCurrentPlanetData();
            if (planet?.Ecology == null ||
                !planet.Ecology.IsRemoved(ecologyAddress.ChunkOrigin.X,
                    ecologyAddress.ChunkOrigin.Y, ecologyGuid))
            {
                throw new InvalidOperationException("生态物品销毁后未写入删除 GUID。");
            }

            // 模拟一次 View 卸载/重绑：确定性基线生成后应被删除列表过滤。
            view.Unbind();
            view.Bind(manager.WorldRuntime, chunk, includeNavigation: true);
            ChunkNaturalItemRenderer renderer =
                view.GetComponentInChildren<ChunkNaturalItemRenderer>(true);
            if (renderer != null && renderer.TryGetSpawnedItem(ecologyGuid, out _))
                throw new InvalidOperationException("生态物品重绑后被错误复活。");

            ecologyPersistenceVerified = true;
            Debug.Log($"[GoldenPath][Ecology] 已验证生成、销毁和重绑持久化：{ecologyItemId}/{ecologyGuid}。");
        }

        private static void AssertEcologyScenarioCompleted()
        {
            if (!ecologyFound || !ecologyPersistenceVerified)
            {
                throw new InvalidOperationException(
                    "黄金路径结束前未完成至少一个生态物品的生成与销毁持久化验证。");
            }
        }

        private static void CleanupEcologyScenario()
        {
            ResetEcologyScenario();
        }

        private static void ObserveEcologyItems(ChunkMgr manager)
        {
            if (ecologyFound || manager == null)
                return;

            foreach (ChunkRuntime chunk in manager.Chunks.Values)
            {
                if (chunk == null || chunk.Ecology == null || chunk.Ecology.Count == 0 ||
                    !manager.TryGetRuntimeChunkView(chunk.Address, out ChunkView view))
                {
                    continue;
                }

                ChunkNaturalItemRenderer renderer =
                    view.GetComponentInChildren<ChunkNaturalItemRenderer>(true);
                if (renderer == null || renderer.SpawnedItemCount == 0)
                    continue;

                for (int i = 0; i < chunk.Ecology.Placements.Count; i++)
                {
                    NaturalItemPlacement placement = chunk.Ecology.Placements[i];
                    // 天然传送门是不可采集的确定性世界特征，不能作为“销毁后不复活”生态样本。
                    if (placement.IsDimensionPortal)
                        continue;
                    if (!renderer.TryGetSpawnedItem(placement.Guid, out Item item) || item == null)
                        continue;

                    ecologyAddress = chunk.Address;
                    ecologyGuid = placement.Guid;
                    ecologyItemId = placement.ItemId;
                    ecologyFound = true;
                    return;
                }
            }
        }
    }
}
