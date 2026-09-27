using System;
using System.Collections;
using System.Collections.Generic;
using MemoryPack;
using UnityEngine;

/// <summary>液体容器的独立固体库存存档；每个真实槽位对应界面中的一个可移动物体，关闭界面不清空内容。</summary>
[MemoryPackable]
public partial class VesselContentsState
{
    public Inventory_Data Items; // 木桶内部的六格物品库存。
}

/// <summary>
/// 可注册到液体容器的固体库存模块。库存和液体分别持久化，放置与拆回通过共享模块状态迁移；
/// 投料配方从当前液体定义读取，满足数量后经库存事务消耗原料，剩余物品仍留在桶内。
/// </summary>
public sealed class Mod_VesselContents : Module, IInventory
{
    #region 数据与生命周期

    public const string ModuleId = "Mod_VesselContents";
    private const int SlotCount = 6; // 木桶剖面一次可呈现并单独取放的物品堆数。
    private const float ReactionSettleSeconds = 1.1f; // 投料落稳后再结算，保留可见的下落过程。

    public Ex_ModData_MemoryPackable ModData = new(); // 独立持久化载体。
    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = value as Ex_ModData_MemoryPackable ?? throw new ArgumentException("容器物品数据类型错误。");
    }
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;

    private VesselContentsState state;
    private readonly VesselInventory contents = new();
    private Mod_WaterVessel liquid;
    private Coroutine pendingReaction;
    private bool applyingReaction;
    public event Action Changed; // 内部库存状态变化。
    public Inventory Contents => contents;

    /// <summary>恢复六格库存并订阅固体、液体变化；数据不依赖面板生命周期。</summary>
    public override void Load()
    {
        state = ModData.BitData == null || ModData.BitData.Length == 0
            ? new VesselContentsState { Items = CreateInventoryData() }
            : ModData.GetData<VesselContentsState>();
        if (state?.Items?.itemSlots == null || state.Items.itemSlots.Count != SlotCount)
            throw new InvalidOperationException("木桶固体库存必须保存六个槽位。");

        contents.item = item;
        contents.Data = state.Items;
        contents.Data.SetUnlimitedSlots(false);
        contents.InitData();
        contents.Data.Event_OnDataChanged += OnContentsChanged;
        liquid = item.itemMods.GetMod_ByID<Mod_WaterVessel>(Mod_WaterVessel.ModuleId)
            ?? throw new InvalidOperationException("固体库存需要同一容器上的液体模块。");
        liquid.Changed += OnLiquidChanged;
        ScheduleReaction();
    }

    /// <summary>把真实库存槽位写入独立模块存档。</summary>
    public override void Save()
    {
        state.Items = contents.Data;
        ModData.WriteData(state);
    }

    /// <summary>释放库存事件与所属物品，避免回池后面板触及旧容器。</summary>
    public override void Unload()
    {
        if (pendingReaction != null) StopCoroutine(pendingReaction);
        pendingReaction = null;
        if (liquid != null) liquid.Changed -= OnLiquidChanged;
        liquid = null;
        if (contents.Data != null) contents.Data.Event_OnDataChanged -= OnContentsChanged;
        contents.UnbindSlotDataEvents();
        contents.UnbindRuntimeDataEvents();
        contents.item = null;
        Changed = null;
        state = null;
    }

    /// <summary>供快捷转移与库存归属解析使用同一真实库存。</summary>
    public Inventory GetDefaultTargetInventory() => contents;

    private static Inventory_Data CreateInventoryData()
    {
        var slots = new List<ItemSlot>(SlotCount);
        for (int i = 0; i < SlotCount; i++) slots.Add(new ItemSlot(i));
        return new Inventory_Data(slots, "木桶内物品");
    }

    #endregion

    #region 投料反应

    /// <summary>库存通知先保存并刷新视图，再等待投料图标落稳。</summary>
    private void OnContentsChanged(ItemSlot _)
    {
        if (applyingReaction) return;
        Save();
        Changed?.Invoke();
        ScheduleReaction();
    }

    /// <summary>补水后也重新检查已留在桶内的原料。</summary>
    private void OnLiquidChanged()
    {
        if (!applyingReaction) ScheduleReaction();
    }

    private void ScheduleReaction()
    {
        if (pendingReaction == null && liquid?.CurrentLiquid?.IngredientReactions.Count > 0)
            pendingReaction = StartCoroutine(ApplyReactionAfterSettling());
    }

    private IEnumerator ApplyReactionAfterSettling()
    {
        yield return new WaitForSecondsRealtime(ReactionSettleSeconds);
        pendingReaction = null;
        TryApplyAvailableReaction();
    }

    /// <summary>只从桶内真实槽位扣料；液体未满足条件时，盐和其它物品原样留存。</summary>
    private void TryApplyAvailableReaction()
    {
        LiquidDefinition definition = liquid?.CurrentLiquid;
        if (definition == null || contents.Data?.itemSlots == null) return;

        foreach (LiquidIngredientReaction reaction in definition.IngredientReactions)
        {
            if (!liquid.CanApplyIngredientReaction(reaction)) continue;
            int available = 0;
            foreach (ItemSlot slot in contents.Data.itemSlots)
            {
                if (!string.Equals(slot?.itemData?.IDName, reaction.ItemId, StringComparison.Ordinal) ||
                    slot.itemData.Stack == null)
                    continue;
                available += Mathf.FloorToInt(slot.itemData.Stack.Amount);
            }
            if (available < reaction.Amount) continue;

            applyingReaction = true;
            try
            {
                int remaining = reaction.Amount;
                foreach (ItemSlot slot in contents.Data.itemSlots)
                {
                    if (remaining == 0) break;
                    if (!string.Equals(slot?.itemData?.IDName, reaction.ItemId, StringComparison.Ordinal) ||
                        slot.itemData.Stack == null)
                        continue;
                    int consumed = Math.Min(remaining, Mathf.FloorToInt(slot.itemData.Stack.Amount));
                    if (consumed == 0) continue;
                    if (!contents.Data.TryConsumeFromSlot(slot, consumed, out _))
                        throw new InvalidOperationException("木桶投料库存事务未能完成。");
                    remaining -= consumed;
                }
                if (!liquid.TryApplyIngredientReaction(reaction))
                    throw new InvalidOperationException("液体投料事务已扣料但液体转换失败。");
                Save();
                Changed?.Invoke();
            }
            finally { applyingReaction = false; }
            return;
        }
    }

    #endregion

    #region 库存接收

    /// <summary>空白落点形成独立物品堆；只有直接投到同类图标才合并，保持库存与物理表现一致。</summary>
    private sealed class VesselInventory : Inventory
    {
        public override bool CanAcceptQuickTransfer(ItemSlot sourceSlot, ItemSlot targetSlot)
        {
            if (!base.CanAcceptQuickTransfer(sourceSlot, targetSlot)) return false;
            ItemData candidate = sourceSlot.itemData;
            if (targetSlot.itemData != null && !targetSlot.itemData.CanStackWith(candidate)) return false;
            if (ReferenceEquals(candidate, item?.itemData)) return false;
            foreach (ModuleData module in candidate.ModuleDataDic.Values)
                if (module?.ID == ModuleId || module?.ID == Mod_WaterVessel.ModuleId)
                    return false; // 液体容器交给原有转液目标，固体桶禁止递归嵌套。
            return true;
        }
    }

    #endregion
}
