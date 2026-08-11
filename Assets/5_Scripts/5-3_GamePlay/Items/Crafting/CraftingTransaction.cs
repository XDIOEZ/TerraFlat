using System;
using System.Collections.Generic;
using FastCloner.Code;
using UnityEngine;

/// <summary>
/// 制作库存事务：先在快照上完成扣料和全部产物放置，全部成功后一次提交。
/// </summary>
public sealed class CraftingTransaction
{
    private readonly List<InventoryState> states;
    private bool committed;

    private CraftingTransaction(List<InventoryState> states)
    {
        this.states = states;
    }

    public static bool TryCreate(
        Inventory inputInventory,
        Inventory outputInventory,
        CraftingRecipeMatch match,
        IReadOnlyList<ItemData> outputs,
        bool allowOutputIntoInput,
        out CraftingTransaction transaction,
        out CraftingResult failure)
    {
        transaction = null;
        failure = null;
        if (inputInventory?.Data?.itemSlots == null || outputInventory?.Data?.itemSlots == null || match == null)
        {
            failure = CraftingResult.Failed(CraftingFailureReason.InvalidInventory, "制作事务缺少有效库存或配方匹配");
            return false;
        }
        if (inputInventory.Data.itemSlots.Exists(slot => slot == null) ||
            outputInventory.Data.itemSlots.Exists(slot => slot == null))
        {
            failure = CraftingResult.Failed(CraftingFailureReason.InvalidInventory, "制作库存包含空槽位引用");
            return false;
        }

        var states = new List<InventoryState>();
        InventoryState inputState = GetOrCreateState(states, inputInventory);
        InventoryState outputState = GetOrCreateState(states, outputInventory);

        foreach (CraftingConsumption consumption in match.Consumptions)
        {
            if (!inputState.TryConsume(consumption.SlotIndex, consumption.Amount))
            {
                failure = CraftingResult.Failed(CraftingFailureReason.MissingMaterials, "输入材料在事务预检时不足", match.Recipe);
                return false;
            }
        }

        foreach (ItemData output in outputs)
        {
            if (outputState.TryAdd(output))
                continue;
            if (allowOutputIntoInput && !ReferenceEquals(inputState, outputState) && inputState.TryAdd(output))
                continue;

            failure = CraftingResult.Failed(
                CraftingFailureReason.OutputSpaceInsufficient,
                $"没有足够空间容纳全部产物：{output?.IDName}",
                match.Recipe);
            return false;
        }

        transaction = new CraftingTransaction(states);
        return true;
    }

    #region 通用原子发放

    /// <summary>
    /// 创建只增加物品、不扣除材料的原子库存事务；任务奖励等系统可复用制作系统已验证的快照提交能力。
    /// 任意一个物品无法完整放入时整笔事务失败，调用方不会看到部分奖励。
    /// </summary>
    public static bool TryCreateGrant(
        Inventory outputInventory,
        IReadOnlyList<ItemData> outputs,
        out CraftingTransaction transaction,
        out CraftingResult failure)
    {
        transaction = null;
        failure = null;
        if (outputInventory?.Data?.itemSlots == null || outputs == null || outputs.Count == 0)
        {
            failure = CraftingResult.Failed(
                CraftingFailureReason.InvalidInventory,
                "物品发放事务缺少有效库存或产物");
            return false;
        }
        if (outputInventory.Data.itemSlots.Exists(slot => slot == null))
        {
            failure = CraftingResult.Failed(
                CraftingFailureReason.InvalidInventory,
                "物品发放库存包含空槽位引用");
            return false;
        }

        var states = new List<InventoryState>();
        InventoryState outputState = GetOrCreateState(states, outputInventory);
        foreach (ItemData output in outputs)
        {
            if (outputState.TryAdd(output))
                continue;

            failure = CraftingResult.Failed(
                CraftingFailureReason.OutputSpaceInsufficient,
                $"没有足够空间容纳全部发放物品：{output?.IDName}");
            return false;
        }

        transaction = new CraftingTransaction(states);
        return true;
    }

    #endregion

    public bool Commit(out CraftingResult failure)
    {
        failure = null;
        if (committed)
        {
            failure = CraftingResult.Failed(CraftingFailureReason.CommitFailed, "制作事务已提交");
            return false;
        }

        foreach (InventoryState state in states)
        {
            if (!state.IsCurrentShapeValid())
            {
                failure = CraftingResult.Failed(CraftingFailureReason.InventoryChanged, "制作期间库存结构发生变化");
                return false;
            }
        }

        try
        {
            foreach (InventoryState state in states)
                state.ApplyWorkingState();
            committed = true;
            return true;
        }
        catch (Exception exception)
        {
            foreach (InventoryState state in states)
                state.RestoreOriginalState();
            foreach (InventoryState state in states)
                state.NotifyChanged();

            Debug.LogException(exception);
            failure = CraftingResult.Failed(
                CraftingFailureReason.CommitFailed,
                $"制作库存提交失败，已恢复原始状态：{exception.Message}");
            return false;
        }
    }

    public void Complete()
    {
        if (!committed)
            return;
        foreach (InventoryState state in states)
            state.NotifyChanged();
    }

    public void Rollback()
    {
        if (!committed)
            return;
        foreach (InventoryState state in states)
            state.RestoreOriginalState();
        foreach (InventoryState state in states)
            state.NotifyChanged();
        committed = false;
    }

    private static InventoryState GetOrCreateState(List<InventoryState> states, Inventory inventory)
    {
        foreach (InventoryState state in states)
        {
            if (ReferenceEquals(state.Inventory, inventory))
                return state;
        }

        var created = new InventoryState(inventory);
        states.Add(created);
        return created;
    }

    private sealed class InventoryState
    {
        private readonly List<ItemData> originalItems = new List<ItemData>();
        private readonly List<ItemData> workingItems = new List<ItemData>();

        public InventoryState(Inventory inventory)
        {
            Inventory = inventory;
            foreach (ItemSlot slot in inventory.Data.itemSlots)
            {
                originalItems.Add(Clone(slot?.itemData));
                workingItems.Add(Clone(slot?.itemData));
            }
        }

        public Inventory Inventory { get; }

        public bool IsCurrentShapeValid()
        {
            return Inventory?.Data?.itemSlots != null && Inventory.Data.itemSlots.Count == workingItems.Count;
        }

        public bool TryConsume(int slotIndex, float amount)
        {
            if (amount <= 0f)
                return true;
            if (slotIndex < 0 || slotIndex >= workingItems.Count)
                return false;

            ItemData itemData = workingItems[slotIndex];
            if (itemData?.Stack == null || itemData.Stack.Amount + 0.0001f < amount)
                return false;

            itemData.Stack.Amount -= amount;
            if (itemData.Stack.Amount <= 0.0001f)
                workingItems[slotIndex] = null;
            return true;
        }

        public bool TryAdd(ItemData source)
        {
            if (source?.Stack == null || source.Stack.Amount <= 0f)
                return false;

            List<ItemData> snapshot = CloneItems(workingItems);
            if (TryAddCore(source))
                return true;

            workingItems.Clear();
            workingItems.AddRange(snapshot);
            return false;
        }

        private bool TryAddCore(ItemData source)
        {

            float unitVolume = source.Stack.Volume > 0f ? source.Stack.Volume : 1f;
            float remaining = source.Stack.Amount;
            if (unitVolume > 1f)
            {
                for (int i = 0; i < workingItems.Count; i++)
                {
                    if (workingItems[i] != null || Inventory.Data.itemSlots[i].SlotMaxVolume < unitVolume)
                        continue;
                    workingItems[i] = Clone(source);
                    return true;
                }
                return false;
            }

            for (int i = 0; i < workingItems.Count && remaining > 0.0001f; i++)
            {
                ItemData target = workingItems[i];
                if (!CanStack(target, source))
                    continue;

                float availableVolume = Mathf.Max(0f, Inventory.Data.itemSlots[i].SlotMaxVolume - target.Stack.CurrentVolume);
                float amountCapacity = availableVolume / unitVolume;
                float amountToAdd = Mathf.Min(remaining, amountCapacity);
                if (amountToAdd <= 0f)
                    continue;
                target.Stack.Amount += amountToAdd;
                remaining -= amountToAdd;
            }

            for (int i = 0; i < workingItems.Count && remaining > 0.0001f; i++)
            {
                if (workingItems[i] != null)
                    continue;

                float amountCapacity = Inventory.Data.itemSlots[i].SlotMaxVolume / unitVolume;
                float amountToAdd = Mathf.Min(remaining, amountCapacity);
                if (amountToAdd <= 0f)
                    continue;

                ItemData created = Clone(source);
                created.Stack.Amount = amountToAdd;
                created.Stack.CanBePickedUp = false;
                workingItems[i] = created;
                remaining -= amountToAdd;
            }

            return remaining <= 0.0001f;
        }

        public void ApplyWorkingState()
        {
            Apply(workingItems);
        }

        public void RestoreOriginalState()
        {
            Apply(originalItems);
        }

        public void NotifyChanged()
        {
            for (int i = 0; i < Inventory.Data.itemSlots.Count; i++)
            {
                ItemSlot slot = Inventory.Data.itemSlots[i];
                Inventory.Data.Event_RefreshUI?.Invoke(i);
                Inventory.Data.Event_OnDataChanged?.Invoke(slot);
                slot?.RefreshUI();
            }
            Inventory.RefreshUI();
        }

        private void Apply(IReadOnlyList<ItemData> source)
        {
            for (int i = 0; i < source.Count; i++)
            {
                ItemSlot slot = Inventory.Data.itemSlots[i];
                Inventory.Data.Event_OnBeforeDataChanged?.Invoke(slot);
                slot.itemData = Clone(source[i]);
            }
        }

        private static bool CanStack(ItemData target, ItemData source)
        {
            return target?.Stack != null && source?.Stack != null &&
                   target.Stack.Volume <= 1f &&
                   string.Equals(target.IDName, source.IDName, StringComparison.Ordinal) &&
                   string.Equals(target.ItemSpecialData, source.ItemSpecialData, StringComparison.Ordinal);
        }

        private static ItemData Clone(ItemData itemData)
        {
            return itemData == null ? null : FastCloner.FastCloner.DeepClone(itemData);
        }

        private static List<ItemData> CloneItems(IReadOnlyList<ItemData> source)
        {
            var result = new List<ItemData>(source.Count);
            for (int i = 0; i < source.Count; i++)
                result.Add(Clone(source[i]));
            return result;
        }
    }
}
