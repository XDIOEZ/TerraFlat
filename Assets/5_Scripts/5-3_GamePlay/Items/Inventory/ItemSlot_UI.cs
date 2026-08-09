using Sirenix.OdinInspector;
using TMPro;
using UltEvents;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class ItemSlot_UI : MonoBehaviour,
    IPointerDownHandler,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerUpHandler,
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

    private GameObject currentMenuInstance;

    private Outline selectionOutline;
    private bool selectionOutlineCreated;
    private bool selectionOutlineBaselineCaptured;
    private bool selectionOutlineBaselineEnabled;
    private Color selectionOutlineBaselineColor;
    private Vector2 selectionOutlineBaselineDistance;
    private bool selectionOutlineBaselineUsesGraphicAlpha;


    private bool isPointerOver = false;

    private static bool _isShiftQuickTransferDragging;
    private static int _shiftQuickTransferSessionId;
    private int _lastHandledShiftQuickTransferSessionId = -1;

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
        image = image ?? GetComponentInChildren<Image>();
        text = text ?? GetComponentInChildren<TMP_Text>();
        EnsureSelectionOutline();
    }

    public void OnDestroy()
    {
        OnLeftClick.Clear();
        OnGamepadSubmit.Clear();
        OnRightClick.Clear();
        OnShiftQuickTransfer.Clear();
        _OnScroll.Clear();
        if (selectionOutlineCreated && selectionOutline != null)
            Destroy(selectionOutline);
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
        UpdateItemAmount();
        UpdateItemIcon();
    }

    public void Click(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            HandleLeftClick();
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
        if (eventData.button == PointerEventData.InputButton.Left && IsShiftPressed())
        {
            _isShiftQuickTransferDragging = true;
            _shiftQuickTransferSessionId++;
            _lastHandledShiftQuickTransferSessionId = -1;
            TryInvokeShiftQuickTransfer();
            return;
        }

        Click(eventData);
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
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            _isShiftQuickTransferDragging = false;
        }
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
    private void UpdateItemAmount()
    {
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
