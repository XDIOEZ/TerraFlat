using System;
using MemoryPack;
using UnityEngine;

/// <summary>燃料仍留在真实槽位里，吸收完成后才通过库存事务消费一份。</summary>
[Serializable, MemoryPackable(GenerateType.VersionTolerant)]
public partial class FuelAbsorptionState
{
    #region 单份燃料吸收
    [MemoryPackOrder(0)] public int SlotIndex = -1;
    [MemoryPackOrder(1)] public int ItemGuid;
    [MemoryPackOrder(2)] public string ItemId = "";
    [MemoryPackOrder(3)] public float ElapsedSeconds;

    public bool Matches(ItemSlot slot)
        => slot?.itemData?.Stack != null && slot.itemData.Stack.Amount >= 1f &&
           SlotIndex == slot.Index && ItemGuid == slot.itemData.Guid && ItemId == slot.itemData.IDName;

    public void Observe(ItemSlot slot)
    {
        if (Matches(slot)) return;
        Reset();
        if (slot?.itemData?.Stack == null || slot.itemData.Stack.Amount < 1f) return;
        SlotIndex = slot.Index;
        ItemGuid = slot.itemData.Guid;
        ItemId = slot.itemData.IDName;
    }

    public float Progress01(float duration)
        => MachineDefinition.Positive(duration) ? Mathf.Clamp01(ElapsedSeconds / duration) : 0f;

    public float RemainingSeconds(float duration) => Mathf.Max(0f, duration - ElapsedSeconds);

    public void Advance(float seconds, float duration)
    {
        if (SlotIndex < 0 || !MachineDefinition.Positive(seconds) || !MachineDefinition.Positive(duration)) return;
        ElapsedSeconds = Mathf.Min(duration, ElapsedSeconds + seconds);
    }

    public bool TryConsumeCompleted(Inventory_Data inventory, float duration, out ItemData consumed)
    {
        consumed = null;
        if (!MachineDefinition.Positive(duration) || ElapsedSeconds + .00001f < duration) return false;
        ItemSlot slot = inventory?.itemSlots != null && SlotIndex >= 0 && SlotIndex < inventory.itemSlots.Count
            ? inventory.itemSlots[SlotIndex] : null;
        bool valid = Matches(slot);
        Reset();
        return valid && inventory.TryConsumeFromSlot(slot, 1, out consumed);
    }

    public void Reset()
    {
        SlotIndex = -1;
        ItemGuid = 0;
        ItemId = "";
        ElapsedSeconds = 0f;
    }
    #endregion
}
