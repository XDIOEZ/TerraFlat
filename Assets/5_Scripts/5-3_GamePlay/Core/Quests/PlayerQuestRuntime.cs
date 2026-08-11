using System;
using System.Collections.Generic;
using System.Linq;
using FlatWorld.Gameplay.Progress;
using UnityEngine;

namespace FlatWorld.Gameplay.Quests
{
    /// <summary>
    /// 单个本地玩家的任务运行时；负责接取、信号推进、状态目标刷新、阶段切换、原子奖励和进度保存。
    /// 运行时不使用 Update 轮询，只有玩家进入、统一玩法信号或外部显式 Refresh 时才重新计算。
    /// </summary>
    public sealed class PlayerQuestRuntime
    {
        #region 字段与事件

        private const int MaximumStabilizationRounds = 128;
        private readonly Player player;
        private readonly Queue<GameplayProgressSignal> pendingSignals = new();
        private QuestProgressSaveDocument document;
        private bool processingSignals;

        public Player Player => player;
        public bool IsEnabled { get; private set; }
        public event Action<QuestSnapshot> QuestChanged;

        #endregion

        #region 生命周期

        public PlayerQuestRuntime(Player player)
        {
            this.player = player ?? throw new ArgumentNullException(nameof(player));
        }

        public bool Initialize(out string error)
        {
            error = null;
            if (!QuestCatalog.IsReady)
            {
                error = "任务目录尚未完成加载";
                return false;
            }
            if (player.Data == null)
            {
                error = "玩家数据为空";
                return false;
            }

            try
            {
                document = QuestProgressStore.Load(player.Data);
                bool changed = NormalizeKnownRecords();
                var changedQuestIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var deferredSignals = new List<GameplayProgressSignal>();
                changed |= Stabilize(changedQuestIds, deferredSignals);
                if (changed)
                    QuestProgressStore.Save(player.Data, document);

                IsEnabled = true;
                NotifyChanged(changedQuestIds);
                PublishDeferredSignals(deferredSignals);
                return true;
            }
            catch (Exception exception)
            {
                IsEnabled = false;
                error = exception.Message;
                Debug.LogError($"[Quest] 玩家任务进度初始化失败，已禁用该玩家任务运行时：{exception.Message}");
                Debug.LogException(exception);
                return false;
            }
        }

        public void Dispose()
        {
            IsEnabled = false;
            pendingSignals.Clear();
            QuestChanged = null;
        }

        #endregion

        #region 公共操作

        public bool AcceptQuest(string questId, out string error)
        {
            error = null;
            if (!CanMutate(out error))
                return false;
            if (!QuestCatalog.TryGet(questId, out QuestDefinition definition))
            {
                error = $"任务不存在：{questId}";
                return false;
            }
            if (document.Quests.ContainsKey(definition.Id))
            {
                error = $"任务已经接取或完成：{definition.Id}";
                return false;
            }
            if (!AreConditionsMet(definition))
            {
                error = $"任务接取条件尚未满足：{definition.Id}";
                return false;
            }

            var changedQuestIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var deferredSignals = new List<GameplayProgressSignal>();
            AddActiveRecord(definition);
            changedQuestIds.Add(definition.Id);
            Stabilize(changedQuestIds, deferredSignals);
            QuestProgressStore.Save(player.Data, document);
            NotifyChanged(changedQuestIds);
            PublishDeferredSignals(deferredSignals);
            return true;
        }

        public bool ClaimQuest(string questId, out string error)
        {
            error = null;
            if (!CanMutate(out error))
                return false;
            if (!QuestCatalog.TryGet(questId, out QuestDefinition definition) ||
                !document.Quests.TryGetValue(definition.Id, out QuestProgressSaveRecord record))
            {
                error = $"任务不存在或尚未接取：{questId}";
                return false;
            }

            var deferredSignals = new List<GameplayProgressSignal>();
            if (!TryClaimInternal(definition, record, deferredSignals, out error))
                return false;

            var changedQuestIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                definition.Id
            };
            Stabilize(changedQuestIds, deferredSignals);
            QuestProgressStore.Save(player.Data, document);
            NotifyChanged(changedQuestIds);
            PublishDeferredSignals(deferredSignals);
            return true;
        }

        public void Refresh()
        {
            if (!IsEnabled)
                return;

            var changedQuestIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var deferredSignals = new List<GameplayProgressSignal>();
            if (!Stabilize(changedQuestIds, deferredSignals))
                return;

            QuestProgressStore.Save(player.Data, document);
            NotifyChanged(changedQuestIds);
            PublishDeferredSignals(deferredSignals);
        }

        public void HandleSignal(GameplayProgressSignal signal)
        {
            if (!IsEnabled || signal.Actor != player)
                return;

            pendingSignals.Enqueue(signal);
            if (processingSignals)
                return;

            processingSignals = true;
            try
            {
                int processed = 0;
                while (pendingSignals.Count > 0 && processed++ < MaximumStabilizationRounds)
                    ProcessSignal(pendingSignals.Dequeue());

                if (pendingSignals.Count > 0)
                {
                    pendingSignals.Clear();
                    Debug.LogError("[Quest] 奖励信号链超过安全上限，剩余信号已丢弃；请检查任务配置是否形成循环");
                }
            }
            finally
            {
                processingSignals = false;
            }
        }

        public bool IsCompleted(string questId)
        {
            return !string.IsNullOrWhiteSpace(questId) &&
                   document?.Quests != null &&
                   document.Quests.TryGetValue(questId, out QuestProgressSaveRecord record) &&
                   record?.Status == QuestStatus.Completed;
        }

        public bool TryGetSnapshot(string questId, out QuestSnapshot snapshot)
        {
            snapshot = null;
            if (!QuestCatalog.TryGet(questId, out QuestDefinition definition) ||
                document?.Quests == null ||
                !document.Quests.TryGetValue(definition.Id, out QuestProgressSaveRecord record) ||
                record == null)
            {
                return false;
            }

            snapshot = CreateSnapshot(definition, record);
            return true;
        }

        public IReadOnlyList<QuestSnapshot> GetSnapshots()
        {
            if (document?.Quests == null)
                return Array.Empty<QuestSnapshot>();

            var snapshots = new List<QuestSnapshot>();
            foreach (QuestDefinition definition in QuestCatalog.All.OrderBy(value => value.Id, StringComparer.OrdinalIgnoreCase))
            {
                if (document.Quests.TryGetValue(definition.Id, out QuestProgressSaveRecord record) && record != null)
                    snapshots.Add(CreateSnapshot(definition, record));
            }

            return snapshots;
        }

        #endregion

        #region 信号与稳定化

        private void ProcessSignal(GameplayProgressSignal signal)
        {
            var changedQuestIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var deferredSignals = new List<GameplayProgressSignal>();
            bool changed = false;

            foreach (KeyValuePair<string, QuestProgressSaveRecord> pair in document.Quests.ToArray())
            {
                QuestProgressSaveRecord record = pair.Value;
                if (record?.Status != QuestStatus.Active ||
                    !QuestCatalog.TryGet(pair.Key, out QuestDefinition definition) ||
                    !TryGetCurrentStage(definition, record, out QuestStageDefinition stage))
                {
                    continue;
                }

                foreach (QuestObjectiveDefinition objective in stage.Objectives)
                {
                    if (!QuestExtensionRegistry.TryGetObjective(objective.Type, out IQuestObjectiveHandler handler) ||
                        handler.IsStateBased)
                    {
                        continue;
                    }

                    string key = GetObjectiveKey(stage.Id, objective.Id);
                    record.ObjectiveProgress.TryGetValue(key, out float current);
                    float next = Mathf.Clamp(handler.ApplySignal(objective, current, signal), 0f, objective.Required);
                    if (Mathf.Approximately(current, next))
                        continue;

                    record.ObjectiveProgress[key] = next;
                    changed = true;
                    changedQuestIds.Add(definition.Id);
                }
            }

            changed |= Stabilize(changedQuestIds, deferredSignals);
            if (!changed)
                return;

            QuestProgressStore.Save(player.Data, document);
            NotifyChanged(changedQuestIds);
            PublishDeferredSignals(deferredSignals);
        }

        private bool Stabilize(
            ISet<string> changedQuestIds,
            ICollection<GameplayProgressSignal> deferredSignals)
        {
            bool anyChanged = false;
            for (int round = 0; round < MaximumStabilizationRounds; round++)
            {
                bool roundChanged = AutoAcceptAvailable(changedQuestIds);
                roundChanged |= RefreshStateObjectives(changedQuestIds);
                roundChanged |= AdvanceCompletedStages(changedQuestIds);
                roundChanged |= AutoClaimReadyQuests(changedQuestIds, deferredSignals);
                anyChanged |= roundChanged;
                if (!roundChanged)
                    return anyChanged;
            }

            throw new InvalidOperationException("任务状态稳定化超过安全上限，请检查任务前置或自动奖励链配置");
        }

        private bool AutoAcceptAvailable(ISet<string> changedQuestIds)
        {
            bool changed = false;
            foreach (QuestDefinition definition in QuestCatalog.All)
            {
                if (definition.DebugOnly ||
                    !string.Equals(definition.AcceptMode, QuestModes.Auto, StringComparison.OrdinalIgnoreCase) ||
                    document.Quests.ContainsKey(definition.Id) ||
                    !AreConditionsMet(definition))
                {
                    continue;
                }

                AddActiveRecord(definition);
                changedQuestIds.Add(definition.Id);
                changed = true;
            }

            return changed;
        }

        private bool RefreshStateObjectives(ISet<string> changedQuestIds)
        {
            bool changed = false;
            foreach (KeyValuePair<string, QuestProgressSaveRecord> pair in document.Quests)
            {
                QuestProgressSaveRecord record = pair.Value;
                if (record?.Status != QuestStatus.Active ||
                    !QuestCatalog.TryGet(pair.Key, out QuestDefinition definition) ||
                    !TryGetCurrentStage(definition, record, out QuestStageDefinition stage))
                {
                    continue;
                }

                foreach (QuestObjectiveDefinition objective in stage.Objectives)
                {
                    if (!QuestExtensionRegistry.TryGetObjective(objective.Type, out IQuestObjectiveHandler handler) ||
                        !handler.IsStateBased)
                    {
                        continue;
                    }

                    string key = GetObjectiveKey(stage.Id, objective.Id);
                    record.ObjectiveProgress.TryGetValue(key, out float current);
                    float next = Mathf.Clamp(handler.EvaluateState(player, objective), 0f, objective.Required);
                    if (Mathf.Approximately(current, next))
                        continue;

                    record.ObjectiveProgress[key] = next;
                    changedQuestIds.Add(definition.Id);
                    changed = true;
                }
            }

            return changed;
        }

        private bool AdvanceCompletedStages(ISet<string> changedQuestIds)
        {
            bool changed = false;
            foreach (KeyValuePair<string, QuestProgressSaveRecord> pair in document.Quests)
            {
                QuestProgressSaveRecord record = pair.Value;
                if (record?.Status != QuestStatus.Active ||
                    !QuestCatalog.TryGet(pair.Key, out QuestDefinition definition))
                {
                    continue;
                }

                int guard = definition.Stages.Count;
                while (record.Status == QuestStatus.Active && guard-- > 0 &&
                       TryGetCurrentStage(definition, record, out QuestStageDefinition stage) &&
                       IsStageCompleted(stage, record))
                {
                    int stageIndex = definition.Stages.IndexOf(stage);
                    if (stageIndex + 1 < definition.Stages.Count)
                        record.CurrentStageId = definition.Stages[stageIndex + 1].Id;
                    else
                        record.Status = QuestStatus.ReadyToClaim;

                    changedQuestIds.Add(definition.Id);
                    changed = true;
                }
            }

            return changed;
        }

        private bool AutoClaimReadyQuests(
            ISet<string> changedQuestIds,
            ICollection<GameplayProgressSignal> deferredSignals)
        {
            bool changed = false;
            foreach (KeyValuePair<string, QuestProgressSaveRecord> pair in document.Quests.ToArray())
            {
                QuestProgressSaveRecord record = pair.Value;
                if (record?.Status != QuestStatus.ReadyToClaim ||
                    !QuestCatalog.TryGet(pair.Key, out QuestDefinition definition) ||
                    !string.Equals(definition.TurnInMode, QuestModes.Auto, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var signals = new List<GameplayProgressSignal>();
                if (!TryClaimInternal(definition, record, signals, out _))
                    continue;

                foreach (GameplayProgressSignal signal in signals)
                    deferredSignals.Add(signal);
                changedQuestIds.Add(definition.Id);
                changed = true;
            }

            return changed;
        }

        #endregion

        #region 状态变更

        private bool NormalizeKnownRecords()
        {
            bool changed = false;
            foreach (KeyValuePair<string, QuestProgressSaveRecord> pair in document.Quests.ToArray())
            {
                if (!QuestCatalog.TryGet(pair.Key, out QuestDefinition definition) || pair.Value == null)
                    continue;

                QuestProgressSaveRecord record = pair.Value;
                record.ObjectiveProgress ??= new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
                if (record.Status == QuestStatus.Completed)
                {
                    if (record.DefinitionVersion != definition.DefinitionVersion || !record.RewardsClaimed)
                    {
                        record.DefinitionVersion = definition.DefinitionVersion;
                        record.RewardsClaimed = true;
                        changed = true;
                    }
                    continue;
                }

                bool missingStage = !definition.Stages.Any(stage =>
                    string.Equals(stage.Id, record.CurrentStageId, StringComparison.OrdinalIgnoreCase));
                if (record.DefinitionVersion != definition.DefinitionVersion || missingStage)
                {
                    record.DefinitionVersion = definition.DefinitionVersion;
                    record.Status = QuestStatus.Active;
                    record.CurrentStageId = definition.Stages[0].Id;
                    record.ObjectiveProgress.Clear();
                    record.RewardsClaimed = false;
                    changed = true;
                }
            }

            return changed;
        }

        private void AddActiveRecord(QuestDefinition definition)
        {
            document.Quests.Add(definition.Id, new QuestProgressSaveRecord
            {
                DefinitionVersion = definition.DefinitionVersion,
                Status = QuestStatus.Active,
                CurrentStageId = definition.Stages[0].Id,
                ObjectiveProgress = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
            });
        }

        private bool TryClaimInternal(
            QuestDefinition definition,
            QuestProgressSaveRecord record,
            ICollection<GameplayProgressSignal> deferredSignals,
            out string error)
        {
            error = null;
            if (record.Status != QuestStatus.ReadyToClaim)
            {
                error = $"任务尚未达到交付状态：{definition.Id}";
                return false;
            }
            if (record.RewardsClaimed)
            {
                error = $"任务奖励已经领取：{definition.Id}";
                return false;
            }

            var plan = new QuestRewardPlan();
            foreach (QuestRewardDefinition reward in definition.Rewards ?? Enumerable.Empty<QuestRewardDefinition>())
            {
                if (!QuestExtensionRegistry.TryGetReward(reward.Type, out IQuestRewardHandler handler) ||
                    !handler.TryPrepare(player, reward, plan, out error))
                {
                    error ??= $"任务奖励处理器不可用：{reward.Type}";
                    return false;
                }
            }

            if (!plan.TryCommit(player, out IReadOnlyList<GameplayProgressSignal> signals, out error))
                return false;

            record.Status = QuestStatus.Completed;
            record.RewardsClaimed = true;
            record.CompletionCount++;
            foreach (GameplayProgressSignal signal in signals)
                deferredSignals.Add(signal);
            return true;
        }

        private bool AreConditionsMet(QuestDefinition definition)
        {
            foreach (QuestConditionDefinition condition in definition.Conditions ?? Enumerable.Empty<QuestConditionDefinition>())
            {
                if (!QuestExtensionRegistry.TryGetCondition(condition.Type, out IQuestConditionEvaluator evaluator) ||
                    !evaluator.IsMet(this, condition))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryGetCurrentStage(
            QuestDefinition definition,
            QuestProgressSaveRecord record,
            out QuestStageDefinition stage)
        {
            stage = definition.Stages.FirstOrDefault(value =>
                string.Equals(value.Id, record.CurrentStageId, StringComparison.OrdinalIgnoreCase));
            return stage != null;
        }

        private static bool IsStageCompleted(QuestStageDefinition stage, QuestProgressSaveRecord record)
        {
            bool any = string.Equals(stage.CompletionMode, QuestCompletionModes.Any, StringComparison.OrdinalIgnoreCase);
            foreach (QuestObjectiveDefinition objective in stage.Objectives)
            {
                record.ObjectiveProgress.TryGetValue(GetObjectiveKey(stage.Id, objective.Id), out float value);
                bool completed = value + 0.0001f >= objective.Required;
                if (any && completed)
                    return true;
                if (!any && !completed)
                    return false;
            }

            return !any;
        }

        #endregion

        #region 快照与通知

        private static string GetObjectiveKey(string stageId, string objectiveId)
        {
            return $"{stageId}/{objectiveId}";
        }

        private static QuestSnapshot CreateSnapshot(
            QuestDefinition definition,
            QuestProgressSaveRecord record)
        {
            return new QuestSnapshot
            {
                QuestId = definition.Id,
                Title = string.IsNullOrWhiteSpace(definition.Title) ? definition.Id : definition.Title,
                Status = record.Status,
                CurrentStageId = record.CurrentStageId,
                ObjectiveProgress = new Dictionary<string, float>(
                    record.ObjectiveProgress,
                    StringComparer.OrdinalIgnoreCase)
            };
        }

        private bool CanMutate(out string error)
        {
            if (!IsEnabled || document == null)
            {
                error = "玩家任务运行时尚未初始化";
                return false;
            }

            error = null;
            return true;
        }

        private void NotifyChanged(IEnumerable<string> questIds)
        {
            if (QuestChanged == null)
                return;

            foreach (string questId in questIds)
            {
                if (!TryGetSnapshot(questId, out QuestSnapshot snapshot))
                    continue;

                foreach (Delegate callback in QuestChanged.GetInvocationList())
                {
                    try
                    {
                        ((Action<QuestSnapshot>)callback)(snapshot);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                    }
                }
            }
        }

        private static void PublishDeferredSignals(IEnumerable<GameplayProgressSignal> signals)
        {
            foreach (GameplayProgressSignal signal in signals)
                GameplayProgressEvents.PublishSignal(signal);
        }

        #endregion
    }
}
