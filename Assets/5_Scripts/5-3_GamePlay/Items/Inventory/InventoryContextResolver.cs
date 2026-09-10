using System.Collections.Generic;

/// <summary>
/// 解析某个 ItemData 当前真正所属的 Inventory。
/// 以同一 ItemData 引用/Guid 为身份依据，使需要“同库存消费”的玩法不会误跨快捷栏、背包或容器取物。
/// </summary>
public static class InventoryContextResolver
{
    /// <summary>在物品拥有者暴露的库存中查找真正包含目标 ItemData 的 Inventory。</summary>
    public static bool TryResolveContainingInventory(Item inventoryOwner, ItemData containedItem, out Inventory inventory)
    {
        inventory = null;
        if (inventoryOwner == null || containedItem == null)
            return false;

        var visited = new HashSet<Inventory>();

        // 快捷栏优先：手持物通常直接绑定这里的槽位数据，优先命中也能避免误扫主背包。
        Inventory_HotBar hotbar = inventoryOwner.itemMods?.GetMod_ByID<Inventory_HotBar>(ModText.Hotbar);
        if (TryCandidate(hotbar?.RuntimeInventory, containedItem, visited, out inventory))
            return true;

        Module[] modules = inventoryOwner.GetComponentsInChildren<Module>(true);
        for (int i = 0; i < modules.Length; i++)
        {
            if (modules[i] is not Mod_Inventory inventoryModule || inventoryModule.InventoryInstances == null)
                continue;

            for (int inventoryIndex = 0; inventoryIndex < inventoryModule.InventoryInstances.Count; inventoryIndex++)
            {
                if (TryCandidate(inventoryModule.InventoryInstances[inventoryIndex], containedItem, visited, out inventory))
                    return true;
            }
        }

        // 兼容其它只实现默认库存契约的模块；已访问的快捷栏/普通背包会被 HashSet 自动排除。
        for (int i = 0; i < modules.Length; i++)
        {
            if (modules[i] is not IInventory provider)
                continue;

            Inventory candidate = provider.GetDefaultTargetInventory();
            if (TryCandidate(candidate, containedItem, visited, out inventory))
                return true;
        }

        return false;
    }

    /// <summary>检查候选库存是否包含目标物品实例；优先引用相等，Guid 仅作运行时重绑后的稳定回退。</summary>
    private static bool TryCandidate(
        Inventory candidate,
        ItemData containedItem,
        HashSet<Inventory> visited,
        out Inventory inventory)
    {
        inventory = null;
        if (candidate?.Data?.itemSlots == null || !visited.Add(candidate))
            return false;

        for (int i = 0; i < candidate.Data.itemSlots.Count; i++)
        {
            ItemData slotItem = candidate.Data.itemSlots[i]?.itemData;
            if (slotItem == null)
                continue;

            if (ReferenceEquals(slotItem, containedItem) ||
                (containedItem.Guid != 0 && slotItem.Guid == containedItem.Guid))
            {
                inventory = candidate;
                return true;
            }
        }

        return false;
    }
}
