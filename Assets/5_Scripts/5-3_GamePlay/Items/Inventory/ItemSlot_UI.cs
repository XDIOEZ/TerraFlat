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

/// <summary>
/// 允许库存拖拽物在不进入另一个库存槽的情况下触发玩法交互，例如把液体容器拖到已打开的容器上进行转移。
/// </summary>
public interface IInventoryDragDropTarget
{
    bool ContainsInventoryDropPoint(Vector2 screenPosition, Camera eventCamera);
    bool TryAcceptInventoryDrag(InventoryDragTransaction transaction, Vector2 screenPosition, Camera eventCamera);
}

public partial class ItemSlot_UI : MonoBehaviour,
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
    IGamepadPrimaryActionHandler
{
    #region 字段
    /// <summary>
    /// 槽位索引（替代对Data的直接引用）
    /// </summary>
    public int slotIndex = -1;

    public Inventory BoundInventory { get; private set; }

    public void ClearInventoryBinding(Inventory inventory)
    { if (ReferenceEquals(BoundInventory, inventory)) BoundInventory = null; }

    [Tooltip("显示当前物体的图标")]
    public Image image;

    [SerializeField, Tooltip("槽内加工的通用半透明进度层，由底部向上展开且不接收输入。")]
    private RectTransform processingProgress;

    [Tooltip("可选的选中描边图形；透明槽位指定物品图标，避免描边绘出整块透明背景")]
    public Graphic selectionGraphic;

    [Tooltip("显示当前物体的数量")]
    public TMP_Text text;

    [Header("鼠标悬浮信息")]
    [SerializeField, Tooltip("统一物品悬浮信息面板")]
    private GameObject itemTooltipPrefab;

    [Tooltip("物体被点击的事件（左键）")]
    public UltEvent<int> OnLeftClick = new UltEvent<int>();

    [Tooltip("手柄确认槽位事件，与鼠标点击路径分离")]
    public UltEvent<int> OnGamepadSubmit = new UltEvent<int>();

    public UltEvent<int, float> _OnScroll = new UltEvent<int, float>();

    /// <summary>Shift+滚轮事件，用于按来源堆叠的一半数量转移物品。</summary>
    public UltEvent<int, float> _OnShiftScroll = new UltEvent<int, float>();

    [Tooltip("Shift+左键快速转移事件")]
    public UltEvent<int> OnShiftQuickTransfer = new UltEvent<int>();

    /// <summary>鼠标与触屏甩动共用屏幕起点和净方向，与 Shift 固定目标入口分开。</summary>
    public System.Action<int, Vector2, Vector2> OnFlickTransfer { get; set; }

    /// <summary>由库存判断当前方向是否指向其他目标界面，范围外不消费快速拖动。</summary>
    public System.Func<Vector2, Vector2, bool> HasFlickTarget { get; set; }

    /// <summary>鼠标或触屏拖拽开始时，创建保留来源槽的拖拽事务。</summary>
    public System.Func<int, InventoryDragTransaction> OnMouseDragBegin { get; set; }

    /// <summary>鼠标或触屏拖拽结束时，由目标槽提交来源拖拽事务。</summary>
    public System.Func<int, InventoryDragTransaction, bool> OnMouseDragDrop { get; set; }

    /// <summary>触屏轻触入口，允许快捷栏将轻触与物品交换分开处理。</summary>
    public System.Action<int> OnTouchTap { get; set; }

    /// <summary>触屏长按入口；返回 true 表示已完成整组放置。</summary>
    public System.Func<int, bool> OnTouchLongPress { get; set; }

    /// <summary>判断当前槽位是否可执行“手部整组长按放下”，仅用于长按进度提示与即时提交。</summary>
    public System.Func<int, bool> CanTouchLongPressPutDown { get; set; }

    /// <summary>由真实库存确认当前槽有物品且玩家手部携带槽为空。</summary>
    public System.Func<int, bool> CanTouchInspectItem { get; set; }

    /// <summary>把目标槽位的长按进度转发给唯一的手部插槽视觉。</summary>
    public System.Action<float, bool> OnTouchLongPressProgress { get; set; }

    /// <summary>触屏拖拽物品后在世界非 UI 区域长按的入口。</summary>
    public System.Func<Vector2, bool> OnTouchWorldLongPress { get; set; }

    /// <summary>触屏长按更久后开始拖拽时，把源堆拆出一半并建立拖拽事务。</summary>
    public System.Func<int, InventoryDragTransaction> OnTouchHalfDragBegin { get; set; }

    /// <summary>桌面左键入口；背包执行整组取放，快捷栏空手时选中槽位。</summary>
    public System.Action<int> OnDesktopTap { get; set; }

    /// <summary>指针事务成功增加物品后发布表现事件，不参与库存扣料或合并。</summary>
    public event System.Action<ItemSlot_UI, Vector2, Camera, bool, float> ItemAddedAtPointer;
    private Vector2 interactionPoint;
    private Camera interactionCamera;

    private void RememberPointer(PointerEventData data)
    {
        ReportPointerToHandVisual(data);
        interactionPoint = data.position;
        interactionCamera = data.pressEventCamera ?? data.enterEventCamera;
    }

    /// <summary>手部槽的触屏位置只接受本次槽位手势自身的 PointerEventData，避免被其它触点或模拟鼠标覆盖。</summary>
    private static void ReportPointerToHandVisual(PointerEventData data)
    {
        if (data == null)
            return;

        if (IsTouchPointer(data))
            UI_FollowMouse.ReportTouchPointerPosition(data.position);
        else
            UI_FollowMouse.ReportDesktopPointerActivity();
    }

    private void PerformPointerAction(System.Action action)
    {
        if (ItemAddedAtPointer == null) { action(); return; }
        ItemData before = GetSlotData()?.itemData;
        string previousId = before?.IDName;
        float previousAmount = before?.Stack?.Amount ?? 0f;
        action();
        ItemData after = GetSlotData()?.itemData;
        float added = (after?.Stack?.Amount ?? 0f) - (after?.IDName == previousId ? previousAmount : 0f);
        if (after != null && added > 0)
            ItemAddedAtPointer?.Invoke(this, interactionPoint, interactionCamera, before != null, added);
    }

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
    private InventoryDragTransaction activeDragTransaction;
    private bool hideSourceContentWhileDragging;
    private bool dragHoverOutlineEnabled;
    private Color dragHoverOutlineColor;
    private Vector2 dragHoverOutlineDistance;
    private bool dragHoverOutlineUsesGraphicAlpha;

    private static bool _isShiftQuickTransferDragging;
    private static int _shiftQuickTransferSessionId;
    private int _lastHandledShiftQuickTransferSessionId = -1;

    [Header("手机长按")]
    [SerializeField, Min(0.1f)] private float touchLongPressSeconds = 0.45f;
    [SerializeField, Min(0f)] private float touchLongPressProgressDelaySeconds = 0.2f;
    [SerializeField, Min(0.1f)] private float touchHalfDragReadySeconds = 0.85f;
    [SerializeField, Min(1f)] private float touchMoveTolerance = 16f;
    [SerializeField] private GameObject touchLongPressProgressRoot;
    [Tooltip("方形长按进度的上边；保留旧字段名以兼容已有 Prefab。")]
    [SerializeField] private Image touchLongPressProgressFill;
    [SerializeField] private Image touchLongPressProgressRight;
    [SerializeField] private Image touchLongPressProgressBottom;
    [SerializeField] private Image touchLongPressProgressLeft;
    private int touchPointerId = int.MinValue;
    private Vector2 touchPressPosition;
    private bool touchMovedTooFar;
    private bool touchLongPressTriggered;
    private bool touchLongPressPutDownIntent;
    private bool touchLongPressPutDownCommitted;
    private bool touchHalfDragReady;
    private bool touchPressStartedWithItem;
    private bool touchItemDragActive;
    private bool touchScrollDragActive;
    private Coroutine touchLongPressCoroutine;
    private Coroutine touchHalfDragReadyCoroutine;
    private Coroutine touchWorldLongPressCoroutine;
    private Vector2 touchWorldPressPosition;
    private Vector2 touchWorldPointerPosition;
    private readonly InventorySlotPointerQuery pointerQuery = new();

    // 两种指针统一读取玩家偏好，槽位不再保留隐藏的独立门槛。
    private readonly InventoryFlickGesture touchFlickGesture = new();
    private readonly InventoryFlickGesture mouseFlickGesture = new();

    [Header("手机长按物品介绍")]
    [SerializeField, Min(0.1f)] private float touchTooltipSeconds = 0.5f;
    private Coroutine touchTooltipCoroutine;
    private bool touchTooltipTriggered;

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

    /// <summary>销毁槽位时收束未完成拖拽并解除所有运行时回调。</summary>
    public void OnDestroy()
    {
        BoundInventory = null;
        InventoryItemTooltip.Hide(this);
        CompleteActiveDrag(true, false);
        OnLeftClick.Clear();
        OnGamepadSubmit.Clear();
        OnShiftQuickTransfer.Clear();
        OnFlickTransfer = null;
        HasFlickTarget = null;
        OnMouseDragBegin = null;
        OnMouseDragDrop = null;
        ItemAddedAtPointer = null;
        OnTouchTap = null;
        OnTouchLongPress = null;
        CanTouchLongPressPutDown = null;
        CanTouchInspectItem = null;
        OnTouchLongPressProgress = null;
        OnTouchWorldLongPress = null;
        OnTouchHalfDragBegin = null;
        OnDesktopTap = null;
        _OnScroll.Clear();
        _OnShiftScroll.Clear();
        if (selectionOutlineCreated && selectionOutline != null)
            Destroy(selectionOutline);
        EndMouseDragVisual();
        CancelTouchPress();
    }

    /// <summary>槽位面板停用时收束未完成拖拽，避免事务悬挂。</summary>
    private void OnDisable()
    {
        InventoryItemTooltip.Hide(this);
        CompleteActiveDrag(true, false);
        EndMouseDragVisual();
        CancelTouchPress();
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused)
        {
            touchFlickGesture.Reset();
            mouseFlickGesture.Reset();
            CancelTouchTooltipHold();
            InventoryItemTooltip.Hide(this);
        }
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused)
        {
            touchFlickGesture.Reset();
            mouseFlickGesture.Reset();
            CancelTouchTooltipHold();
            InventoryItemTooltip.Hide(this);
        }
    }
    #endregion

    #region 公共方法
    /// <summary>
    /// 初始化槽位（替代 Data = ... 的直接赋值）
    /// </summary>
    public void InitializeSlot(int index, System.Func<int, ItemSlot> getSlotFunc, System.Action<int> clearAction,
        Inventory inventory = null)
    {
        SetProcessingProgress(0f);
        CancelTouchTooltipHold();
        InventoryItemTooltip.Hide(this);
        slotIndex = index;
        GetSlotDataFunc = getSlotFunc;
        ClearSlotDataAction = clearAction;
        BoundInventory = inventory;
    }

    /// <summary>获取这个 UI 当前实际绑定的槽位，外部逻辑不要直接调用槽位委托。</summary>
    public ItemSlot GetBoundSlotData()
    {
        return GetSlotData();
    }

    /// <summary>只显示外部提供的真实加工进度，不在界面计时或扣物品。</summary>
    public void SetProcessingProgress(float normalizedProgress)
    {
        if (processingProgress == null) return;
        float value = Mathf.Clamp01(normalizedProgress);
        processingProgress.localScale = new Vector3(1f, value, 1f);
        processingProgress.gameObject.SetActive(value > 0f && !hideSourceContentWhileDragging);
    }

#if UNITY_EDITOR
    public void ConfigureProcessingProgress(RectTransform progress)
    {
        processingProgress = progress;
        SetProcessingProgress(0f);
    }

    /// <summary>编辑器构建器绑定正式 Prefab 内的长按进度视觉。</summary>
    public void ConfigureTouchLongPressProgressVisuals(
        GameObject progressRoot,
        Image progressTop,
        Image progressRight,
        Image progressBottom,
        Image progressLeft)
    {
        touchLongPressProgressRoot = progressRoot;
        touchLongPressProgressFill = progressTop;
        touchLongPressProgressRight = progressRight;
        touchLongPressProgressBottom = progressBottom;
        touchLongPressProgressLeft = progressLeft;
        ResetTouchLongPressProgressVisual();
    }
#endif

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

    /// <summary>非标准槽位复用 UI_Slot 上的统一悬浮面板引用，避免每个库存面板重复配置。</summary>
    private GameObject ResolveItemTooltipPrefab()
    {
        if (itemTooltipPrefab != null)
            return itemTooltipPrefab;

        GameObject sharedSlotPrefab = GameRes.ExistingInstance?.GetPrefab("UI_Slot", false);
        ItemSlot_UI sharedSlot = sharedSlotPrefab != null ? sharedSlotPrefab.GetComponent<ItemSlot_UI>() : null;
        return sharedSlot != null && sharedSlot != this ? sharedSlot.itemTooltipPrefab : null;
    }

    [Button]
    public void RefreshUI()
    {
        // 维度/场景切换期间可能收到旧库存事件，旧槽位的子级控件可能已被 Unity 销毁。
        if (!CacheVisualReferences())
            return;

        UpdateItemAmount();
        UpdateItemIcon();
        if (GetSlotData()?.itemData == null || hideSourceContentWhileDragging)
            SetProcessingProgress(0f);
        if (hideSourceContentWhileDragging)
            HideSlotContent();
        if (isPointerOver)
            InventoryItemTooltip.Refresh(this);
    }

    public void Click(PointerEventData eventData)
    {
        RememberPointer(eventData);
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            if (IsTouchPointer(eventData))
                HandleTouchTap();
            else
                HandleDesktopTap();
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
        PerformPointerAction(() => OnTouchTap?.Invoke(slotIndex));
    }

    private bool HandleTouchLongPress()
    {
        bool handled = false;
        PerformPointerAction(() => handled = OnTouchLongPress?.Invoke(slotIndex) == true);
        return handled;
    }

    private void HandleDesktopTap()
    {
        PerformPointerAction(() => OnDesktopTap?.Invoke(slotIndex));
    }

    /// <summary>让当前目标槽尝试提交指定拖拽事务。</summary>
    private bool HandleMouseDragDrop(InventoryDragTransaction transaction, PointerEventData data)
    {
        RememberPointer(data);
        bool accepted = false;
        PerformPointerAction(() => accepted = OnMouseDragDrop?.Invoke(slotIndex, transaction) == true);
        return accepted;
    }

    #endregion

    #region 滚轮事件处理
    public void OnScroll(PointerEventData eventData)
    {
        if (!isPointerOver) return;

        float scrollY = eventData.scrollDelta.y;

        if (scrollY > 0)
            HandleScrollUp(IsShiftPressed());
        else if (scrollY < 0)
            HandleScrollDown(IsShiftPressed());

        // 指针命中槽位时滚轮专用于逐件取放，只有槽位外区域才交给父级页面滚动。
        if (!Mathf.Approximately(scrollY, 0f))
            eventData.Use();
    }

    private void HandleScrollUp(bool shiftPressed)
    {
        (shiftPressed ? _OnShiftScroll : _OnScroll).Invoke(slotIndex, 1);
    }

    private void HandleScrollDown(bool shiftPressed)
    {
        (shiftPressed ? _OnShiftScroll : _OnScroll).Invoke(slotIndex, -1);
    }
    #endregion

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

        selectionOutline = selectionGraphic != null ? selectionGraphic.GetComponent<Outline>() : GetComponent<Outline>();
        if (selectionOutline == null)
        {
            Graphic targetImage = selectionGraphic != null ? selectionGraphic : (GetComponent<Image>() ?? image);
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
                slotData.itemData,
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
