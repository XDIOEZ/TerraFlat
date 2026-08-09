using System;
using UnityEngine;

namespace FlatWorld.Automation
{
    internal static partial class FlatWorldGoldenPathScenarios
    {
        private static ItemData itemLifecyclePlayerData;
        private static int itemLifecyclePlayerGuid;
        private static string itemLifecyclePlayerId;
        private static int itemLifecycleChunkChecks;
        private static Item itemLifecycleDrop;
        private static bool itemLifecycleDropOwnershipVerified;
        private static float itemLifecycleDropDeadline;

        private static void ResetItemLifecycleScenario()
        {
            itemLifecyclePlayerData = null;
            itemLifecyclePlayerGuid = 0;
            itemLifecyclePlayerId = null;
            itemLifecycleChunkChecks = 0;
            itemLifecycleDrop = null;
            itemLifecycleDropOwnershipVerified = false;
            itemLifecycleDropDeadline = 0f;
        }

        private static void BeginItemLifecycleScenario(FlatWorldGoldenPathScenarioContext context)
        {
            if (context.Player == null || context.Player.itemData == null)
            {
                throw new InvalidOperationException("Item lifecycle: 世界就绪后缺少玩家或 ItemData。");
            }

            itemLifecyclePlayerData = context.Player.itemData;
            itemLifecyclePlayerGuid = itemLifecyclePlayerData.Guid;
            itemLifecyclePlayerId = itemLifecyclePlayerData.IDName;
            AssertItemLifecycleBinding(context, "world ready");
            BeginItemLifecycleDropScenario(context);
        }

        /// <summary>在真实世界中生成一次短距离掉落，验证新区块归属和动画完成。</summary>
        private static void BeginItemLifecycleDropScenario(FlatWorldGoldenPathScenarioContext context)
        {
            if (GameRes.Instance == null || ItemMgr.Instance == null ||
                ChunkMgr.ExistingInstance == null ||
                !ChunkMgr.ExistingInstance.IsWorldModelRuntimeActive)
            {
                throw new InvalidOperationException(
                    "Item lifecycle: WorldModel 掉落回归缺少运行时区块窗口。");
            }

            ItemData dropData = GameRes.Instance.CreateItemData("Berry");
            if (dropData == null)
                throw new InvalidOperationException("Item lifecycle: 无法创建 Berry 掉落数据。");

            Vector2 start = context.Player.transform.position;
            itemLifecycleDrop = ItemMgr.Instance.InstantiateItem(
                dropData, start, Quaternion.identity, Vector3.one * 0.5f);
            if (itemLifecycleDrop == null)
                throw new InvalidOperationException("Item lifecycle: Berry 掉落实例化失败。");

            itemLifecycleDrop.Load();
            itemLifecycleDrop.SetInHand(false);
            Mod_BaseDroper.StaticDropItem_Pos(
                itemLifecycleDrop, start, start + Vector2.right * 0.25f, 0.1f,
                Mod_BaseDroper.MoveMode.StraightLine);
            itemLifecycleDropDeadline = Time.time + 5f;
        }

        /// <summary>跨帧等待掉落动画结束，并确认物品仍由新版区块节点持有。</summary>
        private static void TickItemLifecycleDropScenario()
        {
            if (itemLifecycleDropOwnershipVerified)
                return;
            if (itemLifecycleDrop == null)
                throw new InvalidOperationException("Item lifecycle: 掉落物在验证完成前被销毁。");

            ChunkNaturalItemRenderer owner =
                itemLifecycleDrop.GetComponentInParent<ChunkNaturalItemRenderer>(true);
            if (owner == null)
            {
                if (Time.time >= itemLifecycleDropDeadline)
                    throw new InvalidOperationException(
                        "Item lifecycle: 掉落物没有挂到新版 ChunkView 临时物品节点。");
                return;
            }

            if (itemLifecycleDrop.itemData?.Stack != null &&
                itemLifecycleDrop.itemData.Stack.CanBePickedUp)
            {
                itemLifecycleDropOwnershipVerified = true;
                Debug.Log("[GoldenPath][ItemLifecycle] 掉落物已绑定新版 ChunkView 并完成动画。");
            }
            else if (Time.time >= itemLifecycleDropDeadline)
            {
                throw new InvalidOperationException("Item lifecycle: 掉落动画未在限定时间内完成。");
            }
        }

        private static void VerifyItemLifecycleAtChunkReady(FlatWorldGoldenPathScenarioContext context)
        {
            AssertItemLifecycleBinding(context, $"chunk {context.ExpectedChunk} ready");
            itemLifecycleChunkChecks++;
        }

        private static void AssertItemLifecycleScenarioCompleted(FlatWorldGoldenPathScenarioContext context)
        {
            AssertItemLifecycleBinding(context, "before world exit");
            if (itemLifecycleChunkChecks <= 0)
            {
                throw new InvalidOperationException("Item lifecycle: 未在任何 Chunk Ready 阶段验证玩家数据绑定。");
            }
            if (!itemLifecycleDropOwnershipVerified)
                throw new InvalidOperationException("Item lifecycle: 未完成新版掉落物归属验证。");
        }

        private static void AssertItemLifecycleBinding(
            FlatWorldGoldenPathScenarioContext context,
            string phase)
        {
            if (!ReferenceEquals(context.Player?.itemData, itemLifecyclePlayerData) ||
                !ReferenceEquals(context.Player?.Data, itemLifecyclePlayerData))
            {
                throw new InvalidOperationException($"Item lifecycle: {phase} 阶段玩家 ItemData 绑定被意外替换。");
            }

            if (itemLifecyclePlayerData.Guid != itemLifecyclePlayerGuid ||
                !string.Equals(itemLifecyclePlayerData.IDName, itemLifecyclePlayerId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Item lifecycle: {phase} 阶段玩家 ItemData 标识发生变化。");
            }

            ItemMgr itemManager = ItemMgr.Instance;
            if (itemManager == null || itemManager.GetItemByGuid(itemLifecyclePlayerGuid) != context.Player)
            {
                throw new InvalidOperationException($"Item lifecycle: {phase} 阶段玩家不在 ItemMgr 运行时注册表中。");
            }
        }

        private static void CleanupItemLifecycleScenario()
        {
            if (itemLifecycleDrop != null && !itemLifecycleDrop.DestructionHandled &&
                ItemMgr.Instance != null)
            {
                ItemMgr.Instance.DespawnItem(itemLifecycleDrop,
                    saveData: false, detachFromChunk: false);
            }

            itemLifecyclePlayerData = null;
            itemLifecyclePlayerGuid = 0;
            itemLifecyclePlayerId = null;
            itemLifecycleChunkChecks = 0;
            itemLifecycleDrop = null;
            itemLifecycleDropOwnershipVerified = false;
            itemLifecycleDropDeadline = 0f;
        }
    }
}
