using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>一项可配置的农作物收获产物。</summary>
[Serializable]
public sealed class CropYieldEntry
{
    [Tooltip("Manifest 中存在的物品 ID。")]
    public string itemId;

    [Min(1)] public int minAmount = 1;
    [Min(1)] public int maxAmount = 1;

    [Range(0f, 1f)]
    public float probability = 1f;
}

/// <summary>
/// 通用农作物产物动作：根据 JSON 产物表生成世界掉落物。
/// 每项概率和随机数量都独立乘世界掉落倍率，包含概率为 1 的主产物。
/// </summary>
public sealed class Mod_CropYield : Module, ICropHarvestAction
{
    #region 模块数据

    public Ex_ModData ModData = new();

    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = value as Ex_ModData ??
            throw new ArgumentException("[Mod_CropYield] 模块数据类型错误。", nameof(value));
    }

    public override string CanonicalModuleId => ModText.CropYield;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;

    #endregion

    #region 配置

    [Header("收获产物")]
    public List<CropYieldEntry> outputs = new();

    #endregion

    #region 生命周期

    public override void Awake()
    {
        ModData ??= new Ex_ModData();
        ModData.ID = ModText.CropYield;
        base.Awake();
    }

    public override void Load()
    {
        item ??= GetComponentInParent<Item>();
        if (item == null)
            throw new MissingComponentException("[Mod_CropYield] 未找到所属 Item。");

        ValidateConfiguration();
    }

    public override void Save()
    {
        // 产物表来自 ItemDefinition，本模块没有独立运行时状态。
    }

    #endregion

    #region 收获动作

    public void Execute(CropHarvestContext context)
    {
        if (context.CropItem != item)
            throw new InvalidOperationException("[Mod_CropYield] 收获上下文与模块所属 Item 不一致。");
        if (ItemMgr.Instance == null)
            throw new InvalidOperationException("[Mod_CropYield] ItemMgr 尚未初始化。");

        float lootMultiplier = Mathf.Max(0f, GameDifficultyService.Current.World.LootAmountMultiplier);
        foreach (CropYieldEntry output in outputs)
        {
            float effectiveProbability = Mathf.Clamp01(output.probability * lootMultiplier);
            if (UnityEngine.Random.value > effectiveProbability)
                continue;

            int baseAmount = UnityEngine.Random.Range(output.minAmount, output.maxAmount + 1);
            int finalAmount = GameDifficultyService.ScaleRandomizedAmount(baseAmount, lootMultiplier);
            if (finalAmount <= 0)
                continue;

            for (int i = 0; i < finalAmount; i++)
                SpawnWorldItem(context, output.itemId);
        }
    }

    /// <summary>生成一个数量为 1 的掉落实例，让每份产物独立弹出。</summary>
    private static void SpawnWorldItem(CropHarvestContext context, string itemId)
    {
        GameObject parent = context.CropItem.transform.parent != null
            ? context.CropItem.transform.parent.gameObject
            : null;
        Item product = ItemMgr.Instance.InstantiateItem(
            itemId,
            context.WorldPosition,
            Quaternion.identity,
            Vector3.one,
            parent);
        if (product == null)
            throw new MissingReferenceException($"[Mod_CropYield] 无法实例化收获物品：{itemId}");

        product.Load();
        product.SetInHand(false);
        if (product.itemData?.Stack == null)
            throw new MissingComponentException($"[Mod_CropYield] 收获物品 {itemId} 缺少堆叠数据。");

        product.itemData.Stack.Amount = 1;
        product.DropInRange();
    }

    #endregion

    #region 配置校验

    private void ValidateConfiguration()
    {
        if (outputs == null || outputs.Count == 0)
            throw new InvalidOperationException("[Mod_CropYield] 至少需要配置一项收获产物。");
        if (GameRes.Instance == null)
            throw new InvalidOperationException("[Mod_CropYield] GameRes 尚未初始化。");

        for (int i = 0; i < outputs.Count; i++)
        {
            CropYieldEntry output = outputs[i] ??
                throw new InvalidOperationException($"[Mod_CropYield] outputs[{i}] 为空。");
            if (string.IsNullOrWhiteSpace(output.itemId))
                throw new InvalidOperationException($"[Mod_CropYield] outputs[{i}].itemId 不能为空。");
            if (!GameRes.Instance.TryGetItemDefinition(output.itemId, out _))
                throw new InvalidOperationException($"[Mod_CropYield] 找不到产物定义：{output.itemId}");
            if (output.minAmount < 1 || output.maxAmount < output.minAmount)
                throw new InvalidOperationException($"[Mod_CropYield] {output.itemId} 的数量范围无效。");
            if (output.probability < 0f || output.probability > 1f)
                throw new InvalidOperationException($"[Mod_CropYield] {output.itemId} 的 probability 必须位于 0～1。");
        }
    }

    private void OnValidate()
    {
        if (outputs == null)
            return;

        foreach (CropYieldEntry output in outputs)
        {
            if (output == null)
                continue;
            output.minAmount = Mathf.Max(1, output.minAmount);
            output.maxAmount = Mathf.Max(output.minAmount, output.maxAmount);
            output.probability = Mathf.Clamp01(output.probability);
        }
    }

    #endregion
}
