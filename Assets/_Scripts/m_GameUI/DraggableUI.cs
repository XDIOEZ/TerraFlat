using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
//�Զ����CanvasRayCaster���
[RequireComponent(typeof(GraphicRaycaster))]
public class DraggableUI : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public RectTransform rectTransform;
    public Canvas canvas;
    public Vector2 originalPosition;
    public Vector2 offset;
    public Image draggableImage; // ��Ҫ��ק�� Image
    public bool isDragging = false;

    private void Start()
    {
        rectTransform = GetComponent<RectTransform>();

        // ����Canvas���
        canvas = gameObject.transform.parent.GetComponentInParent<Canvas>();

        if (canvas == null)
        {
            Debug.LogError("DraggableUI ��Ҫ�� Canvas �ڲ�ʹ�ã�");
        }

        if (draggableImage == null)
        {
            draggableImage = GetComponentInChildren<Image>();
          //  Debug.LogError("DraggableUI ��Ҫ������ Image ����ϣ�");
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!IsPointerOverDraggableImage(eventData) || IsRaycastBlocked(eventData))
            return;

        // ��ȡRectTransform�ĵ�ǰ���ű��� 
        Vector2 scale = rectTransform.localScale;

        // ����Ļ����ת��Ϊ�������� 
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rectTransform,
            eventData.position,
            eventData.pressEventCamera,
            out offset);

        // ����ƫ�����Կ������ű��� 
        offset *= scale;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        // �����ק��ʼ���Ƿ��赲
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
        // �����ڴ˴������ק��������߼�
    }

    // ������Ƿ�Ϊ��ǰ Image
    private bool IsPointerOverDraggableImage(PointerEventData eventData)
    {
        GameObject clickedObject = eventData.pointerCurrentRaycast.gameObject;
        return clickedObject != null && clickedObject == draggableImage.gameObject;
    }

    // ��������Ƿ��赲
    private bool IsRaycastBlocked(PointerEventData eventData)
    {
        Vector2 screenPoint = eventData.position;
        Ray ray = Camera.main.ScreenPointToRay(screenPoint);
        RaycastHit2D hit = Physics2D.Raycast(ray.origin, ray.direction, Mathf.Infinity);
        return hit.collider != null && hit.collider.gameObject != draggableImage.gameObject;
    }
}