using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// UI拖拽组件，处理UI元素的拖拽交互
/// 实现了IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler接口
/// </summary>
public class UI_Drag : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    #region 字段和属性
    
    [Header("引用")]
    /// <summary>
    /// 组件的RectTransform，用于控制UI位置
    /// </summary>
    public RectTransform rectTransform;
    
    /// <summary>
    /// 父Canvas引用，用于坐标转换和层级控制
    /// </summary>
    public Canvas canvas;
    
    /// <summary>
    /// 可拖拽的图片组件，用于射线检测
    /// </summary>
    public Image draggableImage;

    [Header("属性")]
    /// <summary>
    /// 原始位置，用于记录拖拽开始前的位置
    /// </summary>
    public Vector2 originalPosition;
    
    /// <summary>
    /// 拖拽偏移量，用于保持鼠标点击位置与UI中心点的相对位置
    /// </summary>
    public Vector2 offset;
    
    /// <summary>
    /// 默认层级顺序
    /// </summary>
    public int DefaultOrder = 0;
    
    /// <summary>
    /// 当前全局层级顺序，确保拖拽物体始终显示在最上层
    /// </summary>
    [ShowInInspector]
    public static int CurrentOrder = 0;

    /// <summary>
    /// 是否正在拖拽状态
    /// </summary>
    public bool IsDragging = false;
    
    #endregion

    #region 生命周期方法
    
    /// <summary>
    /// 初始化组件引用
    /// </summary>
    public void Awake()
    {        
        rectTransform = GetComponent<RectTransform>();
        if (canvas == null)
        {
            // 尝试从父物体获取Canvas引用
            canvas = gameObject.transform.parent.GetComponentInParent<Canvas>();
        }
    }

    /// <summary>
    /// 初始化层级和检查引用
    /// </summary>
    private void Start()
    {        
        // 设置初始层级
        rectTransform.SetSiblingIndex(DefaultOrder);
        CurrentOrder = Mathf.Max(CurrentOrder, DefaultOrder);

        // 验证引用是否正确
        if (canvas == null)
        {
            Debug.LogError($"DraggableUI 需要在 Canvas 的子物体上使用！游戏物体: {gameObject.name}");
        }

        // 自动获取图片引用
        if (draggableImage == null)
        {
            draggableImage = GetComponentInChildren<Image>();
        }
    }

    /// <summary>
    /// 组件禁用时的处理
    /// </summary>
    public void OnDisable()
    {        
        // 重置拖拽状态
        IsDragging = false;
        // 注意：如需保存状态，可在此添加保存逻辑
    }

    //TODO 自动获取所有的需要的字段
    public virtual void OnValidate()
    {
        if (rectTransform == null)
        {
            rectTransform = GetComponent<RectTransform>();
        }
        if (canvas == null)
        {
            canvas = GetComponentInParent<Canvas>();
        }
        if (draggableImage == null)
        {
            draggableImage = GetComponentInChildren<Image>();
        }
        
    }
    #endregion

    #region 拖拽事件处理

    /// <summary>
    /// 处理鼠标按下事件，记录偏移量并准备拖拽
    /// </summary>
    /// <param name="eventData">事件数据</param>
    public void OnPointerDown(PointerEventData eventData)
    {        
        // 只允许左键拖拽
        if (eventData.button != PointerEventData.InputButton.Left)
            return;
            
        // 检查是否点击在可拖拽区域
        if (!IsPointerOverDraggableImage(eventData))
            return;

        // 计算鼠标点击位置与UI位置的偏移量
        Vector2 clickPosInCanvas;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvas.transform as RectTransform,
            eventData.position,
            eventData.pressEventCamera,
            out clickPosInCanvas))
        {
            offset = rectTransform.anchoredPosition - clickPosInCanvas;
        }

        // 提升层级以显示在最上层
        CurrentOrder++;
        rectTransform.SetSiblingIndex(CurrentOrder);
        canvas.sortingOrder = CurrentOrder;

        // 记录拖拽开始状态
        IsDragging = true;
        originalPosition = rectTransform.anchoredPosition;
    }

    /// <summary>
    /// 处理开始拖拽事件（保留接口实现）
    /// </summary>
    /// <param name="eventData">事件数据</param>
    public void OnBeginDrag(PointerEventData eventData)
    {        
        // 只允许左键拖拽
        if (eventData.button != PointerEventData.InputButton.Left)
        {
            IsDragging = false;
            return;
        }
        
        // 开始拖拽时的额外处理可以在此添加
    }

    /// <summary>
    /// 处理拖拽过程中的位置更新
    /// </summary>
    /// <param name="eventData">事件数据</param>
    public void OnDrag(PointerEventData eventData)
    {        
        // 只允许左键拖拽
        if (eventData.button != PointerEventData.InputButton.Left)
        {
            IsDragging = false;
            return;
        }
        
        if (!IsDragging || canvas == null)
            return;

        // 计算新位置并更新UI
        Vector2 localPointerPosition;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvas.transform as RectTransform,
            eventData.position,
            eventData.pressEventCamera,
            out localPointerPosition))
        {
            rectTransform.anchoredPosition = localPointerPosition + offset;
            rectTransform.SetSiblingIndex(CurrentOrder);
        }
    }

    /// <summary>
    /// 处理拖拽结束事件
    /// </summary>
    /// <param name="eventData">事件数据</param>
    public void OnEndDrag(PointerEventData eventData)
    {        
        // 只处理左键拖拽结束
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            IsDragging = false;
            // 拖拽结束后的额外处理可以在此添加
        }
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 检查鼠标点击是否在可拖拽图片上
    /// </summary>
    /// <param name="eventData">事件数据</param>
    /// <returns>是否点击在可拖拽图片上</returns>
    private bool IsPointerOverDraggableImage(PointerEventData eventData)
    {        
        if (eventData == null || draggableImage == null)
            return false;
            
        GameObject clickedObject = eventData.pointerCurrentRaycast.gameObject;
        return clickedObject != null && clickedObject == draggableImage.gameObject;
    }

    #endregion
}