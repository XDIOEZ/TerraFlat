using System;
using System.Collections.Generic;
using FlatWorld.Combat;
using FlatWorld.Networking;
using UnityEngine;

/// <summary>匹配工具造成实际伤害时，独立抽取一件资源掉落，不消耗最终掉落表。</summary>
public static class ResourceHarvestHitBonus
{
    #region 奖励资格与抽样

    public const double TriggerChance = 0.05d;

    public static bool IsEligible(in CombatDamageContext context, float actualDamage,
        float affinityMultiplier, bool hasStateAuthority)
        => hasStateAuthority && context.SourceIsPlayer != 0 && context.IsTrueDamage == 0 &&
           context.Attack.Source.IsValid && context.Attack.Source.Backend != CombatBackend.Environment &&
           context.ResourceToolKind != (int)ResourceToolKind.None &&
           actualDamage > 0f && !float.IsInfinity(actualDamage) &&
           affinityMultiplier > 1f && !float.IsInfinity(affinityMultiplier);

    public static bool PassesRoll(double random) => random >= 0d && random < TriggerChance;

    // 两种后端只适配条目字段，共用按掉落概率加权且固定选一件的规则。
    public static string SelectLoot(IReadOnlyList<LootEntry> entries, double random)
        => Select(entries, random, entry => entry.LootPrefabName,
            entry => entry.DropChance, entry => entry.MaxAmount);

    public static string SelectLoot(IReadOnlyList<RuntimeLootTableEntry> entries, double random)
        => Select(entries, random, entry => entry.ItemId,
            entry => entry.Probability, entry => entry.MaxAmount);

    private static string Select<T>(IReadOnlyList<T> entries, double random,
        Func<T, string> itemId, Func<T, float> probability, Func<T, int> maximum) where T : class
    {
        if (entries == null || double.IsNaN(random) || random < 0d || random > 1d) return null;
        double total = 0d;
        for (int i = 0; i < entries.Count; i++) total += Weight(entries[i], itemId, probability, maximum);
        if (total <= 0d) return null;

        double remaining = random * total;
        string last = null;
        for (int i = 0; i < entries.Count; i++)
        {
            T entry = entries[i];
            double weight = Weight(entry, itemId, probability, maximum);
            if (weight <= 0d) continue;
            last = itemId(entry);
            if (remaining < weight) return last;
            remaining -= weight;
        }
        return last; // Unity 的随机值可以等于一，端点仍选最后一个有效条目。
    }

    private static double Weight<T>(T entry, Func<T, string> itemId,
        Func<T, float> probability, Func<T, int> maximum) where T : class
    {
        if (entry == null || string.IsNullOrWhiteSpace(itemId(entry)) || maximum(entry) <= 0) return 0d;
        float chance = probability(entry);
        return chance > 0f && !float.IsInfinity(chance) ? Math.Min(1d, chance) : 0d;
    }

    #endregion

    #region 权威命中接入

    public static void TrySpawn(DamageReceiverDamageInfo hit, ResourceToolKind weakTool)
    {
        if (hit?.Receiver?.Data?.LootTable == null || hit.Receiver.Data.LootTable.Count == 0 ||
            hit.Context.ResourceToolKind == (int)ResourceToolKind.None) return;

        CombatDamageContext context = hit.Context;
        float affinity = Mod_ResourceHarvest.ResolveAffinityMultiplier(weakTool, 1,
            (ResourceToolKind)context.ResourceToolKind, context.ResourceToolTier, context.ResourceToolEfficiency);
        if (hit.ReceiverItem?.itemMods != null)
            foreach (Module module in hit.ReceiverItem.itemMods.Mods.Values)
                if (module is Mod_ResourceHarvest harvest)
                    affinity *= harvest.GetDamageMultiplier(context);

        if (!IsEligible(context, hit.DamageValue, affinity, GameNetwork.HasStateAuthority) ||
            !PassesRoll(UnityEngine.Random.value)) return;

        Spawn(SelectLoot(hit.Receiver.Data.LootTable, UnityEngine.Random.value), hit.HitPosition);
    }

    public static void TrySpawn(in CombatDamageContext context, float actualDamage,
        float affinityMultiplier, string lootTableId, Vector2 position)
    {
        if (!IsEligible(context, actualDamage, affinityMultiplier, GameNetwork.HasStateAuthority) ||
            string.IsNullOrWhiteSpace(lootTableId) || GameRes.ExistingInstance == null ||
            !GameRes.ExistingInstance.TryGetLootTable(lootTableId, out RuntimeLootTable table) ||
            !PassesRoll(UnityEngine.Random.value)) return;

        Spawn(SelectLoot(table.Entries, UnityEngine.Random.value), position);
    }

    // 小奖励固定一件，不叠加死亡产量倍率，也不扣资源储量。
    private static void Spawn(string itemId, Vector2 position)
    {
        if (string.IsNullOrWhiteSpace(itemId)) return;
        try { DroppedItemService.SpawnLoot(itemId, position, 1f); }
        // 附加奖励失败只记错误，不能打断已经提交的伤害、反馈和死亡收尾。
        catch (Exception exception) { Debug.LogException(exception); }
    }

    #endregion
}
