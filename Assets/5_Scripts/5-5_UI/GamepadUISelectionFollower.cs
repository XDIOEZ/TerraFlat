using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 当手柄选中滚动容器内的控件时，自动把控件滚动到可见区域，避免玩家选中屏幕外对象。
/// </summary>
[DisallowMultipleComponent]
public sealed class GamepadUISelectionFollower : MonoBehaviour, ISelectHandler
{
    private ScrollRect scrollRect;

    /// <summary>
    /// 选中控件后刷新布局并调整滚动位置。
    /// </summary>
    public void OnSelect(BaseEventData eventData)
    {
        scrollRect = GetComponentInParent<ScrollRect>();
        if (scrollRect == null || scrollRect.content == null)
            return;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRect.content);
        EnsureVisible();
    }

    #region 滚动定位

    /// <summary>
    /// 根据控件和视口的局部 Bounds 修正内容位置。
    /// </summary>
    private void EnsureVisible()
    {
        RectTransform viewport = scrollRect.viewport != null
            ? scrollRect.viewport
            : scrollRect.GetComponent<RectTransform>();
        RectTransform target = transform as RectTransform;
        if (viewport == null || target == null)
            return;

        Bounds targetBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, target);
        Rect viewportRect = viewport.rect;
        Vector2 delta = Vector2.zero;

        if (scrollRect.vertical)
        {
            if (targetBounds.min.y < viewportRect.yMin)
                delta.y = viewportRect.yMin - targetBounds.min.y;
            else if (targetBounds.max.y > viewportRect.yMax)
                delta.y = viewportRect.yMax - targetBounds.max.y;
        }

        if (scrollRect.horizontal)
        {
            if (targetBounds.min.x < viewportRect.xMin)
                delta.x = viewportRect.xMin - targetBounds.min.x;
            else if (targetBounds.max.x > viewportRect.xMax)
                delta.x = viewportRect.xMax - targetBounds.max.x;
        }

        if (delta == Vector2.zero)
            return;

        scrollRect.StopMovement();
        Vector2 contentPosition = scrollRect.content.anchoredPosition;
        contentPosition += delta;
        scrollRect.content.anchoredPosition = contentPosition;
    }

    #endregion
}
