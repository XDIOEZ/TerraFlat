using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;

/// <summary>Lua 直接采样正式矩形分桶，验证同名、边界、旧页回填与用途隔离。</summary>
internal static class FlatWorldSpriteAtlasPackingRules
{
    #region 图集分桶规则

    [InitializeOnLoadMethod]
    private static void Register()
        => GameplayTestSystemProbe.Register("core_lifecycle", "sprite_atlas_rectangle_packing", Capture);

    private static JObject Capture(JObject args)
    {
        FlatWorldSpriteAtlasBuildPlanTool.SpritePlanEntry Rect(string id, int size, string usage = "CommonItems")
            => new() { sourceGuid = id, sourceLocalId = 1, spriteName = "同名Sprite", width = size,
                height = size, usage = usage, eligible = true };
        var sameNames = Enumerable.Range(0, 32).Select(index => Rect(index.ToString("D3"), 64)).ToList();
        var samePages = FlatWorldSpriteAtlasBuildPlanTool.EstimateAtlasPages(sameNames);
        string[] forward = sameNames.Select(value => value.estimatedAtlas).ToArray();
        FlatWorldSpriteAtlasBuildPlanTool.EstimateAtlasPages(sameNames.AsEnumerable().Reverse().ToList());
        var padding = Enumerable.Range(0, 10).Select(index => Rect(index.ToString("D3"), 512)).ToList();
        var paddedPages = FlatWorldSpriteAtlasBuildPlanTool.EstimateAtlasPages(padding);
        var fill = new List<FlatWorldSpriteAtlasBuildPlanTool.SpritePlanEntry>
        { Rect("a", 1024), Rect("b", 1024), Rect("c", 512), Rect("d", 512), Rect("e", 512) };
        var fillPages = FlatWorldSpriteAtlasBuildPlanTool.EstimateAtlasPages(fill);
        var usages = new List<FlatWorldSpriteAtlasBuildPlanTool.SpritePlanEntry>
        { Rect("u", 64, "GameplayUI"), Rect("w", 64, "WorldEnvironment"), Rect("c", 64, "Creatures") };
        var usagePages = FlatWorldSpriteAtlasBuildPlanTool.EstimateAtlasPages(usages);
        return new JObject
        {
            ["sameNamePages"] = samePages.Count, ["sameNameCount"] = samePages.Sum(value => value.spriteCount),
            ["stable"] = forward.SequenceEqual(sameNames.Select(value => value.estimatedAtlas)),
            ["paddedPages"] = paddedPages.Count,
            ["paddedLargestPage"] = paddedPages.Max(value => value.spriteCount),
            ["filledEarlierPage"] = fill[0].estimatedAtlas == fill[2].estimatedAtlas && fillPages.Count == 2,
            ["usagePages"] = usagePages.Count, ["usagesSeparated"] = usages.Select(value => value.estimatedAtlas).Distinct().Count() == 3,
            ["menuSharingIsolated"] = FlatWorldSpriteAtlasBuildPlanTool.ResolveSpriteAtlasUsageFromSharedDependencies(
                new[] { "Menu", "GameplayUI" }) == "Menu" &&
                FlatWorldSpriteAtlasBuildPlanTool.ResolveSpriteAtlasUsageFromSharedDependencies(
                    new[] { "CommonItems", "WorldEnvironment" }) == "Shared"
        };
    }

    #endregion
}
