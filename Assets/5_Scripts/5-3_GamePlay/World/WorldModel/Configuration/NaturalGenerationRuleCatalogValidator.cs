using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>世界生成 JSON、物品目录与 Profile 的引用检查，资源发布前阻止悬空配置。</summary>
public sealed class NaturalGenerationRuleCatalogValidator : IResourceCatalogValidator
{
    #region 引用校验

    public string Id => "natural-items";

    public void Validate(GameRes resources, List<string> errors)
    {
        NaturalGenerationRuleCatalog catalog = NaturalGenerationRuleCatalogService.RequireCatalog();
        RiverGenerationConfigCatalog riverConfigs = RiverGenerationConfigService.RequireCatalog();
        foreach (EcologySpawnRuleSnapshot rule in catalog.EcologyRules)
            if (!resources.ItemDefinitions.ContainsKey(rule.ItemId))
                errors.Add($"自然物规则 {rule.RuleId} -> 物品 {rule.ItemId} 未注册");
        foreach (CaveResourceRuleSnapshot rule in catalog.CaveResourceRules)
            if (!resources.ItemDefinitions.ContainsKey(rule.ItemId))
                errors.Add($"矿脉规则 {rule.RuleId} -> 物品 {rule.ItemId} 未注册");

        foreach (ChunkGenerationProfileSO profile in Resources.LoadAll<ChunkGenerationProfileSO>("Config/WorldModel"))
        {
            try { profile.CreateSnapshot(catalog, riverConfigs); }
            catch (Exception exception) { errors.Add($"世界 Profile {profile.name} -> {exception.Message}"); }
        }
    }

    #endregion
}
