using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>配置一种按燃料标签累计并产出的熔炉副产物规则。</summary>
[Serializable]
public sealed class FurnaceFuelByproductRule
{
    /// <summary>跨存档稳定标识。</summary>
    public string id;

    /// <summary>被消费燃料必须至少拥有的一个标签。</summary>
    public List<string> inputTags = new();

    /// <summary>累计多少件匹配燃料后产出一次。</summary>
    public int itemsPerOutput = 1;

    /// <summary>本规则创建的物品定义 ID。</summary>
    public string outputItemId;
}

/// <summary>累计已消费燃料并把副产物提交到指定燃料库存。</summary>
internal static class FurnaceFuelByproductProcessor
{
    #region 规则校验

    /// <summary>校验规则标识、标签和产出数量，避免进度无法稳定保存。</summary>
    public static void ValidateRules(IReadOnlyList<FurnaceFuelByproductRule> rules)
    {
        if (rules == null || rules.Count == 0)
            return;

        HashSet<string> ruleIds = new(StringComparer.Ordinal);
        for (int i = 0; i < rules.Count; i++)
        {
            FurnaceFuelByproductRule rule = rules[i];
            if (rule == null || string.IsNullOrWhiteSpace(rule.id) || !ruleIds.Add(rule.id))
                throw new InvalidOperationException("熔炉燃料副产物规则必须使用唯一且非空的 id。");
            if (rule.inputTags == null || rule.inputTags.Count == 0 ||
                rule.inputTags.Exists(string.IsNullOrWhiteSpace))
                throw new InvalidOperationException($"熔炉燃料副产物规则 {rule.id} 必须配置有效 inputTags。");
            if (rule.itemsPerOutput <= 0)
                throw new InvalidOperationException($"熔炉燃料副产物规则 {rule.id} 的 itemsPerOutput 必须大于 0。");
            if (string.IsNullOrWhiteSpace(rule.outputItemId))
                throw new InvalidOperationException($"熔炉燃料副产物规则 {rule.id} 缺少 outputItemId。");
        }
    }

    #endregion

    #region 消耗累计与产出

    /// <summary>为匹配标签的燃料累计消费数，并把达到门槛的产出记入待交付数。</summary>
    public static bool RecordConsumedFuel(
        ItemData consumedFuel,
        IReadOnlyList<FurnaceFuelByproductRule> rules,
        ModSmeltingData data)
    {
        if (consumedFuel?.Tags == null || rules == null || rules.Count == 0 || data == null)
            return false;

        data.FuelByproductProgress ??= new Dictionary<string, int>();
        data.PendingFuelByproductCount ??= new Dictionary<string, int>();
        bool changed = false;

        for (int i = 0; i < rules.Count; i++)
        {
            FurnaceFuelByproductRule rule = rules[i];
            if (rule?.inputTags == null || !consumedFuel.Tags.ContainsAnyTag(rule.inputTags))
                continue;

            data.FuelByproductProgress.TryGetValue(rule.id, out int progress);
            progress++;
            if (progress >= rule.itemsPerOutput)
            {
                int outputCount = progress / rule.itemsPerOutput;
                progress %= rule.itemsPerOutput;
                data.PendingFuelByproductCount.TryGetValue(rule.id, out int pendingCount);
                data.PendingFuelByproductCount[rule.id] = pendingCount + outputCount;
            }

            data.FuelByproductProgress[rule.id] = progress;
            changed = true;
        }

        return changed;
    }

    /// <summary>把待产副产物加入指定库存；空间不足时保留未交付数量等待后续重试。</summary>
    public static bool TryFlushPendingOutputs(
        Inventory_Data targetInventory,
        IReadOnlyList<FurnaceFuelByproductRule> rules,
        ModSmeltingData data,
        GameRes gameRes)
    {
        if (targetInventory == null || rules == null || rules.Count == 0 || data == null || gameRes == null)
            return false;

        data.PendingFuelByproductCount ??= new Dictionary<string, int>();
        bool changed = false;

        for (int i = 0; i < rules.Count; i++)
        {
            FurnaceFuelByproductRule rule = rules[i];
            if (rule == null || !data.PendingFuelByproductCount.TryGetValue(rule.id, out int pendingCount))
                continue;

            while (pendingCount > 0)
            {
                ItemData outputItem = gameRes.CreateItemData(rule.outputItemId);
                if (outputItem?.Stack == null)
                {
                    Debug.LogError($"熔炉燃料副产物规则 {rule.id} 找不到产出物品 {rule.outputItemId}。");
                    break;
                }

                outputItem.Stack.Amount = 1f;
                if (!targetInventory.TryAddItem(outputItem, false, out float availableAmount) ||
                    availableAmount < 1f - 0.0001f)
                    break;

                if (!targetInventory.TryAddItem(outputItem, true, out float addedAmount) ||
                    addedAmount < 1f - 0.0001f)
                    break;

                pendingCount--;
                changed = true;
            }

            if (pendingCount <= 0)
                data.PendingFuelByproductCount.Remove(rule.id);
            else
                data.PendingFuelByproductCount[rule.id] = pendingCount;
        }

        return changed;
    }

    #endregion
}
