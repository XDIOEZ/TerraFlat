using System;
using System.Collections.Generic;

/// <summary>库存多选分类按并集匹配，与数据层的固定用途分类保持一致。</summary>
public static class InventoryCategoryFilter
{
    #region 分类目录与并集规则

    public static IReadOnlyList<InventoryItemCategoryCatalog.Category> Categories => InventoryItemCategoryCatalog.Categories;

    public static bool MatchesAny(IReadOnlyList<string> tags, IReadOnlyList<string> selectedIds)
    {
        if (selectedIds == null || selectedIds.Count == 0) return true;
        string categoryId = InventoryItemCategoryCatalog.GetPrimaryCategoryForItemTags(tags).Id;
        for (int i = 0; i < selectedIds.Count; i++)
        {
            if (string.Equals(categoryId, selectedIds[i], StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    #endregion
}
