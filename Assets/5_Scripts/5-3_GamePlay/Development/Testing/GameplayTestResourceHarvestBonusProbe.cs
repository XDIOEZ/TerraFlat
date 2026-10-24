using System;
using System.Collections.Generic;
using FlatWorld.Combat;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>只采样正式采集奖励规则，概率、资格和掉落选择的断言由 Lua 持有。</summary>
public static class GameplayTestResourceHarvestBonusProbe
{
    #region 白名单与正式规则采样

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void Register() => GameplayTestSystemProbe.Register("combat", "harvest_hit_bonus", Capture);

    private static JObject Capture(JObject args)
    {
        var context = new CombatDamageContext
        {
            SourceIsPlayer = (byte)(GameplayTestSystemProbe.Flag(args, "sourcePlayer", true) ? 1 : 0),
            IsTrueDamage = (byte)(GameplayTestSystemProbe.Flag(args, "trueDamage", false) ? 1 : 0),
            ResourceToolKind = (int)GameplayTestSystemProbe.Number(args, "tool", (int)ResourceToolKind.Pickaxe),
            ResourceToolTier = 1,
            ResourceToolEfficiency = 1f,
            Attack = new CombatAttackKey { Source = new CombatIdentity
            {
                Backend = GameplayTestSystemProbe.Flag(args, "environment", false)
                    ? CombatBackend.Environment : CombatBackend.External,
                Value = GameplayTestSystemProbe.Flag(args, "validSource", true) ? 1UL : 0UL
            } }
        };
        var preferred = (ResourceToolKind)(int)GameplayTestSystemProbe.Number(args, "preferred", (int)ResourceToolKind.Pickaxe);
        float affinity = Mod_ResourceHarvest.ResolveAffinityMultiplier(preferred, 1,
            (ResourceToolKind)context.ResourceToolKind, context.ResourceToolTier, context.ResourceToolEfficiency);
        float damage = (float)GameplayTestSystemProbe.Number(args, "damage", 1d);
        double roll = GameplayTestSystemProbe.Number(args, "roll", 0d);
        double selection = GameplayTestSystemProbe.Number(args, "selection", 0d);

        var native = new List<RuntimeLootTableEntry>();
        var go = new List<LootEntry>();
        // Lua 空表会转换成空对象，显式空表不能回退成默认掉落表。
        JArray entries = args["entries"] == null
            ? new JArray(new JObject { ["id"] = "Stone", ["chance"] = 1, ["maximum"] = 10 },
                new JObject { ["id"] = "CopperOre", ["chance"] = 1, ["maximum"] = 3 })
            : args["entries"] as JArray ?? new JArray();
        foreach (JToken token in entries)
        {
            if (token.Type == JTokenType.Null) { native.Add(null); go.Add(null); continue; }
            string id = token.Value<string>("id");
            float chance = token.Value<float?>("chance") ?? 1f;
            int maximum = token.Value<int?>("maximum") ?? 1;
            native.Add(new RuntimeLootTableEntry(id, chance, 0, maximum));
            go.Add(new LootEntry { LootPrefabName = id, DropChance = chance, MinAmount = 0, MaxAmount = maximum });
        }

        string lootBefore = JArray.FromObject(go).ToString();
        bool eligible = ResourceHarvestHitBonus.IsEligible(context, damage, affinity,
            GameplayTestSystemProbe.Flag(args, "authority", true));
        int triggers = 0;
        for (int i = 0; i < 10000; i++)
            if (ResourceHarvestHitBonus.PassesRoll((i + .5d) / 10000d)) triggers++;
        bool equivalent = true;
        for (int i = 0; i <= 100; i++)
            equivalent &= ResourceHarvestHitBonus.SelectLoot(go, i / 100d) ==
                          ResourceHarvestHitBonus.SelectLoot(native, i / 100d);

        return new JObject
        {
            ["eligible"] = eligible,
            ["triggered"] = eligible && ResourceHarvestHitBonus.PassesRoll(roll),
            ["chance"] = ResourceHarvestHitBonus.TriggerChance,
            ["affinity"] = affinity,
            ["triggersPer10000"] = triggers,
            ["goItem"] = ResourceHarvestHitBonus.SelectLoot(go, selection) ?? string.Empty,
            ["nativeItem"] = ResourceHarvestHitBonus.SelectLoot(native, selection) ?? string.Empty,
            ["backendEquivalent"] = equivalent,
            ["lootTableUnchanged"] = lootBefore == JArray.FromObject(go).ToString(),
            ["invalidRollRejected"] = !ResourceHarvestHitBonus.PassesRoll(double.NaN) &&
                !ResourceHarvestHitBonus.PassesRoll(double.PositiveInfinity) && !ResourceHarvestHitBonus.PassesRoll(-1d),
            ["invalidDamageRejected"] = !ResourceHarvestHitBonus.IsEligible(context, float.NaN, affinity, true) &&
                !ResourceHarvestHitBonus.IsEligible(context, float.PositiveInfinity, affinity, true),
            ["invalidAffinityRejected"] = !ResourceHarvestHitBonus.IsEligible(context, damage, float.NaN, true) &&
                !ResourceHarvestHitBonus.IsEligible(context, damage, float.PositiveInfinity, true),
            ["invalidSelectionRejected"] = ResourceHarvestHitBonus.SelectLoot(go, double.NaN) == null &&
                ResourceHarvestHitBonus.SelectLoot(native, double.PositiveInfinity) == null,
            ["nullTablesEmpty"] = ResourceHarvestHitBonus.SelectLoot((IReadOnlyList<LootEntry>)null, 0d) == null &&
                ResourceHarvestHitBonus.SelectLoot((IReadOnlyList<RuntimeLootTableEntry>)null, 0d) == null
        };
    }

    #endregion
}
