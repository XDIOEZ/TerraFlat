using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 当手柄选中滚动容器内的控件时，自动把控件滚动到可见区域，避免玩家选中屏幕外对象。
/// 同一帧、同一 ScrollRect 的连续焦点变化只保留最后目标，并在渲染前局部重建一次 Content。
/// </summary>
[DisallowMultipleComponent]
public sealed class GamepadUISelectionFollower : MonoBehaviour, ISelectHandler
{
    #region 帧末队列

    // 本帧每个滚动容器最后一次焦点请求。
    private static Dictionary<ScrollRect, GamepadUISelectionFollower> pendingRequests =
        new Dictionary<ScrollRect, GamepadUISelectionFollower>(4);
    // 当前渲染前回调正在处理的稳定快照。
    private static Dictionary<ScrollRect, GamepadUISelectionFollower> processingRequests =
        new Dictionary<ScrollRect, GamepadUISelectionFollower>(4);
    // 防止重复注册全局 Canvas 回调。
    private static bool flushScheduled;

    #endregion

    #region 缓存引用

    private ScrollRect scrollRect; // 缓存最近的父级滚动容器。
    private RectTransform content; // 缓存当前滚动内容节点。
    private RectTransform viewport; // 缓存当前可见区域。
    private RectTransform target; // 缓存当前焦点控件矩形。

    private void Awake()
    {
        CacheHierarchyReferences();
    }

    private void OnEnable()
    {
        if (scrollRect == null)
            CacheHierarchyReferences();
    }

    private void OnDisable()
    {
        RemovePendingRequest();
    }

    private void OnTransformParentChanged()
    {
        RemovePendingRequest();
        CacheHierarchyReferences();
    }

    /// <summary>仅在初始化或父级变化时解析所属滚动容器。</summary>
    private bool CacheHierarchyReferences()
    {
        target = transform as RectTransform;
        scrollRect = GetComponentInParent<ScrollRect>();
        return RefreshScrollRectReferences();
    }

    /// <summary>同步 ScrollRect 可被运行时替换的 Content 与 Viewport 引用。</summary>
    private bool RefreshScrollRectReferences()
    {
        if (scrollRect == null)
        {
            content = null;
            viewport = null;
            return false;
        }

        content = scrollRect.content;
        viewport = scrollRect.viewport != null
            ? scrollRect.viewport
            : scrollRect.transform as RectTransform;
        return content != null && viewport != null && target != null;
    }

    #endregion

    /// <summary>
    /// 选中控件后只登记最后一次请求，由帧末队列合并布局和滚动处理。
    /// </summary>
    public void OnSelect(BaseEventData eventData)
    {
        if (scrollRect == null)
        {
            if (!CacheHierarchyReferences())
                return;
        }
        else if (!RefreshScrollRectReferences())
        {
            return;
        }

        pendingRequests[scrollRect] = this;
        ScheduleFlush();
    }

    #region 帧末合并

    /// <summary>只注册一次渲染前回调，合并本帧全部焦点变化。</summary>
    private static void ScheduleFlush()
    {
        if (flushScheduled)
            return;

        flushScheduled = true;
        Canvas.willRenderCanvases += FlushPendingRequests;
    }

    /// <summary>每个 ScrollRect 只重建一次局部 Content，并滚动到最后选中的目标。</summary>
    private static void FlushPendingRequests()
    {
        Canvas.willRenderCanvases -= FlushPendingRequests;
        flushScheduled = false;

        Dictionary<ScrollRect, GamepadUISelectionFollower> swap = processingRequests;
        processingRequests = pendingRequests;
        pendingRequests = swap;

        foreach (KeyValuePair<ScrollRect, GamepadUISelectionFollower> request in processingRequests)
        {
            ScrollRect targetScrollRect = request.Key;
            GamepadUISelectionFollower follower = request.Value;
            if (targetScrollRect == null || follower == null ||
                !follower.isActiveAndEnabled || follower.scrollRect != targetScrollRect ||
                !follower.RefreshScrollRectReferences())
            {
                continue;
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(follower.content);
            follower.EnsureVisible();
        }

        processingRequests.Clear();
        if (pendingRequests.Count > 0)
            ScheduleFlush();
    }

    /// <summary>组件失活或换父级时移除尚未执行的旧请求。</summary>
    private void RemovePendingRequest()
    {
        if (scrollRect == null ||
            !pendingRequests.TryGetValue(scrollRect, out GamepadUISelectionFollower follower) ||
            follower != this)
        {
            return;
        }

        pendingRequests.Remove(scrollRect);
        if (pendingRequests.Count == 0 && flushScheduled)
        {
            Canvas.willRenderCanvases -= FlushPendingRequests;
            flushScheduled = false;
        }
    }

    /// <summary>兼容关闭 Domain Reload 的运行模式，进入游戏前清空静态队列。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetScheduler()
    {
        Canvas.willRenderCanvases -= FlushPendingRequests;
        pendingRequests.Clear();
        processingRequests.Clear();
        flushScheduled = false;
    }

    #endregion

    #region 滚动定位

    /// <summary>
    /// 根据控件和视口的局部 Bounds 修正内容位置。
    /// </summary>
    private void EnsureVisible()
    {
        if (scrollRect == null || content == null || viewport == null || target == null)
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
        Vector2 contentPosition = content.anchoredPosition;
        contentPosition += delta;
        content.anchoredPosition = contentPosition;
    }

    #endregion
}
