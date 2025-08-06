using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UI_Drag : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("引用")]
    public RectTransform rectTransform;
    public Canvas canvas;
    public Image draggableImage;

    [Header("属性")]
    public Vector2 originalPosition;
    public Vector2 offset;
    public int DefaultOrder = 0;
    [ShowInInspector]
    public static int CurrentOrder = 0;

    public bool IsDragging = false;

    public void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        canvas = gameObject.transform.parent.GetComponentInParent<Canvas>();
    }

    private void Start()
    {
       
      

        rectTransform.SetSiblingIndex(DefaultOrder);
        CurrentOrder = Mathf.Max(CurrentOrder, DefaultOrder);

        if (canvas == null)
        {
            Debug.LogError($"DraggableUI 需要在 Canvas {gameObject.name}的子物体上使用！");
        }

        if (draggableImage == null)
        {
            draggableImage = GetComponentInChildren<Image>();
        }
    }

    public void OnDisable()
    {
        // 这里可以选择保存状态或不保存，这里保留接口调用但不再依赖外部数据字典
        // 可自行替换为其他保存方式，如 PlayerPrefs 或 ScriptableObject
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!IsPointerOverDraggableImage(eventData) || IsRaycastBlocked(eventData))
            return;

        Vector2 clickPosInCanvas;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvas.transform as RectTransform,
            eventData.position,
            eventData.pressEventCamera,
            out clickPosInCanvas);

        Vector2 pivotPosInCanvas = rectTransform.anchoredPosition;
        offset = pivotPosInCanvas - clickPosInCanvas;

        CurrentOrder++;
        rectTransform.SetSiblingIndex(CurrentOrder);

        IsDragging = true;
        originalPosition = rectTransform.anchoredPosition;

        CurrentOrder++;
        canvas.sortingOrder = CurrentOrder;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
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
