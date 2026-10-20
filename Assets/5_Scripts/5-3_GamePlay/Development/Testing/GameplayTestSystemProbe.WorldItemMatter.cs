using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 世界散落物环境规则采样

    private static void RegisterWorldMatterOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("item_runtime/world_matter_structure", WorldMatterStructure);
        operations.Add("item_runtime/world_matter_contact", WorldMatterContact);
        operations.Add("item_runtime/matter_transition_scan", MatterTransitionScan);
        operations.Add("environment/combustion_oxygen", CombustionOxygen);
    }

    // 独立停用宿主只读取正式资格缓存，不创建世界、拾取器或完整物品管理器。
    private static JObject WorldMatterStructure(JObject args) => WithEntityProbeItem(item =>
    {
        item.BindData(EntityProbeData("probe:world-matter", 2));
        var result = new JObject();
        var initial = new ItemMgr.WorldMatterStructure(item);
        result["initialCurrent"] = initial.IsCurrent(item);
        result["initialBlocked"] = initial.BlocksWorldMatter;
        initial.IsCurrent(item);
        _ = initial.BlocksWorldMatter;
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        bool stable = true;
        for (int i = 0; i < 512; i++) stable &= initial.IsCurrent(item) && !initial.BlocksWorldMatter;
        result["stableAllocatedBytes"] = GC.GetAllocatedBytesForCurrentThread() - allocated;
        result["stableAllowed"] = stable;

        var receiverObject = new GameObject("TileReceiver");
        receiverObject.transform.SetParent(item.transform, false);
        var receiver = receiverObject.AddComponent<Mod_TileEffectReceiver>();
        receiver._Data = new Ex_ModData_MemoryPackable { StableName = "tile_receiver" };
        item.itemMods.AddMod(receiver);
        result["addInvalidated"] = !initial.IsCurrent(item);
        var withReceiver = new ItemMgr.WorldMatterStructure(item);
        result["receiverBlocked"] = withReceiver.BlocksWorldMatter;
        result["receiverMatchesReference"] = withReceiver.BlocksWorldMatter ==
            (item.GetComponentInChildren<Mod_TileEffectReceiver>(true) != null);
        item.itemMods.RemoveMod(receiver);
        UnityEngine.Object.DestroyImmediate(receiverObject);
        result["removeInvalidated"] = !withReceiver.IsCurrent(item);
        var afterRemoval = new ItemMgr.WorldMatterStructure(item);
        result["afterRemoveAllowed"] = !afterRemoval.BlocksWorldMatter;

        var aiObject = new GameObject("AiCapability");
        aiObject.transform.SetParent(item.transform, false);
        aiObject.AddComponent<GameplayTestWorldMatterAiMarker>();
        item.NotifyRuntimeStructureChanged();
        var withAi = new ItemMgr.WorldMatterStructure(item);
        result["aiBlocked"] = withAi.BlocksWorldMatter;
        result["aiMatchesReference"] = withAi.BlocksWorldMatter == RuntimeAiEntityUtility.IsAiEntity(item);
        UnityEngine.Object.DestroyImmediate(aiObject);
        item.NotifyRuntimeStructureChanged();
        var beforeRebind = new ItemMgr.WorldMatterStructure(item);
        item.BindData(EntityProbeData("probe:world-matter-rebound", 3));
        result["rebindInvalidated"] = !beforeRebind.IsCurrent(item);
        var beforeLoad = new ItemMgr.WorldMatterStructure(item);
        item.ResumeRuntimeAfterReplacement();
        result["generationInvalidated"] = !beforeLoad.IsCurrent(item);
        result["generationAllowed"] = !new ItemMgr.WorldMatterStructure(item).BlocksWorldMatter;

        var combustionHost = new GameObject("CombustionCapabilities");
        combustionHost.transform.SetParent(item.transform, false);
        var combustion = combustionHost.AddComponent<Mod_Combustion>();
        var visual = combustionHost.AddComponent<Mod_CombustionVisual>();
        combustion.ModData.StableName = "probe_combustion";
        visual.ModData.StableName = "probe_combustion_visual";
        item.itemMods.AddMod(combustion);
        item.itemMods.AddMod(visual);
        var withCombustion = new ItemMgr.WorldMatterStructure(item);
        result["combustionCached"] = ReferenceEquals(withCombustion.CachedCombustion, combustion) &&
            ReferenceEquals(withCombustion.CachedCombustionVisual, visual);
        result["inactiveCombustionExcluded"] = withCombustion.ActiveCombustion == null &&
            withCombustion.ActiveCombustionVisual == null && !withCombustion.BlocksWorldMatter;
        item.itemMods.RemoveMod(visual);
        result["combustionRemovalInvalidated"] = !withCombustion.IsCurrent(item);
        result["combustionVisualRemoved"] = new ItemMgr.WorldMatterStructure(item).CachedCombustionVisual == null;
        item.itemMods.RemoveMod(combustion);
        UnityEngine.Object.DestroyImmediate(combustionHost);
        return result;
    });

    private static JObject WorldMatterContact(JObject args)
    {
        bool water = Flag(args, "water", false);
        var data = EntityProbeData("probe:world-matter-contact", 7);
        data.MatterState = new ItemMatterState { Initialized = true, TemperatureCelsius = 20f };
        var contact = new WorldLiquidSettings
        {
            WaterContact = water, Temperature = (float)Number(args, "temperature", 1100),
            ContactHeatingPerSecond = (float)Number(args, "rate", 75)
        };
        float seconds = (float)Number(args, "seconds", 2);
        bool changed = WorldItemMatterRules.Advance(data, 20f, seconds, contact, out bool waterContact);
        return new JObject
        {
            ["changed"] = changed, ["waterContact"] = waterContact,
            ["temperature"] = data.MatterState.TemperatureCelsius, ["amount"] = data.Stack.Amount
        };
    }

    /// <summary>用只读列表验证物质条件顺序、边界和运行态变化不被索引优化改变。</summary>
    private static JObject MatterTransitionScan(JObject args)
    {
        var transitions = new List<RuntimeItemMatterTransition>
        {
            new("hot", 600f, null, null, null, "probe:hot", 1f, null),
            new("warm", 500f, null, null, null, "probe:warm", 1f, null),
            new("dry", null, null, null, 0.2f, "probe:dry", 1f, null)
        };
        var matter = new RuntimeItemMatterDefinition(null, 0.8f, 0f, 0f, 0f, 0f,
            transitions.AsReadOnly());
        var state = new ItemMatterState { Initialized = true, TemperatureCelsius = 700f, Moisture = 0.6f };
        bool firstMatch = ItemMatterRuntime.SelectMatchedTransition(matter, state, false) == transitions[0];
        state.TemperatureCelsius = 550f;
        bool secondMatch = ItemMatterRuntime.SelectMatchedTransition(matter, state, false) == transitions[1];
        state.TemperatureCelsius = 20f;
        state.Moisture = 0.2f;
        bool dryBoundary = ItemMatterRuntime.SelectMatchedTransition(matter, state, false) == transitions[2];
        state.Moisture = 0.21f;
        bool dryRejected = ItemMatterRuntime.SelectMatchedTransition(matter, state, false) == null;
        state.Initialized = false;
        bool uninitializedRejected = ItemMatterRuntime.SelectMatchedTransition(matter, state, false) == null;
        state.Initialized = true;
        bool noLiquidMatch = ItemMatterRuntime.SelectMatchedTransition(matter, state, true) == null;
        bool emptyRejected = ItemMatterRuntime.SelectMatchedTransition(
            new RuntimeItemMatterDefinition(null, 0f, 0f, 0f, 0f, 0f,
                Array.Empty<RuntimeItemMatterTransition>()), state, false) == null;
        bool nullRejected = ItemMatterRuntime.SelectMatchedTransition(null, state, false) == null &&
            ItemMatterRuntime.SelectMatchedTransition(matter, null, false) == null;

        return new JObject
        {
            ["firstMatch"] = firstMatch, ["secondMatch"] = secondMatch,
            ["dryBoundary"] = dryBoundary, ["dryRejected"] = dryRejected,
            ["uninitializedRejected"] = uninitializedRejected, ["noLiquidMatch"] = noLiquidMatch,
            ["emptyRejected"] = emptyRejected, ["nullRejected"] = nullRejected
        };
    }

    #endregion

    #region 明火氧气规则采样
    // 使用独立大气与临时环境来源，采样正式阈值和环境覆盖，不修改真实舱气。
    private static JObject CombustionOxygen(JObject args)
    {
        decimal fraction = args?.Value<decimal?>("fraction") ?? .05m;
        double pressure = Number(args, "pressure", 100d);
        var atmosphere = new AtmosphereState
        {
            CapacityMoles = 100m, ReferenceMoles = 100m,
            ReferencePressureKPa = pressure, TemperatureKelvin = 293.15d
        };
        atmosphere.Gases.Add(new AtmosphereGasAmount { FluidId = FluidIds.Oxygen, Moles = 100m * fraction });
        atmosphere.Gases.Add(new AtmosphereGasAmount { FluidId = "core:nitrogen", Moles = 100m * (1m - fraction) });
        if (Flag(args, "vacuum", false)) atmosphere.Gases.Clear();
        bool allowed = AtmosphereService.CanSustainCombustion(atmosphere);
        var source = new GameplayCombustionEnvironmentSource { Allowed = allowed };
        ItemEnvironmentSources.Register(source);
        try
        {
            bool first = ItemEnvironmentSources.CanSustainCombustion("probe:combustion", default);
            source.Allowed = !allowed;
            bool updated = ItemEnvironmentSources.CanSustainCombustion("probe:combustion", default);
            return new JObject
            {
                ["minimumFraction"] = AtmosphereService.MinimumCombustionOxygenFraction,
                ["allowed"] = allowed,
                ["partialPressureAllowed"] = AtmosphereService.CanSustainCombustion(pressure,
                    Flag(args, "vacuum", false) ? 0d : pressure * (double)fraction),
                ["missingAllowed"] = AtmosphereService.CanSustainCombustion(null),
                ["localAllowed"] = first, ["localUpdated"] = updated
            };
        }
        finally { ItemEnvironmentSources.Unregister(source); }
    }

    private sealed class GameplayCombustionEnvironmentSource : IItemEnvironmentSource
    {
        public bool Allowed;
        public bool TrySample(Item item, out ItemEnvironmentSample sample) { sample = default; return false; }
        public float SupplyAmbientOxygen(Item item, float maximumAmount) => 0f;
        public bool TrySampleCombustion(string worldKey, Vector2 position, out bool canBurn)
        { canBurn = Allowed; return worldKey == "probe:combustion"; }
    }
    #endregion
}

internal sealed class GameplayTestWorldMatterAiMarker : MonoBehaviour, IAIActor
{
    #region 独立 AI 能力夹具
    public Item ActorItem => GetComponentInParent<Item>(true);
    public bool IsAlive => true;
    #endregion
}
