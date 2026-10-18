using System;
using System.Collections.Generic;

public static class TileSearchNameRules
{
    #region 地块名称匹配

    // 别名由地块目录声明，常见写法差异统一处理，不为具体地块添加判断分支。
    public static string Normalize(string value)
    {
        string name = (value ?? string.Empty).Trim().ToLowerInvariant().Replace('粘', '黏');
        if (name.StartsWith("tile_", StringComparison.Ordinal)) name = name.Substring(5);
        if (name.EndsWith("地块", StringComparison.Ordinal)) name = name.Substring(0, name.Length - 2);
        return name.Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
    }

    public static int Score(string query, string id, string displayName, IReadOnlyList<string> aliases)
    {
        string normalized = Normalize(query);
        if (normalized.Length == 0) return 0;
        int best = Match(id);
        best = Math.Max(best, Match(displayName));
        if (aliases != null)
            foreach (string alias in aliases) best = Math.Max(best, Match(alias));
        return best;

        int Match(string candidate)
        {
            string name = Normalize(candidate);
            return name == normalized ? 2 : name.Contains(normalized) ? 1 : 0;
        }
    }

    #endregion
}
