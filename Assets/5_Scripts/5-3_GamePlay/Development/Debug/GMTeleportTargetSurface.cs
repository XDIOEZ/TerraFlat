using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// GM 单次传送的鼠标/触屏点选面；独立持有一个 pointerId，拖拽超过 UI 阈值即取消本次点击。
/// 只在主动选择落点时启用，避免常驻遮挡手机摇杆和玩法按钮。
/// </summary>
public sealed class GMTeleportTargetSurface : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
{
    #region 触点所有权

    public event Action<Vector2> PositionSelected;
    private int? pointerId;
    private Vector2 pressPosition;
    private bool dragged;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (pointerId.HasValue || eventData.button != PointerEventData.InputButton.Left ||
            eventData.pointerCurrentRaycast.gameObject != gameObject)
            return;

        pointerId = eventData.pointerId;
        pressPosition = eventData.position;
        dragged = false;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (pointerId == eventData.pointerId)
            dragged = true;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (pointerId != eventData.pointerId)
            return;

        pointerId = null;
        float threshold = EventSystem.current.pixelDragThreshold;
        if (!dragged && eventData.pointerCurrentRaycast.gameObject == gameObject &&
            (eventData.position - pressPosition).sqrMagnitude <= threshold * threshold)
            PositionSelected?.Invoke(eventData.position);
    }

    private void OnDisable()
    {
        pointerId = null;
        dragged = false;
    }

    #endregion
}
