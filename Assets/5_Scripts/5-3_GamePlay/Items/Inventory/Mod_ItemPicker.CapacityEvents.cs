using System.Collections.Generic;
using UnityEngine.Pool;

public partial class Mod_ItemPicker
{
    #region 拾取容量变化与重试

    private readonly List<Inventory_Data> pickupCapacitySources = new();
    private bool pickupCapacityEventsBound;
    private ulong pickupCapacityRevision;
    private ulong triggerPickupCapacityRevision;

    internal ulong PickupCapacityRevision
    {
        get
        {
            if (pickupCapacityEventsBound) SynchronizePickupCapacitySources();
            return pickupCapacityRevision;
        }
    }

    private void BindPickupCapacityEvents()
    {
        pickupCapacityEventsBound = true;
        SynchronizePickupCapacitySources();
    }

    private void UnbindPickupCapacityEvents()
    {
        pickupCapacityEventsBound = false;
        foreach (Inventory_Data data in pickupCapacitySources)
            data.MassChanged -= HandlePickupCapacityChanged;
        pickupCapacitySources.Clear();
    }

    // 只核对数据引用换绑；容量数值改变由事件标记，面板关闭不影响监听。
    private void SynchronizePickupCapacitySources()
    {
        using var lease = ListPool<Inventory_Data>.Get(out var sources);
        if (item is Player player) PlayerCarryCapacityUtility.CollectCarriedInventories(player, sources);
        foreach (IInventory provider in AddTargetInventories)
        {
            Inventory_Data data = provider?.GetDefaultTargetInventory()?.Data;
            if (data != null && !sources.Contains(data)) sources.Add(data);
        }
        bool changed = sources.Count != pickupCapacitySources.Count;
        for (int i = 0; !changed && i < sources.Count; i++)
            changed = !ReferenceEquals(sources[i], pickupCapacitySources[i]);
        if (!changed) return;
        foreach (Inventory_Data data in pickupCapacitySources)
            data.MassChanged -= HandlePickupCapacityChanged;
        pickupCapacitySources.Clear();
        pickupCapacitySources.AddRange(sources);
        foreach (Inventory_Data data in pickupCapacitySources)
        {
            _ = data.MassRevision;
            data.MassChanged += HandlePickupCapacityChanged;
        }
        pickupCapacityRevision++;
    }

    private void HandlePickupCapacityChanged(Inventory_Data _)
    {
        // 入包本身也会发事件，留到下一次接触查询再重试，避免事务内重入。
        pickupCapacityRevision++;
    }

    private void RefreshPickupAttempts()
    {
        ulong revision = PickupCapacityRevision;
        if (triggerPickupCapacityRevision == revision) return;
        triggerPickupCapacityRevision = revision;
        pickupAttemptedItems.Clear();
    }

    #endregion
}
