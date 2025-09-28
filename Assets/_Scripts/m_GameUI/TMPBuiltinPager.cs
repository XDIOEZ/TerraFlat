using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
public class TMPBuiltinPagerMobile : MonoBehaviour
{
    [Header("TMP 设置")]
    public TextMeshProUGUI textComponent;

    [Header("点击区域（可选） - 若为空会自动取父面板）")]
    public RectTransform pageAreaRect;

    [Header("其它")]
    public bool enabledPaging = true;
    public bool showDebugLabel = false;

    // 内部
    private Canvas parentCanvas;
    private GraphicRaycaster graphicRaycaster;
    private EventSystem eventSystem;

    private int currentPageIndex = 0; // 0-based
    private int cachedPageCount = 1;

    // InputAction：一个用于左键/触摸（统一处理），一个用于鼠标右键（上一页）
    private InputAction pointerPressAction;
    private InputAction rightClickAction;

    private bool initialized = false;

    void Awake()
    {
        if (textComponent == null)
        {
            Debug.LogError("TMPBuiltinPagerMobile: 请在 Inspector 绑定 TextMeshProUGUI 到 textComponent 字段！");
            enabled = false;
            return;
        }

        parentCanvas = textComponent.GetComponentInParent<Canvas>();
        if (parentCanvas == null)
        {
            Debug.LogError("TMPBuiltinPagerMobile: TextMeshProUGUI 必须位于 Canvas 下（找不到父 Canvas）。");
            enabled = false;
            return;
        }

        if (pageAreaRect == null)
            pageAreaRect = FindParentPanelRect(textComponent.rectTransform);

        graphicRaycaster = parentCanvas.GetComponent<GraphicRaycaster>();
        eventSystem = EventSystem.current;

        // 确保 TMP 使用 Page 模式
        textComponent.overflowMode = TextOverflowModes.Page;

        // 首次计算页数并缓存
        RebuildAndCachePages();
        ShowPage(0);

        initialized = true;
    }

    void OnEnable()
    {
        // pointerPressAction 绑定到 Mouse left 与 Touch press（事件驱动）
        if (pointerPressAction == null)
        {
            pointerPressAction = new InputAction(type: InputActionType.Button);
            pointerPressAction.AddBinding("<Mouse>/leftButton");
            // 绑定触摸按下（primary touch press）——常用路径
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

    // pointerPress（鼠标左键或触摸按下）处理
    private void OnPointerPressPerformed(InputAction.CallbackContext ctx)
    {
        if (!enabledPaging || !initialized) return;

        // 计算屏幕坐标：优先按触摸读，否则读鼠标
        Vector2 screenPos = Vector2.zero;
        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
        {
            screenPos = Touchscreen.current.primaryTouch.position.ReadValue();
            // 触摸：采用左右半区来决定翻页（更自然的手机交互）
            if (IsPointerOverPageAreaByRaycast(screenPos))
            {
                // 将屏幕点转换到 pageAreaRect 的本地坐标，以判断左右半区
                Camera cam = (parentCanvas.renderMode == RenderMode.ScreenSpaceCamera || parentCanvas.renderMode == RenderMode.WorldSpace)
                    ? parentCanvas.worldCamera : null;

                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(pageAreaRect, screenPos, cam, out Vector2 local))
                {
                    // RectTransform 的 local origin 在中心，x >= 0 表示右半区
                    if (local.x >= 0)
                        NextPage();
                    else
                        PreviousPage();
                }
                else
                {
                    // 若不能转换坐标（极少见），默认下一页
                    NextPage();
                }
            }
        }
        else
        {
            // 非触摸设备（通常为鼠标），直接用鼠标位置并把左键视为“下一页”
            if (Mouse.current != null)
                screenPos = Mouse.current.position.ReadValue();
            else
            {
                // 保险回退：尝试用 Pointer.current
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

    // 只有在真正需要时重建／缓存页信息（调用 ForceMeshUpdate）
    public void RebuildAndCachePages()
    {
        if (textComponent == null)
        {
            Debug.LogWarning("TMPBuiltinPagerMobile.RebuildAndCachePages: textComponent 为 null，跳过重建。");
            cachedPageCount = 1;
            currentPageIndex = 0;
            return;
        }

        if (!textComponent.gameObject.activeInHierarchy)
        {
            Debug.LogWarning("TMPBuiltinPagerMobile.RebuildAndCachePages: textComponent 未处于激活状态，跳过 ForceMeshUpdate。");
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
            Debug.LogError($"TMPBuiltinPagerMobile.RebuildAndCachePages 异常：{ex}");
            cachedPageCount = 1;
            currentPageIndex = 0;
        }
    }

    private void ShowPage(int pageIndex)
    {
        if (textComponent == null) return;
        pageIndex = Mathf.Clamp(pageIndex, 0, cachedPageCount - 1);
        currentPageIndex = pageIndex;
        textComponent.pageToDisplay = currentPageIndex + 1; // TMP 是 1-based
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

    // 矩形命中检测：优先用 GraphicRaycaster
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
        // 若容器尺寸变化（旋转/适配屏幕）时重建页索引
        RebuildAndCachePages();
    }

    void OnGUI()
    {
        if (!showDebugLabel) return;
        GUI.Label(new Rect(10, 10, 300, 20), $"Page: {currentPageIndex + 1}/{cachedPageCount} (mobile)");
    }
}
