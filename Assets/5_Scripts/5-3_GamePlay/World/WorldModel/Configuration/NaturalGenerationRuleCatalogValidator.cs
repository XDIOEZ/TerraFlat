using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using FlatWorld.NaturalEntities;
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
        {
            if (!resources.ItemDefinitions.TryGetValue(rule.ItemId, out RuntimeItemDefinition definition))
                errors.Add($"自然物规则 {rule.RuleId} -> 物品 {rule.ItemId} 未注册");
            else if (rule.SharedRadius > 0) ValidateSharedResource(rule, definition, errors);
        }
        foreach (CaveResourceRuleSnapshot rule in catalog.CaveResourceRules)
            if (!resources.ItemDefinitions.ContainsKey(rule.ItemId))
                errors.Add($"矿脉规则 {rule.RuleId} -> 物品 {rule.ItemId} 未注册");

        foreach (ChunkGenerationProfileSO profile in Resources.LoadAll<ChunkGenerationProfileSO>("Config/WorldModel"))
        {
            try { profile.CreateSnapshot(catalog, riverConfigs); }
            catch (Exception exception) { errors.Add($"世界 Profile {profile.name} -> {exception.Message}"); }
        }
    }

    /// <summary>代理半径包住视觉和受击范围，生成净空完整包含真实阻挡与导航占格。</summary>
    private static void ValidateSharedResource(EcologySpawnRuleSnapshot rule,
        RuntimeItemDefinition definition, List<string> errors)
    {
        string prefix = $"跨区块资源 {rule.RuleId} -> ";
        if (!definition.UsesResourceEntities || definition.IsGroundCover)
        { errors.Add(prefix + "必须使用独立资源 Entity 后端。"); return; }
        var cells = new HashSet<GridCellOffset>(definition.WorldGridOccupancy);
        if (!NaturalEntityEcsProfileCompiler.TryCompile(definition, out var profile, out string failure))
        { errors.Add(prefix + failure); return; }
        if (profile.HasGrowth || profile.HasClimate || profile.Collection != null || profile.Canopy != null)
            errors.Add(prefix + "当前共享资源只支持静态矿体，不能装配依赖锚点气候的生长或伴生模块。");
        profile.SpatialGeometry.Resolve(Matrix4x4.identity, out Bounds body, out Bounds hit);
        Bounds combined = body;
        combined.Encapsulate(hit);
        if (definition.Sprite != null)
        {
            var visual = definition.Visual;
            Matrix4x4 matrix = Matrix4x4.TRS(visual?.RendererLocalPosition ?? Vector3.zero,
                Quaternion.Euler(visual?.RendererLocalEulerAngles ?? Vector3.zero),
                visual?.RendererLocalScale ?? Vector3.one);
            combined.Encapsulate(NaturalEntityEcsService.TransformBounds(matrix,
                definition.Sprite.bounds.center, definition.Sprite.bounds.size));
        }
        float reach = rule.SharedRadius + .5f;
        if (combined.min.x < -reach || combined.max.x > reach || combined.min.y < -reach || combined.max.y > reach)
            errors.Add(prefix + "代理半径没有包住贴图、碰撞体和受击框。");
        if (!profile.SpatialGeometry.BlocksMovement || !profile.SpatialGeometry.HitColliderEnabled ||
            !SharedClearanceContains(rule, body, cells))
            errors.Add(prefix + "生成净空必须包住阻挡框与全部导航占格，导航不能漏掉阻挡格，且必须启用受击。");
    }

    // 生成净空可大于碰撞体，但真实阻挡与导航格都不能超界或留洞。
    internal static bool SharedClearanceContains(EcologySpawnRuleSnapshot rule,
        Bounds body, HashSet<GridCellOffset> cells)
    {
        float halfX = rule.FootprintWidth * .5f, halfY = rule.FootprintHeight * .5f;
        const float tolerance = .0001f;
        if (cells == null || cells.Count == 0 || !(body.size.x > 0f && body.size.y > 0f) ||
            !(body.min.x >= -halfX - tolerance && body.max.x <= halfX + tolerance &&
              body.min.y >= -halfY - tolerance && body.max.y <= halfY + tolerance)) return false;
        foreach (GridCellOffset cell in cells)
            if (Math.Abs(cell.X) > rule.FootprintWidth / 2 ||
                Math.Abs(cell.Y) > rule.FootprintHeight / 2) return false;
        int minX = Mathf.FloorToInt(body.min.x + .5f + tolerance);
        int maxX = Mathf.CeilToInt(body.max.x - .5f - tolerance);
        int minY = Mathf.FloorToInt(body.min.y + .5f + tolerance);
        int maxY = Mathf.CeilToInt(body.max.y - .5f - tolerance);
        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
            if (!cells.Contains(new GridCellOffset(x, y))) return false;
        return true;
    }

    #endregion
}
