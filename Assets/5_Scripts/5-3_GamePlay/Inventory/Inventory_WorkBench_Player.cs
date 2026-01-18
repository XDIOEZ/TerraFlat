using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Inventory_WorkBench_Player : Inventory_WorkBench
{
    public override void OnValidate()
    {
        Data.Name = ModText.WorkBench;
        Data.Name += "Player";
    }
    public override void InitData()
    {
        base.InitData();

        // 添加空值检查
        if (mod_Inventory == null)
        {
            Debug.LogError("无法获取Mod_Inventory组件！");
            return;
        }

        // 确保 InventoryInstances 至少有两个元素，避免索引越界
        if (mod_Inventory.InventoryInstances == null)
        {
            mod_Inventory.InventoryInstances = new List<Inventory>();
        }

        while (mod_Inventory.InventoryInstances.Count <= 1)
        {
            mod_Inventory.InventoryInstances.Add(null);
        }

        // 设置输出库存（如果不存在则使用背包的第一个库存）
        if (mod_Inventory.InventoryInstances[1] == null)
        {
            if (item == null || item.itemMods == null)
            {
                Debug.LogWarning("Inventory_WorkBench_Player: item 或 item.itemMods 为空，无法从 Hotbar 获取输出库存。");
                return;
            }

            var hotbarMod = item.itemMods.GetMod_ByID<Mod_Inventory>(ModText.Hotbar);
            if (hotbarMod != null && hotbarMod.InventoryRefDic != null && hotbarMod.InventoryRefDic.Count > 0)
            {
                mod_Inventory.InventoryInstances[1] = hotbarMod.InventoryRefDic.First().Value;
            }
            else
            {
                Debug.LogWarning("Inventory_WorkBench_Player: 未找到 Hotbar Inventory，输出库存保持为空。");
            }
        }
    }
    
}
