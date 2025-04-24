
// ���������
using MemoryPack;
using System.Collections.Generic;

[System.Serializable]
[MemoryPackable]
public partial class Inventory_Data
{
    // ��������
    public string inventoryName = string.Empty;
    // ������Ʒ���б�
    public List<ItemSlot> itemSlots = new List<ItemSlot>();

    // ���캯��
    [MemoryPackConstructor]
    public Inventory_Data(List<ItemSlot> itemSlots, string inventoryName)
    {
        this.itemSlots = itemSlots;
        this.inventoryName = inventoryName;
    }
    // �հ׹��캯��
    public Inventory_Data()
    {
    }
}