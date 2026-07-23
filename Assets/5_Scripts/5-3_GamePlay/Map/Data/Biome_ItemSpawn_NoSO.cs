using UnityEngine;

[System.Serializable]
public class Biome_ItemSpawn_NoSO
{
    public GameObject itemPrefab;

    public string itemName = "";

    [Min(1)]
    public int itemCount = 1;

    [Range(0f, 1f)]
    public float SpawnChance = 0.01f;

    [Tooltip("该资源额外的生成倍率。0 或 1 表示不调整，可用于补偿全局生成倍率。")]
    [Min(0f)]
    public float SpawnChanceMultiplier = 1f;

    public EnvironmentConditionRange environmentConditionRange;

    [Header("伴生生成（可选）")]
    [Tooltip("当前格成功生成带有该标签的物品后，再尝试伴生生成本物品。留空表示不启用。")]
    public string CompanionHostTag = "";

    [Tooltip("伴生生成的基础概率，仍会乘以全局倍率和本资源生成倍率。")]
    [Range(0f, 1f)]
    public float CompanionSpawnChance = 0f;

    [Tooltip("伴生物相对宿主格中心的位置偏移。")]
    public Vector2 CompanionSpawnOffset = new Vector2(0f, -0.25f);

    public void OnValidate()
    {
        if (itemPrefab == null)
        {
            Debug.LogError("Item prefab is null");
            return;
        }

        Item prefabItem = itemPrefab.GetComponent<Item>();
        if (prefabItem == null || prefabItem.itemData == null)
        {
            Debug.LogError($"Item prefab {itemPrefab.name} 缺少 Item 或 ItemData");
            return;
        }

        itemName = prefabItem.itemData.IDName;
    }
}
