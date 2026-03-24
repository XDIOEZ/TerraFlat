using System;
using System.Collections.Generic;

public static class ListTagExtensions
{
    #region List语义接口
    public static void EnsureTagStructure(this List<string> tags)
    {
        if (tags == null)
        {
            throw new InvalidOperationException("ItemData.Tags 为空，请先初始化为 new List<string>()。");
        }
    }

    public static bool ContainsTag(this List<string> tags, string tagName)
    {
        if (tags == null)
        {
            throw new InvalidOperationException("ItemData.Tags 为空，无法执行 ContainsTag。");
        }

        if (string.IsNullOrWhiteSpace(tagName))
        {
            throw new ArgumentException("tagName 不能为空。", nameof(tagName));
        }

        return tags.Contains(tagName);
    }

    public static bool ContainsAnyTag(this List<string> tags, IEnumerable<string> tagNames)
    {
        if (tags == null)
        {
            throw new InvalidOperationException("ItemData.Tags 为空，无法执行 ContainsAnyTag。");
        }

        if (tagNames == null)
        {
            throw new ArgumentNullException(nameof(tagNames), "tagNames 不能为空。");
        }

        foreach (var tagName in tagNames)
        {
            if (string.IsNullOrWhiteSpace(tagName))
                continue;

            if (tags.Contains(tagName))
                return true;
        }

        return false;
    }
    #endregion

    #region 兼容旧Tag接口
    [Obsolete("请改用 ContainsTag(tagName)。")]
    public static bool HasType(this List<string> tags, string tagName)
    {
        return tags.ContainsTag(tagName);
    }

    [Obsolete("请改用 ContainsTag(tagName)。")]
    public static bool HasTag(this List<string> tags, string tagType, string tagName)
    {
        return tags.ContainsTag(tagName);
    }

    [Obsolete("请改用 ContainsTag(tagName)。")]
    public static bool HasTypeTag(this List<string> tags, string tagType, string tagName)
    {
        return tags.ContainsTag(tagName);
    }
    #endregion
}
