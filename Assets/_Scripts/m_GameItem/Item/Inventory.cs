
using UnityEngine;
using NaughtyAttributes;
using Sirenix.OdinInspector;
using System.Collections.Generic;

public class Inventory : MonoBehaviour
{
    public IInventoryData DataInterface;

    public Item Belong_Item;

    public GameObject ItemSlot_Prefab;

    public Transform ItemSlot_Parent;

    public Inventory_Data Data;

    public List<ItemSlot_UI> itemSlotUIs;

    public Inventory DefaultTarget_Inventory;

    public

   void Start()
    {
        if(ItemSlot_Parent == null)
            ItemSlot_Parent = transform.GetChild(0);

        
        Belong_Item = GetComponentInParent<Item>();
        // 初始化数据接口
        DataInterface = GetComponentInParent<IInventoryData>();

        // 尝试获取现有数据，否则赋值默认数据
        if (!DataInterface.InventoryData_Dict.TryGetValue(gameObject.name, out var existingData) || existingData == null)
        {
            DataInterface.InventoryData_Dict[gameObject.name] = Data;
            DataInterface.Children_Inventory_GameObject[gameObject.name] = this;
        }
        else
        {
            Data = existingData;
        }

        // 同步子对象数量与 itemSlots 数量一致
        int currentCount = ItemSlot_Parent.childCount;
        int targetCount = Data.itemSlots.Count;

        // 删除多余的子对象（从后往前删除更安全）
        for (int i = currentCount - 1; i >= targetCount; i--)
        {
            DestroyImmediate(ItemSlot_Parent.GetChild(i).gameObject); // 或 Destroy() 视情况
        }

        // 添加缺少的子对象
        for (int i = currentCount; i < targetCount; i++)
        {
            GameObject item = Instantiate(ItemSlot_Prefab, ItemSlot_Parent, false); // false: 保持局部坐标
          //  item.name = $"Slot_{i}"; // 可选：命名方便调试
        }

        // 清空旧列表，重新填充 UI 槽位列表
        itemSlotUIs.Clear();
        for (int i = 0; i < ItemSlot_Parent.childCount; i++)
        {
            var ui = ItemSlot_Parent.GetChild(i).GetComponent<ItemSlot_UI>();
            if (ui != null)
                itemSlotUIs.Add(ui);
        }

        // 同步 UI 数据
        SyncData();

        // 若未命名，则赋默认名
        if (string.IsNullOrEmpty(Data.inventoryName))
            Data.inventoryName = gameObject.name;

        if (DefaultTarget_Inventory == null)
            DefaultTarget_Inventory = DataInterface.Children_Inventory_GameObject["手部插槽"];
    }


    //同步UI的Data
    public void SyncData()
    {
        for (int i = 0; i < itemSlotUIs.Count; i++)
        {
           ItemSlot_UI itemSlotUI = itemSlotUIs[i];
            itemSlotUI.ItemSlot = Data.itemSlots[i];
            itemSlotUI.OnItemClick += OnItemClick;
            itemSlotUI.ItemSlot.Belong_Inventory = this;
        }
    }

    public void SyncUI(int index)
    {
        itemSlotUIs[index].RefreshUI();
    }

    public virtual void OnItemClick(int index)
    {
        ItemSlot slot = Data.GetItemSlot(index);
    }

}