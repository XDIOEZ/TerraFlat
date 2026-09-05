// 战利品条目类
using Newtonsoft.Json;
using Sirenix.OdinInspector;
using UnityEngine;

[System.Serializable]
public class LootEntry
{
    [Tooltip("战利品预制体")]
    [JsonIgnore] // 避免JSON序列化此字段
    [UnityEngine.Serialization.FormerlySerializedAs("LootPrefab")]
    public GameObject LootPrefab;

    [Tooltip("战利品的稳定 ItemDefinition ID，与通用外壳名称无关")]
    [SerializeField]
    [ReadOnly]
    public string LootPrefabName = "";

    [Tooltip("掉落概率 (0-1)")]
    [Range(0f, 1f)]
    public float DropChance = 1f;

    [Tooltip("最小掉落数量")]
    public int MinAmount = 1;

    [Tooltip("最大掉落数量")]
    public int MaxAmount = 1;

    // 编辑器验证方法，确保数值有效性
    public void OnValidate()
    {
#if UNITY_EDITOR
        // Prefab 只用于编辑器选取，生成物身份必须来自 Item 数据，不能取通用外壳名。
        if (LootPrefab != null)
        {
            Item lootItem = LootPrefab.GetComponent<Item>();
            if (lootItem?.itemData == null || string.IsNullOrWhiteSpace(lootItem.itemData.IDName))
                throw new System.InvalidOperationException($"战利品 Prefab {LootPrefab.name} 缺少物品 ID。");
            LootPrefabName = lootItem.itemData.IDName;
        }
        
        // 确保掉落数量范围有效
        MinAmount = Mathf.Max(0, MinAmount); // 确保最小数量不小于0
        MaxAmount = Mathf.Max(0, MaxAmount); // 确保最大数量不小于0
        
        // 确保最小数量不会超过最大数量（调整最大值而不是最小值）
        if (MinAmount > MaxAmount)
        {
            MaxAmount = MinAmount; // 调整最大数量为最小数量
        }
#endif
    }

    // 重置方法，用于清除引用但保留名称
    public void ClearPrefabReference()
    {
        LootPrefab = null;
    }
}
