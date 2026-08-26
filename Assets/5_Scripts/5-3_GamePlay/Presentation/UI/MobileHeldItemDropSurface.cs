using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 手机端手持物世界丢弃触控面。组件只负责独立触点的长按所有权与屏幕落点，
/// 最终丢弃统一转交 Module_DiscardItem；中间空白层仅在玩家手上存在物品时参与 UI 射线。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Graphic))]
public sealed class MobileHeldItemDropSurface : MonoBehaviour,
    IPointerDownHandler,
    IDragHandler,
    IPointerUpHandler,
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

    public bool RaycastOnlyWhileHoldingItem => raycastOnlyWhileHoldingItem;

    #endregion

    #region 初始化与射线

    /// <summary>配置该触控面是否只在玩家手持物品时参与 UI 射线。</summary>
    public void Configure(bool onlyRaycastWhileHoldingItem)
    {
        ResetGesture();
        raycastOnlyWhileHoldingItem = onlyRaycastWhileHoldingItem;
    }

    /// <summary>中间空白层平时对输入透明，拿起物品后才接收世界丢弃长按。</summary>
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
        longPressCoroutine = StartCoroutine(WaitForLongPress(eventData.pointerId));
        eventData.Use();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData == null || eventData.pointerId != pointerId)
            return;

        currentScreenPosition = eventData.position;
        if (Vector2.Distance(currentScreenPosition, pressPosition) > moveTolerance)
            ResetGesture();
        eventData.Use();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData == null || eventData.pointerId != pointerId)
            return;

        ResetGesture();
        eventData.Use();
    }

    #endregion

    #region 丢弃事务

    private IEnumerator WaitForLongPress(int pointerIdToCheck)
    {
        yield return new WaitForSecondsRealtime(longPressSeconds);
        longPressCoroutine = null;
        if (pointerId != pointerIdToCheck || !HasPlayerHeldItem())
            yield break;

        TryDropHeldItemAtScreenPosition(currentScreenPosition);
    }

    /// <summary>统一释放计时与触点所有权，允许输入锁和 HUD 生命周期幂等清理。</summary>
    public void ResetGesture()
    {
        if (longPressCoroutine != null)
            StopCoroutine(longPressCoroutine);

        longPressCoroutine = null;
        pointerId = int.MinValue;
    }

    /// <summary>玩家手部槽是否存在可丢弃的有效物品。</summary>
    public static bool HasPlayerHeldItem()
    {
        Inventory handInventory = Inventory_Hand.PlayerHand;
        if (handInventory?.Data?.itemSlots == null)
            return false;

        int index = handInventory.Data.Index;
        if (index < 0 || index >= handInventory.Data.itemSlots.Count)
            return false;

        ItemSlot handSlot = handInventory.Data.itemSlots[index];
        return handSlot?.itemData != null && handSlot.Amount > 0;
    }

    /// <summary>复用玩家丢弃模块，确保手机入口不复制物品生成与扣减逻辑。</summary>
    private static bool TryDropHeldItemAtScreenPosition(Vector2 screenPosition)
    {
        Inventory handInventory = Inventory_Hand.PlayerHand;
        Module_DiscardItem discardModule = handInventory?.item?.GetComponentInChildren<Module_DiscardItem>(true);
        return discardModule?.TryDropCurrentSelectionAtScreenPosition(screenPosition) == true;
    }

    #endregion
}
