using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>一条玩家可见的操作说明，由操作所属系统提供真实用法与使用条件。</summary>
public sealed class PlayerHelpEntry
{
    #region 操作说明

    public string Category { get; }
    public string Title { get; }
    public string Usage { get; }
    public string Description { get; }

    public PlayerHelpEntry(string category, string title, string usage, string description)
    {
        Category = RequireText(category, nameof(category));
        Title = RequireText(title, nameof(title));
        Usage = RequireText(usage, nameof(usage));
        Description = RequireText(description, nameof(description));
    }

    private static string RequireText(string value, string parameter)
        => !string.IsNullOrWhiteSpace(value) ? value.Trim() : throw new ArgumentException("帮助条目内容不能为空。", parameter);

    #endregion
}

/// <summary>中立帮助目录；聊天、GM 和 MOD 分别登记说明，读物系统只收集内容。</summary>
public static class PlayerHelpCatalog
{
    #region 提供方注册

    private static readonly SortedDictionary<string, Func<IEnumerable<PlayerHelpEntry>>> providers = new(StringComparer.Ordinal);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => providers.Clear();

    /// <summary>使用稳定标识注册或替换提供方，重复装配不增加重复目录。</summary>
    public static void RegisterProvider(string id, Func<IEnumerable<PlayerHelpEntry>> provider)
    {
        if (string.IsNullOrWhiteSpace(id) || provider == null)
            throw new ArgumentException("帮助提供方必须包含稳定标识和内容接口。");
        providers[id.Trim()] = provider;
    }

    /// <summary>提供方卸载时移除自己的说明，不影响已经生成的书籍快照。</summary>
    public static void UnregisterProvider(string id)
    {
        if (!string.IsNullOrWhiteSpace(id))
            providers.Remove(id.Trim());
    }

    #endregion

    #region 稳定收集与去重

    /// <summary>按类别、用法及标题稳定排序，完全相同的说明只保留一条。</summary>
    public static IReadOnlyList<PlayerHelpEntry> Collect()
    {
        List<PlayerHelpEntry> entries = new();
        foreach (Func<IEnumerable<PlayerHelpEntry>> provider in providers.Values)
        {
            IEnumerable<PlayerHelpEntry> supplied = provider();
            if (supplied == null)
                continue;
            foreach (PlayerHelpEntry entry in supplied)
                if (entry != null)
                    entries.Add(entry);
        }

        entries.Sort(CompareEntries);
        for (int i = entries.Count - 1; i > 0; i--)
            if (CompareEntries(entries[i - 1], entries[i]) == 0)
                entries.RemoveAt(i);
        return entries;
    }

    private static int CompareEntries(PlayerHelpEntry left, PlayerHelpEntry right)
    {
        int result = StringComparer.Ordinal.Compare(left.Category, right.Category);
        if (result == 0) result = StringComparer.Ordinal.Compare(left.Usage, right.Usage);
        if (result == 0) result = StringComparer.Ordinal.Compare(left.Title, right.Title);
        if (result == 0) result = StringComparer.Ordinal.Compare(left.Description, right.Description);
        return result;
    }

    #endregion
}
