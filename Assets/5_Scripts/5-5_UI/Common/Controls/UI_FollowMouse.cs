using UnityEngine;
using UnityEngine.InputSystem;

public class UI_FollowMouse : MonoBehaviour
{
    #region 层级与交互契约

    [SerializeField, Min(0)]
    [Tooltip("跟随指针的手持物显示层，必须高于所有游戏 UI。")]
    private int sortingOrder = UIManager.HeldItemSortingOrder;

    private Canvas canvas;
    private CanvasGroup canvasGroup;

    // 触屏库存交互必须跟随“实际操作槽位的那根手指”，不能依赖全局 Pointer.current。
    // Pointer.current 在 Device Simulator、多指和触点抬起后可能切回另一设备，导致手部槽被钉到错误坐标。
    private static bool hasTouchPointerPosition;
    private static Vector2 touchPointerPosition;

    #endregion

    [Tooltip("是否启用跟随鼠标功能")]
    public bool followMouse = true;

    [Tooltip("鼠标位置与 UI 元素的偏移量")]
    public Vector3 offset = Vector3.zero;

    public RectTransform rectTransform;

    private void Awake()
    {
        EnsurePresentationLayer();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPointerState()
    {
        hasTouchPointerPosition = false;
        touchPointerPosition = default;
    }

    private void OnEnable()
    {
        EnsurePresentationLayer();
    }

    private void Start()
    {
        if (rectTransform == null)
        {
            rectTransform = GetComponentInChildren<RectTransform>();
        }
    }

    private void Update()
    {
        if (followMouse && rectTransform != null)
        {
            FollowMousePosition();
        }

        // BasePanel.Open 会恢复 CanvasGroup 的交互状态；手持物只是跟随指针的视觉层，不能挡住目标槽位射线。
        if (canvasGroup != null)
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
    }

    private void FollowMousePosition()
    {
        if (!TryGetPointerScreenPosition(out Vector2 pointerPosition))
            return;

        rectTransform.position = new Vector3(pointerPosition.x, pointerPosition.y, 0f) + offset;
    }

    /// <summary>只读取当前实际参与 UI 操作的鼠标或按下触点，并拒绝 Input System 的非法坐标。</summary>
    private static bool TryGetPointerScreenPosition(out Vector2 screenPosition)
    {
        // 一旦触屏库存/丢弃手势提供了明确坐标，就持续保留该坐标，直到桌面指针明确接管。
        // 这样触点抬起后不会被 Device Simulator 的 Mouse.current 或另一根触点覆盖。
        if (hasTouchPointerPosition)
        {
            screenPosition = touchPointerPosition;
            return true;
        }

        // 跟随 Input System 当前指针，保证 Device Simulator/触屏/鼠标使用同一套坐标语义。
        // 触屏库存路径不会走到这里；这里主要服务桌面鼠标以及没有显式触摸所有权的兼容场景。
        Pointer pointer = Pointer.current;
        if (pointer != null)
        {
            screenPosition = pointer.position.ReadValue();
            if (IsFinite(screenPosition))
                return true;
        }

        // Pointer.current 极少数生命周期阶段可能暂时为空，再按具体设备兜底。
        if (Touchscreen.current != null)
        {
            screenPosition = Touchscreen.current.primaryTouch.position.ReadValue();
            if (IsFinite(screenPosition))
                return true;
        }

        if (Mouse.current != null)
        {
            screenPosition = Mouse.current.position.ReadValue();
            return IsFinite(screenPosition);
        }

        screenPosition = default;
        return false;
    }

    /// <summary>由实际触屏库存/丢弃 PointerEventData 提交位置，避免全局 Pointer.current 丢失触点身份。</summary>
    public static void ReportTouchPointerPosition(Vector2 screenPosition)
    {
        if (!IsFinite(screenPosition))
            return;

        touchPointerPosition = screenPosition;
        hasTouchPointerPosition = true;
    }

    /// <summary>桌面指针开始操作库存时解除触屏位置所有权，恢复实时鼠标跟随。</summary>
    public static void ReportDesktopPointerActivity()
    {
        hasTouchPointerPosition = false;
    }

    /// <summary>Transform 不接受 NaN/Infinity，输入坐标在进入 UI 表现层前必须有效。</summary>
    private static bool IsFinite(Vector2 value)
    {
        return !float.IsNaN(value.x) &&
               !float.IsInfinity(value.x) &&
               !float.IsNaN(value.y) &&
               !float.IsInfinity(value.y);
    }

    public void EnableFollowMouse(bool enable)
    {
        followMouse = enable;
    }

    /// <summary>统一手持物 Canvas 到全局最顶层，并缓存其非交互显示属性。</summary>
    private void EnsurePresentationLayer()
    {
        canvas ??= GetComponent<Canvas>();
        canvasGroup ??= GetComponent<CanvasGroup>();

        if (canvas != null)
        {
            canvas.overrideSorting = true;
            canvas.sortingOrder = Mathf.Max(UIManager.HeldItemSortingOrder, sortingOrder);
        }

        if (canvasGroup != null)
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
    }
}
