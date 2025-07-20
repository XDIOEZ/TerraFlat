using JetBrains.Annotations;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Inventory_Equipment : MonoBehaviour
{
    // Start is called before the first frame update
    public Inventory inventory;
    public List<string> EquipmentTypes = new List<string>();
    //盔甲防御力作用实体
    public IReceiveDamage Protect_entity;

    //实体血量
    public Hp Hp
    {
        get
        {
            return Protect_entity.Hp;
        }

        set
        {
            Protect_entity.Hp = value;
        }
    }
    //挂接实体防御
    public Defense Entity_Defense
    {
        get
        {
            return Protect_entity.DefenseValue;
        }

        set
        {
            Protect_entity.DefenseValue = value;
        }
    }

    void Awake()
    {
        inventory = GetComponent<Inventory>();
        inventory.Data.Name = "装备栏";
        inventory.Data.itemSlots = new List<ItemSlot>();

        // 初始化槽位（不创建抽象类实例）
        for (int i = 0; i < 4; i++)
        {
            ItemSlot slot = new ItemSlot();
            //slot.SetInventory(inventory);
            slot._ItemData = null; // 初始为空
            inventory.Data.itemSlots.Add(slot);
        }


        //设置所有槽位的BseType为Armor
        for (int i = 0; i < inventory.Data.itemSlots.Count; i++)
        {
            inventory.Data.itemSlots[i].CanAcceptItemType.Item_TypeTag[0] = "Armor";
        }

        /*        // 初始化装备类型
                EquipmentTypes = new List<string> { "Armor_Hand", "Armor_Head", "Armor_Chest", "Armor_Legs" };*/

        // 遍历所有槽位并设置其类型
        for (int i = 0; i < inventory.Data.itemSlots.Count; i++)
        {
            if (i < EquipmentTypes.Count)
            {
                inventory.Data.itemSlots[i].CanAcceptItemType.Item_TypeTag[1] = EquipmentTypes[i];
            }
            else
            {
             //   Debug.LogWarning("装备类型列表中的类型少于槽位数量。");
                break;
            }
        }

       // inventory.onSlotChanged += EquipArmor;
    }


    void Start()
    {
        //damageReceiver = GetComponentInParent<ItemUIManager>().CanvasBelong_Item.GetComponentInChildren<DamageReceiver>();

        Protect_entity = GetComponentInParent<ItemUIManager>().CanvasBelong_Item.GetComponent<IReceiveDamage>();

       // inventory.onSlotChanged -= inventory.ChangeItemData_Default;
    }

    public void EquipArmor(int slotIndex, ItemSlot InputSlot)
    {
        //inventory.onSlotChanged -= inventory.ChangeItemData_Default;

        //Debug.Log("开始装备");

        if (Protect_entity == null)
        {
            Debug.LogError("未找到 DamageReceiver 组件。");
            return;
        }

        //TODO:如果手和装备都为空,则不做任何操作
        if (InputSlot._ItemData == null && inventory.Data.GetItemData(slotIndex) == null)
        {
            return;
        }

        //卸下盔甲
        if (inventory.Data.GetItemData(slotIndex) != null)//如果手上有装备
        {
            Debug.Log("插槽有装备 || 手上无装备"); 
            if(InputSlot._ItemData==null)//如果输入插槽为空
            {
                Data_Armor armorData = (Data_Armor)inventory.Data.GetItemData(slotIndex);

                Entity_Defense -= armorData.defense;

                inventory.Data.ChangeItemData_Default(slotIndex, InputSlot);
                inventory.Data.Event_RefreshUI?.Invoke(slotIndex);
                return;
            }
        }

        //前提是手部为空,如果有装备则交换插槽{AddTargetInventory.ChangeItemData_Default( slotIndex, InputSlot );AddTargetInventory.onUIChanged?.Invoke(slotIndex); }
        //如果手上也是装备,则交换装备


        //装备盔甲
        if (InputSlot._ItemData.ItemTags.Item_TypeTag[0] == "Armor")
        {
            Debug.Log("插槽无装备|| 手上有装备");
            if (InputSlot._ItemData.ItemTags.Item_TypeTag[0] == inventory.Data.itemSlots[slotIndex].CanAcceptItemType.Item_TypeTag[0])
            {
               

                Data_Armor armorData = (Data_Armor)InputSlot._ItemData;

               Entity_Defense += armorData.defense;

                inventory.Data.ChangeItemData_Default(slotIndex, InputSlot);
                // AddTargetInventory.onUIChanged?.Invoke(slotIndex);
                Debug.Log("装备了: " + inventory.Data.itemSlots[slotIndex]._ItemData.IDName + "，防御值为 " + Entity_Defense + "。");
                return;
            }
        }


        Debug.Log("装备的不是盔甲。而是:" + InputSlot._ItemData.ItemTags.Item_TypeTag[0]);
        Debug.Log(slotIndex + " 号槽位的装备类型与槽位类型不匹配。");
        Debug.Log("装备的防具类型与槽位防具类型不匹配。" + InputSlot._ItemData.ItemTags.Item_TypeTag[1] + " 与 " + inventory.Data.itemSlots[slotIndex].CanAcceptItemType.Item_TypeTag[1]);
    } 
}


