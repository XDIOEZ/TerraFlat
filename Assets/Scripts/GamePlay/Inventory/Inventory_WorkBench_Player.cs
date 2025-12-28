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

        // 获取物品模块
        mod_Inventory = GetComponent<Mod_Inventory>();

        // 添加空值检查
        if (mod_Inventory == null)
        {
            Debug.LogError("无法获取Mod_Inventory组件！");
            return;
        }

        // 设置输出库存（如果不存在则使用背包的第一个库存）
        if (!InventoryRefDic.ContainsKey("输出"))
        {
            InventoryRefDic["输出"] = item.itemMods.GetMod_ByID<Mod_Inventory>(ModText.Hotbar).InventoryRefDic.FirstOrDefault().Value;
        }
    }

}
