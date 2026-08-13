using FlatWorld.Mobile;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 正式手机 HUD 的多指摇杆。每个实例只认领自己的 pointerId：左摇杆写移动、右半屏浮动区写普通指向、
/// 固定攻击摇杆在按下瞬间写攻击按钮并持续更新攻击方向，避免不同手指互相抢占。
/// </summary>
[DisallowMultipleComponent]
public sealed class MobileVirtualJoystick : MonoBehaviour,
    IPointerDownHandler,
    IDragHandler,
    IPointerUpHandler
{
    public enum JoystickRole
    {
        Move,
        Aim,
        Attack
    }

    #region 配置与状态

    [SerializeField] private JoystickRole role;
    [SerializeField] private RectTransform baseRect;
    [SerializeField] private RectTransform knobRect;
    [SerializeField, Min(1f)] private float radius = 92f;
    [SerializeField, Range(0f, 0.95f)] private float deadZone = 0.18f;
    [SerializeField] private bool floatingOrigin;

    private RectTransform interactionRect;
    private Canvas rootCanvas;
    private Camera eventCamera;
    private CanvasGroup baseCanvasGroup;
    private int pointerId = int.MinValue;
    private Vector2 originLocal;
    private Vector2 fixedBasePosition;

    public bool HasPointerOwnership => pointerId != int.MinValue;

    #endregion

    #region 初始化

    private void Awake()
    {
        CacheReferences();
    }

    private void OnEnable()
    {
        CacheReferences();
        ResetOwnership();
    }

    private void OnDisable()
    {
        ResetOwnership();
    }

    public void Configure(
        JoystickRole joystickRole,
        RectTransform joystickBase,
        RectTransform knob,
        float joystickRadius,
        bool useFloatingOrigin)
    {
        role = joystickRole;
        baseRect = joystickBase;
        knobRect = knob;
        radius = Mathf.Max(1f, joystickRadius);
        floatingOrigin = useFloatingOrigin;
        CacheReferences();
        ResetOwnership();
    }

    private void CacheReferences()
    {
        interactionRect = transform as RectTransform;
        rootCanvas = GetComponentInParent<Canvas>();
        eventCamera = rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? rootCanvas.worldCamera
            : null;
        if (baseRect != null)
        {
            fixedBasePosition = baseRect.anchoredPosition;
            baseCanvasGroup = baseRect.GetComponent<CanvasGroup>();
        }
    }

    #endregion

    #region 指针事件

    public void OnPointerDown(PointerEventData eventData)
    {
        if (HasPointerOwnership || eventData == null)
            return;

        pointerId = eventData.pointerId;
        if (!TryGetLocalPosition(eventData.position, out Vector2 pointerLocal))
        {
            ResetOwnership();
            return;
        }

        if (floatingOrigin)
        {
            originLocal = pointerLocal;
            if (baseRect != null)
                baseRect.anchoredPosition = originLocal;
            SetFloatingVisualVisible(true);
        }
        else
        {
            // 固定摇杆始终以正式 Prefab 的底座中心为原点，不能因手指按在命中区边缘而漂移。
            originLocal = fixedBasePosition;
        }

        if (role == JoystickRole.Attack)
            MobileInputRuntime.SetButton(MobileVirtualButton.Attack, true);

        UpdateDirection(eventData.position);
        eventData.Use();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData == null || eventData.pointerId != pointerId)
            return;

        UpdateDirection(eventData.position);
        eventData.Use();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData == null || eventData.pointerId != pointerId)
            return;

        ResetOwnership();
        eventData.Use();
    }

    private void UpdateDirection(Vector2 screenPosition)
    {
        if (!TryGetLocalPosition(screenPosition, out Vector2 localPosition))
            return;

        Vector2 delta = localPosition - originLocal;
        Vector2 normalized = Vector2.ClampMagnitude(delta / radius, 1f);
        Vector2 output = normalized.sqrMagnitude >= deadZone * deadZone ? normalized : Vector2.zero;
        if (knobRect != null)
            knobRect.anchoredPosition = normalized * radius;

        switch (role)
        {
            case JoystickRole.Move:
                MobileInputRuntime.SetMove(output);
                break;
            case JoystickRole.Aim:
                MobileInputRuntime.SetAim(output);
                break;
            case JoystickRole.Attack:
                MobileInputRuntime.SetAttackAim(output);
                break;
        }
    }

    private bool TryGetLocalPosition(Vector2 screenPosition, out Vector2 localPosition)
    {
        RectTransform coordinateSpace = floatingOrigin ? interactionRect : baseRect?.parent as RectTransform;
        if (coordinateSpace == null)
        {
            localPosition = default;
            return false;
        }

        return RectTransformUtility.ScreenPointToLocalPointInRectangle(
            coordinateSpace,
            screenPosition,
            eventCamera,
            out localPosition);
    }

    #endregion

    #region 清理

    public void ResetOwnership()
    {
        bool owned = HasPointerOwnership;
        pointerId = int.MinValue;
        if (knobRect != null)
            knobRect.anchoredPosition = Vector2.zero;
        if (floatingOrigin && baseRect != null)
            baseRect.anchoredPosition = fixedBasePosition;
        SetFloatingVisualVisible(false);

        switch (role)
        {
            case JoystickRole.Move:
                MobileInputRuntime.SetMove(Vector2.zero);
                break;
            case JoystickRole.Aim:
                MobileInputRuntime.SetAim(Vector2.zero);
                break;
            case JoystickRole.Attack:
                MobileInputRuntime.SetAttackAim(Vector2.zero);
                if (owned)
                    MobileInputRuntime.SetButton(MobileVirtualButton.Attack, false);
                break;
        }
    }

    /// <summary>浮动指向只在当前手指持有区域时显示反馈，空闲时保持透明。</summary>
    private void SetFloatingVisualVisible(bool visible)
    {
        if (!floatingOrigin || baseCanvasGroup == null)
            return;

        baseCanvasGroup.alpha = visible ? 1f : 0f;
        baseCanvasGroup.interactable = false;
        baseCanvasGroup.blocksRaycasts = false;
    }

    #endregion
}
