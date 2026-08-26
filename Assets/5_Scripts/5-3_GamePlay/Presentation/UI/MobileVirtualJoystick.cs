using FlatWorld.Mobile;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 正式手机 HUD 的多指摇杆。每个实例只认领自己的 pointerId：左侧触控区写移动、右侧触控区写普通指向、
/// 固定攻击摇杆以底座自身为触点坐标系，让摇杆头直接跟随手指；按下瞬间写攻击按钮并持续更新攻击方向，避免不同手指互相抢占。
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
    [SerializeField, Range(0f, 0.95f)] private float deadZone = PlayerAimCursorSystem.DefaultDeadZone;
    [SerializeField] private bool floatingOrigin;
    // 移动与普通指向摇杆空闲时隐藏视觉，固定攻击摇杆保持常驻。
    [SerializeField] private bool showOnlyWhileOwned;

    private RectTransform interactionRect;
    private Camera eventCamera;
    private CanvasGroup baseCanvasGroup;
    private int pointerId = int.MinValue;
    private Camera pointerEventCamera;
    private Vector2 originLocal;
    private Vector2 fixedBasePosition;

    [Header("手机手持物长按丢弃")]
    [SerializeField, Min(0.1f)] private float heldItemDropLongPressSeconds = 0.45f;
    [SerializeField, Min(1f)] private float heldItemDropMoveTolerance = 16f;
    private int heldItemDropPointerId = int.MinValue;
    private Vector2 heldItemDropPressPosition;
    private Vector2 heldItemDropScreenPosition;
    private bool heldItemDropTriggered;
    private Coroutine heldItemDropCoroutine;

    public bool HasPointerOwnership => pointerId != int.MinValue;

    /// <summary>普通指向区虽然是透明 UI 射线层，但其空白区域仍代表世界落点。</summary>
    public bool IsWorldDropSurface => role == JoystickRole.Aim;

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
        CancelHeldItemDropLongPress();
        ResetOwnership();
    }

    private void Update()
    {
        if (HasPointerOwnership && HasPlayerHeldItem())
            ResetOwnership();
    }

    /// <summary>配置摇杆职责、坐标系、死区与触点持有期间的可见性。</summary>
    public void Configure(
        JoystickRole joystickRole,
        RectTransform joystickBase,
        RectTransform knob,
        float joystickRadius,
        bool useFloatingOrigin,
        float joystickDeadZone,
        bool hideVisualUntilOwned)
    {
        // 切换固定/浮动模式前先按旧配置释放，避免底座位置与输入值残留。
        ResetOwnership();
        role = joystickRole;
        baseRect = joystickBase;
        knobRect = knob;
        radius = Mathf.Max(1f, joystickRadius);
        floatingOrigin = useFloatingOrigin;
        deadZone = Mathf.Clamp(joystickDeadZone, 0f, 0.95f);
        showOnlyWhileOwned = hideVisualUntilOwned;
        CacheReferences();
        ResetOwnership();
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

        if (HasPlayerHeldItem())
        {
            BeginHeldItemDropLongPress(eventData);
            eventData.Use();
            return;
        }

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
        }
        else
        {
            // 固定摇杆的触点直接换算到底座自身坐标，原点永远是摇杆底座中心，消除父节点锚点带来的固定偏移。
            originLocal = Vector2.zero;
        }

        SetOwnershipVisualVisible(true);

        if (role == JoystickRole.Attack)
            MobileInputRuntime.SetButton(MobileVirtualButton.Attack, true);

        UpdateDirection(eventData.position);
        eventData.Use();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData == null)
            return;

        if (eventData.pointerId == heldItemDropPointerId)
        {
            heldItemDropScreenPosition = eventData.position;
            if (Vector2.Distance(eventData.position, heldItemDropPressPosition) > heldItemDropMoveTolerance)
                CancelHeldItemDropLongPress();
            eventData.Use();
            return;
        }

        if (eventData.pointerId != pointerId)
            return;

        UpdateDirection(eventData.position);
        eventData.Use();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData == null)
            return;

        if (eventData.pointerId == heldItemDropPointerId)
        {
            CancelHeldItemDropLongPress();
            eventData.Use();
            return;
        }

        if (eventData.pointerId != pointerId)
            return;

        ResetOwnership(returnKnobToCenter: role != JoystickRole.Attack);
        eventData.Use();
    }

    /// <summary>在当前摇杆坐标中计算方向，固定摇杆不混用父节点偏移。</summary>
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
        // 浮动摇杆在命中区内移动底座；固定摇杆直接使用底座坐标，确保摇杆头与手指处于同一坐标系。
        RectTransform coordinateSpace = floatingOrigin ? interactionRect : baseRect;
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

    #region 手持物长按丢弃

    /// <summary>开始监听“先拿起物品，再在地图空白处长按”的触屏丢弃手势。</summary>
    private void BeginHeldItemDropLongPress(PointerEventData eventData)
    {
        CancelHeldItemDropLongPress();
        heldItemDropPointerId = eventData.pointerId;
        heldItemDropPressPosition = eventData.position;
        heldItemDropScreenPosition = eventData.position;
        heldItemDropTriggered = false;
        heldItemDropCoroutine = StartCoroutine(WaitForHeldItemDropLongPress(eventData.pointerId));
    }

    /// <summary>长按计时结束后，把玩家当前手持整组物品丢到触点对应的世界位置。</summary>
    private IEnumerator WaitForHeldItemDropLongPress(int pointerIdToCheck)
    {
        yield return new WaitForSecondsRealtime(heldItemDropLongPressSeconds);
        heldItemDropCoroutine = null;

        if (heldItemDropPointerId != pointerIdToCheck || heldItemDropTriggered ||
            !HasPlayerHeldItem())
        {
            yield break;
        }

        if (TryDropHeldItemAtScreenPosition(heldItemDropScreenPosition))
            heldItemDropTriggered = true;
    }

    /// <summary>复用玩家现有丢弃模块，确保触屏丢弃和菜单丢弃使用同一物品生成事务。</summary>
    private static bool TryDropHeldItemAtScreenPosition(Vector2 screenPosition)
    {
        Inventory handInventory = Inventory_Hand.PlayerHand;
        Module_DiscardItem discardModule = handInventory?.item?.GetComponentInChildren<Module_DiscardItem>(true);
        return discardModule?.TryDropCurrentSelectionAtScreenPosition(screenPosition) == true;
    }

    /// <summary>取消当前手持物长按丢弃监听，避免切换面板或切换控制方式后误丢物品。</summary>
    private void CancelHeldItemDropLongPress()
    {
        if (heldItemDropCoroutine != null)
            StopCoroutine(heldItemDropCoroutine);

        heldItemDropCoroutine = null;
        heldItemDropPointerId = int.MinValue;
        heldItemDropTriggered = false;
    }

    #endregion

    #region 手持物屏蔽

    private static bool HasPlayerHeldItem()
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

    #endregion

    #region 清理

    /// <summary>释放触点和输入；自然抬起时可保留战斗摇杆的最后视觉位置。</summary>
    public void ResetOwnership(bool returnKnobToCenter = true)
    {
        CancelHeldItemDropLongPress();
        bool owned = HasPointerOwnership;
        pointerId = int.MinValue;
        pointerEventCamera = null;
        originLocal = floatingOrigin ? fixedBasePosition : Vector2.zero;
        if (returnKnobToCenter && knobRect != null)
            knobRect.anchoredPosition = Vector2.zero;
        if (floatingOrigin && baseRect != null)
            baseRect.anchoredPosition = fixedBasePosition;
        SetOwnershipVisualVisible(false);

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

    /// <summary>按配置只在当前手指持有区域时显示摇杆反馈，攻击摇杆等常驻控件不受影响。</summary>
    private void SetOwnershipVisualVisible(bool visible)
    {
        if (baseCanvasGroup == null)
            return;

        baseCanvasGroup.alpha = !showOnlyWhileOwned || visible ? 1f : 0f;
        baseCanvasGroup.interactable = false;
        baseCanvasGroup.blocksRaycasts = false;
    }

    #endregion
}
