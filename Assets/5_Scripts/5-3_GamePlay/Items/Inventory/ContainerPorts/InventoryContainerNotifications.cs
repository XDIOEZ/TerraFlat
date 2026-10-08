using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using UltEvents;

/// <summary>原生库存事务先保留通知，双方提交后才让界面和玩法订阅者观察完整结果。</summary>
internal sealed class InventoryContainerNotifications
{
    #region 延后库存通知
    private readonly List<Action> pending = new();
    public T Mutate<T>(Inventory inventory, Func<T> mutation, bool discard = false)
    {
        Inventory_Data data = inventory.Data;
        var before = data.Event_OnBeforeDataChanged; var changed = data.Event_OnDataChanged;
        var refresh = data.Event_RefreshUI; var pair = data.Event_OnDataChanged_TwoSlots;
        var slots = new Dictionary<ItemSlot, UltEvent<ItemSlot>>();
        foreach (ItemSlot slot in data.itemSlots) if (slot != null)
        {
            var original = slot.onSlotDataChanged; slots.Add(slot, original);
            slot.onSlotDataChanged = new UltEvent<ItemSlot>();
            slot.onSlotDataChanged += value => pending.Add(() => original?.Invoke(value));
        }
        data.Event_OnBeforeDataChanged = new UltEvent<ItemSlot>();
        data.Event_OnBeforeDataChanged += value => pending.Add(() => before?.Invoke(value));
        data.Event_OnDataChanged = new UltEvent<ItemSlot>();
        data.Event_OnDataChanged += value => pending.Add(() => changed?.Invoke(value));
        data.Event_RefreshUI = new UltEvent<int>();
        data.Event_RefreshUI += value => pending.Add(() => refresh?.Invoke(value));
        data.Event_OnDataChanged_TwoSlots = new UltEvent<ItemSlot, ItemSlot>();
        data.Event_OnDataChanged_TwoSlots += (a, b) => pending.Add(() => pair?.Invoke(a, b));
        try { return mutation(); }
        finally
        {
            data.Event_OnBeforeDataChanged = before; data.Event_OnDataChanged = changed;
            data.Event_RefreshUI = refresh; data.Event_OnDataChanged_TwoSlots = pair;
            foreach (var slot in slots) slot.Key.onSlotDataChanged = slot.Value;
            if (discard) pending.Clear();
        }
    }
    public void Publish()
    {
        Action[] callbacks = pending.ToArray(); pending.Clear(); Exception first = null;
        foreach (Action callback in callbacks)
            try { callback(); } catch (Exception exception) { first ??= exception; }
        if (first != null) ExceptionDispatchInfo.Capture(first).Throw();
    }
    public void Clear() => pending.Clear();
    #endregion
}
