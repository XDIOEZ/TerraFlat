using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class ItemInventoryManager : MonoBehaviour
{
    [ShowNonSerializedField]
    public Dictionary<string, Inventory_Data> Item_inventory_Data;
    [ShowNonSerializedField]
    public Dictionary<string, Inventory> Item_inventory = new Dictionary<string, Inventory>();
    //[/*ShowNonSerializedField]
    //public IInventoryData_Dict inventoryData_Dict;*/

    private void Start()
    {
        //从父对象上获取IInventoryData_Dict此接口的实现
       //  inventoryData_Dict = transform.parent.GetComponentInChildren<IInventoryData_Dict>();
       // Item_inventory_Data = inventoryData_Dict.Item_inventoryData;
        InitInventoryDataDictionaries();
    }

    [Button("写入字典_Inventory")]
     
    private void WriteInventoryDataToDictionaries()
    {
        Inventory[] allInventories = GetComponentsInChildren<Inventory>();
        foreach (Inventory inventory in allInventories)
        {
            if (inventory.Data != null && !string.IsNullOrEmpty(inventory.Data.Name))
            {
                // 将 Inventory 存入字典
                Item_inventory[inventory.Data.Name] = inventory;
            }
        }
    }
    [Button("初始化字典_Inventory_Data")]
    /// <summary>
    /// 将子对象的 Inventory 组件中的数据写入字典
    /// </summary>
    private void InitInventoryDataDictionaries()
    {
        Inventory[] allInventories = GetComponentsInChildren<Inventory>();
        foreach (Inventory inventory in allInventories)
        {
            if (inventory.Data != null && !string.IsNullOrEmpty(inventory.Data.Name))
            {
                // 将 Inventory_Data 存入字典
                Item_inventory_Data[inventory.Data.Name] = inventory.Data;
                // 将 Inventory 存入字典
                Item_inventory[inventory.Data.Name] = inventory;
            }
        }
    }

    [Button("读取字典")]
    /// <summary>
    /// 从字典中读取 Inventory 数据
    /// </summary>
    private void ReadInventoryDataFromDictionaries()
    {
        // 读取代码
        // 获取 Item_inventory_Data 字典中的 Inventory_Data,根据 Inventory_Data.inventoryName 获取其中的物品信息 
        // 这里只是简单的读取信息，根据实际情况可以添加更多逻辑
        foreach (KeyValuePair<string, Inventory_Data> kvp in Item_inventory_Data)
        {
            string inventoryName = kvp.Key;
            Inventory_Data inventoryData = kvp.Value;

            // 根据 inventoryName 从 Item_inventory 字典中获取对应的 Inventory
            if (Item_inventory.ContainsKey(inventoryName))
            {
                Inventory inventory = Item_inventory[inventoryName];
                // 这里可以根据实际需求使用 inventoryData 和 inventory 中的信息
                Debug.Log($"Inventory Name: {inventoryName}, Inventory Data: {inventoryData.Name}");
                inventory.Data = inventoryData;
            }
        }
    }
}
