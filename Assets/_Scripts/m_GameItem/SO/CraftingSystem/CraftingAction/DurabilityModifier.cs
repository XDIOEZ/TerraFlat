using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[CreateAssetMenu(fileName = "DurabilityModifier", menuName = "Crafting/DurabilityModifier")]
public class DurabilityModifier : CraftingAction
{
    [Header("耐久度设置")]
    public float durabilityCost = 1f; // 消耗的耐久度
    
    public override void Apply(ItemSlot itemSlot)
    {
        if (itemSlot.itemData == null)
        {
            Debug.LogWarning("DurabilityModifier: 传入的ItemData为空");
            return;
        }
        
        // 检查是否有耐久度数据
        if (itemSlot.itemData.Durability > 0 && itemSlot.itemData.MaxDurability > 0)
        {
            // 减少耐久度
            itemSlot.itemData.Durability -= durabilityCost;
            
            Debug.Log($"物品 {itemSlot.itemData.IDName} 消耗耐久度: {durabilityCost}, 剩余耐久度: {itemSlot.itemData.Durability}/{itemSlot.itemData.MaxDurability}");
            
            // 确保耐久度不低于0
            if (itemSlot.itemData.Durability <= 0)
            {
                itemSlot.itemData.Durability = 0;
                itemSlot.ClearData();
                Debug.Log($"物品 {itemSlot.itemData?.IDName ?? "Unknown"} 耐久度归零，已清除数据");
            }
        }
        else
        {
            Debug.LogWarning($"物品 {itemSlot.itemData.IDName} 没有耐久度数据，无法应用耐久度修改");
        }
    }
}