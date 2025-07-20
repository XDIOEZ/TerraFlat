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
        //�Ӹ������ϻ�ȡIInventoryData_Dict�˽ӿڵ�ʵ��
       //  inventoryData_Dict = transform.parent.GetComponentInChildren<IInventoryData_Dict>();
       // Item_inventory_Data = inventoryData_Dict.Item_inventoryData;
        InitInventoryDataDictionaries();
    }

    [Button("д���ֵ�_Inventory")]
     
    private void WriteInventoryDataToDictionaries()
    {
        Inventory[] allInventories = GetComponentsInChildren<Inventory>();
        foreach (Inventory inventory in allInventories)
        {
            if (inventory.Data != null && !string.IsNullOrEmpty(inventory.Data.Name))
            {
                // �� Inventory �����ֵ�
                Item_inventory[inventory.Data.Name] = inventory;
            }
        }
    }
    [Button("��ʼ���ֵ�_Inventory_Data")]
    /// <summary>
    /// ���Ӷ���� Inventory ����е�����д���ֵ�
    /// </summary>
    private void InitInventoryDataDictionaries()
    {
        Inventory[] allInventories = GetComponentsInChildren<Inventory>();
        foreach (Inventory inventory in allInventories)
        {
            if (inventory.Data != null && !string.IsNullOrEmpty(inventory.Data.Name))
            {
                // �� Inventory_Data �����ֵ�
                Item_inventory_Data[inventory.Data.Name] = inventory.Data;
                // �� Inventory �����ֵ�
                Item_inventory[inventory.Data.Name] = inventory;
            }
        }
    }

    [Button("��ȡ�ֵ�")]
    /// <summary>
    /// ���ֵ��ж�ȡ Inventory ����
    /// </summary>
    private void ReadInventoryDataFromDictionaries()
    {
        // ��ȡ����
        // ��ȡ Item_inventory_Data �ֵ��е� Inventory_Data,���� Inventory_Data.inventoryName ��ȡ���е���Ʒ��Ϣ 
        // ����ֻ�Ǽ򵥵Ķ�ȡ��Ϣ������ʵ�����������Ӹ����߼�
        foreach (KeyValuePair<string, Inventory_Data> kvp in Item_inventory_Data)
        {
            string inventoryName = kvp.Key;
            Inventory_Data inventoryData = kvp.Value;

            // ���� inventoryName �� Item_inventory �ֵ��л�ȡ��Ӧ�� Inventory
            if (Item_inventory.ContainsKey(inventoryName))
            {
                Inventory inventory = Item_inventory[inventoryName];
                // ������Ը���ʵ������ʹ�� inventoryData �� inventory �е���Ϣ
                Debug.Log($"Inventory Name: {inventoryName}, Inventory Data: {inventoryData.Name}");
                inventory.Data = inventoryData;
            }
        }
    }
}
