using System;
using System.Collections.Generic;

/// <summary>背包筛选和排序共用固定用途分类，新增内容复用分类标签。</summary>
public static class InventoryItemCategoryCatalog
{
    #region 固定分类目录

    public sealed class Category
    {
        public string Id { get; }
        public string Label { get; }
        public string Tag { get; }
        public int SortOrder { get; internal set; }
        internal int InferencePriority { get; }
        internal string[] SemanticTags { get; }

        internal Category(string id, string label, string tag, int inferencePriority, params string[] semanticTags)
        {
            Id = id;
            Label = label;
            Tag = tag;
            InferencePriority = inferencePriority;
            SemanticTags = semanticTags;
        }

        public bool Matches(IReadOnlyList<string> tags) => ReferenceEquals(this, GetPrimaryCategoryForItemTags(tags));
    }

    private static readonly IReadOnlyList<Category> categories = Array.AsReadOnly(new Category[]
    {
        new("building", "建筑装饰", "InventoryCategory.Building", 6, "35"),
        new("nature", "自然植物", "InventoryCategory.Nature", 1, "Seed", "Plant", "Tree", "Crop", "BeeForage.Crop", "BeeForage.Flower"),
        new("functional", "功能设施", "InventoryCategory.Functional", 5,
            "InventoryGroup.MechanicalPower", "InventoryGroup.ElectricalPower", "InventoryGroup.Electrical",
            "InventoryGroup.Chemistry", "InventoryGroup.Chemical", "InventoryGroup.WorkFacilities", "ManualForge", "HandDrill", "FluidTank"),
        new("tool", "工具用品", "InventoryCategory.Tool", 2, "Tool", "Container", "Combustion.Tinder", "readable.book", "Medicine"),
        new("equipment", "武器装备", "InventoryCategory.Equipment", 0, "Equipment", "Armor", "Shield", "Weapon", "RangedWeapon", "Spear", "Knife", "Arrow"),
        new("food", "食物饮品", "InventoryCategory.Food", 3, "47", "Food", "Fruit", "Meat", "SaltFood", "InventoryGroup.Food"),
        new("material", "材料零件", "InventoryCategory.Material", 7, "Material", "Wood", "Stone", "Ore", "Mineral", "Ingot", "Fiber", "Metal"),
        new("transport", "交通运输", "InventoryCategory.Transport", 4, "Vehicle", "Railway", "ShipComponent")
    });

    private static readonly Dictionary<string, Category> explicitTagCategories = BuildTagCategoryLookup(true);
    private static readonly Dictionary<string, Category> semanticTagCategories = BuildTagCategoryLookup(false);

    public static IReadOnlyList<Category> Categories => categories;

    private static Dictionary<string, Category> BuildTagCategoryLookup(bool explicitTags)
    {
        var lookup = new Dictionary<string, Category>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < categories.Count; index++)
        {
            Category category = categories[index];
            category.SortOrder = index;
            if (explicitTags) lookup.Add(category.Tag, category);
            else foreach (string tag in category.SemanticTags) lookup.Add(tag, category);
        }
        return lookup;
    }

    #endregion

    #region 物品归类与排序

    public static Category GetPrimaryCategoryForItemTags(IReadOnlyList<string> tags)
    {
        Category declared = null;
        Category inferred = null;
        if (tags != null)
        {
            for (int index = 0; index < tags.Count; index++)
            {
                string tag = tags[index];
                if (string.IsNullOrEmpty(tag)) continue;
                if (explicitTagCategories.TryGetValue(tag, out Category explicitCategory))
                {
                    // 显式分类优先；误配多个时按固定目录顺序归一，不依赖标签排列。
                    if (declared == null || explicitCategory.SortOrder < declared.SortOrder) declared = explicitCategory;
                }
                else if (semanticTagCategories.TryGetValue(tag, out Category semanticCategory)
                    && (inferred == null || semanticCategory.InferencePriority < inferred.InferencePriority))
                    inferred = semanticCategory;
            }
        }
        return declared ?? inferred ?? categories[6];
    }

    public static int ComparePrimaryCategoryForItemTags(IReadOnlyList<string> left, IReadOnlyList<string> right) =>
        GetPrimaryCategoryForItemTags(left).SortOrder.CompareTo(GetPrimaryCategoryForItemTags(right).SortOrder);

    #endregion
}
