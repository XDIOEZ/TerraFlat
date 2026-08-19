using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using TMPro;
using UltEvents;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public class ItemSlot_UI : MonoBehaviour,
    IPointerDownHandler,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerUpHandler,
    IPointerMoveHandler,
    IInitializePotentialDragHandler,
    IBeginDragHandler,
    IDragHandler,
    IEndDragHandler,
    IScrollHandler,
    ISubmitHandler,
    ISelectHandler,
    IDeselectHandler,
    IGamepadContextActionHandler,
    IGamepadPrimaryActionHandler
{
    #region 字段
    /// <summary>
    /// 槽位索引（替代对Data的直接引用）
    /// </summary>
    public int slotIndex = -1;

    [Tooltip("显示当前物体的图标")]
    public Image image;

    [Tooltip("显示当前物体的数量")]
    public TMP_Text text;

    [Tooltip("物体被点击的事件（左键）")]
    public UltEvent<int> OnLeftClick = new UltEvent<int>();

    [Tooltip("手柄确认槽位事件，与鼠标点击路径分离")]
    public UltEvent<int> OnGamepadSubmit = new UltEvent<int>();

    public UltEvent<int, float> _OnScroll = new UltEvent<int, float>();

    public UltEvent<int> OnRightClick = new UltEvent<int>();

    [Tooltip("Shift+左键快速转移事件")]
    public UltEvent<int> OnShiftQuickTransfer = new UltEvent<int>();

    /// <summary>鼠标或触屏拖拽开始时，把源物品明确转入玩家手上槽位。</summary>
    public System.Func<int, bool> OnMouseDragBegin { get; set; }

    /// <summary>鼠标或触屏拖拽结束时，在目标槽执行定向放置事务。</summary>
    public System.Action<int> OnMouseDragDrop { get; set; }

    /// <summary>触屏轻触入口，允许快捷栏将轻触与物品交换分开处理。</summary>
    public System.Action<int> OnTouchTap { get; set; }

    /// <summary>触屏长按入口；返回 true 表示已完成整组放置并阻止物品菜单。</summary>
    public System.Func<int, bool> OnTouchLongPress { get; set; }

    /// <summary>触屏拖拽物品后在世界非 UI 区域长按的入口。</summary>
    public System.Func<Vector2, bool> OnTouchWorldLongPress { get; set; }

    /// <summary>触屏长按更久后开始拖拽时，把源堆拆出一半的入口。</summary>
    public System.Func<int, bool> OnTouchHalfDragBegin { get; set; }

    /// <summary>桌面轻触入口；空手时保持选中语义，拖拽后手持整组时用于单件分发。</summary>
    public System.Action<int> OnDesktopTap { get; set; }

    private GameObject currentMenuInstance;

    private Outline selectionOutline;
    private bool selectionOutlineCreated;
    private bool selectionOutlineBaselineCaptured;
    private bool selectionOutlineBaselineEnabled;
    private Color selectionOutlineBaselineColor;
    private Vector2 selectionOutlineBaselineDistance;
    private bool selectionOutlineBaselineUsesGraphicAlpha;


    private bool isPointerOver = false;

    [Header("鼠标拖拽")]
    private bool mousePressStartedWithItem;
    private bool mouseDragActive;
    private bool suppressDesktopTapAfterDrag;
    private RectTransform mouseDragGhost;
    private ItemSlot_UI mouseDragHoverSlot;
    private bool dragHoverOutlineEnabled;
    private Color dragHoverOutlineColor;
    private Vector2 dragHoverOutlineDistance;
    private bool dragHoverOutlineUsesGraphicAlpha;

    private static bool _isShiftQuickTransferDragging;
    private static int _shiftQuickTransferSessionId;
    private int _lastHandledShiftQuickTransferSessionId = -1;

    [Header("手机长按")]
    [SerializeField, Min(0.1f)] private float touchLongPressSeconds = 0.45f;
    [SerializeField, Min(0.1f)] private float touchHalfDragReadySeconds = 0.85f;
    [SerializeField, Min(1f)] private float touchMoveTolerance = 16f;
    private int touchPointerId = int.MinValue;
    private Vector2 touchPressPosition;
    private bool touchMovedTooFar;
    private bool touchLongPressTriggered;
    private bool touchLongPressMenuPending;
    private bool touchHalfDragReady;
    private bool touchPressStartedWithItem;
    private bool touchItemDragActive;
    private bool touchScrollDragActive;
    private Coroutine touchLongPressCoroutine;
    private Coroutine touchHalfDragReadyCoroutine;
    private Coroutine touchWorldLongPressCoroutine;
    private Vector2 touchWorldPressPosition;
    private Vector2 touchWorldPointerPosition;
    private readonly List<RaycastResult> touchRaycastResults = new List<RaycastResult>(8);

    /// <summary>
    /// 用于获取槽位数据的委托（解除对Data的直接依赖）
    /// </summary>
    public System.Func<int, ItemSlot> GetSlotDataFunc { get; set; }

    /// <summary>
    /// 清空数据的委托
    /// </summary>
    public System.Action<int> ClearSlotDataAction { get; set; }
    #endregion

    #region Unity生命周期方法
    private void Start()
    {
        CacheVisualReferences();
        EnsureSelectionOutline();
    }

    public void OnDestroy()
    {
        OnLeftClick.Clear();
        OnGamepadSubmit.Clear();
        OnRightClick.Clear();
        OnShiftQuickTransfer.Clear();
        OnMouseDragBegin = null;
        OnMouseDragDrop = null;
        OnTouchTap = null;
        OnTouchLongPress = null;
        OnTouchWorldLongPress = null;
        OnTouchHalfDragBegin = null;
        OnDesktopTap = null;
        _OnScroll.Clear();
        if (selectionOutlineCreated && selectionOutline != null)
            Destroy(selectionOutline);
        EndMouseDragVisual();
        CancelTouchPress();
    }
    #endregion

    #region 公共方法
    /// <summary>
    /// 初始化槽位（替代 Data = ... 的直接赋值）
    /// </summary>
    public void InitializeSlot(int index, System.Func<int, ItemSlot> getSlotFunc, System.Action<int> clearAction)
    {
        slotIndex = index;
        GetSlotDataFunc = getSlotFunc;
        ClearSlotDataAction = clearAction;
    }

    /// <summary>
    /// 获取当前槽位数据
    /// </summary>
    private ItemSlot GetSlotData()
    {
        if (GetSlotDataFunc == null)
        {
            Debug.LogWarning($"[ItemSlot_UI] GetSlotDataFunc 未设置，槽位索引: {slotIndex}");
            return null;
        }
        return GetSlotDataFunc(slotIndex);
    }

    [Button]
    public void RefreshUI()
    {
        // 维度/场景切换期间可能收到旧库存事件，旧槽位的子级控件可能已被 Unity 销毁。
        if (!CacheVisualReferences())
            return;

        UpdateItemAmount();
        UpdateItemIcon();
    }

    public void Click(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            if (IsTouchPointer(eventData))
                HandleTouchTap();
            else
                HandleDesktopTap();
        }
        else if (eventData.button == PointerEventData.InputButton.Right)
        {
            HandleRightClick();
        }
    }
    #endregion

    #region 鼠标点击处理
    private void HandleLeftClick()
    {
        OnLeftClick.Invoke(slotIndex);
    }

    private void HandleTouchTap()
    {
        // 手机轻触必须使用独立的单件事务入口，未绑定时也不能回退到整堆交换。
        OnTouchTap?.Invoke(slotIndex);
    }

    private void HandleTouchLongPress()
    {
        if (OnTouchLongPress?.Invoke(slotIndex) == true)
            return;

        CreateRightClickUI();
    }

    private void HandleDesktopTap()
    {
        OnDesktopTap?.Invoke(slotIndex);
    }

    private void HandleMouseDragDrop()
    {
        if (OnMouseDragDrop != null)
            OnMouseDragDrop.Invoke(slotIndex);
        else
            HandleLeftClick();
    }

    private void HandleRightClick()
    {
        CreateRightClickUI();
    }
    #endregion

    #region 滚轮事件处理
    public void OnScroll(PointerEventData eventData)
    {
        if (!isPointerOver) return;

        float scrollY = eventData.scrollDelta.y;

        if (scrollY > 0)
            HandleScrollUp();
        else if (scrollY < 0)
            HandleScrollDown();
    }

    private void HandleScrollUp()
    {
        Debug.Log("滚轮向上：执行你定义的行为（如增加选择数量）");
        _OnScroll.Invoke(slotIndex, 1);
    }

    private void HandleScrollDown()
    {
        Debug.Log("滚轮向下：执行你定义的行为（如减少选择数量）");

        _OnScroll.Invoke(slotIndex, -1);
    }
    #endregion

    #region 创建右键菜单方法
    void CreateRightClickUI()
    {
        OnRightClick.Invoke(slotIndex);
    }
    #endregion

    #region 接口实现
    public void OnPointerDown(PointerEventData eventData)
    {
        bool isTouch = IsTouchPointer(eventData);
        if (eventData.button == PointerEventData.InputButton.Left && !isTouch)
        {
            EventSystemGuard.SetGamepadMode(false);
            mousePressStartedWithItem = !IsItemSlotEmpty(GetSlotData());
            mouseDragActive = false;
            suppressDesktopTapAfterDrag = false;
        }

        if (eventData.button == PointerEventData.InputButton.Left && IsShiftPressed())
        {
            _isShiftQuickTransferDragging = true;
            _shiftQuickTransferSessionId++;
            _lastHandledShiftQuickTransferSessionId = -1;
            TryInvokeShiftQuickTransfer();
            return;
        }

        // 触屏轻触延后到抬起确认，以便 0.45 秒长按能独立打开物品菜单而不先交换槽位。
        if (eventData.button == PointerEventData.InputButton.Left && isTouch)
        {
            CancelTouchPress();
            touchPointerId = eventData.pointerId;
            touchPressPosition = eventData.position;
            touchMovedTooFar = false;
            touchLongPressTriggered = false;
            touchPressStartedWithItem = !IsItemSlotEmpty(GetSlotData());
            touchItemDragActive = false;
            touchScrollDragActive = false;
            touchLongPressCoroutine = StartCoroutine(WaitForTouchLongPress());
            if (touchPressStartedWithItem)
                touchHalfDragReadyCoroutine = StartCoroutine(WaitForTouchHalfDragReady());
            return;
        }

        if (eventData.button == PointerEventData.InputButton.Right)
            HandleRightClick();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isPointerOver = true;

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
        if (eventData == null || eventData.pointerId != touchPointerId)
            return;

        // 指针离开槽位等同超过移动阈值，禁止滑动背包时误弹长按菜单。
        touchMovedTooFar = true;
        if (touchLongPressCoroutine != null)
        {
            StopCoroutine(touchLongPressCoroutine);
            touchLongPressCoroutine = null;
        }
        CancelTouchHalfDragReady();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left && eventData.pointerId == touchPointerId)
        {
            bool shouldTap = !touchMovedTooFar && !touchLongPressTriggered;
            bool shouldShowLongPressMenu = !touchMovedTooFar && touchLongPressMenuPending;
            CancelTouchPress();
            if (shouldShowLongPressMenu)
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
        if (eventData == null || eventData.pointerId != touchPointerId || touchMovedTooFar)
            return;

        Canvas canvas = GetComponentInParent<Canvas>();
        float scaleFactor = canvas != null ? Mathf.Max(0.01f, canvas.scaleFactor) : 1f;
        touchMovedTooFar = (eventData.position - touchPressPosition).magnitude / scaleFactor > touchMoveTolerance;
        if (touchMovedTooFar && touchLongPressCoroutine != null)
        {
            StopCoroutine(touchLongPressCoroutine);
            touchLongPressCoroutine = null;
        }
        if (touchMovedTooFar)
            CancelTouchHalfDragReady();
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

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (IsTouchPointer(eventData))
        {
            touchMovedTooFar = true;
            CancelTouchLongPress();
            if (eventData.button == PointerEventData.InputButton.Left && touchPressStartedWithItem)
            {
                Sprite touchDraggedSprite = image != null ? image.sprite : null;
                System.Func<int, bool> beginDrag = touchHalfDragReady
                    ? OnTouchHalfDragBegin ?? OnMouseDragBegin
                    : OnMouseDragBegin;
                touchItemDragActive = beginDrag?.Invoke(slotIndex) == true;
                if (touchItemDragActive)
                {
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
        mouseDragActive = OnMouseDragBegin?.Invoke(slotIndex) == true;
        if (!mouseDragActive)
        {
            mousePressStartedWithItem = false;
            return;
        }

        CreateMouseDragGhost(draggedSprite, eventData.position);
        UpdateMouseDragHover(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (IsTouchPointer(eventData))
        {
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

        PositionMouseDragGhost(eventData.position);
        UpdateMouseDragHover(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (IsTouchPointer(eventData))
        {
            if (touchItemDragActive)
            {
                ItemSlot_UI touchTargetSlot = FindSlotUnderPointer(eventData);
                if (touchTargetSlot != null && touchTargetSlot.isActiveAndEnabled)
                    touchTargetSlot.HandleMouseDragDrop();
                // 松手未命中槽位时不提交放下事务，整组物品继续留在玩家手上供后续单件分发。
                touchItemDragActive = false;
                touchPressStartedWithItem = false;
                EndMouseDragVisual();
                EventSystemGuard.SetGamepadMode(false);
            }
            else if (touchScrollDragActive)
            {
                FindParentScrollRect()?.OnEndDrag(eventData);
            }

            touchScrollDragActive = false;
            CancelTouchPress();
            return;
        }

        if (!mouseDragActive)
            return;

        ItemSlot_UI targetSlot = FindSlotUnderPointer(eventData);
        if (targetSlot != null && targetSlot.isActiveAndEnabled)
            targetSlot.HandleMouseDragDrop();
        // 松手未命中槽位时保留手上整组物品，不调用源槽位的放下逻辑。
        mousePressStartedWithItem = false;
        mouseDragActive = false;
        suppressDesktopTapAfterDrag = true;
        EndMouseDragVisual();
        EventSystemGuard.SetGamepadMode(false);
    }

    private IEnumerator WaitForTouchLongPress()
    {
        yield return new WaitForSecondsRealtime(touchLongPressSeconds);
        touchLongPressCoroutine = null;
        if (touchPointerId == int.MinValue || touchMovedTooFar)
            yield break;

        touchLongPressTriggered = true;
        if (touchPressStartedWithItem)
            touchLongPressMenuPending = true;
        else
            HandleTouchLongPress();
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

        if (OnTouchWorldLongPress?.Invoke(touchWorldPointerPosition) != true)
            yield break;

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
        CancelTouchLongPress();
        CancelTouchWorldLongPress();
        touchPointerId = int.MinValue;
        touchMovedTooFar = false;
        touchPressStartedWithItem = false;
        touchLongPressMenuPending = false;
        CancelTouchHalfDragReady();
    }

    private void CancelTouchLongPress()
    {
        if (touchLongPressCoroutine != null)
            StopCoroutine(touchLongPressCoroutine);
        touchLongPressCoroutine = null;
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

    /// <summary>
    /// 手柄 A/Submit 直接执行槽位的主要操作，补齐旧槽位仅支持鼠标 PointerDown 的缺口。
    /// </summary>
    public void OnSubmit(BaseEventData eventData)
    {
        eventData.Use();
        // InputSystem 的 Submit 同时包含 Enter 与手柄 A；Enter 仍按键鼠路径处理。
        if (WasKeyboardSubmitPressedThisFrame())
            HandleLeftClick();
        else
            HandleGamepadPrimaryAction();
    }

    /// <summary>
    /// 手柄 A/Submit 的独立确认入口，避免复用键鼠点击时的交换状态。
    /// </summary>
    public bool HandleGamepadPrimaryAction()
    {
        if (!isActiveAndEnabled)
            return false;

        OnGamepadSubmit.Invoke(slotIndex);
        return true;
    }

    /// <summary>
    /// 识别键盘 Enter，避免键盘确认误进入手柄交换目标。
    /// </summary>
    private static bool WasKeyboardSubmitPressedThisFrame()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            return keyboard.enterKey.wasPressedThisFrame ||
                   keyboard.numpadEnterKey.wasPressedThisFrame;
        }

        return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
    }

    /// <summary>
    /// 手柄次要键打开当前槽位的物品操作菜单。
    /// </summary>
    public bool HandleGamepadContextAction()
    {
        if (!isActiveAndEnabled)
            return false;

        CreateRightClickUI();
        return true;
    }

    /// <summary>
    /// 手柄选中槽位时加深描边，取消选中后恢复原始描边。
    /// </summary>
    public void OnSelect(BaseEventData eventData)
    {
        if (!EventSystemGuard.IsGamepadMode)
        {
            RestoreSelectionOutline();
            return;
        }

        EnsureSelectionOutline();
        if (selectionOutline == null)
            return;

        selectionOutline.enabled = true;
        selectionOutline.effectColor = FlatWorldUITheme.SelectionOutline;
        selectionOutline.effectDistance = FlatWorldUITheme.SelectionOutlineDistance;
        selectionOutline.useGraphicAlpha = false;
    }

    public void OnDeselect(BaseEventData eventData)
    {
        RestoreSelectionOutline();
    }

    private void EnsureSelectionOutline()
    {
        if (selectionOutline != null && selectionOutlineBaselineCaptured)
            return;

        selectionOutline = GetComponent<Outline>();
        if (selectionOutline == null)
        {
            Image targetImage = GetComponent<Image>() ?? image;
            if (targetImage != null)
            {
                selectionOutline = targetImage.GetComponent<Outline>();
                if (selectionOutline == null)
                {
                    selectionOutline = targetImage.gameObject.AddComponent<Outline>();
                    selectionOutlineCreated = true;
                    selectionOutline.enabled = false;
                }
            }
        }

        if (selectionOutline == null)
            return;

        selectionOutlineBaselineEnabled = selectionOutline.enabled;
        selectionOutlineBaselineColor = selectionOutline.effectColor;
        selectionOutlineBaselineDistance = selectionOutline.effectDistance;
        selectionOutlineBaselineUsesGraphicAlpha = selectionOutline.useGraphicAlpha;
        selectionOutlineBaselineCaptured = true;
    }

    private void RestoreSelectionOutline()
    {
        if (selectionOutline == null || !selectionOutlineBaselineCaptured)
            return;

        selectionOutline.enabled = selectionOutlineBaselineEnabled;
        selectionOutline.effectColor = selectionOutlineBaselineColor;
        selectionOutline.effectDistance = selectionOutlineBaselineDistance;
        selectionOutline.useGraphicAlpha = selectionOutlineBaselineUsesGraphicAlpha;
    }

    #endregion

    #region 鼠标拖放
    /// <summary>
    /// 创建跟随鼠标的半透明图标，让拖拽状态和携带物品清晰可见。
    /// </summary>
    private void CreateMouseDragGhost(Sprite draggedSprite, Vector2 screenPosition)
    {
        Canvas rootCanvas = GetComponentInParent<Canvas>()?.rootCanvas;
        if (rootCanvas == null || draggedSprite == null)
            return;

        GameObject ghostObject = new GameObject(
            "InventoryDragGhost",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        ghostObject.transform.SetParent(rootCanvas.transform, false);
        ghostObject.transform.SetAsLastSibling();
        mouseDragGhost = ghostObject.GetComponent<RectTransform>();
        mouseDragGhost.sizeDelta = image != null
            ? image.rectTransform.rect.size
            : new Vector2(64f, 64f);

        Image ghostImage = ghostObject.GetComponent<Image>();
        ghostImage.sprite = draggedSprite;
        ghostImage.preserveAspect = true;
        ghostImage.raycastTarget = false;
        ghostImage.color = new Color(1f, 1f, 1f, 0.82f);
        PositionMouseDragGhost(screenPosition);
    }

    private void PositionMouseDragGhost(Vector2 screenPosition)
    {
        if (mouseDragGhost == null)
            return;

        Canvas rootCanvas = mouseDragGhost.GetComponentInParent<Canvas>();
        RectTransform canvasRect = rootCanvas != null ? rootCanvas.transform as RectTransform : null;
        Camera eventCamera = rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? rootCanvas.worldCamera
            : null;
        if (canvasRect != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, screenPosition, eventCamera, out Vector2 localPosition))
        {
            mouseDragGhost.anchoredPosition = localPosition;
        }
    }

    private void UpdateMouseDragHover(PointerEventData eventData)
    {
        ItemSlot_UI nextSlot = FindSlotUnderPointer(eventData);
        if (nextSlot != mouseDragHoverSlot)
        {
            SetMouseDragHover(mouseDragHoverSlot, false);
            mouseDragHoverSlot = nextSlot;
            SetMouseDragHover(mouseDragHoverSlot, true);
        }

        if (!IsTouchPointer(eventData) || !touchItemDragActive)
            return;

        if (IsPointerOverUI(eventData))
            CancelTouchWorldLongPress();
        else
            UpdateTouchWorldLongPress(eventData);
    }

    private bool IsPointerOverUI(PointerEventData eventData)
    {
        if (EventSystem.current == null)
            return false;

        touchRaycastResults.Clear();
        EventSystem.current.RaycastAll(eventData, touchRaycastResults);
        for (int i = 0; i < touchRaycastResults.Count; i++)
        {
            GameObject hitObject = touchRaycastResults[i].gameObject;
            if (hitObject == null)
                continue;

            // 物品槽优先视为 UI，避免把槽位重叠区域当成世界落点。
            if (hitObject.GetComponentInParent<ItemSlot_UI>() != null)
                return true;

            BasePanel panel = hitObject.GetComponentInParent<BasePanel>();
            if (panel != null)
            {
                if (!panel.IsOpen())
                    continue;

                RectTransform panelRect = panel.Dragger != null
                    ? panel.Dragger.rectTransform
                    : panel.rectTransform;
                if (panelRect == null ||
                    RectTransformUtility.RectangleContainsScreenPoint(
                        panelRect,
                        eventData.position,
                        eventData.pressEventCamera))
                {
                    return true;
                }

                continue;
            }

            // 手机普通指向区是透明的世界指向层，允许在其空白区域长按丢弃。
            MobileVirtualJoystick joystick = hitObject.GetComponentInParent<MobileVirtualJoystick>();
            if (joystick != null && joystick.IsWorldDropSurface)
                continue;

            // 只拦截真实可操作控件，面板外的装饰 Image/Text 不应吞掉世界长按。
            if (hitObject.GetComponentInParent<Selectable>() != null)
                return true;
        }

        return false;
    }

    private void SetMouseDragHover(ItemSlot_UI slot, bool active)
    {
        if (slot == null)
            return;

        slot.EnsureSelectionOutline();
        if (slot.selectionOutline == null)
            return;

        if (active)
        {
            slot.dragHoverOutlineEnabled = slot.selectionOutline.enabled;
            slot.dragHoverOutlineColor = slot.selectionOutline.effectColor;
            slot.dragHoverOutlineDistance = slot.selectionOutline.effectDistance;
            slot.dragHoverOutlineUsesGraphicAlpha = slot.selectionOutline.useGraphicAlpha;
            slot.selectionOutline.enabled = true;
            slot.selectionOutline.effectColor = FlatWorldUITheme.AccentHover;
            slot.selectionOutline.effectDistance = FlatWorldUITheme.SelectionOutlineDistance;
            slot.selectionOutline.useGraphicAlpha = false;
            return;
        }

        slot.selectionOutline.enabled = slot.dragHoverOutlineEnabled;
        slot.selectionOutline.effectColor = slot.dragHoverOutlineColor;
        slot.selectionOutline.effectDistance = slot.dragHoverOutlineDistance;
        slot.selectionOutline.useGraphicAlpha = slot.dragHoverOutlineUsesGraphicAlpha;
    }

    private void EndMouseDragVisual()
    {
        SetMouseDragHover(mouseDragHoverSlot, false);
        mouseDragHoverSlot = null;
        if (mouseDragGhost != null)
            Destroy(mouseDragGhost.gameObject);
        mouseDragGhost = null;
    }

    /// <summary>
    /// 从完整射线结果中查找槽位，允许图标、文字或装饰层位于槽位背景上方。
    /// </summary>
    private static ItemSlot_UI FindSlotUnderPointer(PointerEventData eventData)
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
            return null;

        var raycastResults = new List<RaycastResult>();
        eventSystem.RaycastAll(eventData, raycastResults);
        for (int i = 0; i < raycastResults.Count; i++)
        {
            GameObject hitObject = raycastResults[i].gameObject;
            ItemSlot_UI slot = hitObject != null ? hitObject.GetComponentInParent<ItemSlot_UI>() : null;
            if (slot != null)
                return slot;
        }

        return null;
    }
    #endregion

    #region Shift快速转移
    private bool TryInvokeShiftQuickTransfer()
    {
        if (_lastHandledShiftQuickTransferSessionId == _shiftQuickTransferSessionId)
            return false;

        _lastHandledShiftQuickTransferSessionId = _shiftQuickTransferSessionId;
        OnShiftQuickTransfer.Invoke(slotIndex);
        return true;
    }

    private bool IsShiftPressed()
    {
        if (Keyboard.current != null)
            return Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed;

        return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
    }

    private bool IsLeftMousePressed()
    {
        if (Mouse.current != null)
            return Mouse.current.leftButton.isPressed;

        return Input.GetMouseButton(0);
    }
    #endregion

    #region UI更新方法
    /// <summary>缓存并校验槽位的图标与数量文本引用，兼容面板销毁后的延迟刷新。</summary>
    private bool CacheVisualReferences()
    {
        if (this == null || gameObject == null)
            return false;

        if (image == null)
            image = GetComponentInChildren<Image>(true);
        if (text == null)
            text = GetComponentInChildren<TMP_Text>(true);

        return image != null && text != null;
    }

    private void UpdateItemAmount()
    {
        if (text == null)
            return;

        ItemSlot slotData = GetSlotData();

        if (slotData == null || IsItemSlotEmpty(slotData))
        {
            text.enabled = false;
            return;
        }

        int itemAmount = (int)slotData.itemData.Stack.Amount;

        if (itemAmount == 0)
        {
            text.enabled = false;
            // 清空槽位数据
            ClearSlotDataAction?.Invoke(slotIndex);
        }
        else
        {
            text.text = itemAmount.ToString();
            text.enabled = true;
        }
    }

    private bool IsItemSlotEmpty(ItemSlot slotData)
    {
        return slotData?.itemData == null;
    }

    private void UpdateItemIcon()
    {
        if (image == null)
            return;

        ItemSlot slotData = GetSlotData();

        if (slotData == null ||
            slotData.itemData == null ||
            string.IsNullOrEmpty(slotData.itemData.IDName) ||
            GameRes.Instance == null)
        {
            image.gameObject.SetActive(false);
            return;
        }

        if (!GameRes.Instance.TryGetItemPresentation(
                slotData.itemData.IDName,
                out _,
                out Sprite sprite) ||
            sprite == null)
        {
            Debug.LogWarning($"[ItemSlot_UI] 无法找到物品显示贴图: {slotData.itemData.IDName}");
            image.gameObject.SetActive(false);
            return;
        }

        image.sprite = sprite;
        image.gameObject.SetActive(true);
    }
    #endregion
}
