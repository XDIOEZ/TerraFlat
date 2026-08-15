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
    private Camera eventCamera;
    private CanvasGroup baseCanvasGroup;
    private int pointerId = int.MinValue;
    private Camera pointerEventCamera;
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
        // 切换固定/浮动模式前先按旧配置释放，避免底座位置与输入值残留。
        ResetOwnership();
        role = joystickRole;
        baseRect = joystickBase;
        knobRect = knob;
        radius = Mathf.Max(1f, joystickRadius);
        floatingOrigin = useFloatingOrigin;
        CacheReferences();
        ResetOwnership();
        if (baseCanvasGroup != null)
            baseCanvasGroup.alpha = floatingOrigin ? 0f : 1f;
    }

    private void CacheReferences()
    {
        interactionRect = transform as RectTransform;
        Canvas rootCanvas = GetComponentInParent<Canvas>();
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

        Camera inputCamera = eventData.pressEventCamera != null ? eventData.pressEventCamera : eventCamera;
        if (!TryGetLocalPosition(eventData.position, inputCamera, out Vector2 pointerLocal))
            return;
        if (baseRect == null)
            return;

        pointerId = eventData.pointerId;
        pointerEventCamera = inputCamera;

        if (floatingOrigin)
        {
            // 浮动摇杆只移动视觉底座；输入原点仍锁定在命中区坐标系，避免移动底座后重复换算。
            originLocal = pointerLocal;
            baseRect.anchoredPosition = originLocal;
            SetFloatingVisualVisible(true);
        }
        else
        {
            // 固定摇杆以底座初始锚点为原点，按下区域边缘不会改变方向基准。
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

    /// <summary>在稳定的命中区坐标中计算方向，避免浮动底座移动后原点漂移。</summary>
    private void UpdateDirection(Vector2 screenPosition)
    {
        if (!TryGetLocalPosition(screenPosition, pointerEventCamera, out Vector2 localPosition))
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

    /// <summary>把屏幕触点转换到当前摇杆的稳定输入坐标系。</summary>
    private bool TryGetLocalPosition(Vector2 screenPosition, Camera inputCamera, out Vector2 localPosition)
    {
        RectTransform coordinateSpace = floatingOrigin
            ? interactionRect
            : (baseRect != null ? baseRect.parent as RectTransform : null);
        if (coordinateSpace == null)
        {
            localPosition = default;
            return false;
        }

        return RectTransformUtility.ScreenPointToLocalPointInRectangle(
            coordinateSpace,
            screenPosition,
            inputCamera,
            out localPosition);
    }

    #endregion

    #region 清理

    public void ResetOwnership()
    {
        bool owned = HasPointerOwnership;
        pointerId = int.MinValue;
        pointerEventCamera = null;
        originLocal = fixedBasePosition;
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
