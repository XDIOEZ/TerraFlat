using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 页签的业务选中状态表现。普通状态默认取自 Prefab，也允许使用方指定未选中配色，选中颜色统一读取全局 JSON；
/// 不依赖设置页控制器，不与鼠标/手柄的 Selectable 焦点状态混用。
/// </summary>
[DisallowMultipleComponent]
public sealed class ReusableUITabVisual : MonoBehaviour
{
    #region 选中表现

    [SerializeField] private Image surface;
    [SerializeField] private TMP_Text caption;
    private Color normalSurface;
    private Color normalCaption;
    private bool captured;

    /// <summary>供 Prefab 构建器设置序列化引用。</summary>
    public void Configure(Image background, TMP_Text label)
    {
        surface = background;
        caption = label;
        captured = false;
    }

    /// <summary>状态变化时刷新一次，未指定普通色时沿用 Prefab 原始配色。</summary>
    public void SetSelected(bool selected, Color? inactiveSurface = null, Color? inactiveCaption = null)
    {
        if (surface == null || caption == null)
            return;
        if (!captured)
        {
            normalSurface = surface.color;
            normalCaption = caption.color;
            captured = true;
        }
        surface.color = selected ? FlatWorldUITheme.TabActive : (inactiveSurface ?? normalSurface);
        caption.color = selected ? FlatWorldUITheme.TabLabelActive : (inactiveCaption ?? normalCaption);
    }

    #endregion
}
