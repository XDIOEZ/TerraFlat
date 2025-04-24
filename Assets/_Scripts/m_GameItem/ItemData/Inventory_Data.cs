
// 库存数据类
using MemoryPack;
using System.Collections.Generic;

[System.Serializable]
[MemoryPackable]
public partial class Inventory_Data
{
    // 背包名称
    public string inventoryName = string.Empty;
    // 保存物品的列表
    public List<ItemSlot> itemSlots = new List<ItemSlot>();

    // 构造函数
    [MemoryPackConstructor]
    public Inventory_Data(List<ItemSlot> itemSlots, string inventoryName)
    {
        this.itemSlots = itemSlots;
        this.inventoryName = inventoryName;
    }
    // 空白构造函数
    public Inventory_Data()
    {
    }
}