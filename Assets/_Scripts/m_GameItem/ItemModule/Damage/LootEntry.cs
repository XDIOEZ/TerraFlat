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

    [Tooltip("战利品预制体名称")]
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


    // 公共方法用于更新预制体名称（供编辑器使用）
    public void UpdatePrefabName()
    {
#if UNITY_EDITOR
        if (LootPrefab != null)
        {
            LootPrefabName = LootPrefab.name;
        }
#endif
    }

    // 重置方法，用于清除引用但保留名称
    public void ClearPrefabReference()
    {
        LootPrefab = null;
    }
}