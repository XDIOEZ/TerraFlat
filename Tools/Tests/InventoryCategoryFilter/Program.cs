using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string name)
    {
        checks++;
        if (!condition) throw new Exception(name);
    }

    private static void Main(string[] args)
    {
        Check(InventoryCategoryFilter.MatchesAny(null, null), "No selection is unrestricted");
        Check(!InventoryCategoryFilter.MatchesAny(null, new[] { "tool" }), "Missing tags cannot match");
        Check(!InventoryCategoryFilter.MatchesAny(new[] { "Toolsmith" }, new[] { "tool" }), "Tags are exact");
        Check(!InventoryCategoryFilter.MatchesAny(new[] { "Tool" }, new[] { "unknown" }), "Unknown categories cannot match");
        Check(InventoryCategoryFilter.MatchesAny(new[] { "tool", "inventorygroup.mechanicalpower" }, new[] { "TOOL", "mechanical" }), "Case-insensitive stable IDs");
        Check(InventoryCategoryFilter.MatchesAny(new[] { "InventoryGroup.Electrical" }, new[] { "electrical" }), "Electrical alias");
        Check(InventoryCategoryFilter.MatchesAny(new[] { "InventoryGroup.Chemical" }, new[] { "chemistry" }), "Chemistry alias");

        // 独立真值表覆盖多选并集，不调用生产代码拼接预期值。
        string[] categoryIds = { "tool", "mechanical", "food" };
        string[] tags = { "Tool", "InventoryGroup.MechanicalPower", "Food" };
        for (int itemMask = 0; itemMask < 8; itemMask++)
        for (int selectionMask = 0; selectionMask < 8; selectionMask++)
        {
            var itemTags = new List<string>();
            var selected = new List<string>();
            for (int bit = 0; bit < 3; bit++)
            {
                if ((itemMask & (1 << bit)) != 0) itemTags.Add(tags[bit]);
                if ((selectionMask & (1 << bit)) != 0) selected.Add(categoryIds[bit]);
            }
            bool expected = selectionMask == 0 || (itemMask & selectionMask) != 0;
            Check(InventoryCategoryFilter.MatchesAny(itemTags, selected) == expected,
                $"OR truth table item={itemMask} selected={selectionMask}");
        }

        string[] allTags = { "35", "InventoryGroup.MechanicalPower", "InventoryGroup.ElectricalPower",
            "InventoryCategory.PipeBlock", "Tool", "Food", "Equipment", "Material", "Seed", "InventoryGroup.Chemistry" };
        var categories = InventoryCategoryFilter.Categories;
        Check(categories.Select(category => category.Id).Distinct().Count() == categories.Count, "Unique category IDs");
        for (int mask = 0; mask < (1 << categories.Count); mask++)
        {
            var selected = new List<string>();
            for (int bit = 0; bit < categories.Count; bit++)
                if ((mask & (1 << bit)) != 0) selected.Add(categories[bit].Id);
            Check(InventoryCategoryFilter.MatchesAny(allTags, selected), "Full-tag item supports every category union");
        }

        string root = args[0];
        using JsonDocument pipes = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "Assets/StreamingAssets/GameConfig/Items/shells/chemical_machines.json")));
        foreach (JsonElement item in pipes.RootElement.GetProperty("items").EnumerateArray())
        {
            string id = item.GetProperty("id").GetString() ?? string.Empty;
            if (!new[] { "FluidPipe", "FluidValve", "FluidOutlet", "GasSelector", "FluidFilter" }.Any(name => id == name || id == name + "_Summoner")) continue;
            var itemTags = item.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()!).ToArray();
            Check(InventoryCategoryFilter.MatchesAny(itemTags, new[] { "pipe", "industrial", "chemistry" }), "Pipe definition " + id);
            Check(!InventoryCategoryFilter.MatchesAny(itemTags, new[] { "tool", "food" }), "Pipe is not a handheld tool or food: " + id);
        }
        foreach (string file in new[] { "building_bodies.json", "building_summoners.json" })
        {
            using JsonDocument buildings = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
                "Assets/StreamingAssets/GameConfig/Items/shells", file)));
            foreach (JsonElement item in buildings.RootElement.GetProperty("items").EnumerateArray())
            {
                string id = item.GetProperty("id").GetString() ?? string.Empty;
                if (!new[] { "BlastFurnace", "Smelter", "WorkBench" }.Any(name => id == name || id == name + "_Summoner")) continue;
                var itemTags = item.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()!).ToArray();
                Check(InventoryCategoryFilter.MatchesAny(itemTags, new[] { "building", "industrial" }), "Industrial definition " + id);
            }
        }
        Console.WriteLine($"PASS: {checks} category/filter/config assertions against production code.");
    }
}
