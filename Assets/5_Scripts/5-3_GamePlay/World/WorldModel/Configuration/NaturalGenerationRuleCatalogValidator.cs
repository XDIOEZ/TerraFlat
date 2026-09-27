using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>自然物 JSON、物品目录与世界 SO 的引用检查，资源发布前阻止悬空规则。</summary>
public sealed class NaturalGenerationRuleCatalogValidator : IResourceCatalogValidator
{
    #region 引用校验

    public string Id => "natural-items";

    public void Validate(GameRes resources, List<string> errors)
    {
        NaturalGenerationRuleCatalog catalog = NaturalGenerationRuleCatalogService.RequireCatalog();
        foreach (EcologySpawnRuleSnapshot rule in catalog.EcologyRules)
            if (!resources.ItemDefinitions.ContainsKey(rule.ItemId))
                errors.Add($"自然物规则 {rule.RuleId} -> 物品 {rule.ItemId} 未注册");
        foreach (CaveResourceRuleSnapshot rule in catalog.CaveResourceRules)
            if (!resources.ItemDefinitions.ContainsKey(rule.ItemId))
                errors.Add($"矿脉规则 {rule.RuleId} -> 物品 {rule.ItemId} 未注册");

        foreach (ChunkGenerationProfileSO profile in Resources.LoadAll<ChunkGenerationProfileSO>("Config/WorldModel"))
        {
            try { profile.CreateSnapshot(catalog); }
            catch (Exception exception) { errors.Add($"世界 Profile {profile.name} -> {exception.Message}"); }
        }
    }

    #endregion
}
