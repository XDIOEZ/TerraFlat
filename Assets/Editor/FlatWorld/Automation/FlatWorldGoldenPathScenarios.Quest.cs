using System;
using System.Linq;
using FlatWorld.Gameplay.Progress;
using FlatWorld.Gameplay.Quests;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlatWorld.Automation
{
    /// <summary>
    /// 黄金路径任务场景；先确认玩家入世时自动接取，再复用正式制作操作产生的成功信号推进任务，
    /// 最后验证原子奖励、完成态以及 flatworld.quests 命名空间已经同步写入玩家存档模型。
    /// </summary>
    internal static partial class FlatWorldGoldenPathScenarios
    {
        #region 任务进度

        private const string GoldenQuestId = "flatworld:first_chipped_tool";
        private static bool goldenQuestActiveObserved;
        private static bool goldenQuestCompleted;
        private static PlayerQuestTrackerHUD goldenQuestTrackerHud;

        private static IFlatWorldGoldenPathOperation CreateQuestProgressionOperation() =>
            new FlatWorldGoldenPathOperation(
                "quest.progression", "quest",
                reset: ResetQuestProgressionOperation,
                onWorldReady: BeginQuestProgressionOperation,
                beforeWorldExit: AssertQuestProgressionOperationCompleted,
                cleanup: _ => CleanupQuestProgressionOperation());

        private static void ResetQuestProgressionOperation()
        {
            goldenQuestActiveObserved = false;
            goldenQuestCompleted = false;
            goldenQuestTrackerHud = null;
        }

        /// <summary>在制作操作运行前确认示例任务已由玩家入世事件自动接取。</summary>
        private static void BeginQuestProgressionOperation(FlatWorldGoldenPathScenarioContext context)
        {
            if (context.Player == null || !QuestCatalog.TryGet(GoldenQuestId, out _))
                throw new InvalidOperationException($"任务目录或真实玩家缺失：{GoldenQuestId}。");
            if (QuestManager.Instance == null ||
                !QuestManager.Instance.TryGetRuntime(context.Player, out PlayerQuestRuntime runtime))
            {
                throw new InvalidOperationException("本地玩家任务运行时未随玩家入世完成绑定。");
            }
            if (!runtime.TryGetSnapshot(GoldenQuestId, out QuestSnapshot snapshot) ||
                snapshot.Status != QuestStatus.Active)
            {
                throw new InvalidOperationException(
                    $"示例任务没有自动进入 Active：actual={snapshot?.Status.ToString() ?? "<missing>"}。");
            }

            QuestDefinition[] debugDefinitions = QuestCatalog.All
                .Where(definition => definition.DebugOnly)
                .ToArray();
            if (debugDefinitions.Length < 4)
                throw new InvalidOperationException("任务目录没有加载完整的 GM 测试任务分包。");
            string accidentallyAcceptedDebugQuest = debugDefinitions
                .Select(definition => definition.Id)
                .FirstOrDefault(questId => runtime.TryGetSnapshot(questId, out _));
            if (!string.IsNullOrEmpty(accidentallyAcceptedDebugQuest))
            {
                throw new InvalidOperationException(
                    $"debugOnly 任务不应在普通入世流程自动接取：{accidentallyAcceptedDebugQuest}。");
            }

            goldenQuestTrackerHud = context.Player.GetComponent<PlayerQuestTrackerHUD>();
            if (goldenQuestTrackerHud == null ||
                !goldenQuestTrackerHud.IsViewReady ||
                !goldenQuestTrackerHud.IsInputTransparent ||
                !goldenQuestTrackerHud.IsQuestTracked(GoldenQuestId))
            {
                throw new InvalidOperationException(
                    "任务追踪 HUD 未完成 Prefab 绑定、输入穿透或示例任务展示。");
            }

            goldenQuestActiveObserved = true;
        }

        /// <summary>制作信号后确认任务完成；背包恰好满时先腾出隔离槽再重试原子奖励。</summary>
        private static void AssertQuestProgressionOperationCompleted(
            FlatWorldGoldenPathScenarioContext context)
        {
            if (!goldenQuestActiveObserved)
                throw new InvalidOperationException("任务操作没有观察到自动接取状态。");
            if (!crossSystemCraftingCompleted)
            {
                throw new InvalidOperationException(
                    "任务进度操作依赖 inventory.crafting 的正式成功事务，但制作操作未完成。");
            }
            if (!QuestManager.Instance.TryGetRuntime(context.Player, out PlayerQuestRuntime runtime) ||
                !runtime.TryGetSnapshot(GoldenQuestId, out QuestSnapshot snapshot))
            {
                throw new InvalidOperationException("制作完成后无法读取示例任务快照。");
            }

            if (snapshot.Status == QuestStatus.ReadyToClaim)
            {
                if (crossSystemBag?.Data?.itemSlots == null || crossSystemBag.Data.itemSlots.Count == 0)
                    throw new InvalidOperationException("任务奖励待领取，但隔离玩家背包不可用。");

                crossSystemBag.Data.itemSlots[crossSystemBag.Data.itemSlots.Count - 1].itemData = null;
                runtime.Refresh();
                runtime.TryGetSnapshot(GoldenQuestId, out snapshot);
            }

            if (snapshot?.Status != QuestStatus.Completed || !runtime.IsCompleted(GoldenQuestId))
            {
                throw new InvalidOperationException(
                    $"正式制作信号没有完成示例任务：actual={snapshot?.Status.ToString() ?? "<missing>"}。");
            }
            if (goldenQuestTrackerHud == null || goldenQuestTrackerHud.IsQuestTracked(GoldenQuestId))
                throw new InvalidOperationException("已完成的示例任务仍残留在任务追踪 HUD 中。");

            JObject namespaceData = ItemSpecialDataJsonStore.ReadNamespace(
                context.Player.Data,
                QuestProgressStore.NamespaceKey);
            JObject record = namespaceData["quests"]?[GoldenQuestId] as JObject;
            if (record == null ||
                !string.Equals(record.Value<string>("status"), nameof(QuestStatus.Completed),
                    StringComparison.OrdinalIgnoreCase) ||
                record.Value<bool?>("rewardsClaimed") != true)
            {
                throw new InvalidOperationException(
                    $"任务完成态未写入 {QuestProgressStore.NamespaceKey} 命名空间。");
            }

            goldenQuestCompleted = true;
            Debug.Log(
                $"[GoldenPath][Quest] 自动接取、制作推进、原子奖励与命名空间进度通过：" +
                $"quest={GoldenQuestId}。");
        }

        private static void CleanupQuestProgressionOperation()
        {
            // 玩家背包由 inventory.crafting 的逆序清理恢复；任务进度保留给真实退出重进验证。
            if (!goldenQuestCompleted)
                Debug.LogWarning("[GoldenPath][Quest] 任务场景未完成，通用清理流程将继续回收其他临时状态。");
            goldenQuestTrackerHud = null;
        }

        #endregion
    }
}
