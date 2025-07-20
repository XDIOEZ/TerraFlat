using FastCloner.Code;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CloneTest : MonoBehaviour
{
    [ShowInInspector]
    public Dictionary<string, Inventory_Data> inventory_data = new Dictionary<string, Inventory_Data>();
[ShowInInspector]
    public Dictionary<string, Inventory_Data> inventory_data_Clone = new Dictionary<string, Inventory_Data>();

    public Inventory inventory;

    [Button]
    private void Test()
    {
        inventory_data["test"] =inventory.Data;
         inventory_data_Clone = FastCloner.FastCloner.DeepClone(inventory_data);
    }
}
