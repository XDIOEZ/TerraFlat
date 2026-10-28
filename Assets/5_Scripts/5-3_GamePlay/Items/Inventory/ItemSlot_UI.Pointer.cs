using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// 库存指针与触屏手势。
public partial class ItemSlot_UI
{
    #region 库存指针与触屏手势
    public void OnPointerDown(PointerEventData eventData)
    {
        bool isTouch = IsTouchPointer(eventData);
        if (isTouch && touchPointerId != int.MinValue && touchPointerId != eventData.pointerId)
            return;
        InventoryItemTooltip.DismissTouchAt(eventData.position);
        RememberPointer(eventData);
        if (eventData.button == PointerEventData.InputButton.Left && !isTouch)
        {
            EventSystemGuard.SetGamepadMode(false);
            mousePressStartedWithItem = !IsItemSlotEmpty(GetSlotData());
            mouseDragActive = false;
            suppressDesktopTapAfterDrag = false;
            Canvas mouseCanvas = GetComponentInParent<Canvas>();
            mouseFlickGesture.Begin(eventData.position, Time.unscaledTimeAsDouble,
                mouseCanvas != null ? mouseCanvas.scaleFactor : 1f);
        }

        if (eventData.button == PointerEventData.InputButton.Left && IsShiftPressed())
        {
            InventoryItemTooltip.Hide(this);
            _isShiftQuickTransferDragging = true;
            _shiftQuickTransferSessionId++;
            _lastHandledShiftQuickTransferSessionId = -1;
            TryInvokeShiftQuickTransfer();
            return;
        }

        // 触屏轻触延后到抬起确认，让长按整组放置与轻触交换保持互斥。
        if (eventData.button == PointerEventData.InputButton.Left && isTouch)
        {
            CancelTouchPress();
            touchPointerId = eventData.pointerId;
            touchPressPosition = eventData.position;
            Canvas touchCanvas = GetComponentInParent<Canvas>();
            touchFlickGesture.Begin(eventData.position, Time.unscaledTimeAsDouble,
                touchCanvas != null ? touchCanvas.scaleFactor : 1f);
            touchMovedTooFar = false;
            touchLongPressTriggered = false;
            touchLongPressPutDownIntent = CanTouchLongPressPutDown?.Invoke(slotIndex) == true;
            touchLongPressPutDownCommitted = false;
            touchPressStartedWithItem = !IsItemSlotEmpty(GetSlotData());
            touchItemDragActive = false;
            touchScrollDragActive = false;
            touchLongPressCoroutine = StartCoroutine(WaitForTouchLongPress());
            if (touchPressStartedWithItem)
                touchHalfDragReadyCoroutine = StartCoroutine(WaitForTouchHalfDragReady());
            if (CanTouchInspectItem?.Invoke(slotIndex) == true)
                touchTooltipCoroutine = StartCoroutine(WaitForTouchTooltip());
            return;
        }

    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isPointerOver = true;

        if (eventData != null && !IsTouchPointer(eventData) && !eventData.dragging && !_isShiftQuickTransferDragging)
            InventoryItemTooltip.Show(this, ResolveItemTooltipPrefab(), eventData.position);

        if (!_isShiftQuickTransferDragging)
            return;

        if (!IsShiftPressed() || !IsLeftMousePressed())
        {
            _isShiftQuickTransferDragging = false;
            return;
        }

        TryInvokeShiftQuickTransfer();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isPointerOver = false;
        if (!InventoryItemTooltip.IsTouchPinned(this)) InventoryItemTooltip.Hide(this);
        if (eventData == null || eventData.pointerId != touchPointerId)
            return;

        // 指针离开槽位等同超过移动阈值，禁止滑动背包时误触长按操作。
        touchMovedTooFar = true;
        CancelTouchLongPress();
        CancelTouchHalfDragReady();
        CancelTouchTooltipHold();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        RememberPointer(eventData);
        if (eventData.button == PointerEventData.InputButton.Left && !IsTouchPointer(eventData))
        {
            // 鼠标抬起先于 EndDrag，保留最后一段速度到实际落点判定完成。
            if (mouseDragActive) mouseFlickGesture.Record(eventData.position, Time.unscaledTimeAsDouble);
            else mouseFlickGesture.Reset();
        }
        if (eventData.button == PointerEventData.InputButton.Left && eventData.pointerId == touchPointerId)
        {
            bool shouldTap = !touchMovedTooFar && !touchLongPressTriggered && !touchTooltipTriggered;
            bool shouldHandleLongPress = !touchMovedTooFar && touchPressStartedWithItem && touchLongPressTriggered &&
                                         !touchLongPressPutDownCommitted && !touchTooltipTriggered;
            // EventSystem 先派发抬起再派发结束拖拽，保留轨迹到松手目标判定完成。
            if (touchItemDragActive || touchScrollDragActive)
            {
                touchFlickGesture.Record(eventData.position, Time.unscaledTimeAsDouble);
                CancelTouchLongPress();
                CancelTouchHalfDragReady();
                CancelTouchWorldLongPress();
            }
            else
                CancelTouchPress();
            if (shouldHandleLongPress)
                HandleTouchLongPress();
            else if (shouldTap)
                HandleTouchTap();
        }

        if (eventData.button == PointerEventData.InputButton.Left && !IsTouchPointer(eventData) &&
            !mouseDragActive && !_isShiftQuickTransferDragging)
        {
            // 拖拽结束后的同一次抬起不能再次变成点击，否则会立刻分发一件回源槽位。
            bool shouldHandleDesktopTap = !suppressDesktopTapAfterDrag;
            mousePressStartedWithItem = false;
            suppressDesktopTapAfterDrag = false;
            if (shouldHandleDesktopTap)
                HandleDesktopTap();
            EventSystemGuard.SetGamepadMode(false);
        }

        if (eventData.button == PointerEventData.InputButton.Left)
        {
            _isShiftQuickTransferDragging = false;
        }
    }

    public void OnPointerMove(PointerEventData eventData)
    {
        ReportPointerToHandVisual(eventData);
        if (eventData != null && !IsTouchPointer(eventData) && !eventData.dragging)
            InventoryItemTooltip.Move(this, eventData.position);
        if (eventData != null && !IsTouchPointer(eventData) && mousePressStartedWithItem)
            mouseFlickGesture.Record(eventData.position, Time.unscaledTimeAsDouble);
        if (eventData == null || eventData.pointerId != touchPointerId)
            return;
        touchFlickGesture.Record(eventData.position, Time.unscaledTimeAsDouble);
        if (touchMovedTooFar)
            return;

        Canvas canvas = GetComponentInParent<Canvas>();
        float scaleFactor = canvas != null ? Mathf.Max(0.01f, canvas.scaleFactor) : 1f;
        touchMovedTooFar = (eventData.position - touchPressPosition).magnitude / scaleFactor > touchMoveTolerance;
        if (touchMovedTooFar)
            CancelTouchLongPress();
        if (touchMovedTooFar)
            CancelTouchHalfDragReady();
        if (touchMovedTooFar)
            CancelTouchTooltipHold();
    }

    public void OnInitializePotentialDrag(PointerEventData eventData)
    {
        if (IsTouchPointer(eventData))
        {
            eventData.useDragThreshold = true;
            if (!touchPressStartedWithItem)
                FindParentScrollRect()?.OnInitializePotentialDrag(eventData);
        }
        else
            eventData.useDragThreshold = true;
    }

    /// <summary>根据输入类型创建整组或半组拖拽事务并显示跟随图标。</summary>
    public void OnBeginDrag(PointerEventData eventData)
    {
        if (IsTouchPointer(eventData) && eventData.pointerId == touchPointerId && touchTooltipTriggered) return;
        InventoryItemTooltip.Hide(this);
        if (IsTouchPointer(eventData))
        {
            if (eventData.pointerId != touchPointerId) return;
            touchMovedTooFar = true;
            CancelTouchLongPress();
            CancelTouchTooltipHold();
            if (eventData.button == PointerEventData.InputButton.Left && touchPressStartedWithItem)
            {
                // 长按拆半已经把物品转进手部槽，不能再从原槽甩走剩余半组。
                if (touchHalfDragReady) touchFlickGesture.Reset();
                else touchFlickGesture.Record(eventData.position, Time.unscaledTimeAsDouble);
                Sprite touchDraggedSprite = image != null ? image.sprite : null;
                System.Func<int, InventoryDragTransaction> beginDrag = touchHalfDragReady
                    ? OnTouchHalfDragBegin ?? OnMouseDragBegin
                    : OnMouseDragBegin;
                activeDragTransaction = beginDrag?.Invoke(slotIndex);
                touchItemDragActive = activeDragTransaction != null;
                if (touchItemDragActive)
                {
                    SetSourceContentHidden(activeDragTransaction.HideSourceVisual);
                    CreateMouseDragGhost(touchDraggedSprite, eventData.position);
                    UpdateMouseDragHover(eventData);
                }
                else
                {
                    touchPressStartedWithItem = false;
                }

                return;
            }

            touchScrollDragActive = true;
            FindParentScrollRect()?.OnBeginDrag(eventData);
            return;
        }

        if (eventData.button != PointerEventData.InputButton.Left || !mousePressStartedWithItem ||
            IsShiftPressed())
            return;

        Sprite draggedSprite = image != null ? image.sprite : null;
        activeDragTransaction = OnMouseDragBegin?.Invoke(slotIndex);
        mouseDragActive = activeDragTransaction != null;
        if (!mouseDragActive)
        {
            mousePressStartedWithItem = false;
            return;
        }

        SetSourceContentHidden(activeDragTransaction.HideSourceVisual);
        mouseFlickGesture.Record(eventData.position, Time.unscaledTimeAsDouble);
        CreateMouseDragGhost(draggedSprite, eventData.position);
        UpdateMouseDragHover(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        ReportPointerToHandVisual(eventData);
        if (IsTouchPointer(eventData))
        {
            if (eventData.pointerId != touchPointerId) return;
            touchFlickGesture.Record(eventData.position, Time.unscaledTimeAsDouble);
            if (touchItemDragActive)
            {
                PositionMouseDragGhost(eventData.position);
                UpdateMouseDragHover(eventData);
            }
            else if (touchScrollDragActive)
            {
                FindParentScrollRect()?.OnDrag(eventData);
            }
            return;
        }

        if (!mouseDragActive)
            return;

        mouseFlickGesture.Record(eventData.position, Time.unscaledTimeAsDouble);
        PositionMouseDragGhost(eventData.position);
        UpdateMouseDragHover(eventData);
    }

    /// <summary>两种指针共用拖拽目标提交，设备只负责自己的手势收尾。</summary>
    public void OnEndDrag(PointerEventData eventData)
    {
        if (IsTouchPointer(eventData))
        {
            if (eventData.pointerId != touchPointerId) return;
            if (touchItemDragActive)
            {
                CompletePointerDrag(eventData, touchFlickGesture);
                touchItemDragActive = false;
                touchPressStartedWithItem = false;
            }
            else if (touchScrollDragActive)
                FindParentScrollRect()?.OnEndDrag(eventData);
            touchScrollDragActive = false;
            CancelTouchPress();
            return;
        }
        if (!mouseDragActive) return;
        CompletePointerDrag(eventData, mouseFlickGesture);
        mousePressStartedWithItem = false;
        mouseDragActive = false;
        suppressDesktopTapAfterDrag = true;
    }

    private void CompletePointerDrag(PointerEventData eventData, InventoryFlickGesture gesture)
    {
        pointerQuery.Capture(eventData);
        ItemSlot_UI slot = pointerQuery.FindSlot();
        bool hasSlot = slot != null && slot.isActiveAndEnabled;
        bool accepted = hasSlot && slot.HandleMouseDragDrop(activeDragTransaction, eventData);
        IInventoryDragDropTarget target = accepted ? null : pointerQuery.FindGameplayTarget(eventData);
        target?.TryAcceptInventoryDrag(activeDragTransaction, eventData.position,
            eventData.pressEventCamera ?? eventData.enterEventCamera);
        bool hasTarget = hasSlot || target != null;
        CompleteActiveDrag(!hasTarget && !TryCompleteFlick(eventData, gesture, hasTarget));
        EndMouseDragVisual();
        EventSystemGuard.SetGamepadMode(false);
    }

    // 两种指针共用来源校验、方向转移和失败留源，正式拖放目标始终优先。
    private bool TryCompleteFlick(PointerEventData eventData, InventoryFlickGesture gesture, bool hasDropTarget)
    {
        Vector2 origin = gesture.ScreenOrigin, direction = eventData.position - origin;
        if (hasDropTarget || OnFlickTransfer == null || activeDragTransaction?.HideSourceVisual != true ||
            HasFlickTarget?.Invoke(origin, direction) != true ||
            !gesture.IsFlick(eventData.position, Time.unscaledTimeAsDouble,
                InventoryFlickUserSettings.MinimumDistance, InventoryFlickUserSettings.MinimumSpeed))
            return false;
        activeDragTransaction.TryConsumeSourceItem(sourceItem =>
        {
            if (!ReferenceEquals(GetSlotData()?.itemData, sourceItem)) return false;
            OnFlickTransfer(slotIndex, origin, direction);
            return true;
        });
        return true;
    }

    private IEnumerator WaitForTouchLongPress()
    {
        float duration = Mathf.Max(0.1f, touchLongPressSeconds);
        float progressDelay = Mathf.Clamp(touchLongPressProgressDelaySeconds, 0f, duration);
        float visibleDuration = Mathf.Max(0.0001f, duration - progressDelay);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (touchPointerId == int.MinValue || touchMovedTooFar)
            {
                touchLongPressCoroutine = null;
                PublishTouchLongPressProgress(0f, false);
                yield break;
            }

            elapsed += Time.unscaledDeltaTime;
            if (touchLongPressPutDownIntent && elapsed >= progressDelay)
            {
                float visibleProgress = Mathf.Clamp01((elapsed - progressDelay) / visibleDuration);
                PublishTouchLongPressProgress(visibleProgress, true);
            }
            yield return null;
        }

        touchLongPressCoroutine = null;
        if (touchPointerId == int.MinValue || touchMovedTooFar)
        {
            PublishTouchLongPressProgress(0f, false);
            yield break;
        }

        if (touchLongPressPutDownIntent)
            PublishTouchLongPressProgress(1f, true);
        touchLongPressTriggered = true;
        if (touchLongPressPutDownIntent)
        {
            // 进度走满即提交，不再要求玩家松手后才完成整组放置。
            touchLongPressPutDownCommitted = HandleTouchLongPress();
            if (touchLongPressPutDownCommitted)
            {
                touchPressStartedWithItem = false;
                CancelTouchHalfDragReady();
            }
        }
        else if (!touchPressStartedWithItem)
            HandleTouchLongPress();

        PublishTouchLongPressProgress(0f, false);
    }

    private IEnumerator WaitForTouchHalfDragReady()
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(touchLongPressSeconds, touchHalfDragReadySeconds));
        touchHalfDragReadyCoroutine = null;
        if (touchPointerId == int.MinValue || touchMovedTooFar || !touchPressStartedWithItem)
            yield break;

        touchHalfDragReady = true;
    }

    private IEnumerator WaitForTouchWorldLongPress(int pointerId)
    {
        yield return new WaitForSecondsRealtime(touchLongPressSeconds);
        touchWorldLongPressCoroutine = null;
        if (touchPointerId != pointerId || !touchItemDragActive)
            yield break;

        if (activeDragTransaction == null || !activeDragTransaction.PrepareFallback())
            yield break;

        SetSourceContentHidden(activeDragTransaction.HideSourceVisual);
        if (OnTouchWorldLongPress?.Invoke(touchWorldPointerPosition) != true)
            yield break;

        activeDragTransaction.Resolve();
        CompleteActiveDrag(false);
        touchLongPressTriggered = true;
        touchItemDragActive = false;
        touchPressStartedWithItem = false;
        EndMouseDragVisual();
    }

    private void UpdateTouchWorldLongPress(PointerEventData eventData)
    {
        touchWorldPointerPosition = eventData.position;

        Canvas canvas = GetComponentInParent<Canvas>();
        float scaleFactor = canvas != null ? Mathf.Max(0.01f, canvas.scaleFactor) : 1f;
        if (touchWorldLongPressCoroutine == null)
        {
            touchWorldPressPosition = eventData.position;
            touchWorldLongPressCoroutine = StartCoroutine(WaitForTouchWorldLongPress(eventData.pointerId));
            return;
        }

        if ((eventData.position - touchWorldPressPosition).magnitude / scaleFactor <= touchMoveTolerance)
            return;

        CancelTouchWorldLongPress();
        touchWorldPressPosition = eventData.position;
        touchWorldLongPressCoroutine = StartCoroutine(WaitForTouchWorldLongPress(eventData.pointerId));
    }

    private void CancelTouchPress()
    {
        CancelTouchTooltipHold();
        touchTooltipTriggered = false;
        touchFlickGesture.Reset();
        CancelTouchLongPress();
        CancelTouchWorldLongPress();
        touchPointerId = int.MinValue;
        touchMovedTooFar = false;
        touchPressStartedWithItem = false;
        touchLongPressPutDownIntent = false;
        touchLongPressPutDownCommitted = false;
        CancelTouchHalfDragReady();
    }
    private IEnumerator WaitForTouchTooltip()
    {
        ItemSlot pressedSlot = GetSlotData();
        ItemData pressedItem = pressedSlot?.itemData;
        BasePanel pressedPanel = GetComponentInParent<BasePanel>();
        CanvasGroup[] groups = GetComponentsInParent<CanvasGroup>();
        double deadline = Time.unscaledTimeAsDouble + Mathf.Max(0.1f, touchTooltipSeconds);
        while (touchPointerId != int.MinValue && !touchMovedTooFar && !touchItemDragActive &&
               (pressedPanel == null || pressedPanel.IsOpen()) &&
               ReferenceEquals(GetSlotData(), pressedSlot) &&
               InventoryItemTooltip.MatchesItem(pressedSlot?.itemData, pressedItem) &&
               CanTouchInspectItem?.Invoke(slotIndex) == true)
        {
            foreach (CanvasGroup group in groups)
                if (group != null && group.alpha <= 0.001f)
                {
                    touchTooltipCoroutine = null;
                    yield break;
                }
            if (Time.unscaledTimeAsDouble >= deadline)
            {
                touchTooltipCoroutine = null;
                if (!InventoryItemTooltip.ShowTouch(this, ResolveItemTooltipPrefab(), touchPressPosition))
                    yield break;
                touchTooltipTriggered = true;
                touchLongPressTriggered = true;
                CancelTouchLongPress();
                CancelTouchHalfDragReady();
                yield break;
            }
            yield return null;
        }
        touchTooltipCoroutine = null;
    }

    private void CancelTouchTooltipHold()
    {
        if (touchTooltipCoroutine != null) StopCoroutine(touchTooltipCoroutine);
        touchTooltipCoroutine = null;
    }

    private void CancelTouchLongPress()
    {
        if (touchLongPressCoroutine != null)
            StopCoroutine(touchLongPressCoroutine);
        touchLongPressCoroutine = null;
        PublishTouchLongPressProgress(0f, false);
    }

    /// <summary>由目标槽转发长按进度；目标槽自身不承载进度视觉。</summary>
    private void PublishTouchLongPressProgress(float normalizedProgress, bool visible)
    {
        OnTouchLongPressProgress?.Invoke(Mathf.Clamp01(normalizedProgress), visible);
    }

    /// <summary>供手部库存把转发来的长按进度显示在唯一的手部插槽上。</summary>
    public void SetTouchLongPressProgressVisual(float normalizedProgress, bool visible)
    {
        if (touchLongPressProgressRoot == null ||
            touchLongPressProgressFill == null ||
            touchLongPressProgressRight == null ||
            touchLongPressProgressBottom == null ||
            touchLongPressProgressLeft == null)
            return;

        SetTouchLongPressProgressSquare(visible ? normalizedProgress : 0f);
        touchLongPressProgressRoot.SetActive(visible);
        if (visible)
            touchLongPressProgressRoot.transform.SetAsLastSibling();
    }

    /// <summary>取消、完成或离开槽位时立即收起进度视觉。</summary>
    private void ResetTouchLongPressProgressVisual()
    {
        SetTouchLongPressProgressSquare(0f);
        if (touchLongPressProgressRoot != null)
            touchLongPressProgressRoot.SetActive(false);
    }

    /// <summary>把 0~1 进度依次分配给上、右、下、左四条边，形成顺时针方形环。</summary>
    private void SetTouchLongPressProgressSquare(float normalizedProgress)
    {
        float squareProgress = Mathf.Clamp01(normalizedProgress) * 4f;
        SetTouchLongPressProgressEdge(touchLongPressProgressFill, Mathf.Clamp01(squareProgress), true);
        SetTouchLongPressProgressEdge(touchLongPressProgressRight, Mathf.Clamp01(squareProgress - 1f), false);
        SetTouchLongPressProgressEdge(touchLongPressProgressBottom, Mathf.Clamp01(squareProgress - 2f), true);
        SetTouchLongPressProgressEdge(touchLongPressProgressLeft, Mathf.Clamp01(squareProgress - 3f), false);
    }

    /// <summary>从每条边预设的起点 Pivot 朝前缩放，避免无 Sprite 的 Image 依赖 Filled 裁切。</summary>
    private static void SetTouchLongPressProgressEdge(Image edge, float normalizedProgress, bool horizontal)
    {
        if (edge == null)
            return;

        float progress = Mathf.Clamp01(normalizedProgress);
        edge.rectTransform.localScale = horizontal
            ? new Vector3(progress, 1f, 1f)
            : new Vector3(1f, progress, 1f);
    }

    private void CancelTouchHalfDragReady()
    {
        if (touchHalfDragReadyCoroutine != null)
            StopCoroutine(touchHalfDragReadyCoroutine);
        touchHalfDragReadyCoroutine = null;
        touchHalfDragReady = false;
    }

    private void CancelTouchWorldLongPress()
    {
        if (touchWorldLongPressCoroutine != null)
            StopCoroutine(touchWorldLongPressCoroutine);
        touchWorldLongPressCoroutine = null;
    }

    private static bool IsTouchPointer(PointerEventData eventData)
    {
        if (eventData is ExtendedPointerEventData extendedData)
            return extendedData.pointerType == UIPointerType.Touch;

        return eventData != null && eventData.pointerId >= 0 && Touchscreen.current != null;
    }

    private ScrollRect FindParentScrollRect()
    {
        return transform.parent != null ? transform.parent.GetComponentInParent<ScrollRect>() : null;
    }
    #endregion
}
