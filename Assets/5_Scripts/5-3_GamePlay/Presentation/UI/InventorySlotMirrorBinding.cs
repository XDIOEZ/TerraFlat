using System;

/// <summary>附加视图直接绑定库存原槽，不抢占该库存自己的面板绑定。</summary>
public sealed class InventorySlotMirrorBinding : IDisposable
{
    #region 真实槽镜像
    public Inventory Inventory { get; }
    private readonly ItemSlot_UI slot;

    public InventorySlotMirrorBinding(Inventory inventory, ItemSlot_UI slot)
    {
        Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        this.slot = slot ?? throw new ArgumentNullException(nameof(slot));
        inventory.BindSlotUI(slot, 0, registerPrimary: false);
        inventory.Data.Event_OnDataChanged += OnChanged;
        inventory.Data.Event_RefreshUI += OnRefresh;
        slot.gameObject.SetActive(true);
    }

    private void OnChanged(ItemSlot _) { if (slot != null) slot.RefreshUI(); }
    private void OnRefresh(int _) { if (slot != null) slot.RefreshUI(); }

    public void Dispose()
    {
        Inventory.Data.Event_OnDataChanged -= OnChanged;
        Inventory.Data.Event_RefreshUI -= OnRefresh;
        if (slot == null) return;
        slot.ClearInventoryBinding(Inventory);
        slot.gameObject.SetActive(false);
    }
    #endregion
}
