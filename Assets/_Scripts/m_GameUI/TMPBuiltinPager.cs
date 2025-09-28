using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
public class TMPBuiltinPagerMobile : MonoBehaviour
{
    [Header("TMP ����")]
    public TextMeshProUGUI textComponent;

    [Header("������򣨿�ѡ�� - ��Ϊ�ջ��Զ�ȡ����壩")]
    public RectTransform pageAreaRect;

    [Header("����")]
    public bool enabledPaging = true;
    public bool showDebugLabel = false;

    // �ڲ�
    private Canvas parentCanvas;
    private GraphicRaycaster graphicRaycaster;
    private EventSystem eventSystem;

    private int currentPageIndex = 0; // 0-based
    private int cachedPageCount = 1;

    // InputAction��һ���������/������ͳһ������һ����������Ҽ�����һҳ��
    private InputAction pointerPressAction;
    private InputAction rightClickAction;

    private bool initialized = false;

    void Awake()
    {
        if (textComponent == null)
        {
            Debug.LogError("TMPBuiltinPagerMobile: ���� Inspector �� TextMeshProUGUI �� textComponent �ֶΣ�");
            enabled = false;
            return;
        }

        parentCanvas = textComponent.GetComponentInParent<Canvas>();
        if (parentCanvas == null)
        {
            Debug.LogError("TMPBuiltinPagerMobile: TextMeshProUGUI ����λ�� Canvas �£��Ҳ����� Canvas����");
            enabled = false;
            return;
        }

        if (pageAreaRect == null)
            pageAreaRect = FindParentPanelRect(textComponent.rectTransform);

        graphicRaycaster = parentCanvas.GetComponent<GraphicRaycaster>();
        eventSystem = EventSystem.current;

        // ȷ�� TMP ʹ�� Page ģʽ
        textComponent.overflowMode = TextOverflowModes.Page;

        // �״μ���ҳ��������
        RebuildAndCachePages();
        ShowPage(0);

        initialized = true;
    }

    void OnEnable()
    {
        // pointerPressAction �󶨵� Mouse left �� Touch press���¼�������
        if (pointerPressAction == null)
        {
            pointerPressAction = new InputAction(type: InputActionType.Button);
            pointerPressAction.AddBinding("<Mouse>/leftButton");
            // �󶨴������£�primary touch press����������·��
            pointerPressAction.AddBinding("<Touchscreen>/primaryTouch/press");
        }

        if (rightClickAction == null)
        {
            rightClickAction = new InputAction(type: InputActionType.Button);
            rightClickAction.AddBinding("<Mouse>/rightButton");
        }

        pointerPressAction.performed += OnPointerPressPerformed;
        rightClickAction.performed += OnRightClickPerformed;

        pointerPressAction.Enable();
        rightClickAction.Enable();
    }

    void OnDisable()
    {
        if (pointerPressAction != null)
        {
            pointerPressAction.performed -= OnPointerPressPerformed;
            pointerPressAction.Disable();
        }
        if (rightClickAction != null)
        {
            rightClickAction.performed -= OnRightClickPerformed;
            rightClickAction.Disable();
        }
    }

    // pointerPress���������������£�����
    private void OnPointerPressPerformed(InputAction.CallbackContext ctx)
    {
        if (!enabledPaging || !initialized) return;

        // ������Ļ���꣺���Ȱ�����������������
        Vector2 screenPos = Vector2.zero;
        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
        {
            screenPos = Touchscreen.current.primaryTouch.position.ReadValue();
            // �������������Ұ�����������ҳ������Ȼ���ֻ�������
            if (IsPointerOverPageAreaByRaycast(screenPos))
            {
                // ����Ļ��ת���� pageAreaRect �ı������꣬���ж����Ұ���
                Camera cam = (parentCanvas.renderMode == RenderMode.ScreenSpaceCamera || parentCanvas.renderMode == RenderMode.WorldSpace)
                    ? parentCanvas.worldCamera : null;

                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(pageAreaRect, screenPos, cam, out Vector2 local))
                {
                    // RectTransform �� local origin �����ģ�x >= 0 ��ʾ�Ұ���
                    if (local.x >= 0)
                        NextPage();
                    else
                        PreviousPage();
                }
                else
                {
                    // ������ת�����꣨���ټ�����Ĭ����һҳ
                    NextPage();
                }
            }
        }
        else
        {
            // �Ǵ����豸��ͨ��Ϊ��꣩��ֱ�������λ�ò��������Ϊ����һҳ��
            if (Mouse.current != null)
                screenPos = Mouse.current.position.ReadValue();
            else
            {
                // ���ջ��ˣ������� Pointer.current
                if (Pointer.current != null)
                {
                    var posControl = Pointer.current.position;
                    if (posControl != null) screenPos = posControl.ReadValue();
                }
            }

            if (IsPointerOverPageAreaByRaycast(screenPos))
            {
                NextPage();
            }
        }
    }

    private void OnRightClickPerformed(InputAction.CallbackContext ctx)
    {
        if (!enabledPaging || !initialized) return;

        Vector2 screenPos = Vector2.zero;
        if (Mouse.current != null)
            screenPos = Mouse.current.position.ReadValue();
        else if (Pointer.current != null)
            screenPos = Pointer.current.position.ReadValue();

        if (IsPointerOverPageAreaByRaycast(screenPos))
            PreviousPage();
    }

    // ֻ����������Ҫʱ�ؽ�������ҳ��Ϣ������ ForceMeshUpdate��
    public void RebuildAndCachePages()
    {
        if (textComponent == null)
        {
            Debug.LogWarning("TMPBuiltinPagerMobile.RebuildAndCachePages: textComponent Ϊ null�������ؽ���");
            cachedPageCount = 1;
            currentPageIndex = 0;
            return;
        }

        if (!textComponent.gameObject.activeInHierarchy)
        {
            Debug.LogWarning("TMPBuiltinPagerMobile.RebuildAndCachePages: textComponent δ���ڼ���״̬������ ForceMeshUpdate��");
            cachedPageCount = 1;
            currentPageIndex = 0;
            return;
        }

        try
        {
            textComponent.ForceMeshUpdate();
            cachedPageCount = Mathf.Max(1, textComponent.textInfo.pageCount);
            currentPageIndex = Mathf.Clamp(currentPageIndex, 0, cachedPageCount - 1);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"TMPBuiltinPagerMobile.RebuildAndCachePages �쳣��{ex}");
            cachedPageCount = 1;
            currentPageIndex = 0;
        }
    }

    private void ShowPage(int pageIndex)
    {
        if (textComponent == null) return;
        pageIndex = Mathf.Clamp(pageIndex, 0, cachedPageCount - 1);
        currentPageIndex = pageIndex;
        textComponent.pageToDisplay = currentPageIndex + 1; // TMP �� 1-based
    }

    public void NextPage()
    {
        if (currentPageIndex < cachedPageCount - 1)
            ShowPage(currentPageIndex + 1);
    }

    public void PreviousPage()
    {
        if (currentPageIndex > 0)
            ShowPage(currentPageIndex - 1);
    }

    public void RefreshIfNeeded()
    {
        RebuildAndCachePages();
        ShowPage(currentPageIndex);
    }

    // �������м�⣺������ GraphicRaycaster
    private bool IsPointerOverPageAreaByRaycast(Vector2 screenPos)
    {
        if (pageAreaRect == null || parentCanvas == null) return false;

        if (graphicRaycaster != null && eventSystem != null)
        {
            PointerEventData ped = new PointerEventData(eventSystem) { position = screenPos };
            var results = new List<RaycastResult>();
            graphicRaycaster.Raycast(ped, results);

            foreach (var r in results)
            {
                if (IsDescendantOf(r.gameObject.transform, pageAreaRect.transform))
                    return true;
            }
            return false;
        }
        else
        {
            Camera cam = (parentCanvas.renderMode == RenderMode.ScreenSpaceCamera || parentCanvas.renderMode == RenderMode.WorldSpace)
                ? parentCanvas.worldCamera : null;
            return RectTransformUtility.RectangleContainsScreenPoint(pageAreaRect, screenPos, cam);
        }
    }

    private bool IsDescendantOf(Transform child, Transform ancestor)
    {
        if (child == null || ancestor == null) return false;
        Transform t = child;
        while (t != null)
        {
            if (t == ancestor) return true;
            t = t.parent;
        }
        return false;
    }

    private RectTransform FindParentPanelRect(RectTransform start)
    {
        Transform t = start.parent;
        while (t != null)
        {
            var rt = t as RectTransform;
            if (rt != null && rt.GetComponent<Canvas>() == null)
                return rt;
            t = t.parent;
        }
        return start;
    }

    void OnRectTransformDimensionsChange()
    {
        if (!initialized) return;
        // �������ߴ�仯����ת/������Ļ��ʱ�ؽ�ҳ����
        RebuildAndCachePages();
    }

    void OnGUI()
    {
        if (!showDebugLabel) return;
        GUI.Label(new Rect(10, 10, 300, 20), $"Page: {currentPageIndex + 1}/{cachedPageCount} (mobile)");
    }
}
