using System;
using System.Collections.Generic;

/// <summary>临时合成输入只引用来源库存的真实槽，持久化仍由各来源负责。</summary>
public sealed class LinkedCraftingInput : Inventory, IDisposable
{
    #region 共享材料视图
    public Inventory Primary { get; }
    public Inventory Shared { get; private set; }
    private bool forwarding;

    public LinkedCraftingInput(Inventory primary)
    {
        Primary = primary ?? throw new ArgumentNullException(nameof(primary));
        MachineOwner = primary.MachineOwner;
        Data = new Inventory_Data(new List<ItemSlot>(primary.Data.itemSlots), primary.Data.Name);
        primary.Data.Event_OnDataChanged += OnSourceChanged;
        Data.Event_OnBeforeDataChanged += OnBeforeChanged;
        Data.Event_OnDataChanged += OnViewChanged;
    }

    public bool SetShared(Inventory inventory)
    {
        if (ReferenceEquals(Shared, inventory)) return false;
        if (inventory != null && (ReferenceEquals(inventory, Primary) || inventory.Data.itemSlots.Count != 1))
            throw new ArgumentException("共享材料来源必须是独立的单格库存。");
        if (Shared != null) Shared.Data.Event_OnDataChanged -= OnSourceChanged;
        Shared = inventory;
        if (Shared != null) Shared.Data.Event_OnDataChanged += OnSourceChanged;
        RebuildSlots();
        return true;
    }

    public void RebuildSlots()
    {
        Data.itemSlots.Clear();
        Data.itemSlots.AddRange(Primary.Data.itemSlots);
        if (Shared != null) Data.itemSlots.Add(Shared.Data.itemSlots[0]);
        Data.NotifySlotsChanged();
    }

    private Inventory OwnerOf(ItemSlot slot)
        => Shared != null && ReferenceEquals(slot, Shared.Data.itemSlots[0]) ? Shared : Primary;

    private void OnBeforeChanged(ItemSlot slot)
    {
        if (!forwarding) OwnerOf(slot).Data.Event_OnBeforeDataChanged?.Invoke(slot);
    }

    private void OnViewChanged(ItemSlot slot)
    {
        if (forwarding || slot == null) return;
        forwarding = true;
        try
        {
            Inventory owner = OwnerOf(slot);
            owner.Data.Event_RefreshUI?.Invoke(slot.Index);
            owner.Data.Event_OnDataChanged?.Invoke(slot);
        }
        finally { forwarding = false; }
    }

    private void OnSourceChanged(ItemSlot slot)
    {
        if (forwarding) return;
        forwarding = true;
        try { Data.Event_OnDataChanged?.Invoke(slot); }
        finally { forwarding = false; }
    }

    public void Dispose()
    {
        Primary.Data.Event_OnDataChanged -= OnSourceChanged;
        if (Shared != null) Shared.Data.Event_OnDataChanged -= OnSourceChanged;
        Shared = null;
        Data.Event_OnBeforeDataChanged -= OnBeforeChanged;
        Data.Event_OnDataChanged -= OnViewChanged;
        Data.itemSlots.Clear();
        Data.NotifySlotsChanged();
    }
    #endregion
}
