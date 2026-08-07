using System;

namespace FlatWorld.Automation
{
    internal static partial class FlatWorldGoldenPathScenarios
    {
        private static ItemData itemLifecyclePlayerData;
        private static int itemLifecyclePlayerGuid;
        private static string itemLifecyclePlayerId;
        private static int itemLifecycleChunkChecks;

        private static void ResetItemLifecycleScenario()
        {
            itemLifecyclePlayerData = null;
            itemLifecyclePlayerGuid = 0;
            itemLifecyclePlayerId = null;
            itemLifecycleChunkChecks = 0;
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
            itemLifecyclePlayerData = null;
            itemLifecyclePlayerGuid = 0;
            itemLifecyclePlayerId = null;
            itemLifecycleChunkChecks = 0;
        }
    }
}
