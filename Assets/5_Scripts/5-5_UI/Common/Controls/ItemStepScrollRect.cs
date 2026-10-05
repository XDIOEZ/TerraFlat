using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// 通用列表滚动：网格每个滚轮刻度移动设置行数，普通列表移动相同行数的布局条目。
/// 步长读取当前布局，不使用旧 Prefab 的像素倍率；拖动、惯性和滚动条仍由 ScrollRect 负责。
/// </summary>
[AddComponentMenu("UI/Item Step Scroll Rect")]
public sealed class ItemStepScrollRect : ScrollRect
{
    #region 配置与短期状态

    [SerializeField, Min(0.01f)] private float wheelSmoothTime = 0.10f;
    [SerializeField, Min(1f)] private float fallbackItemSize = 48f;
    private Vector2 targetPosition;
    private Vector2 wheelVelocity;
    private bool wheelActive;

    #endregion

    #region 滚轮与拖动仲裁

    /// <summary>Input System 已把硬件刻度转换为模块倍率；先还原刻度，再按布局尺寸换算。</summary>
    public override void OnScroll(PointerEventData data)
    {
        if (!IsActive() || content == null || data == null) return;
        Canvas.ForceUpdateCanvases(); // 只在真实滚轮事件读取刚变化的列表布局。
        Vector2 delta = data.scrollDelta;
        if (data.currentInputModule is InputSystemUIInputModule inputModule)
        {
            float units = Mathf.Abs(inputModule.scrollDeltaPerTick);
            if (units > 0.0001f) delta /= units;
        }
        Vector2 normalizedDelta = new Vector2(-delta.x, delta.y);
        if (vertical && !horizontal && Mathf.Abs(delta.x) > Mathf.Abs(delta.y))
            normalizedDelta.y = -delta.x;
        else if (horizontal && !vertical && Mathf.Abs(delta.y) > Mathf.Abs(delta.x))
            normalizedDelta.x = delta.y;

        // 单个滚轮事件最多移动一个步长，避免设备倍率或合并刻度让列表直接跳到底部。
        normalizedDelta.x = Mathf.Clamp(normalizedDelta.x, -1f, 1f);
        normalizedDelta.y = Mathf.Clamp(normalizedDelta.y, -1f, 1f);

        if (!wheelActive) targetPosition = normalizedPosition;
        float rowsPerWheelTick = UIUserSettings.ScrollRowsPerWheelTick;
        RectTransform view = viewport != null ? viewport : (RectTransform)transform;
        Vector2 hidden = Vector2.Max(Vector2.zero,
            Vector2.Scale(content.rect.size, new Vector2(Mathf.Abs(content.localScale.x), Mathf.Abs(content.localScale.y))) - view.rect.size);
        if (!(horizontal && hidden.x > 0.01f) && !(vertical && hidden.y > 0.01f))
        {
            CancelWheel();
            return;
        }
        if (horizontal && hidden.x > 0.01f)
            targetPosition.x = Mathf.Clamp01(targetPosition.x + normalizedDelta.x * ResolveItemStep(0) * rowsPerWheelTick * Mathf.Abs(content.localScale.x) / hidden.x);
        if (vertical && hidden.y > 0.01f)
            targetPosition.y = Mathf.Clamp01(targetPosition.y + normalizedDelta.y * ResolveItemStep(1) * rowsPerWheelTick * Mathf.Abs(content.localScale.y) / hidden.y);
        StopMovement();
        wheelActive = true;
        data.Use();
    }

    /// <summary>拖动优先，不能同时被上一轮滚轮目标拉回。</summary>
    public override void OnBeginDrag(PointerEventData data)
    {
        CancelWheel();
        base.OnBeginDrag(data);
    }

    /// <summary>滚动条点击或选择新控件时取消旧目标，避免争抢位置。</summary>
    public override void OnInitializePotentialDrag(PointerEventData data)
    {
        CancelWheel();
        base.OnInitializePotentialDrag(data);
    }

    protected override void OnDisable()
    {
        CancelWheel();
        base.OnDisable();
    }

    /// <summary>保留原 ScrollRect 的布局和滚动条更新，额外平滑只在短暂滚轮阶段运行。</summary>
    protected override void LateUpdate()
    {
        if (wheelActive) StopMovement();
        base.LateUpdate();
        if (!wheelActive || content == null) return;
        RectTransform view = viewport != null ? viewport : (RectTransform)transform;
        bool scrollX = horizontal && content.rect.width * Mathf.Abs(content.localScale.x) > view.rect.width + 0.01f;
        bool scrollY = vertical && content.rect.height * Mathf.Abs(content.localScale.y) > view.rect.height + 0.01f;
        if (!scrollX && !scrollY) { CancelWheel(); return; }
        Vector2 current = normalizedPosition;
        // 过滤/刷新列表可能改变不可滚动轴，不能让该轴使平滑阶段永远无法结束。
        if (!scrollX) targetPosition.x = current.x;
        if (!scrollY) targetPosition.y = current.y;
        Vector2 next = Vector2.SmoothDamp(current, targetPosition, ref wheelVelocity,
            Mathf.Max(0.01f, wheelSmoothTime), Mathf.Infinity, Time.unscaledDeltaTime);
        if ((next - targetPosition).sqrMagnitude < 0.0000001f)
        {
            next = targetPosition;
            CancelWheel();
        }
        if (horizontal) horizontalNormalizedPosition = next.x;
        if (vertical) verticalNormalizedPosition = next.y;
    }

    private void CancelWheel()
    {
        wheelActive = false;
        wheelVelocity = Vector2.zero;
    }

    #endregion

    #region 布局步长

    /// <summary>网格按一行/列，普通布局按一个活动条目；隐藏模板和忽略布局节点不参与。</summary>
    private float ResolveItemStep(int axis)
    {
        if (content.TryGetComponent(out GridLayoutGroup grid))
            return Mathf.Max(1f, grid.cellSize[axis] + grid.spacing[axis]);
        float spacing = content.TryGetComponent(out HorizontalOrVerticalLayoutGroup layout) ? layout.spacing : 0f;
        for (int i = 0; i < content.childCount; i++)
        {
            if (!(content.GetChild(i) is RectTransform child) || !child.gameObject.activeSelf) continue;
            if (child.TryGetComponent(out LayoutElement element) && element.ignoreLayout) continue;
            float size = child.rect.size[axis];
            if (size <= 0f) size = axis == 0 ? LayoutUtility.GetPreferredWidth(child) : LayoutUtility.GetPreferredHeight(child);
            if (size > 0f) return Mathf.Max(1f, size * Mathf.Abs(child.localScale[axis]) + spacing);
        }
        return fallbackItemSize;
    }

    #endregion
}
