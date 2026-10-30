using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 盾牌正式规则采样
    private static void RegisterShieldOperations(Dictionary<string, Func<JObject, JObject>> result)
    {
        result.Add("combat/shield_actions", ShieldActions);
        result.Add("combat/shield_hit_stamina", ShieldHitStamina);
    }

    private static JObject ShieldActions(JObject args)
    {
        var settings = new ShieldActionSettings();
        var clock = new ShieldActionClock();
        bool started = Text(args, "action", "primary") == "parry" ? clock.TryParry(settings) : clock.TryPress(settings);
        clock.Advance((float)Number(args, "beforeRelease", 0));
        bool bashOnRelease = Flag(args, "release", false) && clock.Release();
        if (Flag(args, "cancel", false)) clock.Cancel();
        clock.Advance((float)Number(args, "afterRelease", 0));
        string kind = clock.Kind.ToString();
        float defense = clock.Defense, speed = clock.SpeedMultiplier;
        bool secondParry = clock.TryParry(settings);
        var rules = EquipmentSlotCatalog.CreateDefaultRules();
        return new JObject
        {
            ["started"] = started, ["state"] = kind, ["defense"] = defense, ["speed"] = speed,
            ["bashOnRelease"] = bashOnRelease, ["canParryAgain"] = secondParry,
            ["offhandIndex"] = rules.FindIndex(rule => rule.Id == EquipmentSlotCatalog.OffHand),
            ["oldFeetIndex"] = rules.FindIndex(rule => rule.Id == EquipmentSlotCatalog.FeetInner),
            ["offhandTag"] = EquipmentSlotCatalog.OffHandTag
        };
    }
    #endregion

    #region 举盾受击体力正式链路采样
    private static JObject ShieldHitStamina(JObject args)
    {
        if (args["steps"] is not JArray steps || steps.Count == 0 || steps.Count > 64)
            throw new ArgumentException("举盾受击采样需要 1 至 64 个步骤。");
        using var fixture = new GameplayTestInvulnerabilityFixture();
        Mod_DamageReceiver health = fixture.Health;
        Item actor = health.item;
        GameObject root = actor.gameObject;
        var stamina = root.AddComponent<Mod_Stamina>();
        stamina.ModuleInit(actor, new Ex_ModData_MemoryPackable { ID = ModText.Stamina, Enabled = true });
        stamina.Data = new Mod_Stamina.StaminaData { CurrentStamina = (float)Number(args, "stamina", 100d), MaxStamina = 100f };
        actor.itemMods.AddMod(stamina);
        var roll = root.AddComponent<Mod_Roll>();
        roll.ModuleInit(actor, new Ex_ModData { ID = Mod_Roll.ModuleId, Enabled = true });
        roll.StaminaCost = (float)Number(args, "rollCost", Mod_Roll.DefaultStaminaCost);
        if (!Flag(args, "missingRoll", false)) actor.itemMods.AddMod(roll);
        var hotbar = root.AddComponent<Mod_HotBar>();
        hotbar.ModuleInit(actor, new Ex_ModData_MemoryPackable { ID = ModText.Hotbar, Enabled = true });
        actor.itemMods.AddMod(hotbar);
        var shieldRoot = new GameObject("Shield");
        shieldRoot.transform.SetParent(root.transform, false);
        var shieldItem = shieldRoot.AddComponent<GameItem>();
        shieldItem.BindData(new Data_GeneralItem { IDName = "test:guard_shield", Guid = shieldRoot.GetInstanceID(),
            inHand = true, Stack = new ItemStack(), MaxDurability = 100f, Durability = 100f,
            Tags = new List<string> { EquipmentDurabilityRules.AttackEquipmentTag } });
        shieldItem.Owner = actor;
        var shield = shieldRoot.AddComponent<Mod_Shield>();
        shield.ModuleInit(shieldItem, new Ex_ModData { ID = Mod_Shield.ModuleId, Enabled = true });
        shieldItem.itemMods.AddMod(shield);
        hotbar.CurentSelectItem = shieldItem;
        var combat = root.AddComponent<Mod_ShieldCombat>();
        combat.ModuleInit(actor, new Ex_ModData { ID = Mod_ShieldCombat.ModuleId, Enabled = true });
        actor.itemMods.AddMod(combat);
        int transactions = 0, hitEvents = 0;
        float requestedCost = 0f;
        string source = string.Empty;
        stamina.StaminaConsumed += record => { transactions++; requestedCost = record.RequestedBaseAmount; source = record.SourceId; };
        health.OnHitReceived += _ => hitEvents++;
        IDisposable immune = null;
        try
        {
            // 只激活独立宿主，盾牌、受击和扣体力均调用正式接口。
            shield.Load();
            root.SetActive(true);
            combat.Load();
            var samples = new JArray();
            double nextHitTime = 100d;
            foreach (JObject step in steps)
            {
                bool accepted = false;
                float damage = -1f;
                switch (Text(step, "action", "hit"))
                {
                    case "guard": accepted = combat.TryPress(shield); if (accepted) combat.ModUpdate(.2f); break;
                    case "press": accepted = combat.TryPress(shield); break;
                    case "bash": accepted = combat.TryBash(shield); break;
                    case "cancel": combat.CancelAction(); break;
                    case "disable": combat.enabled = false; break;
                    case "unload": combat.Unload(); break;
                    case "reload": combat.Unload(); combat.enabled = true; combat.Load(); break;
                    case "immune": immune?.Dispose(); immune = health.AcquireTemporaryInvulnerability(); break;
                    case "release_immune": immune?.Dispose(); immune = null; break;
                    case "environment": damage = health.HurtEnvironmentalDamage(1, Vector2.zero, (float)Number(step, "damage", 0)); break;
                    case "force": damage = health.ForceHurt((float)Number(step, "damage", 1), "test:guard"); break;
                    case "legacy_hit":
                        fixture.Sender.DamageValues = new CombatDamage(0f, 0f, 0f, (float)Number(step, "damage", 1));
                        damage = health.Hurt(fixture.Sender);
                        break;
                    case "hit":
                        double time = Number(step, "time", nextHitTime);
                        var context = fixture.Context(time);
                        context.Damage = new float4(0f, 0f, 0f, (float)Number(step, "damage", 1));
                        damage = health.Hurt(context);
                        nextHitTime = time + 61d;
                        break;
                    default: throw new ArgumentException("举盾受击采样操作未登记。");
                }
                samples.Add(new JObject
                {
                    ["accepted"] = accepted, ["state"] = combat.ActionKind.ToString(),
                    ["damage"] = damage, ["hp"] = health.Hp, ["hitEvents"] = hitEvents,
                    ["stamina"] = stamina.CurrentValue, ["transactions"] = transactions,
                    ["shieldDurability"] = shieldItem.itemData.Durability,
                    ["requestedCost"] = requestedCost, ["source"] = source
                });
            }
            return new JObject { ["samples"] = samples };
        }
        finally
        {
            immune?.Dispose();
            combat.Unload();
            shield.Unload();
        }
    }
    #endregion
}
