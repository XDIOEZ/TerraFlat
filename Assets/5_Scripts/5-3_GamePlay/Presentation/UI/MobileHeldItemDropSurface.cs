using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 手机端手持物世界丢弃触控面：轻点松手丢一个，按住 0.45 秒丢整组，移动超过 16 个 UI 像素取消。
/// 组件只负责独立触点的手势所有权与屏幕落点，长按完成后不会再触发轻点，
/// 最终丢弃统一转交 Module_DiscardItem；中间空白层仅在玩家手上存在物品时参与 UI 射线。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Graphic))]
public sealed class MobileHeldItemDropSurface : MonoBehaviour,
    IPointerDownHandler,
    IDragHandler,
    IPointerUpHandler,
    IPointerExitHandler,
    ICanvasRaycastFilter
{
    #region 配置与状态

    [SerializeField] private bool raycastOnlyWhileHoldingItem;
    [SerializeField, Min(0.1f)] private float longPressSeconds = 0.45f;
    [SerializeField, Min(1f)] private float moveTolerance = 16f;

    private int pointerId = int.MinValue;
    private Vector2 pressPosition;
    private Vector2 currentScreenPosition;
    private Coroutine longPressCoroutine;
    private float pressStartedAt; // 非缩放时间，短按和长按共用同一判定时刻。
    private ItemData pressedItem; // 锁定按下时的物品，其他手指换物后取消本次丢弃。

    public bool RaycastOnlyWhileHoldingItem => raycastOnlyWhileHoldingItem;

    #endregion

    #region 初始化与射线

    /// <summary>配置该触控面是否只在玩家手持物品时参与 UI 射线。</summary>
    public void Configure(bool onlyRaycastWhileHoldingItem)
    {
        ResetGesture();
        raycastOnlyWhileHoldingItem = onlyRaycastWhileHoldingItem;
    }

    /// <summary>中间空白层平时对输入透明，拿起物品后才接收世界丢弃手势。</summary>
    public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
    {
        return !raycastOnlyWhileHoldingItem || HasPlayerHeldItem();
    }

    private void OnDisable()
    {
        ResetGesture();
    }

    #endregion

    #region 指针事件

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData == null || eventData.button != PointerEventData.InputButton.Left ||
            pointerId != int.MinValue || !HasPlayerHeldItem())
        {
            return;
        }

        pointerId = eventData.pointerId;
        pressPosition = eventData.position;
        currentScreenPosition = eventData.position;
        pressStartedAt = Time.unscaledTime;
        pressedItem = GetPlayerHeldSlot().itemData;
        longPressCoroutine = StartCoroutine(WaitForLongPress(eventData.pointerId));
        eventData.Use();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData == null || eventData.pointerId != pointerId)
            return;

        currentScreenPosition = eventData.position;
        if (HasMovedTooFar(currentScreenPosition))
            ResetGesture();
        eventData.Use();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData == null || eventData.pointerId != pointerId)
            return;

        GameObject hit = eventData.pointerCurrentRaycast.gameObject;
        if (HasMovedTooFar(eventData.position) || hit == null ||
            hit.GetComponentInParent<MobileHeldItemDropSurface>() != this)
        {
            ResetGesture();
        }
        else
        {
            CompleteGesture(eventData.position, Time.unscaledTime - pressStartedAt >= longPressSeconds);
        }
        eventData.Use();
    }

    /// <summary>触点离开世界触控面时取消，避免滑到按钮或面板上仍然丢弃。</summary>
    public void OnPointerExit(PointerEventData eventData)
    {
        if (eventData != null && eventData.pointerId == pointerId)
            ResetGesture();
    }

    /// <summary>按 Canvas 缩放换算移动容差，与库存轻触手势保持一致。</summary>
    private bool HasMovedTooFar(Vector2 screenPosition)
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        float scaleFactor = canvas != null ? canvas.scaleFactor : 1f;
        float tolerance = moveTolerance * scaleFactor;
        return (screenPosition - pressPosition).sqrMagnitude > tolerance * tolerance;
    }

    #endregion

    #region 丢弃事务

    private IEnumerator WaitForLongPress(int pointerIdToCheck)
    {
        yield return new WaitForSecondsRealtime(longPressSeconds);
        longPressCoroutine = null;
        if (pointerId != pointerIdToCheck)
            yield break;

        CompleteGesture(currentScreenPosition, entireStack: true);
    }

    /// <summary>先结束本次触点再提交丢弃，防止长按后的松手或回调重入再次扣减。</summary>
    private void CompleteGesture(Vector2 screenPosition, bool entireStack)
    {
        ItemSlot handSlot = GetPlayerHeldSlot();
        bool itemUnchanged = pressedItem != null && ReferenceEquals(handSlot?.itemData, pressedItem) &&
                             handSlot.Amount > 0;
        ResetGesture();
        if (!itemUnchanged)
            return;

        Module_DiscardItem discardModule =
            Inventory_Hand.PlayerHand.item?.GetComponentInChildren<Module_DiscardItem>(true);
        discardModule?.TryDropHeldItemAtScreenPosition(screenPosition, entireStack ? (int?)null : 1);
    }

    /// <summary>统一释放计时与触点所有权，允许输入锁和 HUD 生命周期幂等清理。</summary>
    public void ResetGesture()
    {
        if (longPressCoroutine != null)
            StopCoroutine(longPressCoroutine);

        longPressCoroutine = null;
        pointerId = int.MinValue;
        pressedItem = null;
        pressStartedAt = 0f;
    }

    /// <summary>玩家手部槽是否存在可丢弃的有效物品。</summary>
    public static bool HasPlayerHeldItem()
    {
        ItemSlot handSlot = GetPlayerHeldSlot();
        return handSlot?.itemData != null && handSlot.Amount > 0;
    }

    /// <summary>只读取玩家手部携带槽，空手时不取快捷栏选中物品。</summary>
    private static ItemSlot GetPlayerHeldSlot()
    {
        Inventory handInventory = Inventory_Hand.PlayerHand;
        if (handInventory?.Data?.itemSlots == null)
            return null;

        int index = handInventory.Data.Index;
        if (index < 0 || index >= handInventory.Data.itemSlots.Count)
            return null;

        return handInventory.Data.itemSlots[index];
    }

    #endregion
}
