using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 页签的业务选中状态表现。普通状态取自 Prefab 图形本身，选中颜色统一读取全局 JSON 配色；
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

    /// <summary>状态变化时刷新一次，不轮询、不改写公共资产。</summary>
    public void SetSelected(bool selected)
    {
        if (surface == null || caption == null)
            return;
        if (!captured)
        {
            normalSurface = surface.color;
            normalCaption = caption.color;
            captured = true;
        }
        surface.color = selected ? FlatWorldUITheme.TabActive : normalSurface;
        caption.color = selected ? FlatWorldUITheme.TabLabelActive : normalCaption;
    }

    #endregion
}
