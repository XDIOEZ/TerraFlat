using System;
using System.Collections.Generic;
using UnityEngine;

public partial class Inventory_Data
{
    #region 整仓原子转移
    // 整仓打包先检查全部槽位与总承载，提交时搬迁原实例，通知异常不能中断一半物资。
    public bool TryTransferAllToEmptyInventory(Inventory_Data destination, out string reason)
    {
        reason = "目标库存没有足够容量接收整包物资。";
        if (destination == null || ReferenceEquals(this, destination) || itemSlots == null ||
            destination.itemSlots == null || destination.IsDepositBlocked) return false;
        foreach (ItemSlot target in destination.itemSlots)
            if (target?.itemData != null) return false;
        var transfers = new List<(ItemSlot Source, ItemSlot Target, ItemData Data)>();
        int nextTarget = 0;
        foreach (ItemSlot source in itemSlots)
        {
            ItemData data = source?.itemData;
            if (data == null) continue;
            if (data.Stack == null || !float.IsFinite(data.Stack.Amount) || data.Stack.Amount < 1f ||
                (double)data.Stack.Amount > int.MaxValue || data.Stack.Amount != Math.Floor((double)data.Stack.Amount))
            { reason = "物品数量必须是可转移的有限整数，含小数或超大数量的物资无法打包。"; return false; }
            while (nextTarget < destination.itemSlots.Count && destination.itemSlots[nextTarget] == null) nextTarget++;
            if (nextTarget >= destination.itemSlots.Count) return false;
            ItemSlot target = destination.itemSlots[nextTarget++];
            if (itemSlots.Contains(target) || data.Stack.Amount > target.SlotMaxVolume + .0001f ||
                !data.Stack.Stackable && data.Stack.Amount != 1f ||
                !destination.CanAcceptItem(data, target)) return false;
            transfers.Add((source, target, data));
        }
        if (transfers.Count == 0) { reason = "仓库里还没有可以打包的物品。"; return false; }
        GetCarryUsage(out float weight, out float volume);
        if (destination.HasCarryCapacity && !destination.HasUnlimitedCarryCapacity &&
            (!float.IsFinite(weight) || !float.IsFinite(volume) ||
             weight > destination.MaxAllowedCarryWeight + .0001f || volume > destination.MaxCarryVolume + .0001f)) return false;
        EnsureRuntimeEvents(); destination.EnsureRuntimeEvents();
        foreach (var transfer in transfers)
        {
            InvokeBatchNotification(() => Event_OnBeforeDataChanged.Invoke(transfer.Source));
            InvokeBatchNotification(() => destination.Event_OnBeforeDataChanged.Invoke(transfer.Target));
        }
        foreach (var transfer in transfers)
            if (!ReferenceEquals(transfer.Source.itemData, transfer.Data) || transfer.Target.itemData != null)
            { reason = "打包期间库存已变化，请重新发送。"; return false; }
        foreach (var transfer in transfers)
        {
            InvokeBatchNotification(() => transfer.Source.itemData = null);
            InvokeBatchNotification(() => transfer.Target.itemData = transfer.Data);
        }
        foreach (var transfer in transfers)
        {
            InvokeBatchNotification(() => transfer.Source.RefreshUI());
            InvokeBatchNotification(() => transfer.Target.RefreshUI());
            InvokeBatchNotification(() => Event_RefreshUI.Invoke(transfer.Source.Index));
            InvokeBatchNotification(() => destination.Event_RefreshUI.Invoke(transfer.Target.Index));
            InvokeBatchNotification(() => NotifyItemDataChanged(transfer.Source));
            InvokeBatchNotification(() => destination.NotifyItemDataChanged(transfer.Target));
            InvokeBatchNotification(() => Event_OnDataChanged_TwoSlots.Invoke(transfer.Source, transfer.Target));
            InvokeBatchNotification(() => destination.Event_OnDataChanged_TwoSlots.Invoke(transfer.Target, transfer.Source));
        }
        reason = null;
        return true;
    }

    private static void InvokeBatchNotification(Action action)
    {
        try { action(); }
        catch (Exception error) { Debug.LogException(error); }
    }
    #endregion
}
