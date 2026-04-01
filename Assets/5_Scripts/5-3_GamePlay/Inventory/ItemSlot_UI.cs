using Sirenix.OdinInspector;
using TMPro;
using UltEvents;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class ItemSlot_UI : MonoBehaviour, IPointerDownHandler, IPointerEnterHandler, IPointerExitHandler, IPointerUpHandler, IScrollHandler
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

    public UltEvent<int, float> _OnScroll = new UltEvent<int, float>();

    public UltEvent<int> OnRightClick = new UltEvent<int>();

    [Tooltip("Shift+左键快速转移事件")]
    public UltEvent<int> OnShiftQuickTransfer = new UltEvent<int>();

    private GameObject currentMenuInstance;


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
    }

    public void OnDestroy()
    {
        OnLeftClick.Clear();
        OnRightClick.Clear();
        OnShiftQuickTransfer.Clear();
        _OnScroll.Clear();
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

        if (slotData == null || slotData.itemData == null || string.IsNullOrEmpty(slotData.itemData.IDName))
        {
            image.gameObject.SetActive(false);
            return;
        }

        GameObject go = GameRes.Instance.AllPrefabs[slotData.itemData.IDName];
        if (go == null)
        {
            Debug.LogWarning($"[ItemSlot_UI] 无法找到预制体: {slotData.itemData.IDName}");
            image.gameObject.SetActive(false);
            return;
        }

        SpriteRenderer spriteRenderer = go.GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            image.sprite = spriteRenderer.sprite;
            image.gameObject.SetActive(true);
        }
        else
        {
            image.gameObject.SetActive(false);
        }
    }
    #endregion
}
