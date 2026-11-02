using System;
using System.Collections.Generic;

/// <summary>库存单选用途分类，与数据层的固定用途分类保持一致。</summary>
public static class InventoryCategoryFilter
{
    #region 分类目录与单选规则

    public static IReadOnlyList<InventoryItemCategoryCatalog.Category> Categories => InventoryItemCategoryCatalog.Categories;

    public static bool MatchesCategory(IReadOnlyList<string> tags, string selectedId)
    {
        return string.IsNullOrEmpty(selectedId) || string.Equals(
            InventoryItemCategoryCatalog.GetPrimaryCategoryForItemTags(tags).Id,
            selectedId,
            StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}
