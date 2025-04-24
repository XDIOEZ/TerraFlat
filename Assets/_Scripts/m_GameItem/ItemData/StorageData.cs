using MemoryPack;
using NaughtyAttributes;
using System.Collections.Generic;

[System.Serializable, MemoryPackable]
public partial class StorageData : ItemData
{
    [ShowNonSerializedField]
    public Dictionary<string, Inventory_Data> Item_inventoryData_Dict = new Dictionary<string, Inventory_Data>();
}