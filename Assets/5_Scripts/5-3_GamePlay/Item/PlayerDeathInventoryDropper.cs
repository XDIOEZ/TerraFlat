using System;
using System.Collections.Generic;
using Force.DeepCloner;
using UnityEngine;

/// <summary>
/// 将玩家拥有的库存集中转换为世界掉落物。
/// 新增玩家库存类型时，只需在 CollectPlayerInventories 中登记其 Inventory。
/// </summary>
public static class PlayerDeathInventoryDropper
{
    private const float DropRadius = 1.35f;
    private const float DropDuration = 0.45f;
    private static readonly Vector2 GoldenAngleDirection =
        new Vector2(-0.7373689f, 0.6754903f);

    public static int DropAll(Player player)
    {
        if (player == null)
            throw new ArgumentNullException(nameof(player));

        if (ItemMgr.Instance == null || ChunkMgr.Instance == null)
        {
            Debug.LogError("[PlayerDeathInventoryDropper] 世界物品系统未就绪，已保留玩家库存。");
            return 0;
        }

        List<Inventory> inventories = CollectPlayerInventories(player);
        int droppedStackCount = 0;
        int attemptedStackIndex = 0;

        for (int inventoryIndex = 0; inventoryIndex < inventories.Count; inventoryIndex++)
        {
            Inventory inventory = inventories[inventoryIndex];
            Inventory_Data inventoryData = inventory?.Data;
            if (inventoryData?.itemSlots == null)
                continue;

            for (int slotIndex = 0; slotIndex < inventoryData.itemSlots.Count; slotIndex++)
            {
                ItemSlot slot = inventoryData.itemSlots[slotIndex];
                if (slot?.itemData == null || slot.Amount <= 0)
                    continue;

                Vector2 endPosition = CalculateDropPosition(
                    player.transform.position,
                    attemptedStackIndex++);

                if (TryDropSlot(player, inventoryData, slot, slotIndex, endPosition))
                    droppedStackCount++;
            }
        }

        Inventory_HotBar hotbar =
            player.itemMods.GetMod_ByID<Inventory_HotBar>(ModText.Hotbar);
        if (hotbar?.CurentSelectItem != null &&
            (hotbar.CurrentSelectItemSlot == null || hotbar.CurrentSelectItemSlot.itemData == null))
        {
            hotbar.OnDestroyCurrentObject(hotbar.CurentSelectItem);
        }

        return droppedStackCount;
    }

    private static List<Inventory> CollectPlayerInventories(Player player)
    {
        List<Inventory> result = new List<Inventory>();
        HashSet<Inventory_Data> seenData = new HashSet<Inventory_Data>();

        if (player.itemMods?.Mods_List == null)
            return result;

        HashSet<Module> seenModules = new HashSet<Module>();
        foreach (List<Module> modules in player.itemMods.Mods_List.Values)
        {
            if (modules == null)
                continue;

            for (int moduleIndex = 0; moduleIndex < modules.Count; moduleIndex++)
            {
                Module module = modules[moduleIndex];
                if (module == null || !seenModules.Add(module))
                    continue;

                try
                {
                    switch (module)
                    {
                        case Mod_Inventory inventoryModule:
                            AddInventories(result, seenData, inventoryModule.InventoryRefDic?.Values);
                            AddInventories(result, seenData, inventoryModule.InventoryInstances);
                            break;

                        case Inventory_HotBar hotbar:
                            AddInventory(result, seenData, hotbar.RuntimeInventory);
                            break;

                        case Mod_Equipment equipment:
                            equipment.Save();
                            AddInventory(result, seenData, equipment.EquipmentInventory);
                            break;

                        case Mod_Hand hand:
                            AddInventory(result, seenData, hand.HandInventory);
                            break;

                        case Mod_HandCraftTable handCraft:
                            AddInventory(result, seenData, handCraft.inputInventory);
                            AddInventory(result, seenData, handCraft.outputInventory);
                            break;

                        case Mod_MakeTable makeTable:
                            AddInventory(result, seenData, makeTable.inputInventory);
                            AddInventory(result, seenData, makeTable.outputInventory);
                            break;

                        case Mod_HandMade handMade:
                            AddInventories(result, seenData, handMade.InventoryRefDic?.Values);
                            break;
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogError(
                        $"[PlayerDeathInventoryDropper] 收集玩家库存模块失败，已跳过模块 {module.GetType().Name}：{exception.Message}");
                    Debug.LogException(exception);
                }
            }
        }

        return result;
    }

    private static void AddInventories(
        ICollection<Inventory> result,
        ISet<Inventory_Data> seenData,
        IEnumerable<Inventory> inventories)
    {
        if (inventories == null)
            return;

        foreach (Inventory inventory in inventories)
            AddInventory(result, seenData, inventory);
    }

    private static void AddInventory(
        ICollection<Inventory> result,
        ISet<Inventory_Data> seenData,
        Inventory inventory)
    {
        if (inventory?.Data == null || !seenData.Add(inventory.Data))
            return;

        result.Add(inventory);
    }

    private static bool TryDropSlot(
        Player player,
        Inventory_Data inventoryData,
        ItemSlot slot,
        int slotIndex,
        Vector2 endPosition)
    {
        Item spawnedItem = null;

        try
        {
            ItemData droppedData = slot.itemData.DeepClone();
            droppedData.Stack.Amount = slot.Amount;
            droppedData.Stack.CanBePickedUp = false;
            droppedData.inHand = false;
            droppedData.transform.position = player.transform.position;
            droppedData.transform.scale = Vector3.one * 0.5f;

            spawnedItem = ItemMgr.Instance.InstantiateItem(
                droppedData,
                player.transform.position,
                Quaternion.identity,
                Vector3.one * 0.5f);

            spawnedItem.Load();
            spawnedItem.SetInHand(false);
            Mod_BaseDroper.StaticDropItem_Pos(
                spawnedItem,
                player.transform.position,
                endPosition,
                DropDuration);

            inventoryData.RemoveItemAll(slot, slotIndex);
            inventoryData.Event_OnDataChanged_TwoSlots.Invoke(slot, null);
            slot.RefreshUI();
            return true;
        }
        catch (Exception exception)
        {
            bool inventoryRemovalCommitted = slot.itemData == null;
            if (!inventoryRemovalCommitted &&
                spawnedItem != null &&
                ItemMgr.Instance != null)
            {
                ItemMgr.Instance.DespawnItem(spawnedItem, saveData: false);
            }

            string result = inventoryRemovalCommitted
                ? "掉落已完成，但库存事件处理出现异常。"
                : "掉落失败，已保留原物品。";
            Debug.LogError(
                $"[PlayerDeathInventoryDropper] {result}库存={inventoryData.Name}, 槽位={slotIndex}, 原因={exception.Message}");
            Debug.LogException(exception);
            return inventoryRemovalCommitted;
        }
    }

    private static Vector2 CalculateDropPosition(Vector2 origin, int index)
    {
        if (index <= 0)
            return origin + Vector2.right * (DropRadius * 0.45f);

        Vector2 direction = Vector2.right;
        for (int i = 0; i < index; i++)
        {
            direction = new Vector2(
                direction.x * GoldenAngleDirection.x - direction.y * GoldenAngleDirection.y,
                direction.x * GoldenAngleDirection.y + direction.y * GoldenAngleDirection.x);
        }

        float radius = DropRadius * Mathf.Lerp(0.45f, 1f, (index % 5) / 4f);
        return origin + direction.normalized * radius;
    }
}
