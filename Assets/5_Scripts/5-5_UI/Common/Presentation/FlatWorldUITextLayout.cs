using TMPro;
using UnityEngine;

/// <summary>有固定边界的 UI 文字共用缩放范围，长文本先缩小，放不下时显示省略号。</summary>
public static class FlatWorldUITextLayout
{
    #region 固定区域文字排版

    public static float MinimumFontSize(float maximumFontSize)
    {
        return Mathf.Min(maximumFontSize, Mathf.Clamp(maximumFontSize * 0.6f, 12f, 18f));
    }

    public static void Configure(TMP_Text text, float maximumFontSize)
    {
        if (text == null)
            return;

        // 最大字号沿用设计值，只在文字放不下时缩小，避免长翻译挤出控件。
        text.fontSize = maximumFontSize;
        text.fontSizeMax = maximumFontSize;
        text.fontSizeMin = MinimumFontSize(maximumFontSize);
        text.enableAutoSizing = true;
        text.overflowMode = TextOverflowModes.Ellipsis;
    }

    #endregion
}
