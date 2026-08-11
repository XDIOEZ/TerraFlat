using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FlatWorld.Gameplay.Quests
{
    /// <summary>
    /// 任务定义的唯一运行时目录；内建内容先整体替换，MOD 内容随后增量注册，最后统一做引用与循环校验。
    /// 目录只保存定义，不保存玩家状态，因此热重载时不会把不同玩家的进度混在一起。
    /// </summary>
    public static class QuestCatalog
    {
        #region 常量与状态

        public const int SupportedSchemaVersion = 1;
        private const string BuiltInPrefix = "flatworld:";
        private static readonly StringComparer IdComparer = StringComparer.OrdinalIgnoreCase;
        private static readonly Dictionary<string, QuestDefinition> Definitions = new(IdComparer);
        private static readonly HashSet<string> ExternalIds = new(IdComparer);

        public static bool IsReady { get; private set; }
        public static IReadOnlyCollection<QuestDefinition> All => Definitions.Values;

        #endregion

        #region 注册

        public static void ReplaceBuiltIns(IEnumerable<QuestDefinition> definitions)
        {
            Definitions.Clear();
            ExternalIds.Clear();
            IsReady = false;

            foreach (QuestDefinition definition in definitions ?? Enumerable.Empty<QuestDefinition>())
            {
                if (definition == null)
                    throw new InvalidDataException("内建任务目录包含空定义");
                if (string.IsNullOrWhiteSpace(definition.Id) ||
                    !definition.Id.StartsWith(BuiltInPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"内建任务 ID 必须使用 {BuiltInPrefix} 前缀：{definition.Id}");
                }

                definition.Id = definition.Id.Trim();
                if (!Definitions.TryAdd(definition.Id, definition))
                    throw new InvalidDataException($"内建任务 ID 重复：{definition.Id}");
            }
        }

        public static void RegisterExternal(QuestDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            if (string.IsNullOrWhiteSpace(definition.Id))
                throw new InvalidDataException("外部任务 ID 不能为空");

            definition.Id = definition.Id.Trim();
            if (!Definitions.TryAdd(definition.Id, definition))
                throw new InvalidDataException($"任务 ID 重复：{definition.Id}");

            ExternalIds.Add(definition.Id);
            IsReady = false;
        }

        public static void RemoveExternalDefinitions()
        {
            foreach (string id in ExternalIds)
                Definitions.Remove(id);

            ExternalIds.Clear();
            IsReady = false;
        }

        public static void FinalizeRegistration()
        {
            IsReady = false;
            QuestExtensionRegistry.EnsureBuiltInsRegistered();

            foreach (QuestDefinition definition in Definitions.Values)
                ValidateDefinition(definition);

            ValidatePrerequisiteGraph();
            IsReady = true;
        }

        public static bool TryGet(string questId, out QuestDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(questId))
            {
                definition = null;
                return false;
            }

            return Definitions.TryGetValue(questId.Trim(), out definition);
        }

        public static void Reset()
        {
            Definitions.Clear();
            ExternalIds.Clear();
            IsReady = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Reset();
        }

        #endregion

        #region 校验

        private static void ValidateDefinition(QuestDefinition definition)
        {
            if (definition.DefinitionVersion < 1)
                throw new InvalidDataException($"任务 {definition.Id} 的 definitionVersion 必须大于 0");
            if (!IsMode(definition.AcceptMode))
                throw new InvalidDataException($"任务 {definition.Id} 的 acceptMode 无效：{definition.AcceptMode}");
            if (!IsMode(definition.TurnInMode))
                throw new InvalidDataException($"任务 {definition.Id} 的 turnInMode 无效：{definition.TurnInMode}");
            if (definition.Stages == null || definition.Stages.Count == 0)
                throw new InvalidDataException($"任务 {definition.Id} 至少需要一个阶段");

            var stageIds = new HashSet<string>(IdComparer);
            foreach (QuestStageDefinition stage in definition.Stages)
            {
                if (stage == null || string.IsNullOrWhiteSpace(stage.Id))
                    throw new InvalidDataException($"任务 {definition.Id} 包含空阶段或空阶段 ID");
                stage.Id = stage.Id.Trim();
                if (!stageIds.Add(stage.Id))
                    throw new InvalidDataException($"任务 {definition.Id} 的阶段 ID 重复：{stage.Id}");
                if (!string.Equals(stage.CompletionMode, QuestCompletionModes.All, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(stage.CompletionMode, QuestCompletionModes.Any, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"任务 {definition.Id} 阶段 {stage.Id} 的 completionMode 无效：{stage.CompletionMode}");
                }
                if (stage.Objectives == null || stage.Objectives.Count == 0)
                    throw new InvalidDataException($"任务 {definition.Id} 阶段 {stage.Id} 至少需要一个目标");

                var objectiveIds = new HashSet<string>(IdComparer);
                foreach (QuestObjectiveDefinition objective in stage.Objectives)
                {
                    if (objective == null || string.IsNullOrWhiteSpace(objective.Id))
                        throw new InvalidDataException($"任务 {definition.Id} 阶段 {stage.Id} 包含空目标或空目标 ID");
                    objective.Id = objective.Id.Trim();
                    if (!objectiveIds.Add(objective.Id))
                        throw new InvalidDataException(
                            $"任务 {definition.Id} 阶段 {stage.Id} 的目标 ID 重复：{objective.Id}");
                    if (objective.Required <= 0f)
                        throw new InvalidDataException(
                            $"任务 {definition.Id} 目标 {objective.Id} 的 required 必须大于 0");
                    if (!QuestExtensionRegistry.TryGetObjective(objective.Type, out IQuestObjectiveHandler handler))
                        throw new InvalidDataException($"任务 {definition.Id} 使用未知目标类型：{objective.Type}");
                    if (!handler.Validate(objective, out string error))
                        throw new InvalidDataException($"任务 {definition.Id} 目标 {objective.Id} 无效：{error}");
                }
            }

            foreach (QuestConditionDefinition condition in definition.Conditions ?? Enumerable.Empty<QuestConditionDefinition>())
            {
                if (condition == null ||
                    !QuestExtensionRegistry.TryGetCondition(condition.Type, out IQuestConditionEvaluator evaluator))
                {
                    throw new InvalidDataException($"任务 {definition.Id} 使用未知条件类型：{condition?.Type}");
                }
                if (!evaluator.Validate(condition, out string error))
                    throw new InvalidDataException($"任务 {definition.Id} 条件无效：{error}");
            }

            var rewardIds = new HashSet<string>(IdComparer);
            foreach (QuestRewardDefinition reward in definition.Rewards ?? Enumerable.Empty<QuestRewardDefinition>())
            {
                if (reward == null || string.IsNullOrWhiteSpace(reward.Id))
                    throw new InvalidDataException($"任务 {definition.Id} 包含空奖励或空奖励 ID");
                reward.Id = reward.Id.Trim();
                if (!rewardIds.Add(reward.Id))
                    throw new InvalidDataException($"任务 {definition.Id} 的奖励 ID 重复：{reward.Id}");
                if (!QuestExtensionRegistry.TryGetReward(reward.Type, out IQuestRewardHandler handler))
                    throw new InvalidDataException($"任务 {definition.Id} 使用未知奖励类型：{reward.Type}");
                if (!handler.Validate(reward, out string error))
                    throw new InvalidDataException($"任务 {definition.Id} 奖励 {reward.Id} 无效：{error}");
            }
        }

        private static void ValidatePrerequisiteGraph()
        {
            var visiting = new HashSet<string>(IdComparer);
            var visited = new HashSet<string>(IdComparer);
            foreach (string questId in Definitions.Keys)
                VisitPrerequisites(questId, visiting, visited);
        }

        private static void VisitPrerequisites(
            string questId,
            HashSet<string> visiting,
            HashSet<string> visited)
        {
            if (visited.Contains(questId))
                return;
            if (!visiting.Add(questId))
                throw new InvalidDataException($"任务前置条件存在循环：{questId}");

            QuestDefinition definition = Definitions[questId];
            foreach (QuestConditionDefinition condition in definition.Conditions ?? Enumerable.Empty<QuestConditionDefinition>())
            {
                if (!string.Equals(condition.Type, QuestBuiltInTypes.QuestCompleted, StringComparison.OrdinalIgnoreCase))
                    continue;

                string prerequisiteId = condition.Parameters?.Value<string>("questId")?.Trim();
                if (string.IsNullOrWhiteSpace(prerequisiteId) || !Definitions.ContainsKey(prerequisiteId))
                    throw new InvalidDataException($"任务 {questId} 引用了不存在的前置任务：{prerequisiteId}");
                VisitPrerequisites(prerequisiteId, visiting, visited);
            }

            visiting.Remove(questId);
            visited.Add(questId);
        }

        private static bool IsMode(string value)
        {
            return string.Equals(value, QuestModes.Manual, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, QuestModes.Auto, StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}
