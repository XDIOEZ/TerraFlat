using System.Collections.Generic;
using TMPro;
using UnityEngine;
using FlatWorld.Localization;

/// <summary>
/// 面板运行时 UI 本地化入口：扫描当前 Prefab 中的静态中文 TMP 文本，
/// 绑定到 FlatWorldUI String Table。仅增加本地化组件，不改变 UI 层级、尺寸或布局。
/// </summary>
public static class FlatWorldUIAutoLocalizer
{
    #region 运行时绑定

    /// <summary>为面板内仍保持原始中文的 TMP 文本补充本地化绑定。</summary>
    public static void BindStaticTexts(Transform panelRoot)
    {
        if (panelRoot == null)
            return;

        TMP_Text[] texts = panelRoot.GetComponentsInChildren<TMP_Text>(true);
        BindStaticTexts(texts);
    }

    /// <summary>复用面板层级快照完成本地化绑定，避免再次遍历整个 Transform 树。</summary>
    public static void BindStaticTexts(IReadOnlyList<TMP_Text> texts)
    {
        if (texts == null)
            return;

        for (int i = 0; i < texts.Count; i++)
        {
            TMP_Text text = texts[i];
            if (text == null || string.IsNullOrWhiteSpace(text.text) || !ContainsChinese(text.text))
                continue;

            LocalizedTextBinder binder = text.GetComponent<LocalizedTextBinder>();
            if (binder == null)
                binder = text.gameObject.AddComponent<LocalizedTextBinder>();

            binder.Configure(
                FlatWorldLocalizationService.UiTable,
                FlatWorldLocalizationService.GetUiTextKey(text.text),
                text.text);
        }
    }

    #endregion

    #region 文本判断

    /// <summary>判断文本是否包含常用 CJK 汉字，避免为数字和英文动态文本创建绑定。</summary>
    private static bool ContainsChinese(string value)
    {
        foreach (char character in value)
        {
            if (character >= '\u4E00' && character <= '\u9FFF')
                return true;
        }

        return false;
    }

    #endregion
}
