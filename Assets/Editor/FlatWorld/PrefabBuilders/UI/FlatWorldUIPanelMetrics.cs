using UnityEngine;

/// <summary>
/// 统一 FlatWorld 主菜单四个模态面板的视觉尺寸，避免不同面板占屏比例不一致。
/// 1480×840 以 1920×1080 为参考，保留适度背景边距，同时让主要操作成为视觉焦点。
/// </summary>
internal static class FlatWorldUIPanelMetrics
{
    #region 统一面板尺寸

    public static readonly Vector2 SharedModalCardSize = new Vector2(1480f, 840f);

    #endregion
}
