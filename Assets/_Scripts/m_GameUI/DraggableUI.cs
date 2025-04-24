using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
//自动添加CanvasRayCaster组件
[RequireComponent(typeof(GraphicRaycaster))]
public class DraggableUI : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public RectTransform rectTransform;
    public Canvas canvas;
    public Vector2 originalPosition;
    public Vector2 offset;
    public Image draggableImage; // 需要拖拽的 Image
    public bool isDragging = false;

    private void Start()
    {
        rectTransform = GetComponent<RectTransform>();

        // 查找Canvas组件
        canvas = gameObject.transform.parent.GetComponentInParent<Canvas>();

        if (canvas == null)
        {
            Debug.LogError("DraggableUI 需要在 Canvas 内部使用！");
        }

        if (draggableImage == null)
        {
            draggableImage = GetComponentInChildren<Image>();
          //  Debug.LogError("DraggableUI 需要挂载在 Image 组件上！");
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!IsPointerOverDraggableImage(eventData) || IsRaycastBlocked(eventData))
            return;

        // 获取RectTransform的当前缩放比例 
        Vector2 scale = rectTransform.localScale;

        // 将屏幕坐标转换为本地坐标 
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rectTransform,
            eventData.position,
            eventData.pressEventCamera,
            out offset);

        // 调整偏移量以考虑缩放比例 
        offset *= scale;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        // 检查拖拽起始点是否被阻挡
        if (!IsPointerOverDraggableImage(eventData) || IsRaycastBlocked(eventData))
            return;

        isDragging = true;
        originalPosition = rectTransform.anchoredPosition;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isDragging || canvas == null)
            return;

        Vector2 localPointerPosition;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvas.transform as RectTransform,
            eventData.position,
            eventData.pressEventCamera,
            out localPointerPosition))
        {
    
            rectTransform.anchoredPosition = localPointerPosition - offset;
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        isDragging = false;
        // 可以在此处添加拖拽结束后的逻辑
    }

    // 检测点击是否为当前 Image
    private bool IsPointerOverDraggableImage(PointerEventData eventData)
    {
        GameObject clickedObject = eventData.pointerCurrentRaycast.gameObject;
        return clickedObject != null && clickedObject == draggableImage.gameObject;
    }

    // 检测射线是否被阻挡
    private bool IsRaycastBlocked(PointerEventData eventData)
    {
        Vector2 screenPoint = eventData.position;
        Ray ray = Camera.main.ScreenPointToRay(screenPoint);
        RaycastHit2D hit = Physics2D.Raycast(ray.origin, ray.direction, Mathf.Infinity);
        return hit.collider != null && hit.collider.gameObject != draggableImage.gameObject;
    }
}