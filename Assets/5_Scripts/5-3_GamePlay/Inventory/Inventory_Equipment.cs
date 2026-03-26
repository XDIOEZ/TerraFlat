using JetBrains.Annotations;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
[System.Serializable]
public class Inventory_Equipment : Inventory
{
    public override void OnValidate()
    {
            Data.Name = ModText.Equipment_Module;
    }

    public override void OnLeftClick(int index)
    {
        ItemSlot slot = Data.GetItemSlot(index);

        //防御性检查：确保DefaultTarget_Inventory不为null
        if (DefaultTarget_Inventory == null)
        {
            Debug.LogWarning($"[{Data.Name}] 手部为空：DefaultTarget_Inventory未设置 ,点击了 [{index}]");
            return;
        }

        //防御性检查：确保DefaultTarget_Inventory的Data不为null
        if (DefaultTarget_Inventory.Data == null)
        {
            Debug.LogWarning($"[{Data.Name}] 手部为空：DefaultTarget_Inventory.Data未设置");
            return;
        }

        // 先确定本次交互所使用的输入槽位（来自 DefaultTarget_Inventory）
        ItemSlot inputSlot;
        int inputIndex = index;

        if (DefaultTarget_Inventory.Data.itemSlots.Count > index)
        {
            inputSlot = DefaultTarget_Inventory.Data.itemSlots[index];
        }
        else
        {
            if (DefaultTarget_Inventory.Data.itemSlots.Count == 0)
            {
                Debug.LogWarning($"[{Data.Name}] 手部物品槽列表为空");
                return;
            }

            inputSlot = DefaultTarget_Inventory.Data.itemSlots[0];
            inputIndex = 0;
        }

        if (inputSlot == null)
        {
            Debug.LogWarning($"[{Data.Name}] 手部槽位 [{inputIndex}] 为空");
            return;
        }

        // 在真正交换前，通过模块数据检查：
        // 仅当“手上有物品要放入装备栏”时，才要求该物品包含 Equipment_Store 模块
        if (inputSlot.itemData != null)
        {
            var modData = inputSlot.itemData.GetModuleData_Frist(ModText.Equipment_Store) as Ex_ModData_MemoryPackable;
            if (modData == null)
            {
                Debug.LogWarning($"[{Data.Name}] 输入槽位 [{inputIndex}] 物品不包含 [{ModText.Equipment_Store}] 模块，无法与装备栏交换");
                return;
            }
        }

        // 通过检查后再执行默认交换逻辑
        Data.ChangeItemData_Default(index, inputSlot);
        DefaultTarget_Inventory.RefreshUI(inputIndex);
        RefreshUI(index);
    }

}
