using UnityEngine;
public class DurabilityModifier : CraftingAction
{
    [Header("耐久度设置")]
    public float durabilityCost = 1f; // 消耗的耐久度
    [Header("损失耐久的Item的Tag名称")]
    public string lostDurabilityItemTag = "Axe"; // 损失耐久度的物品的Tag名称

    public override void Apply(IInventory inventory)
    {
        // 修改为遍历获取inventory.InventoryRefDic[InventoryName].Data.itemSlots 中Tag为lostDurabilityItemTag的ItemData 并消耗其耐久
        Inventory targetInventory = inventory.GetDefaultTargetInventory();
        if (targetInventory == null || targetInventory.Data == null || targetInventory.Data.itemSlots == null)
        {
            Debug.LogWarning("DurabilityModifier: 目标库存为空或未初始化");
            return;
        }

        // 遍历库存中的所有物品槽
        foreach (ItemSlot itemSlot in targetInventory.Data.itemSlots)
        {
            // 检查物品槽和物品数据是否存在
            if (itemSlot == null || itemSlot.itemData == null)
                continue;

            // 检查物品是否包含指定的Tag
            if (itemSlot.itemData.Tags != null && 
                itemSlot.itemData.Tags.Contains(lostDurabilityItemTag))
            {
                // 检查是否有耐久度数据
                if (itemSlot.itemData.Durability > 0 && itemSlot.itemData.MaxDurability > 0)
                {
                    // 共用基类耐久事务，损毁只消耗一件，不清掉整个材料堆。
                    targetInventory.Data.TryConsumeDurability(itemSlot.itemData, durabilityCost, out _);
                    
                    // 找到并处理了目标物品后退出循环
                    break;
                }
                else
                {
                    Debug.LogWarning($"物品 {itemSlot.itemData.IDName} 没有耐久度数据，无法应用耐久度修改");
                }
            }
        }
    }
}
