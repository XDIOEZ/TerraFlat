// 战利品条目类
using Newtonsoft.Json;
using Sirenix.OdinInspector;
using UnityEngine;
using FlatWorld.WorldModel;

#region 掉落数量分布

public enum LootAmountDistribution { Uniform, DiscreteTable }

/// <summary>所有掉落后端共用显式概率表或均匀数量抽样。</summary>
public static class LootAmountRules
{
    public static void Validate(int minimum, int maximum, LootAmountDistribution distribution,
        string probabilityTableId = null)
    {
        if (minimum < 0 || maximum < minimum)
            throw new System.ArgumentException("掉落数量范围无效。");
        if (distribution != LootAmountDistribution.Uniform && distribution != LootAmountDistribution.DiscreteTable)
            throw new System.ArgumentException("未知的掉落数量分布。");
        if (distribution == LootAmountDistribution.DiscreteTable)
        {
            var table = DiscreteProbabilityConfiguration.Capture().Require(probabilityTableId ?? string.Empty);
            for (int i = 0; i < table.Count; i++)
                if (table.GetValue(i) != System.Math.Floor(table.GetValue(i)) ||
                    table.GetValue(i) < minimum || table.GetValue(i) > maximum)
                    throw new System.ArgumentException("掉落概率表结果须为数量范围内的整数。");
        }
    }

    public static int Sample(int minimum, int maximum, LootAmountDistribution distribution, double random,
        string probabilityTableId = null)
    {
        if (distribution == LootAmountDistribution.DiscreteTable)
            return (int)DiscreteProbability.Sample(DiscreteProbabilityConfiguration.Capture().Require(probabilityTableId ?? string.Empty), random);
        Validate(minimum, maximum, distribution, probabilityTableId);
        if (double.IsNaN(random) || random < 0d || random > 1d)
            throw new System.ArgumentOutOfRangeException(nameof(random));
        if (minimum == maximum || random == 0d) return minimum;
        if (random == 1d) return maximum;
        if (distribution == LootAmountDistribution.Uniform)
            return (int)(minimum + (long)(random * ((long)maximum - minimum + 1L)));

        throw new System.ArgumentException("未知的掉落数量分布。");
    }
}

#endregion

[System.Serializable]
public class LootEntry
{
    #region 掉落配置与抽样
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

    public LootAmountDistribution AmountDistribution;
    public string AmountProbabilityTableId;

    public int SampleAmount(double random) => LootAmountRules.Sample(MinAmount, MaxAmount, AmountDistribution, random, AmountProbabilityTableId);
    public int RollAmount() => SampleAmount(Random.value);

    // 编辑器验证方法，确保数值有效性
    public void OnValidate()
    {
#if UNITY_EDITOR
        // Prefab 只用于编辑器选取，生成物身份必须来自 Item 数据，不能取通用外壳名。
        if (LootPrefab != null)
        {
            Item lootItem = LootPrefab.GetComponent<Item>();
            string prefabItemId = lootItem?.itemData?.IDName;
            // 导入或构建时引用的 Prefab 可能尚未反序列化完成，保留已有 ID，引用有效性交给内容目录加载时校验。
            if (!string.IsNullOrWhiteSpace(prefabItemId))
                LootPrefabName = prefabItemId;
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
    #endregion
}
