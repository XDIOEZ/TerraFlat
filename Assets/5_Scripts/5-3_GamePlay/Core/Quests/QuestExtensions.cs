using System;
using System.Collections.Generic;
using FlatWorld.Gameplay.Progress;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlatWorld.Gameplay.Quests
{
    #region 扩展契约

    /// <summary>任务目标处理器；事件型目标累加信号，状态型目标从玩家当前状态重新计算。</summary>
    public interface IQuestObjectiveHandler
    {
        string Type { get; }
        bool IsStateBased { get; }
        bool Validate(QuestObjectiveDefinition definition, out string error);
        float ApplySignal(QuestObjectiveDefinition definition, float current, GameplayProgressSignal signal);
        float EvaluateState(Player player, QuestObjectiveDefinition definition);
    }

    /// <summary>任务接取条件处理器。</summary>
    public interface IQuestConditionEvaluator
    {
        string Type { get; }
        bool Validate(QuestConditionDefinition definition, out string error);
        bool IsMet(PlayerQuestRuntime runtime, QuestConditionDefinition definition);
    }

    /// <summary>任务奖励处理器；只能准备奖励计划，不可在准备阶段产生外部副作用。</summary>
    public interface IQuestRewardHandler
    {
        string Type { get; }
        bool Validate(QuestRewardDefinition definition, out string error);
        bool TryPrepare(
            Player player,
            QuestRewardDefinition definition,
            QuestRewardPlan plan,
            out string error);
    }

    #endregion

    #region 注册表

    /// <summary>内建任务扩展类型 ID。</summary>
    public static class QuestBuiltInTypes
    {
        public const string SignalCount = "signal.count";
        public const string InventoryOwns = "inventory.owns";
        public const string QuestCompleted = "quest.completed";
        public const string ItemGrant = "item.grant";
        public const string SignalEmit = "signal.emit";
    }

    /// <summary>
    /// 任务扩展处理器注册表；新玩法只需注册处理器并在 JSON 中引用 type，无需修改任务运行时。
    /// 同一类型默认禁止覆盖，显式 replace 仅供受控热重载或兼容层使用。
    /// </summary>
    public static class QuestExtensionRegistry
    {
        private static readonly StringComparer TypeComparer = StringComparer.OrdinalIgnoreCase;
        private static readonly Dictionary<string, IQuestObjectiveHandler> Objectives = new(TypeComparer);
        private static readonly Dictionary<string, IQuestConditionEvaluator> Conditions = new(TypeComparer);
        private static readonly Dictionary<string, IQuestRewardHandler> Rewards = new(TypeComparer);
        private static bool builtInsRegistered;

        public static void EnsureBuiltInsRegistered()
        {
            if (builtInsRegistered)
                return;

            builtInsRegistered = true;
            RegisterObjective(new SignalCountObjectiveHandler());
            RegisterObjective(new InventoryOwnsObjectiveHandler());
            RegisterCondition(new QuestCompletedConditionEvaluator());
            RegisterReward(new ItemGrantRewardHandler());
            RegisterReward(new SignalEmitRewardHandler());
        }

        public static void RegisterObjective(IQuestObjectiveHandler handler, bool replace = false)
        {
            Register(Objectives, handler?.Type, handler, replace, "目标");
        }

        public static void RegisterCondition(IQuestConditionEvaluator handler, bool replace = false)
        {
            Register(Conditions, handler?.Type, handler, replace, "条件");
        }

        public static void RegisterReward(IQuestRewardHandler handler, bool replace = false)
        {
            Register(Rewards, handler?.Type, handler, replace, "奖励");
        }

        public static bool TryGetObjective(string type, out IQuestObjectiveHandler handler)
        {
            EnsureBuiltInsRegistered();
            return TryGet(Objectives, type, out handler);
        }

        public static bool TryGetCondition(string type, out IQuestConditionEvaluator handler)
        {
            EnsureBuiltInsRegistered();
            return TryGet(Conditions, type, out handler);
        }

        public static bool TryGetReward(string type, out IQuestRewardHandler handler)
        {
            EnsureBuiltInsRegistered();
            return TryGet(Rewards, type, out handler);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Objectives.Clear();
            Conditions.Clear();
            Rewards.Clear();
            builtInsRegistered = false;
        }

        private static void Register<T>(
            IDictionary<string, T> registry,
            string type,
            T handler,
            bool replace,
            string category)
            where T : class
        {
            if (handler == null || string.IsNullOrWhiteSpace(type))
                throw new ArgumentException($"任务{category}处理器及类型不能为空");

            string normalized = type.Trim();
            if (!replace && registry.ContainsKey(normalized))
                throw new InvalidOperationException($"任务{category}处理器重复注册：{normalized}");
            registry[normalized] = handler;
        }

        private static bool TryGet<T>(IDictionary<string, T> registry, string type, out T handler)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                handler = default;
                return false;
            }

            return registry.TryGetValue(type.Trim(), out handler);
        }
    }

    #endregion

    #region 奖励计划

    /// <summary>
    /// 一次交付的奖励计划；物品先在库存快照中整体预检并提交，信号则延迟到任务存档更新后发布。
    /// 这样背包空间不足时不会出现只发一半奖励或任务被误标完成。
    /// </summary>
    public sealed class QuestRewardPlan
    {
        private readonly List<ItemData> items = new();
        private readonly List<GameplayProgressSignal> signals = new();

        public void AddItem(ItemData itemData)
        {
            if (itemData == null)
                throw new ArgumentNullException(nameof(itemData));
            items.Add(itemData);
        }

        public void AddSignal(GameplayProgressSignal signal)
        {
            signals.Add(signal);
        }

        public bool TryCommit(
            Player player,
            out IReadOnlyList<GameplayProgressSignal> deferredSignals,
            out string error)
        {
            deferredSignals = signals.ToArray();
            error = null;
            if (items.Count == 0)
                return true;

            Inventory inventory = QuestInventoryAccess.FindRewardInventory(player);
            if (inventory == null)
            {
                error = "玩家没有可用的背包或快捷栏，无法发放任务物品奖励";
                return false;
            }

            if (!CraftingTransaction.TryCreateGrant(
                    inventory,
                    items,
                    out CraftingTransaction transaction,
                    out CraftingResult prepareFailure))
            {
                error = prepareFailure?.Message ?? "任务物品奖励预检失败";
                return false;
            }

            if (!transaction.Commit(out CraftingResult commitFailure))
            {
                error = commitFailure?.Message ?? "任务物品奖励提交失败";
                return false;
            }

            transaction.Complete();
            return true;
        }
    }

    #endregion

    #region 内建处理器

    internal sealed class SignalCountObjectiveHandler : IQuestObjectiveHandler
    {
        public string Type => QuestBuiltInTypes.SignalCount;
        public bool IsStateBased => false;

        public bool Validate(QuestObjectiveDefinition definition, out string error)
        {
            string eventType = definition.Parameters?.Value<string>("eventType");
            error = string.IsNullOrWhiteSpace(eventType) ? "parameters.eventType 不能为空" : null;
            return error == null;
        }

        public float ApplySignal(
            QuestObjectiveDefinition definition,
            float current,
            GameplayProgressSignal signal)
        {
            string eventType = definition.Parameters?.Value<string>("eventType")?.Trim();
            string targetId = definition.Parameters?.Value<string>("targetId")?.Trim();
            string dimensionId = definition.Parameters?.Value<string>("dimensionId")?.Trim();
            if (!string.Equals(eventType, signal.Type, StringComparison.OrdinalIgnoreCase))
                return current;
            if (!string.IsNullOrWhiteSpace(targetId) &&
                !string.Equals(targetId, signal.TargetId, StringComparison.OrdinalIgnoreCase))
            {
                return current;
            }
            if (!string.IsNullOrWhiteSpace(dimensionId) &&
                !string.Equals(
                    dimensionId,
                    signal.Payload?.Value<string>(GameplayProgressEvents.DimensionIdPayloadKey),
                    StringComparison.OrdinalIgnoreCase))
            {
                return current;
            }

            return Mathf.Min(definition.Required, current + Mathf.Max(0f, signal.Amount));
        }

        public float EvaluateState(Player player, QuestObjectiveDefinition definition)
        {
            return 0f;
        }
    }

    internal sealed class InventoryOwnsObjectiveHandler : IQuestObjectiveHandler
    {
        public string Type => QuestBuiltInTypes.InventoryOwns;
        public bool IsStateBased => true;

        public bool Validate(QuestObjectiveDefinition definition, out string error)
        {
            string itemId = definition.Parameters?.Value<string>("itemId");
            error = string.IsNullOrWhiteSpace(itemId) ? "parameters.itemId 不能为空" : null;
            return error == null;
        }

        public float ApplySignal(
            QuestObjectiveDefinition definition,
            float current,
            GameplayProgressSignal signal)
        {
            return current;
        }

        public float EvaluateState(Player player, QuestObjectiveDefinition definition)
        {
            string itemId = definition.Parameters?.Value<string>("itemId")?.Trim();
            return Mathf.Min(definition.Required, QuestInventoryAccess.CountItem(player, itemId));
        }
    }

    internal sealed class QuestCompletedConditionEvaluator : IQuestConditionEvaluator
    {
        public string Type => QuestBuiltInTypes.QuestCompleted;

        public bool Validate(QuestConditionDefinition definition, out string error)
        {
            string questId = definition.Parameters?.Value<string>("questId");
            error = string.IsNullOrWhiteSpace(questId) ? "parameters.questId 不能为空" : null;
            return error == null;
        }

        public bool IsMet(PlayerQuestRuntime runtime, QuestConditionDefinition definition)
        {
            string questId = definition.Parameters?.Value<string>("questId")?.Trim();
            return runtime != null && runtime.IsCompleted(questId);
        }
    }

    internal sealed class ItemGrantRewardHandler : IQuestRewardHandler
    {
        public string Type => QuestBuiltInTypes.ItemGrant;

        public bool Validate(QuestRewardDefinition definition, out string error)
        {
            string itemId = definition.Parameters?.Value<string>("itemId");
            float amount = definition.Parameters?.Value<float?>("amount") ?? 1f;
            if (string.IsNullOrWhiteSpace(itemId))
                error = "parameters.itemId 不能为空";
            else if (amount <= 0f)
                error = "parameters.amount 必须大于 0";
            else
                error = null;
            return error == null;
        }

        public bool TryPrepare(
            Player player,
            QuestRewardDefinition definition,
            QuestRewardPlan plan,
            out string error)
        {
            string itemId = definition.Parameters.Value<string>("itemId")?.Trim();
            float amount = definition.Parameters.Value<float?>("amount") ?? 1f;
            try
            {
                ItemData itemData = GameRes.Instance?.CreateItemData(itemId);
                if (itemData?.Stack == null)
                {
                    error = $"无法创建奖励物品：{itemId}";
                    return false;
                }

                itemData.Stack.Amount = amount;
                plan.AddItem(itemData);
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                error = $"创建奖励物品 {itemId} 失败：{exception.Message}";
                return false;
            }
        }
    }

    internal sealed class SignalEmitRewardHandler : IQuestRewardHandler
    {
        public string Type => QuestBuiltInTypes.SignalEmit;

        public bool Validate(QuestRewardDefinition definition, out string error)
        {
            string eventType = definition.Parameters?.Value<string>("eventType");
            float amount = definition.Parameters?.Value<float?>("amount") ?? 1f;
            if (string.IsNullOrWhiteSpace(eventType))
                error = "parameters.eventType 不能为空";
            else if (amount <= 0f)
                error = "parameters.amount 必须大于 0";
            else
                error = null;
            return error == null;
        }

        public bool TryPrepare(
            Player player,
            QuestRewardDefinition definition,
            QuestRewardPlan plan,
            out string error)
        {
            string eventType = definition.Parameters.Value<string>("eventType")?.Trim();
            string targetId = definition.Parameters.Value<string>("targetId")?.Trim();
            float amount = definition.Parameters.Value<float?>("amount") ?? 1f;
            JObject payload = definition.Parameters["payload"] as JObject;
            plan.AddSignal(new GameplayProgressSignal(
                player,
                eventType,
                targetId,
                amount,
                payload != null ? (JObject)payload.DeepClone() : null));
            error = null;
            return true;
        }
    }

    #endregion

    #region 库存适配

    /// <summary>任务系统访问玩家背包的集中适配层，避免目标与奖励处理器各自猜测库存结构。</summary>
    internal static class QuestInventoryAccess
    {
        public static float CountItem(Player player, string itemId)
        {
            if (player?.itemMods == null || string.IsNullOrWhiteSpace(itemId))
                return 0f;

            float total = 0f;
            var visited = new HashSet<Inventory_Data>();
            Mod_Inventory bag = player.itemMods.GetMod_ByID<Mod_Inventory>(ModText.Bag);
            if (bag?.InventoryInstances != null)
            {
                foreach (Inventory inventory in bag.InventoryInstances)
                    total += CountItem(inventory, itemId, visited);
            }

            Inventory_HotBar hotbar = player.itemMods.GetMod_ByID<Inventory_HotBar>(ModText.Hotbar);
            total += CountItem(hotbar?.RuntimeInventory, itemId, visited);
            return total;
        }

        public static Inventory FindRewardInventory(Player player)
        {
            if (player?.itemMods == null)
                return null;

            Mod_Inventory bag = player.itemMods.GetMod_ByID<Mod_Inventory>(ModText.Bag);
            if (bag?.InventoryInstances != null)
            {
                foreach (Inventory inventory in bag.InventoryInstances)
                {
                    if (IsUsable(inventory))
                        return inventory;
                }
            }

            Inventory_HotBar hotbar = player.itemMods.GetMod_ByID<Inventory_HotBar>(ModText.Hotbar);
            return IsUsable(hotbar?.RuntimeInventory) ? hotbar.RuntimeInventory : null;
        }

        private static float CountItem(
            Inventory inventory,
            string itemId,
            ISet<Inventory_Data> visited)
        {
            if (!IsUsable(inventory) || !visited.Add(inventory.Data))
                return 0f;

            float total = 0f;
            foreach (ItemSlot slot in inventory.Data.itemSlots)
            {
                ItemData itemData = slot?.itemData;
                if (itemData?.Stack != null &&
                    string.Equals(itemData.IDName, itemId, StringComparison.OrdinalIgnoreCase))
                {
                    total += itemData.Stack.Amount;
                }
            }

            return total;
        }

        private static bool IsUsable(Inventory inventory)
        {
            return inventory?.Data?.itemSlots != null &&
                   inventory.Data.itemSlots.TrueForAll(slot => slot != null);
        }
    }

    #endregion
}
