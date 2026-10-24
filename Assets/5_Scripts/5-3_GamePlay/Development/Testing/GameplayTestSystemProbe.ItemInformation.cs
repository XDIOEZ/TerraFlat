using System;
using System.Collections.Generic;
using System.Linq;
using FlatWorld.Localization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 物品信息双层接口采样
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterItemInformationRules()
    {
        Register("ui_settings", "item_info_food", CaptureFoodInformation);
        Register("ui_settings", "item_info_lifetime", CaptureLifetimeInformation);
        Register("ui_settings", "item_info_extension", CaptureInformationExtension);
        Register("ui_settings", "item_info_numeric", CaptureNumericInformation);
    }

    private static JObject CaptureFoodInformation(JObject args)
    {
        string mode = Text(args, "mode", "both");
        var item = new Data_GeneralItem { Stack = null, MaxDurability = 0f, IDName = mode == "same_target" ? "Meat_Rotten" : "info_probe" };
        var data = new ModData_FoodData { StableName = "food", ModuleId = ModText.Food };
        float interval = mode == "nan" ? float.NaN : mode == "infinity" ? float.PositiveInfinity :
            mode == "zero" ? 0f : 300f;
        var spoilage = new FoodSpoilageObserverData
        {
            EnableSpoilage = mode != "ice", SpoilageIntervalSeconds = 1800f,
            SpoilageElapsedSeconds = 120f, SpoilageTargetItemID = "Meat_Rotten"
        };
        var melting = new FoodMeltingObserverData
        {
            EnableMelting = mode != "food", MeltingIntervalSeconds = interval,
            MeltingElapsedSeconds = mode == "expired" ? 400f : mode == "negative" ? -12f : 175f
        };
        spoilage.Save(data);
        melting.Save(data);
        if (mode == "disabled") data.Enabled = false;
        if (mode == "wrong_identity") data.ModuleId = "unrelated.ability";
        item.ModuleDataDic = new Dictionary<string, ModuleData>();
        if (mode != "missing") item.ModuleDataDic.Add("food", data);
        var definitions = new List<RuntimeItemModuleDefinition>
        {
            new("food", ModText.Food, "", "{}", mode != "definition_disabled")
        };
        if (mode == "two_slots")
        {
            var second = new ModData_FoodData { StableName = "second", ModuleId = ModText.Food };
            spoilage.Save(second);
            melting.Save(second);
            item.ModuleDataDic.Add("second", second);
            definitions.Add(new("second", ModText.Food, "", "{}", true));
        }
        string before = JsonConvert.SerializeObject(data.MechanicStates);
        var timers = new ItemInfoBuffer();
        ItemInfoQuery.Collect(item, definitions, timers);
        var repeat = new ItemInfoBuffer();
        ItemInfoQuery.Collect(item, definitions, repeat);
        var text = new List<string>();
        IItemTooltipInfoProvider display = new ItemTooltipInfoFormatter();
        display.AppendItemTooltipInfo(new ItemTooltipInfoContext(null, null, timers), text);
        return new JObject
        {
            ["timers"] = InformationSamples(timers), ["display"] = new JArray(text),
            ["unchanged"] = before == JsonConvert.SerializeObject(data.MechanicStates),
            ["repeatStable"] = JToken.DeepEquals(InformationSamples(timers), InformationSamples(repeat)),
            ["minuteText"] = ItemTooltipInfoFormatter.FormatRemainingTime(125d),
            ["expectedMinuteText"] = FlatWorldLocalizationService.GetUiFormat("{0}分{1}秒", 2, 5),
            ["roundedText"] = ItemTooltipInfoFormatter.FormatRemainingTime(0.01d),
            ["expectedRoundedText"] = FlatWorldLocalizationService.GetUiFormat("{0}秒", 1)
        };
    }

    private static JObject CaptureLifetimeInformation(JObject args)
    {
        string mode = Text(args, "mode", "same_world");
        var data = new Ex_ModData_MemoryPackable { StableName = "lifetime", ModuleId = Mod_TimedDespawn.ModuleId };
        if (mode != "new")
            data.WriteData(new TimedDespawnState
            {
                Initialized = true, ClockScene = "world_a", LastGameSeconds = 1000d, ElapsedGameSeconds = 400d
            });
        var item = new Data_GeneralItem { Stack = null, MaxDurability = 0f,
            IDName = "info_probe", ModuleDataDic = new Dictionary<string, ModuleData> { ["lifetime"] = data }
        };
        var modules = new[] { new RuntimeItemModuleDefinition("lifetime", Mod_TimedDespawn.ModuleId, "",
            "{\"LifetimeGameSeconds\":1440}", true) };
        double now = mode == "rewound" ? 900d : mode == "expired" ? 2500d : 1100d;
        ItemInfoClock clock = mode == "no_clock" ? default : new ItemInfoClock(mode == "other_world" ? "world_b" : "world_a", now);
        byte[] beforeReference = data.BitData;
        byte[] before = data.BitData?.ToArray();
        int notifications = 0;
        data.PayloadChanged += _ => notifications++;
        var timers = new ItemInfoBuffer();
        ItemInfoQuery.Collect(item, modules, timers, clock);
        var repeat = new ItemInfoBuffer();
        ItemInfoQuery.Collect(item, modules, repeat, clock);
        var lines = new List<string>();
        IItemTooltipInfoProvider display = ItemTooltipInfoFormatter.Default;
        display.AppendItemTooltipInfo(new ItemTooltipInfoContext(null, null, timers), lines);
        return new JObject
        {
            ["timers"] = InformationSamples(timers), ["display"] = new JArray(lines),
            ["samePayloadReference"] = ReferenceEquals(beforeReference, data.BitData),
            ["samePayloadBytes"] = before == null ? data.BitData == null : before.SequenceEqual(data.BitData),
            ["payloadNotifications"] = notifications,
            ["repeatStable"] = JToken.DeepEquals(InformationSamples(timers), InformationSamples(repeat))
        };
    }

    private sealed class InformationExtensionProbe : IItemInfoProvider
    {
        public void CollectItemInfo(ItemInfoContext context, ItemInfoBuffer results)
        {
            if (ItemRemainingTimeInfo.TryCreate("probe.countdown", context.ModuleDefinition.StableName,
                    100d, 58d, ItemTimerClock.Simulation, ItemTimerScope.NextUnit, out var value)) results.RemainingTimes.Add(value);
        }
    }

    private static JObject CaptureInformationExtension(JObject args)
    {
        const string id = "test.item_info_extension";
        var provider = new InformationExtensionProbe();
        ItemInfoQuery.Register(id, provider);
        try
        {
            ItemInfoQuery.Register(id, provider);
            bool rejectsDuplicate = false;
            try { ItemInfoQuery.Register(id, new InformationExtensionProbe()); }
            catch (InvalidOperationException) { rejectsDuplicate = true; }
            bool wrongOwnerRejected = !ItemInfoQuery.Unregister(id, new InformationExtensionProbe());
            var item = new Data_GeneralItem { Stack = null, MaxDurability = 0f, ModuleDataDic = new Dictionary<string, ModuleData>
                { ["custom"] = new Ex_ModData_MemoryPackable { ModuleId = id } } };
            var modules = new[] { new RuntimeItemModuleDefinition("custom", id, "", "{}", true) };
            var timers = new ItemInfoBuffer();
            ItemInfoQuery.Collect(item, modules, timers);
            var formatter = new ItemTooltipInfoFormatter();
            var lines = new List<string>();
            formatter.AppendItemTooltipInfo(new ItemTooltipInfoContext(null, null, timers), lines);
            bool unregisters = ItemInfoQuery.Unregister(id, provider);
            var after = new ItemInfoBuffer();
            ItemInfoQuery.Collect(item, modules, after);
            bool valid = ItemRemainingTimeInfo.TryCreate("boundary", "custom", 100d, double.NaN,
                ItemTimerClock.Simulation, ItemTimerScope.WholeStack, out _);
            return new JObject
            {
                ["timers"] = InformationSamples(timers), ["fallbackDisplayCount"] = lines.Count,
                ["rejectsDuplicate"] = rejectsDuplicate, ["wrongOwnerRejected"] = wrongOwnerRejected,
                ["unregisters"] = unregisters, ["afterUnregisterCount"] = after.RemainingTimes.Count, ["rejectsInvalidElapsed"] = !valid
            };
        }
        finally { ItemInfoQuery.Unregister(id, provider); }
    }

    private static JArray InformationSamples(ItemInfoBuffer values)
        => new(values.RemainingTimes.Select(value => new JObject
        {
            ["id"] = value.Id, ["slot"] = value.ModuleSlot, ["remaining"] = value.RemainingSeconds,
            ["duration"] = value.DurationSeconds, ["clock"] = value.Clock.ToString(),
            ["scope"] = value.Scope.ToString(), ["target"] = value.TargetItemId
        }));

    private static JObject CaptureNumericInformation(JObject args)
    {
        var item = new Data_GeneralItem
        {
            IDName = "numeric_info_probe", Stack = new ItemStack { Amount = 3f, Weight = 2f, Volume = 0.5f },
            MaxDurability = 10f, Durability = 7f,
            ModuleDataDic = new Dictionary<string, ModuleData>
            { ["damage"] = new Ex_ModData_MemoryPackable { ModuleId = nameof(Mod_Damage) } }
        };
        RuntimeItemModuleDefinition Definition(float physical) => new("damage", nameof(Mod_Damage), "",
            new JObject { ["DamageValues"] = new JObject { ["Physical"] = physical } }.ToString(), true);
        var first = new ItemInfoBuffer();
        ItemInfoQuery.Collect(item, new[] { Definition(12.5f) }, first);
        var lines = new List<string>();
        IItemTooltipInfoProvider display = new ItemTooltipInfoFormatter();
        display.AppendItemTooltipInfo(new ItemTooltipInfoContext(null, null, first), lines);
        var reloaded = new ItemInfoBuffer();
        ItemInfoQuery.Collect(item, new[] { Definition(8f) }, reloaded);
        var baseOnly = new ItemInfoBuffer();
        ItemInfoQuery.Collect(item, null, baseOnly);
        var defaults = new ItemInfoBuffer();
        ItemInfoQuery.Collect(new Data_GeneralItem { Stack = null, MaxDurability = 1f, Durability = 1f }, null, defaults);
        var defaultLines = new List<string>();
        display.AppendItemTooltipInfo(new ItemTooltipInfoContext(null, null, defaults), defaultLines);
        JArray Sample(ItemInfoBuffer buffer) => new(buffer.NumericValues.Select(value => new JObject
        {
            ["id"] = value.Id, ["value"] = value.Value, ["maximum"] = value.Maximum,
            ["slot"] = value.ModuleSlot
        }));
        return new JObject
        {
            ["values"] = Sample(first), ["displayCount"] = lines.Count, ["reloaded"] = Sample(reloaded),
            ["baseOnly"] = Sample(baseOnly), ["defaultValues"] = Sample(defaults), ["defaultDisplayCount"] = defaultLines.Count,
            ["attackVisible"] = lines.Contains(FlatWorldLocalizationService.GetUiFormat("物理攻击力：{0}", 12.5f.ToString("0.##"))),
            ["unchanged"] = item.Stack.Amount == 3f && item.Durability == 7f && item.MaxDurability == 10f
        };
    }
    #endregion
}
