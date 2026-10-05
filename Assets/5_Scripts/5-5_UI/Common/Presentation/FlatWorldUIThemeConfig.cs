using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>全局 UI 配色 JSON 数据，只保存可调色块，不承载具体控件逻辑。</summary>
[Serializable]
public sealed class FlatWorldUIThemeConfig
{
    public int schemaVersion;
    public FlatWorldUIThemeSwatch[] colors;
}

/// <summary>一个具名 UI 色块，hex 支持 #RRGGBB 与 #RRGGBBAA。</summary>
[Serializable]
public sealed class FlatWorldUIThemeSwatch
{
    public string id;
    public string hex;
}

/// <summary>唯一的 UI 配色加载入口；缺色、重名或非法颜色直接报错，避免静默回退到代码常量。</summary>
public static class FlatWorldUIThemeConfigCatalog
{
    #region 配置

    private const string ResourcePath = "GameConfig/UI/ui-theme";
    private const int SupportedSchemaVersion = 1;

    #endregion

    #region 缓存

    private static readonly Dictionary<string, Color> Colors =
        new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
    private static bool loaded;

    #endregion

    #region 查询

    public static Color GetColor(string id)
    {
        EnsureLoaded();
        if (string.IsNullOrWhiteSpace(id) || !Colors.TryGetValue(id, out Color color))
            throw new InvalidOperationException($"UI 配色缺少色块：{id}");
        return color;
    }

    #endregion

    #region 加载

    private static void EnsureLoaded()
    {
        if (loaded)
            return;

        TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
        if (asset == null)
            throw new InvalidOperationException($"缺少 UI 配色配置：Resources/{ResourcePath}.json");

        FlatWorldUIThemeConfig config = JsonUtility.FromJson<FlatWorldUIThemeConfig>(asset.text);
        if (config == null || config.schemaVersion != SupportedSchemaVersion || config.colors == null || config.colors.Length == 0)
            throw new InvalidOperationException($"UI 配色配置结构无效：Resources/{ResourcePath}.json");

        Colors.Clear();
        foreach (FlatWorldUIThemeSwatch swatch in config.colors)
        {
            if (swatch == null || string.IsNullOrWhiteSpace(swatch.id) || string.IsNullOrWhiteSpace(swatch.hex))
                throw new InvalidOperationException("UI 配色存在空的 id 或 hex。");
            if (Colors.ContainsKey(swatch.id))
                throw new InvalidOperationException($"UI 配色色块重复：{swatch.id}");

            string value = swatch.hex.StartsWith("#", StringComparison.Ordinal) ? swatch.hex : $"#{swatch.hex}";
            if (!ColorUtility.TryParseHtmlString(value, out Color color))
                throw new InvalidOperationException($"UI 配色色值无效：{swatch.id} = {swatch.hex}");

            Colors.Add(swatch.id, color);
        }

        loaded = true;
    }

    #endregion
}
