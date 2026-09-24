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
        return TryBuild(
            inputInventory,
            outputInventory,
            match,
            outputs,
            allowOutputIntoInput,
            true,
            true,
            out transaction,
            out failure);
    }

    /// <summary>只模拟扣料与全部产物放置，不保留回滚快照，也不创建可提交事务。</summary>
    public static bool CanCreate(
        Inventory inputInventory,
        Inventory outputInventory,
        CraftingRecipeMatch match,
        IReadOnlyList<ItemData> outputs,
        bool allowOutputIntoInput,
        out CraftingResult failure)
    {
        return TryBuild(
            inputInventory,
            outputInventory,
            match,
            outputs,
            allowOutputIntoInput,
            true,
            false,
            out _,
            out failure);
    }

    /// <summary>只扣除输入材料并保留可回滚事务，适用于把结果写入输入物品自身状态的加工。</summary>
    public static bool TryCreateInputOnly(
        Inventory inputInventory,
        CraftingRecipeMatch match,
        out CraftingTransaction transaction,
        out CraftingResult failure)
    {
        return TryBuild(
            inputInventory,
            inputInventory,
            match,
            Array.Empty<ItemData>(),
            true,
            false,
            true,
            out transaction,
            out failure);
    }

    /// <summary>构建制作快照；预览仅保留工作态，正式提交同时保留原始态。</summary>
    private static bool TryBuild(
        Inventory inputInventory,
        Inventory outputInventory,
        CraftingRecipeMatch match,
        IReadOnlyList<ItemData> outputs,
        bool allowOutputIntoInput,
        bool requireOutput,
        bool retainTransaction,
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
        if (outputs == null || (requireOutput && outputs.Count == 0))
        {
            failure = CraftingResult.Failed(CraftingFailureReason.InvalidOutput, "制作事务没有有效产物", match.Recipe);
            return false;
        }
        if (inputInventory.Data.itemSlots.Exists(slot => slot == null) ||
            outputInventory.Data.itemSlots.Exists(slot => slot == null))
        {
            failure = CraftingResult.Failed(CraftingFailureReason.InvalidInventory, "制作库存包含空槽位引用");
            return false;
        }

        var states = new List<InventoryState>();
        InventoryState inputState = GetOrCreateState(states, inputInventory, retainTransaction);
        InventoryState outputState = GetOrCreateState(states, outputInventory, retainTransaction);

        foreach (CraftingConsumption consumption in match.Consumptions)
        {
            if (!inputState.TryConsume(consumption.SlotIndex, consumption.Amount))
            {
                failure = CraftingResult.Failed(
                    CraftingFailureReason.MissingMaterials,
                    "输入材料在事务预检时不足",
                    match.Recipe);
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

        if (retainTransaction)
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
        InventoryState outputState = GetOrCreateState(states, outputInventory, true);
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

    private static InventoryState GetOrCreateState(
        List<InventoryState> states,
        Inventory inventory,
        bool captureOriginalState)
    {
        foreach (InventoryState state in states)
        {
            if (ReferenceEquals(state.Inventory, inventory))
                return state;
        }

        var created = new InventoryState(inventory, captureOriginalState);
        states.Add(created);
        return created;
    }

    private sealed class InventoryState
    {
        private readonly List<ItemData> originalItems = new List<ItemData>();
        private readonly List<ItemData> workingItems = new List<ItemData>();
        private readonly int originalSlotCount;

        public InventoryState(Inventory inventory, bool captureOriginalState)
        {
            Inventory = inventory;
            originalSlotCount = inventory.Data.itemSlots.Count;
            foreach (ItemSlot slot in inventory.Data.itemSlots)
            {
                ItemData snapshot = Clone(slot?.itemData);
                if (captureOriginalState)
                {
                    originalItems.Add(snapshot);
                    workingItems.Add(Clone(snapshot));
                }
                else
                {
                    workingItems.Add(snapshot);
                }
            }
        }

        public Inventory Inventory { get; }

        public bool IsCurrentShapeValid()
        {
            return Inventory?.Data?.itemSlots != null && Inventory.Data.itemSlots.Count == originalSlotCount;
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
            if (source.Stack.Weight < 0f || source.Stack.Volume < 0f)
                return false;

            float remaining = source.Stack.Amount;
            GetWorkingCarryUsage(out float currentWeight, out float currentVolume);
            float capacityAllowed = Inventory.Data.GetCapacityLimitedAmount(
                source,
                remaining,
                currentWeight,
                currentVolume);
            if (capacityAllowed + CraftingIngredientMatcher.AmountEpsilon < remaining)
                return false;

            if (!source.Stack.Stackable)
                return TryAddNonStackable(source, remaining);

            for (int i = 0; i < workingItems.Count && remaining > CraftingIngredientMatcher.AmountEpsilon; i++)
            {
                ItemData target = workingItems[i];
                if (!CanStack(target, source))
                    continue;

                float amountCapacity = Inventory.Data.HasUnlimitedStackSize
                    ? remaining
                    : Mathf.Max(0f, GetWorkingSlotCapacity(i, 0f) - target.Stack.Amount);
                float amountToAdd = Mathf.Min(remaining, amountCapacity);
                if (amountToAdd <= 0f)
                    continue;
                target.Stack.Amount += amountToAdd;
                remaining -= amountToAdd;
            }

            for (int i = 0; remaining > CraftingIngredientMatcher.AmountEpsilon; i++)
            {
                if (!EnsureWorkingSlot(i))
                    break;
                if (workingItems[i] != null)
                    continue;

                float amountCapacity = GetWorkingSlotCapacity(i, remaining);
                float amountToAdd = Mathf.Min(remaining, amountCapacity);
                if (amountToAdd <= 0f)
                    continue;

                ItemData created = Clone(source);
                created.Stack.Amount = amountToAdd;
                created.Stack.CanBePickedUp = false;
                workingItems[i] = created;
                remaining -= amountToAdd;
            }

            return remaining <= CraftingIngredientMatcher.AmountEpsilon;
        }

        /// <summary>不可堆叠物品每个整数单位必须独占一个空槽。</summary>
        private bool TryAddNonStackable(ItemData source, float amount)
        {
            int unitCount = Mathf.RoundToInt(amount);
            if (unitCount <= 0 || Mathf.Abs(amount - unitCount) > CraftingIngredientMatcher.AmountEpsilon)
                return false;

            for (int i = 0; unitCount > 0; i++)
            {
                if (!EnsureWorkingSlot(i))
                    break;
                if (workingItems[i] != null)
                    continue;

                ItemData created = Clone(source);
                created.Stack.Amount = 1f;
                created.Stack.CanBePickedUp = false;
                workingItems[i] = created;
                unitCount--;
            }

            return unitCount == 0;
        }

        /// <summary>无限槽位只在事务快照中扩容，预检不会改动真实库存。</summary>
        private bool EnsureWorkingSlot(int index)
        {
            if (index < workingItems.Count)
                return true;
            if (index != workingItems.Count || !Inventory.Data.HasUnlimitedSlots)
                return false;

            workingItems.Add(null);
            return true;
        }

        /// <summary>新增槽位沿用库存的单格堆叠规则。</summary>
        private float GetWorkingSlotCapacity(int index, float requested)
        {
            if (Inventory.Data.HasUnlimitedStackSize)
                return requested;
            return index < Inventory.Data.itemSlots.Count
                ? Mathf.Max(0f, Inventory.Data.itemSlots[index].SlotMaxVolume)
                : Inventory_Data.DefaultSlotVolume;
        }

        /// <summary>按事务工作快照计算整包占用，确保扣料后释放的容量能立即用于本次产物。</summary>
        private void GetWorkingCarryUsage(out float totalWeight, out float totalVolume)
        {
            totalWeight = 0f;
            totalVolume = 0f;
            for (int i = 0; i < workingItems.Count; i++)
            {
                ItemStack stack = workingItems[i]?.Stack;
                if (stack == null)
                    continue;

                totalWeight += Mathf.Max(0f, stack.CurrentWeight);
                totalVolume += Mathf.Max(0f, stack.CurrentVolume);
            }
        }

        public void ApplyWorkingState()
        {
            Apply(workingItems);
        }

        public void RestoreOriginalState()
        {
            Apply(originalItems);
            List<ItemSlot> slots = Inventory.Data.itemSlots;
            for (int index = slots.Count - 1; index >= originalSlotCount; index--)
                slots.RemoveAt(index);
            Inventory.Data.EnsureSpareSlot();
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
            List<ItemSlot> slots = Inventory.Data.itemSlots;
            while (slots.Count < source.Count)
                slots.Add(new ItemSlot(slots.Count)
                {
                    SlotMaxVolume = Inventory.Data.HasUnlimitedStackSize
                        ? float.MaxValue
                        : Inventory_Data.DefaultSlotVolume
                });

            for (int i = 0; i < source.Count; i++)
            {
                ItemSlot slot = slots[i];
                Inventory.Data.Event_OnBeforeDataChanged?.Invoke(slot);
                slot.itemData = Clone(source[i]);
            }

            Inventory.Data.EnsureSpareSlot();
        }

        private static bool CanStack(ItemData target, ItemData source)
        {
            return target != null && target.CanStackWith(source);
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
