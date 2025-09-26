using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[CreateAssetMenu(fileName = "DurabilityModifier", menuName = "Crafting/DurabilityModifier")]
public class DurabilityModifier : CraftingAction
{
    [Header("�;ö�����")]
    public float durabilityCost = 1f; // ���ĵ��;ö�
    
    public override void Apply(ItemSlot itemSlot)
    {
        if (itemSlot.itemData == null)
        {
            Debug.LogWarning("DurabilityModifier: �����ItemDataΪ��");
            return;
        }
        
        // ����Ƿ����;ö�����
        if (itemSlot.itemData.Durability > 0 && itemSlot.itemData.MaxDurability > 0)
        {
            // �����;ö�
            itemSlot.itemData.Durability -= durabilityCost;
            
            Debug.Log($"��Ʒ {itemSlot.itemData.IDName} �����;ö�: {durabilityCost}, ʣ���;ö�: {itemSlot.itemData.Durability}/{itemSlot.itemData.MaxDurability}");
            
            // ȷ���;öȲ�����0
            if (itemSlot.itemData.Durability <= 0)
            {
                itemSlot.itemData.Durability = 0;
                itemSlot.ClearData();
                Debug.Log($"��Ʒ {itemSlot.itemData?.IDName ?? "Unknown"} �;öȹ��㣬���������");
            }
        }
        else
        {
            Debug.LogWarning($"��Ʒ {itemSlot.itemData.IDName} û���;ö����ݣ��޷�Ӧ���;ö��޸�");
        }
    }
}