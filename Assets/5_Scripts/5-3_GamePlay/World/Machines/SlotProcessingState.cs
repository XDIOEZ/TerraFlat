using System;
using System.Collections.Generic;
using MemoryPack;

/// <summary>整槽加工保存真实库存和计时；随机结果先记账，提交失败后不得重新掷骰。</summary>
[Serializable, MemoryPackable]
public partial class SlotProcessingState
{
    #region 槽位加工状态
    public Inventory_Data Inventory;
    public List<float> Elapsed = new();
    public List<string> Signatures = new();
    public List<int> PendingAmounts = new();
    public List<string> PendingItems = new();

    public void EnsureSlots(int count)
    {
        while (Elapsed.Count < count) Elapsed.Add(0f);
        while (Signatures.Count < count) Signatures.Add("");
        while (PendingAmounts.Count < count) PendingAmounts.Add(-1);
        while (PendingItems.Count < count) PendingItems.Add("");
    }

    public void Observe(int index, ItemData item)
    {
        string signature = item == null ? "" : item.Guid + "|" + item.IDName + "|" + item.ItemSpecialData + "|" + item.Stack.Amount;
        if (Signatures[index] == signature) return;
        Reset(index);
        Signatures[index] = signature;
    }

    public void Reset(int index)
    { Elapsed[index] = 0f; PendingAmounts[index] = -1; PendingItems[index] = ""; }

    public void ApplyRemote(SlotProcessingState incoming, Inventory inventory)
    {
        if (incoming == null) throw new ArgumentNullException(nameof(incoming));
        MachineInventory.ApplySnapshot(inventory, incoming.Inventory);
        Inventory = inventory.Data;
        Elapsed = incoming.Elapsed;
        Signatures = incoming.Signatures;
        PendingAmounts = incoming.PendingAmounts;
        PendingItems = incoming.PendingItems;
        EnsureSlots(inventory.Data.itemSlots.Count);
    }
    #endregion
}
