#if UNITY_EDITOR
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static partial class GameplayTestSystemProbe
{
    #region 库存分类弹层安全区采样

    [InitializeOnLoadMethod]
    private static void RegisterMultiSelectDropdownEditorRules()
        => Register("ui_settings", "inventory_category_popup_layout", CaptureInventoryCategoryPopupLayout);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterMultiSelectDropdownRuntimeRules() => RegisterMultiSelectDropdownEditorRules();

    // 实例化正式分类控件，只采样生产布局和滚动结果，越界判断交给 Lua。
    private static JObject CaptureInventoryCategoryPopupLayout(JObject args)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/2_Prefabs/2-1_UI/Common/Controls/UI_MultiSelectDropdown.prefab");
        float width = (float)Number(args, "width", 1920);
        float height = (float)Number(args, "height", 1080);
        float scale = Mathf.Min(width / 1920f, height / 1080f) * (float)Number(args, "scale", 1.5);
        var fixture = new GameObject("[LuaInventoryCategoryPopup]", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        fixture.hideFlags = HideFlags.HideAndDontSave;
        fixture.SetActive(false);
        EventSystem eventSystem = EventSystem.current;
        GameObject previousSelection = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
        UIMultiSelectDropdown dropdown = null;
        bool editorCallbacksInitialized = false;
        try
        {
            fixture.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var canvasRect = (RectTransform)fixture.transform;
            canvasRect.sizeDelta = new Vector2(width / scale, height / scale);
            var safeArea = (RectTransform)new GameObject("SafeAreaRoot", typeof(RectTransform)).transform;
            safeArea.SetParent(canvasRect, false);
            safeArea.anchorMin = Vector2.zero;
            safeArea.anchorMax = Vector2.one;
            safeArea.offsetMin = new Vector2((float)Number(args, "left", 0), (float)Number(args, "bottom", 0)) / scale;
            safeArea.offsetMax = -new Vector2((float)Number(args, "right", 0), (float)Number(args, "top", 0)) / scale;

            var window = (RectTransform)new GameObject("ScaledOwnerWindow", typeof(RectTransform)).transform;
            window.SetParent(safeArea, false);
            window.sizeDelta = new Vector2(1004, 560);
            window.localScale = new Vector3((float)Number(args, "parentScaleX", 1), (float)Number(args, "parentScaleY", 1), 1);
            GameObject instance = Object.Instantiate(prefab, window, false);
            var controlRect = (RectTransform)instance.transform;
            float corner = Flag(args, "oppositeCorner", false) ? -1f : 1f;
            controlRect.anchoredPosition = new Vector2(corner * (safeArea.rect.width * .5f - 40f) / window.localScale.x,
                -corner * (safeArea.rect.height * .5f - 40f) / window.localScale.y);
            dropdown = instance.GetComponent<UIMultiSelectDropdown>();
            var trigger = instance.GetComponent<Button>();
            var popup = (RectTransform)instance.transform.Find("Popup");
            var dismiss = instance.transform.Find("DismissArea").GetComponent<Button>();
            var scroll = popup.GetComponent<ScrollRect>();
            Vector2 sourceSize = popup.rect.size;
            fixture.SetActive(true);
            if (!Application.isPlaying)
            {
                InvokeInventoryCategoryPreviewLifecycle(dropdown, "Awake");
                editorCallbacksInitialized = true;
                InvokeInventoryCategoryPreviewLifecycle(dropdown, "OnEnable");
            }
            var options = new List<UIMultiSelectDropdown.Option>();
            for (int i = 0; i < 14; i++) options.Add(new UIMultiSelectDropdown.Option("category_" + i, "分类 " + i));
            dropdown.SetOptions(options);
            trigger.onClick.Invoke();
            Canvas.ForceUpdateCanvases();

            JObject CaptureExpandedPopup()
            {
                var corners = new Vector3[4];
                popup.GetWorldCorners(corners);
                Bounds bounds = new(safeArea.InverseTransformPoint(corners[0]), Vector3.zero);
                foreach (Vector3 point in corners) bounds.Encapsulate(safeArea.InverseTransformPoint(point));
                Rect safe = safeArea.rect;
                return new JObject
                {
                    ["left"] = bounds.min.x, ["right"] = bounds.max.x,
                    ["bottom"] = bounds.min.y, ["top"] = bounds.max.y,
                    ["safeLeft"] = safe.xMin, ["safeRight"] = safe.xMax,
                    ["safeBottom"] = safe.yMin, ["safeTop"] = safe.yMax,
                    ["scaleX"] = popup.localScale.x, ["scaleY"] = popup.localScale.y,
                    ["parentScaleX"] = window.localScale.x, ["parentScaleY"] = window.localScale.y,
                    ["hostIsSafeArea"] = popup.parent == safeArea && dismiss.transform.parent == safeArea,
                    ["popupOnTop"] = popup.GetSiblingIndex() == safeArea.childCount - 1 &&
                        dismiss.transform.GetSiblingIndex() == safeArea.childCount - 2,
                    ["popupActive"] = dropdown.IsExpanded && dismiss.gameObject.activeInHierarchy,
                    ["raycasts"] = dismiss.targetGraphic.raycastTarget && popup.GetComponent<Image>().raycastTarget,
                    ["viewportHeight"] = scroll.viewport.rect.height,
                    ["contentHeight"] = scroll.content.rect.height,
                    ["rows"] = scroll.content.GetComponentsInChildren<Toggle>().Length
                };
            }

            JObject beforeResize = CaptureExpandedPopup();
            float beforeScroll = scroll.content.anchoredPosition.y;
            scroll.verticalNormalizedPosition = 0f;
            Canvas.ForceUpdateCanvases();
            float scrollMovement = scroll.content.anchoredPosition.y - beforeScroll;
            canvasRect.sizeDelta *= (float)Number(args, "resizeFactor", .7);
            canvasRect.ForceUpdateRectTransforms();
            if (!Application.isPlaying) dropdown.RefreshExpandedPopupLayoutWithinSafeArea();
            Canvas.ForceUpdateCanvases();
            JObject afterResize = CaptureExpandedPopup();

            dropdown.SetSelected("category_4", true);
            dismiss.onClick.Invoke();
            bool closed = !dropdown.IsExpanded && !dismiss.gameObject.activeSelf && popup.parent == instance.transform &&
                dismiss.transform.parent == instance.transform && popup.localScale == Vector3.one && popup.rect.size == sourceSize;
            trigger.onClick.Invoke();
            Canvas.ForceUpdateCanvases();
            JObject reopened = CaptureExpandedPopup();
            bool selectionPreserved = dropdown.SelectedValues.Count == 1 && dropdown.SelectedValues[0] == "category_4";
            dropdown.Close();
            return new JObject { ["beforeResize"] = beforeResize, ["afterResize"] = afterResize, ["reopened"] = reopened,
                ["scrollMovement"] = scrollMovement, ["closed"] = closed, ["selectionPreserved"] = selectionPreserved };
        }
        finally
        {
            if (editorCallbacksInitialized) InvokeInventoryCategoryPreviewLifecycle(dropdown, "OnDisable");
            Object.DestroyImmediate(fixture);
            if (eventSystem != null) eventSystem.SetSelectedGameObject(previousSelection);
        }
    }

    // 固定三个生命周期仅供此正式控件的编辑态副本补齐初始化与解除订阅。
    private static void InvokeInventoryCategoryPreviewLifecycle(UIMultiSelectDropdown dropdown, string method)
        => typeof(UIMultiSelectDropdown).GetMethod(method, System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic).Invoke(dropdown, null);

    #endregion
}
#endif
