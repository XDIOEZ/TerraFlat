using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using UnityEngine;

public static partial class MachineWorld
{
    #region 加工耗尽后的材料与库存交付
    public static bool TryDestroyWornMachine(MachineEntity node, float recoveryFraction, out string reason)
    {
        reason = null;
        using var nodeScope = UseNodeScope(node);
        if (!GameNetwork.HasStateAuthority || !Contains(node) || node.Snapshot.Durability > 0f)
        { reason = "损毁工作站已失效或耐久尚未耗尽"; return false; }
        string carrier = Mod_Building.GetSummonerPrefabId(node.Definition.Id);
        var drops = new List<ItemData>();
        if (recoveryFraction > 0f && !BuildingMaterialSalvage.TryCreateProportionalMaterialSalvage(carrier, recoveryFraction,
                out drops, out reason, node.Snapshot)) return false;
        var seen = new HashSet<Inventory_Data>();
        foreach (Inventory inventory in node.Logic.Inventories)
        {
            if (!seen.Add(inventory.Data)) continue;
            foreach (ItemSlot slot in inventory.Data.itemSlots)
                if (slot?.itemData?.Stack?.Amount > 0f) drops.Add(ItemInstanceDataFactory.CloneRuntime(slot.itemData));
        }
        if (FlatWorld.Spaceflight.SpaceSession.Current?.Owns(node.Id) == true)
            return FlatWorld.Spaceflight.SpaceSession.Current.TryReleaseWornMachine(node, drops, out reason);

        var spawned = new List<DroppedItemHandle>(drops.Count);
        try
        {
            foreach (ItemData drop in drops)
            {
                DroppedItemHandle handle = DroppedItemService.Spawn(drop, node.Position);
                if (!handle.IsValid) throw new InvalidOperationException("工作站损毁掉落没有生成有效物品。");
                spawned.Add(handle);
            }
            if (Remove(node.Id) == null) throw new InvalidOperationException("工作站移除失败。");
            return true;
        }
        catch (Exception exception)
        {
            // 删除后的通知异常不撤销已经交付的掉落，尚在场景的节点则保留全部库存。
            if (!Contains(node)) { Debug.LogException(exception); return true; }
            foreach (DroppedItemHandle handle in spawned) DroppedItemService.Remove(handle);
            reason = exception.Message;
            return false;
        }
    }
    #endregion
}
