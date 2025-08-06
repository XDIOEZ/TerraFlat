using JetBrains.Annotations;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Inventory_Equipment : Inventory
{
    [ShowInInspector]
    public Dictionary<string, List<Module>> EquipmentModules_Dictionary = new();

    public override void OnClick(int index)
    {
        // 获取当前装备槽中的物品（即需要被卸下或被替换的物品）。
        var currentEquippedItem = Data.itemSlots[index].itemData;

        // 获取从源背包（DefaultTarget_Inventory）中即将装备过来的新物品。
        var newIncomingItem = DefaultTarget_Inventory.Data.itemSlots[0].itemData;

        // --- 禁用阶段 (DEACTIVATION PHASE) ---
        // 如果当前槽位有装备，我们必须禁用它的模块，因为它即将被移除或替换。
        if (currentEquippedItem != null)
        {
            var key = GetKey(currentEquippedItem); // 现在调用是安全的，因为 currentEquippedItem 不为 null。

            if (EquipmentModules_Dictionary.TryGetValue(key, out var modList))
            {
                // 使用 .ToList() 会创建一个临时副本。这一点至关重要，因为
                // DeactivateEquipmentAttributes 会修改原始的 'modList' 集合，
                // 而你不能在遍历一个集合的同时修改它，否则会引发异常。
                foreach (var mod in modList.ToList())
                {
                    // 这段逻辑看起来是用于在卸下装备前，将模块数据恢复到物品实例上。
                    if (Belong_Item.itemMods.GetMod_ByName(mod._Data.Name) != null)
                    {
                        currentEquippedItem.ModuleDataDic[mod._Data.Name] =
                            Belong_Item.itemMods.GetMod_ByName(mod._Data.Name)._Data;
                    }

                    DeactivateEquipmentAttributes(Belong_Item, mod._Data, key);
                }
            }
        }

        // --- 激活阶段 (ACTIVATION PHASE) ---
        // 如果有新物品要装备，检查它是否是有效的装备，然后激活其模块。
        if (newIncomingItem != null)
        {
            // 检查新物品是否拥有类型为 "Equipment" 的模块。
            bool isEquipment = newIncomingItem.ModuleDataDic.Values.Any(modData => modData.Type == ModuleType.Equipment);

            if (isEquipment)
            {
                // 激活新物品上的所有装备模块。
                foreach (var modData in newIncomingItem.ModuleDataDic.Values)
                {
                    if (modData.Type == ModuleType.Equipment)
                    {
                        ActivateEquipmentAttributes(Belong_Item, modData, newIncomingItem);
                    }
                }
            }
            else
            {
                // 可选：如果你尝试装备一个非装备物品，你可能希望阻止交换。
                // 如果是这样，在这里直接 'return;'。
                // 否则，代码会继续执行，卸下旧装备，并将新的非装备物品移入该槽位，这也许是你期望的行为。
            }
        }

        // --- 完成交换 (FINALIZE SWAP) ---
        // 在处理完属性后，执行实际的物品槽位数据交换。
        // 只有在确实发生了装备或卸下操作时（即涉及至少一个物品），才执行交换。
        if (currentEquippedItem != null || newIncomingItem != null)
        {
            base.OnClick(index);
        }
    }

    public void ActivateEquipmentAttributes(Item player, ModuleData equipment, ItemData sourceItemData)
    {
        // 这个检查很重要，可以防止 sourceItemData 为 null 时产生错误。
        if (sourceItemData == null) return;

        string key = GetKey(sourceItemData);

        if (!EquipmentModules_Dictionary.ContainsKey(key))
        {
            EquipmentModules_Dictionary[key] = new List<Module>();
        }

        Module equipmentModule = Module.ADDModTOItem(player, equipment, sourceItemData);
        EquipmentModules_Dictionary[key].Add(equipmentModule);
    }

    public void DeactivateEquipmentAttributes(Item player, ModuleData modData, string key)
    {
        // 对 key 本身进行安全检查
        if (string.IsNullOrEmpty(key)) return;

        if (EquipmentModules_Dictionary.TryGetValue(key, out var moduleList))
        {
            var modInstance = moduleList.FirstOrDefault(m => m._Data == modData);
            if (modInstance != null)
            {
                Module.REMOVEModFROMItem(player, modData);
                moduleList.Remove(modInstance);
            }

            // 如果该装备的所有模块都已被移除，就从字典中移除这个 key。
            if (moduleList.Count == 0)
            {
                EquipmentModules_Dictionary.Remove(key);
            }
        }
    }

    private string GetKey(ItemData data)
    {
        // 这里是错误的根源。我们必须确保在调用此方法时 'data' 永不为 null。
        if (data == null)
        {
            Debug.LogError("尝试从一个为 null 的 ItemData 获取 key。这不应该发生。");
            return string.Empty; // 返回空字符串以避免崩溃
        }
        return data.IDName + "_" + data.Guid;
    }
}