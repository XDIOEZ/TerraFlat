using JetBrains.Annotations;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Inventory_Equipment : Inventory
{
    [ShowInInspector]
    public Dictionary<string, List<Module>> EquipmentModules_Dictionary = new();

    public GameObject ModulesParent;

    /// <summary>
    /// 初始化时激活所有已装备物品的模块
    /// </summary>
    public override void Init()
    {
        base.Init();

        ModulesParent = new GameObject("ModulesParent");
        ModulesParent.transform.SetParent(transform, false);

        // 遍历所有装备槽
        for (int i = 0; i < Data.itemSlots.Count; i++)
        {
            var equippedItem = Data.itemSlots[i].itemData;

            // 检查槽位是否有装备
            if (equippedItem != null)
            {
                // 激活该装备的所有Equipment类型模块
                ActivateAllEquipmentModules(equippedItem);
            }
        }

    }

    /// <summary>
    /// 保存时移除所有装备模块
    /// </summary>
    public override void Save()
    {
        base.Save();

        // 创建所有键的副本以避免在遍历中修改字典
        var allKeys = EquipmentModules_Dictionary.Keys.ToList();

        foreach (var key in allKeys)
        {
            // 尝试获取该键对应的所有模块
            if (EquipmentModules_Dictionary.TryGetValue(key, out var modList))
            {
                // 创建模块列表副本进行遍历
                var modListCopy = modList.ToList();

                foreach (var mod in modListCopy)
                {
                    // 移除每个模块
                    DeactivateEquipmentAttributes(mod);
                }
                // 移除该键对应的所有模块
                EquipmentModules_Dictionary.Remove(key);
            }
        }
    }

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
        var key = GetKey(currentEquippedItem);

        if (EquipmentModules_Dictionary.TryGetValue(key, out var modList))
        {
            // 使用 .ToList() 会创建一个临时副本，避免遍历中修改集合引发异常
            foreach (var mod in modList.ToList())
            {
                // 如果模块实现了IItemValueModifier接口，则调用Unequip方法
                if (mod is IItemValueModifier itemValueModifier)
                {
                    itemValueModifier.Unequip(Owner, currentEquippedItem);
                }

                // 从EquipmentModules_Dictionary中获取模块，而不是通过Owner.itemMods
                // 在卸下装备前，将模块数据恢复到物品实例上
                var moduleFromDict = modList.FirstOrDefault(m => m._Data.Name == mod._Data.Name);
                if (moduleFromDict != null)
                {
                    currentEquippedItem.ModuleDataDic[mod._Data.Name] = moduleFromDict._Data;
                }

                DeactivateEquipmentAttributes(mod);
                EquipmentModules_Dictionary.Remove(key);
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
                    Module module = ActivateEquipmentAttributes(Owner, modData, newIncomingItem);
                    
                    // 如果模块实现了IItemValueModifier接口，则调用Equip方法
                    if (module is IItemValueModifier itemValueModifier)
                    {
                        itemValueModifier.Equip(Owner, newIncomingItem);
                    }
                }
            }
        }
        else
        {
            // 可选：如果你尝试装备一个非装备物品，你可能希望阻止交换。
            // 如果是这样，在这里直接 'return;'。
        }
    }

    // --- 完成交换 (FINALIZE SWAP) ---
    // 在处理完属性后，执行实际的物品槽位数据交换。
    if (currentEquippedItem != null || newIncomingItem != null)
    {
        base.OnClick(index);
    }
}

public Module ActivateEquipmentAttributes(Item player, ModuleData equipment, ItemData sourceItemData)
{
    // 防止 sourceItemData 为 null 时产生错误
    if (sourceItemData == null) return null;

    // 使用与Deactivate相同的key生成逻辑，保持一致性
    string key = GetKey(sourceItemData);

    if (!EquipmentModules_Dictionary.ContainsKey(key))
    {
        EquipmentModules_Dictionary[key] = new List<Module>();
    }

    Module equipmentModule = EquipMod(player, equipment, sourceItemData);
    EquipmentModules_Dictionary[key].Add(equipmentModule);
    
    return equipmentModule;
}
    public Module EquipMod(Item item, ModuleData mod, ItemData itemData)
    {
        GameObject @object = GameRes.Instance.InstantiatePrefab(mod.ID);

        @object.transform.SetParent(ModulesParent.transform);

        @object.transform.localPosition = Vector3.zero;

        Module module = @object.GetComponentInChildren<Module>();

        module._Data = mod;

        module.ModuleInit(item, mod, itemData);

        module.Load();
        return module;
    }

    // 其他方法保持不变，确保Deactivate时使用的key与Activate时一致
    public void DeactivateEquipmentAttributes(Module mod)
    {
        // 现有逻辑不变，因为key来自GetKey()，现在与Activate保持一致了
        mod.Save();
        Destroy(mod.gameObject);
    }

    private string GetKey(ItemData data)
    {
        if (data == null)
        {
            Debug.LogError("尝试从一个为 null 的 ItemData 获取 key。这不应该发生。");
            return string.Empty;
        }
        return data.IDName + "_" + data.Guid;
    }


    /// <summary>
    /// 激活物品上所有的Equipment类型模块
    /// </summary>
    /// <param name="itemData">要激活模块的物品数据</param>
    private void ActivateAllEquipmentModules(ItemData itemData)
    {
        if (itemData == null) return;

        // 遍历物品的所有模块数据
        foreach (var modData in itemData.ModuleDataDic.Values)
        {
            // 只激活Equipment类型的模块
            if (modData.Type == ModuleType.Equipment)
            {
                ActivateEquipmentAttributes(Owner, modData, itemData);
            }
        }
    }
}
