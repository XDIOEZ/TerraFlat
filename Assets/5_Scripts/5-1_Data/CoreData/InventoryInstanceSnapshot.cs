using System;
using System.Collections.Generic;
using MemoryPack;
using UnityEngine;

/// <summary>库存只保存槽位内容与用户状态，UI 资源、输入绑定及接收条件由当前布局提供。</summary>
[MemoryPackable]
public sealed partial class InventoryInstanceSnapshot
{
    #region 库存状态

    [MemoryPackInclude] private string name;
    [MemoryPackInclude] private int selectedIndex;
    [MemoryPackInclude] private bool initialized;
    [MemoryPackInclude] private Vector3 panelPosition;
    [MemoryPackInclude] private bool panelIsOpen;
    [MemoryPackInclude] private bool depositBlocked;
    [MemoryPackInclude] private ItemInstanceSnapshot[] slots;

    [MemoryPackIgnore] public int SlotCount => slots?.Length ?? 0;

    public static InventoryInstanceSnapshot Capture(Inventory_Data data)
    {
        if (data == null) return null;
        var state = new InventoryInstanceSnapshot
        {
            name = data.Name, selectedIndex = data.Index, initialized = data.IsInjected,
            panelPosition = data.PanelPosition, panelIsOpen = data.PanelIsOpen, depositBlocked = data.IsDepositBlocked,
            slots = new ItemInstanceSnapshot[data.itemSlots?.Count ?? 0]
        };
        for (int i = 0; i < state.slots.Length; i++)
            if (data.itemSlots[i]?.itemData != null) state.slots[i] = ItemInstanceSnapshot.Capture(data.itemSlots[i].itemData);
        return state;
    }

    public Inventory_Data CreateColdData()
    {
        var data = new Inventory_Data(new List<ItemSlot>(), name);
        RestoreTo(data);
        return data;
    }

    public void RestoreTo(Inventory_Data data, Func<ItemData, ItemData> restoreItem = null)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        data.Index = selectedIndex;
        data.IsInjected = initialized;
        data.PanelPosition = panelPosition;
        data.PanelIsOpen = panelIsOpen;
        data.IsDepositBlocked = depositBlocked;
        data.itemSlots ??= new();
        int count = slots?.Length ?? 0;
        var available = new Dictionary<(string DefinitionId, int Guid, ItemInstanceKind Kind), ItemData>();
        var currentGuids = new HashSet<int>();
        var savedGuids = new HashSet<int>();
        var ambiguousGuids = new HashSet<int>();
        var claimed = new HashSet<ItemData>();
        foreach (ItemSlot slot in data.itemSlots)
        {
            ItemData item = slot?.itemData;
            if (item == null || item.Guid == 0) continue;
            if (!currentGuids.Add(item.Guid)) ambiguousGuids.Add(item.Guid);
            available.TryAdd((item.IDName, item.Guid, ItemInstanceSnapshot.GetKind(item)), item);
        }
        foreach (ItemInstanceSnapshot saved in slots ?? Array.Empty<ItemInstanceSnapshot>())
            if (saved != null && saved.Guid != 0 && !savedGuids.Add(saved.Guid)) ambiguousGuids.Add(saved.Guid);
        // 保留当前布局的槽位容量与标签，扩展槽位仍保留物品以免缩容丢失。
        while (data.itemSlots.Count < count) data.itemSlots.Add(new ItemSlot(data.itemSlots.Count));
        for (int i = 0; i < data.itemSlots.Count; i++)
        {
            data.itemSlots[i] ??= new ItemSlot(i);
            ItemInstanceSnapshot saved = i < count ? slots[i] : null;
            ItemData current = data.itemSlots[i].itemData;
            if (saved == null) { data.itemSlots[i].itemData = null; continue; }
            bool identityCanBeReused = !ambiguousGuids.Contains(saved.Guid);
            if (current != null && current.IDName == saved.DefinitionId && current.Guid == saved.Guid &&
                ItemInstanceSnapshot.GetKind(current) == saved.Kind && identityCanBeReused && !claimed.Contains(current))
            {
                claimed.Add(current);
                saved.RestoreTo(current);
                data.itemSlots[i].itemData = restoreItem == null ? current : restoreItem(current);
            }
            else if (saved.Guid != 0 && identityCanBeReused &&
                available.TryGetValue((saved.DefinitionId, saved.Guid, saved.Kind), out ItemData relocated) && claimed.Add(relocated))
            {
                // 同一实例跨槽移动继续复用原数据，重复 GUID 不参与引用复用。
                saved.RestoreTo(relocated);
                data.itemSlots[i].itemData = restoreItem == null ? relocated : restoreItem(relocated);
            }
            else
            {
                ItemData restored = saved.CreateColdData();
                data.itemSlots[i].itemData = restoreItem == null ? restored : restoreItem(restored);
            }
        }
    }

    public static Dictionary<string, InventoryInstanceSnapshot> CaptureDictionary(IDictionary<string, Inventory_Data> data)
    {
        var captured = new Dictionary<string, InventoryInstanceSnapshot>(StringComparer.Ordinal);
        if (data != null)
            foreach (KeyValuePair<string, Inventory_Data> pair in data)
                captured.Add(pair.Key, Capture(pair.Value));
        return captured;
    }

    public static void RestoreDictionary(IDictionary<string, InventoryInstanceSnapshot> states,
        IDictionary<string, Inventory_Data> target)
    {
        foreach (KeyValuePair<string, InventoryInstanceSnapshot> pair in states)
        {
            if (pair.Value == null) { target[pair.Key] = null; continue; }
            if (target.TryGetValue(pair.Key, out Inventory_Data current) && current != null)
                pair.Value.RestoreTo(current);
            else target[pair.Key] = pair.Value.CreateColdData();
        }
    }

    #endregion
}
