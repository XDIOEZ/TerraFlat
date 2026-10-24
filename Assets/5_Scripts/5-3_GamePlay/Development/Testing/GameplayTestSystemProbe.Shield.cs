using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

public static partial class GameplayTestSystemProbe
{
    #region 盾牌正式规则采样
    private static void RegisterShieldOperations(Dictionary<string, Func<JObject, JObject>> result)
        => result.Add("combat/shield_actions", ShieldActions);

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
}
