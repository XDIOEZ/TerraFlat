using NaughtyAttributes;
using System.Collections.Generic; // 引入泛型集合的命名空间，例如 Dictionary。
using UltEvents;
using UnityEngine; // 引入 UltEvents 命名空间，用于事件处理。

public interface IInventoryData // 定义一个接口，用于管理与物品清单相关的数据和操作。
{
    // 一个字典，用于将物品清单的名称映射到与 GameObject 相关联的 Inventory 对象。
    public Dictionary<string, Inventory> Children_Inventory_GameObject { get; set; }

    // 一个字典，用于存储物品清单数据，以物品清单的名称作为键。
    public Dictionary<string, Inventory_Data> Data_InventoryData { get; set; }

    // 当 Inventory_Data_Dict 发生更改时触发的事件。
    public UltEvent OnInventoryData_Dict_Changed { get; set; }

    [Tooltip("选择")]
    public SelectSlot SelectSlot { get; set; }

    // 方法：根据名称获取物品清单数据，默认为 "Default"。
    Inventory_Data GetInventoryData(string inventoryName = "Default")
    {
        // 检查字典中是否包含指定的物品清单名称。
        if (Data_InventoryData.ContainsKey(inventoryName))
        {
            return Data_InventoryData[inventoryName]; // 如果找到则返回对应的物品清单数据。
        }
        else
        {
            return null; // 如果未找到，返回 null。
        }
    }

    // 方法：更新物品清单数据并触发更改事件。
    void SetInventoryData(Inventory_Data inventoryData)
    {
        // 使用提供的数据更新字典。
        Data_InventoryData[inventoryData.inventoryName] = inventoryData;
        OnInventoryData_Dict_Changed.Invoke(); // 通知事件的监听者字典发生了更改。
    }

    // 方法：将物品归属设置为当前对象，并更新子物品清单映射。
    void FillDict_SetBelongItem(UnityEngine.Transform Item)
    {
        // 获取指定物品下所有子对象中的 Inventory 组件。
        Inventory[] Inventory_s = Item.GetComponentsInChildren<Inventory>();
        foreach (var inventory in Inventory_s)
        {
            inventory.Belong_Item = this; // 将物品清单的归属设置为当前对象。

            inventory.SyncSlotBelongInventory();
            if (inventory.UI.itemSlots_UI[0] == null)
            {
                inventory.UI.Instantiate_ItemSlotUI();
            }
            inventory.UI.AddListenersToItemSlots();

            Children_Inventory_GameObject[inventory.Data.inventoryName] = inventory; // 更新字典，将物品清单名称与其对应对象关联。

            //遍历这个物品的所有的Value，Children_Inventory_GameObject
            foreach (var inventory_ in Children_Inventory_GameObject.Values)
            {
                inventory_.UI.TargetSendItemSlot = SelectSlot;
            }
        }
        // 如果需要调试，可以取消以下行的注释以记录物品的名称。
        // Debug.Log("设置物品归属对象" + Item.name);
    }

    // 方法：根据指定的大小初始化物品清单数据和插槽。
    void InitializeInventory(Inventory inventory, int inventorySize)
    {
        // 如果字典中已经存在此物品清单的数据，则跳过初始化。
        if (Data_InventoryData.ContainsKey(inventory.Data.inventoryName))
        {
            return; // 直接退出方法。
        }

        // 根据指定大小初始化物品插槽列表（TODO：需完成具体实现）。
        inventory.Data.itemSlots = new List<ItemSlot>();
        for (int i = 0; i < inventorySize; i++)
        {
            inventory.Data.itemSlots.Add(new ItemSlot(i)); // 按索引添加新插槽。
        }

        // 设置每个物品插槽的归属物品清单。
        foreach (var itemSlot in inventory.Data.itemSlots)
        {
            itemSlot.Belong_Inventory = inventory; // 为插槽分配所属的物品清单引用。
        }

        // 创建物品插槽的 UI 表现形式。
        inventory.UI.Instantiate_ItemSlotUI();
    }

    // 方法：用于在 Awake 时执行初始化逻辑。
    public void Awake()
    {
        // 预留初始化逻辑。
    }
}
