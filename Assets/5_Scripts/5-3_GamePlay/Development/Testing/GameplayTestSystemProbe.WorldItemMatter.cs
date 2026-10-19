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

    #endregion
}

internal sealed class GameplayTestWorldMatterAiMarker : MonoBehaviour, IAIActor
{
    #region 独立 AI 能力夹具
    public Item ActorItem => GetComponentInParent<Item>(true);
    public bool IsAlive => true;
    #endregion
}
