using NaughtyAttributes;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

/// <summary>
/// 用于管理与物品清单相关的数据和操作的接口。
/// </summary>
public interface IInventoryData
{
    /// <summary>
    /// 以清单名为键，映射到对应的 Inventory 对象。
    /// </summary>
    Dictionary<string, Inventory> Children_Inventory_GameObject { get; set; }

    /// <summary>
    /// 以清单名为键，存储 Inventory_Data 数据。
    /// </summary>
    Dictionary<string, Inventory_Data> InventoryData_Dict { get; set; }

    /// <summary>
    /// 当 Data_InventoryData 发生变化时触发的事件。
    /// </summary>
    UltEvent OnInventoryData_Dict_Changed { get; }

    /// <summary>
    /// 当前手部选择栏。
    /// </summary>
    [Tooltip("选择")]
    SelectSlot SelectSlot { get; set; }
/*
    /// <summary>
    /// 获取指定名称的 Inventory_Data，默认获取 "Default"。
    /// </summary>
    Inventory_Data GetInventoryData(string inventoryName = "Default")
    {
        if (Data_InventoryData.ContainsKey(inventoryName))
            return Data_InventoryData[inventoryName];
        else
            return null;
    }

    /// <summary>
    /// 设置 Inventory_Data 并触发事件。
    /// </summary>
    void SetInventoryData(Inventory_Data inventoryData)
    {
        Data_InventoryData[inventoryData.inventoryName] = inventoryData;
        OnInventoryData_Dict_Changed.Invoke();
    }

    /// <summary>
    /// 设置子物品清单的归属关系，并更新字典与 UI。
    /// </summary>
    void FillDict_SetBelongItem(Transform item)
    {
        Inventory[] inventories = item.GetComponentsInChildren<Inventory>();
        Item itemComponent = item.GetComponentInChildren<Item>();

        foreach (var inventory in inventories)
        {

            if (inventory.UI.itemSlots_UI[0] == null)
            {
                inventory.UI.Instantiate_ItemSlotUI();
            }

            inventory.UI.AddListenersToItemSlots();
            Children_Inventory_GameObject[inventory.Data.inventoryName] = inventory;
        }

        // 设置每个物品的目标发送栏为手部选择栏
        foreach (var inventory in Children_Inventory_GameObject.Values)
        {
            inventory.UI.TargetSendItemSlot = SelectSlot;
        }
    }

    /// <summary>
    /// 初始化指定 Inventory 的数据与插槽。
    /// </summary>
    void InitializeInventory(Inventory inventory, int inventorySize)
    {
        if (Data_InventoryData.ContainsKey(inventory.Data.inventoryName))
            return;

        inventory.Data.itemSlots = new List<ItemSlot>();
        for (int i = 0; i < inventorySize; i++)
        {
            inventory.Data.itemSlots.Add(new ItemSlot(i));
        }

        foreach (var itemSlot in inventory.Data.itemSlots)
        {
            itemSlot.Belong_Inventory = inventory;
        }

        inventory.UI.Instantiate_ItemSlotUI();
    }

    /// <summary>
    /// 初始化方法（目前为空，实现类可根据需要自定义）。
    /// </summary>
    void Awake()
    {
        // 预留初始化逻辑
    }*/
}

