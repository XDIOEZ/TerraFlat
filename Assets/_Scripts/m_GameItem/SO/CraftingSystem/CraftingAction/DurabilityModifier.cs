using UnityEngine;
[CreateAssetMenu(fileName = "DurabilityModifier", menuName = "Crafting/DurabilityModifier")]
public class DurabilityModifier : CraftingAction
{
    [Header("�;ö�����")]
    public float durabilityCost = 1f; // ���ĵ��;ö�
    [Header("��ʧ�;õ�Item��Tag����")]
    public string lostDurabilityItemTag = "Axe"; // ��ʧ�;öȵ���Ʒ��Tag����

    public override void Apply(IInventory inventory)
    {
        // �޸�Ϊ������ȡinventory.InventoryRefDic[InventoryName].Data.itemSlots ��TagΪlostDurabilityItemTag��ItemData ���������;�
        Inventory targetInventory = inventory.InventoryRefDic[InventoryName];
        if (targetInventory == null || targetInventory.Data == null || targetInventory.Data.itemSlots == null)
        {
            Debug.LogWarning("DurabilityModifier: Ŀ����Ϊ�ջ�δ��ʼ��");
            return;
        }

        // ��������е�������Ʒ��
        foreach (ItemSlot itemSlot in targetInventory.Data.itemSlots)
        {
            // �����Ʒ�ۺ���Ʒ�����Ƿ����
            if (itemSlot == null || itemSlot.itemData == null)
                continue;

            // �����Ʒ�Ƿ����ָ����Tag
            if (itemSlot.itemData.Tags != null && 
                itemSlot.itemData.Tags.MakeTag != null && 
                itemSlot.itemData.Tags.MakeTag.values != null && 
                itemSlot.itemData.Tags.MakeTag.values.Contains(lostDurabilityItemTag))
            {
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
                    
                    // �ҵ���������Ŀ����Ʒ���˳�ѭ��
                    break;
                }
                else
                {
                    Debug.LogWarning($"��Ʒ {itemSlot.itemData.IDName} û���;ö����ݣ��޷�Ӧ���;ö��޸�");
                }
            }
        }
    }
}