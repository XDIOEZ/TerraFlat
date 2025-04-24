using System.Collections.Generic;

public interface IInventoryData_Dict
{
    Dictionary<string, Inventory_Data> Item_inventoryData { get; set; }
}