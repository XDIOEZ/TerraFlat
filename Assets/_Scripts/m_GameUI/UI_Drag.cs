using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UI_Drag : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("引用")]
    public RectTransform rectTransform;    // 当前物体的 RectTransform
    public Canvas canvas;                  // 所在的 Canvas
    public Image draggableImage;           // 可拖拽的目标图片
    [Header("属性")]
    public Vector2 originalPosition;       // 拖拽前的位置
    public Vector2 offset;                 // 鼠标点击点与物体中心的偏移
    public int DefaultOrder = 0;
    [ShowInInspector]
    public static int CurrentOrder = 0;

    public bool IsDragging = false;        // 拖拽状态标志
    [ShowInInspector]
    public IUI UIer;

    private void Start()
    {
        UIer = gameObject.transform.parent.GetComponentInParent<IUI>();
        rectTransform = GetComponent<RectTransform>();
        canvas = gameObject.transform.parent.GetComponentInParent<Canvas>();

        // 设置初始层级
        rectTransform.SetSiblingIndex(DefaultOrder);
        CurrentOrder = Mathf.Max(CurrentOrder, DefaultOrder);

        if (canvas == null)
        {
            Debug.LogError("DraggableUI 需要在 Canvas 的子物体上使用！");
        }

        if (draggableImage == null)
        {
            draggableImage = GetComponentInChildren<Image>();
        }

        if (UIer.UIDataDictionary.ContainsKey(transform.parent.gameObject.name))
        {
            SetUIState(UIer.UIDataDictionary[transform.parent.gameObject.name]);
        }
        else
        {
            UIer.UIDataDictionary[transform.parent.gameObject.name] = GetUIState();
        }
           
    }

    public void OnDisable()
    {
        UIer.UIDataDictionary[transform.parent.gameObject.name] = GetUIState();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!IsPointerOverDraggableImage(eventData) || IsRaycastBlocked(eventData))
            return;

        // Get click position in Canvas local space
        Vector2 clickPosInCanvas;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvas.transform as RectTransform,
            eventData.position,
            eventData.pressEventCamera,
            out clickPosInCanvas);

        // Get pivot position in Canvas local space (anchoredPosition is relative to anchor)
        Vector2 pivotPosInCanvas = rectTransform.anchoredPosition; // Assumes anchor aligns with Canvas coordinates

        // Calculate offset in Canvas local space
        offset = pivotPosInCanvas - clickPosInCanvas;

        // Detect Canvas center and adjust offset
        RectTransform canvasRect = canvas.GetComponent<RectTransform>();
        Vector2 canvasCenter = new Vector2(canvasRect.rect.width / 2, canvasRect.rect.height / 2);
        // Example adjustment: Shift offset relative to center (optional, customize as needed)
        // offset += canvasCenter; // Uncomment if you want to offset by center

        CurrentOrder++;
        rectTransform.SetSiblingIndex(CurrentOrder);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!IsPointerOverDraggableImage(eventData) || IsRaycastBlocked(eventData))
            return;

        IsDragging = true;
        originalPosition = rectTransform.anchoredPosition;

        CurrentOrder++;
        canvas.sortingOrder = CurrentOrder;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!IsDragging || canvas == null)
            return;

        Vector2 localPointerPosition;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvas.transform as RectTransform,
            eventData.position,
            eventData.pressEventCamera,
            out localPointerPosition))
        {
            // Set anchoredPosition so click point follows mouse
            rectTransform.anchoredPosition = localPointerPosition + offset;
            rectTransform.SetSiblingIndex(CurrentOrder);
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        IsDragging = false;
    }

    private bool IsPointerOverDraggableImage(PointerEventData eventData)
    {
        GameObject clickedObject = eventData.pointerCurrentRaycast.gameObject;
        return clickedObject != null && clickedObject == draggableImage.gameObject;
    }

    private bool IsRaycastBlocked(PointerEventData eventData)
    {
        Vector2 screenPoint = eventData.position;
        Ray ray = Camera.main.ScreenPointToRay(screenPoint);
        RaycastHit2D hit = Physics2D.Raycast(ray.origin, ray.direction, Mathf.Infinity);
        return hit.collider != null && hit.collider.gameObject != draggableImage.gameObject;
    }

    [Button("获取UI状态")]
    public UIData GetUIState()
    {
        return new UIData(rectTransform.anchoredPosition, rectTransform.localScale);
    }

    [Button("设置UI状态")]
    public void SetUIState(UIData data)
    {
        if (data == null)
            return;
        rectTransform.anchoredPosition = data.Position;
        rectTransform.localScale = data.Scale;
    }
}