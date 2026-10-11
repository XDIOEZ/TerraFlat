using System;
using System.Collections.Generic;
using FastCloner.Code;
using MemoryPack;
using Newtonsoft.Json;

public partial class Inventory_Data
{
    #region 库存实际质量版本
    [NonSerialized, MemoryPackIgnore, JsonIgnore, FastClonerIgnore] private ulong massRevision;
    [NonSerialized, MemoryPackIgnore, JsonIgnore, FastClonerIgnore] private Dictionary<ItemSlot, ItemData> massSlotItems;
    [MemoryPackIgnore, JsonIgnore] public ulong MassRevision { get { SynchronizeRuntimeSlotBindings(); return massRevision; } }
    [field: NonSerialized, MemoryPackIgnore, JsonIgnore, FastClonerIgnore] public event Action<Inventory_Data> MassChanged;

    public void NotifyMassChanged()
    {
        unchecked { massRevision++; }
        MassChanged?.Invoke(this);
    }
    private void RebindMassSlots()
    {
        if (massSlotItems != null)
            foreach (ItemData data in massSlotItems.Values)
                if (data != null) data.MassChanged -= HandleItemMassChanged;
        massSlotItems ??= new Dictionary<ItemSlot, ItemData>();
        massSlotItems.Clear();
        if (itemSlots != null)
            foreach (ItemSlot slot in itemSlots)
            {
                if (slot == null) continue;
                ItemData data = slot.itemData; massSlotItems[slot] = data;
                if (data != null) { _ = data.Stack; data.MassChanged += HandleItemMassChanged; }
            }
        NotifyMassChanged();
    }
    private void HandleItemMassChanged(ItemData _) => NotifyMassChanged();
    #endregion
}
