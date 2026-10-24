using System;
using System.Collections.Generic;

/// <summary>库存分类只读取稳定标签；类别内允许别名，多选时任一类别命中即可。</summary>
public static class InventoryCategoryFilter
{
    #region 分类目录与并集规则

    public sealed class Category
    {
        public string Id { get; }
        public string Label { get; }
        private readonly string[][] alternatives;

        public Category(string id, string label, params string[][] alternatives)
        {
            Id = id;
            Label = label;
            this.alternatives = alternatives;
        }

        public bool Matches(IReadOnlyList<string> tags)
        {
            if (tags == null) return false;
            foreach (string[] required in alternatives)
            {
                bool all = true;
                foreach (string tag in required)
                {
                    bool found = false;
                    for (int i = 0; i < tags.Count; i++)
                        if (string.Equals(tags[i], tag, StringComparison.OrdinalIgnoreCase))
                        { found = true; break; }
                    if (!found) { all = false; break; }
                }
                if (all) return true;
            }
            return false;
        }
    }

    // 新物品复用这些语义标签即可参与筛选，不按名称或物品 ID 写特例。
    private static readonly Category[] categories =
    {
        new("mechanical", "机械动力", new[] { "InventoryGroup.MechanicalPower" }),
        new("electrical", "电力", new[] { "InventoryGroup.ElectricalPower" }, new[] { "InventoryGroup.Electrical" }),
        new("industrial", "工业方块", new[] { "InventoryCategory.IndustrialBlock" },
            new[] { "35", "InventoryGroup.MechanicalPower" }, new[] { "35", "InventoryGroup.ElectricalPower" },
            new[] { "35", "InventoryGroup.Electrical" }, new[] { "35", "InventoryGroup.Chemistry" }, new[] { "35", "ManualForge" }),
        new("pipe", "管道方块", new[] { "InventoryCategory.PipeBlock" }),
        new("tool", "工具", new[] { "Tool" }),
        new("food", "食物", new[] { "Food" }),
        new("equipment", "装备", new[] { "Equipment" }, new[] { "Armor" }),
        new("material", "材料", new[] { "Material" }, new[] { "Wood" }, new[] { "Stone" },
            new[] { "Ore" }, new[] { "Mineral" }, new[] { "Ingot" }, new[] { "Fiber" }, new[] { "Metal" }),
        new("building", "建筑方块", new[] { "35" }, new[] { "InventoryCategory.Building" }),
        new("seed", "种子", new[] { "Seed" }),
        new("chemistry", "化工", new[] { "InventoryGroup.Chemistry" }, new[] { "InventoryGroup.Chemical" })
    };

    public static IReadOnlyList<Category> Categories => categories;

    public static bool MatchesAny(IReadOnlyList<string> tags, IReadOnlyList<string> selectedIds)
    {
        if (selectedIds == null || selectedIds.Count == 0) return true;
        for (int i = 0; i < selectedIds.Count; i++)
        {
            foreach (Category category in categories)
            {
                if (!string.Equals(category.Id, selectedIds[i], StringComparison.OrdinalIgnoreCase)) continue;
                if (category.Matches(tags)) return true;
                break;
            }
        }
        return false;
    }

    #endregion
}
