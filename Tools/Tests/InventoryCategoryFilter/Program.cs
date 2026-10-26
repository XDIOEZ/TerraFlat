using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

internal static class Program
{
    #region 固定分类、并集与配置回归

    private static int checks;
    private static readonly string[] categoryIds = { "building", "nature", "functional", "tool", "equipment", "food", "material", "transport" };

    private static void Check(bool condition, string name)
    {
        checks++;
        if (!condition) throw new Exception(name);
    }

    private static void Main(string[] args)
    {
        var categories = InventoryCategoryFilter.Categories;
        Check(categories.Select(category => category.Id).SequenceEqual(categoryIds), "Exactly eight categories in stable display/sort order");
        Check(categories.Select(category => category.Tag).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 8, "Unique explicit category tags");
        Check(InventoryCategoryFilter.MatchesAny(null, null), "No selection is unrestricted");
        Check(InventoryCategoryFilter.MatchesAny(null, new[] { "material" }), "Untagged items have a visible material fallback");
        Check(!InventoryCategoryFilter.MatchesAny(new[] { "Toolsmith" }, new[] { "tool" }), "Tags are exact");
        Check(!InventoryCategoryFilter.MatchesAny(new[] { "Tool" }, new[] { "unknown" }), "Unknown categories cannot match");
        Check(InventoryCategoryFilter.MatchesAny(new[] { "inventorycategory.tool" }, new[] { "TOOL" }), "Case-insensitive category IDs and tags");

        foreach (var row in new[]
        {
            (new[] { "Seed", "Food" }, "nature"),
            (new[] { "Equipment", "Tool", "Wood", "InventoryGroup.ElectricalPower" }, "equipment"),
            (new[] { "Tool", "InventoryGroup.MechanicalPower" }, "tool"),
            (new[] { "35", "InventoryGroup.Electrical" }, "functional"),
            (new[] { "InventoryGroup.Chemical" }, "functional"),
            (new[] { "47" }, "food"),
            (new[] { "35", "Wood" }, "building"),
            (new[] { "InventoryCategory.Material", "Knife", "InventoryGroup.Chemistry" }, "material"),
            (new[] { "InventoryCategory.Transport", "35", "InventoryGroup.Chemistry" }, "transport"),
            (new[] { "InventoryGroup.NewMachineKind", "InventoryCategory.Unregistered" }, "material"),
            (new[] { "InventoryCategory.Tool", "InventoryCategory.Building" }, "building")
        })
        {
            Check(InventoryItemCategoryCatalog.GetPrimaryCategoryForItemTags(row.Item1).Id == row.Item2, "Primary category precedence");
            Check(InventoryItemCategoryCatalog.GetPrimaryCategoryForItemTags(row.Item1.Reverse().ToArray()).Id == row.Item2, "Tag order cannot change category");
        }

        // 每种单一主分类都独立覆盖全部多选组合，避免分类重叠掩盖错误。
        for (int itemIndex = 0; itemIndex < categories.Count; itemIndex++)
        for (int selectionMask = 0; selectionMask < (1 << categories.Count); selectionMask++)
        {
            string[] selected = categoryIds.Where((_, index) => (selectionMask & (1 << index)) != 0).ToArray();
            bool expected = selectionMask == 0 || (selectionMask & (1 << itemIndex)) != 0;
            Check(InventoryCategoryFilter.MatchesAny(new[] { categories[itemIndex].Tag }, selected) == expected,
                $"OR truth table item={itemIndex} selected={selectionMask}");
        }
        for (int left = 0; left < categories.Count; left++)
        for (int right = 0; right < categories.Count; right++)
            Check(Math.Sign(InventoryItemCategoryCatalog.ComparePrimaryCategoryForItemTags(new[] { categories[left].Tag }, new[] { categories[right].Tag })) == Math.Sign(left - right),
                "Category sorting follows dropdown order");

        VerifyAllEnabledItemDefinitions(args[0]);
        Console.WriteLine($"PASS: {checks} category/filter/config assertions against production code.");
    }

    private static void VerifyAllEnabledItemDefinitions(string root)
    {
        string config = Path.Combine(root, "Assets/StreamingAssets/GameConfig/Items");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(config, "item-manifest.json")));
        var sources = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonElement package in manifest.RootElement.GetProperty("packages").EnumerateArray())
        {
            if (package.TryGetProperty("enabled", out JsonElement enabled) && !enabled.GetBoolean()) continue;
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(config, package.GetProperty("path").GetString())));
            foreach (JsonElement item in document.RootElement.GetProperty("items").EnumerateArray()) sources.Add(item.GetProperty("id").GetString(), item.Clone());
        }
        string[] Tags(string id)
        {
            JsonElement item = sources[id];
            if (item.TryGetProperty("tags", out JsonElement tags)) return tags.EnumerateArray().Select(tag => tag.GetString()).ToArray();
            return item.TryGetProperty("parent", out JsonElement parent) ? Tags(parent.GetString()) : Array.Empty<string>();
        }

        using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "Tools/Tests/InventoryCategoryFilter/expected-categories.json")));
        var expected = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty category in fixture.RootElement.EnumerateObject())
        foreach (JsonElement id in category.Value.EnumerateArray()) expected.Add(id.GetString(), category.Name);

        int concrete = 0;
        foreach (var source in sources)
        {
            if (source.Value.TryGetProperty("abstract", out JsonElement abstractItem) && abstractItem.GetBoolean()) continue;
            concrete++;
            string id = source.Key;
            string[] tags = Tags(id);
            Check(expected.ContainsKey(id), "New item must be assigned to an existing category: " + id);
            Check(tags.Count(tag => tag.StartsWith("InventoryCategory.", StringComparison.OrdinalIgnoreCase)) == 1, "One explicit primary category: " + id);
            Check(InventoryItemCategoryCatalog.GetPrimaryCategoryForItemTags(tags).Id == expected[id], "Reviewed item assignment: " + id);
            foreach (var category in InventoryCategoryFilter.Categories)
                Check(category.Matches(tags) == (category.Id == expected[id]), "No overlapping categories: " + id + "/" + category.Id);
        }
        Check(concrete == expected.Count, "Fixture covers all enabled concrete items and no deleted items");
        Console.WriteLine($"PASS: all {concrete} enabled item definitions have one reviewed category.");
    }

    #endregion
}
