using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class Biome_ItemSpawn_NoSO
{
    public GameObject itemPrefab;

    public string itemName = "";
    //生成物品的数量
    public int itemCount = 1;

    public float SpawnChance = 0.01f;

    public EnvironmentConditionRange environmentConditionRange;

    public void OnValidate()
    {
        if (itemPrefab == null)
        {
            Debug.LogError("Item prefab is null");
            return;
        }
        itemName = itemPrefab.GetComponent<Item>().itemData.IDName;
    }
}
