#if UNITY_EDITOR
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static partial class GameplayTestSystemProbe
{
    #region 正式 UI 文字排版采样

    [InitializeOnLoadMethod]
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterUITextLayoutRules()
    {
        Register("ui_settings", "text_fitting_assets", CaptureUITextLayoutAssets);
        Register("ui_settings", "food_segmented_bar", CaptureFoodSegmentedBar);
    }

    // 使用正式食物行的独立副本采样几何与过渡，不创建玩家、不修改营养或用户 HUD。
    private static JObject CaptureFoodSegmentedBar(JObject args)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/2_Prefabs/2-1_UI/Gameplay/Status/Player/UI_Food.prefab");
        Transform source = prefab.transform.Find("Panel/食物");
        GameObject holder = new GameObject("FoodSegmentProbe", typeof(RectTransform));
        holder.SetActive(false);
        try
        {
            GameObject row = Object.Instantiate(source.gameObject, holder.transform, false);
            ThreeSegmentStatusBarGraphic graphic = row.GetComponentInChildren<ThreeSegmentStatusBarGraphic>(true);
            Color first = graphic.FirstColor, second = graphic.SecondColor, third = graphic.ThirdColor;
            FlatWorldUITheme.Apply(row.transform);
            FlatWorldUITheme.ApplyRuntimePalette(row.transform);
            var values = (JArray)args["values"];
            Vector3 target = new Vector3(values[0].Value<float>(), values[1].Value<float>(), values[2].Value<float>());
            float maximum = args["maximum"].Value<float>();
            if (args["previous"] is JArray previous)
                graphic.SetSegmentValues(new Vector3(previous[0].Value<float>(), previous[1].Value<float>(),
                    previous[2].Value<float>()), maximum, 0f);
            graphic.SetSegmentValues(target, maximum, .24f);
            Vector3 initial = graphic.SegmentFractions;
            graphic.AdvanceTransition(.12f);
            Vector3 middle = graphic.SegmentFractions;
            if (args["redirect"] is JArray redirect)
                graphic.SetSegmentValues(new Vector3(redirect[0].Value<float>(), redirect[1].Value<float>(),
                    redirect[2].Value<float>()), maximum, .24f);
            Vector3 redirectedStart = graphic.SegmentFractions;
            graphic.AdvanceTransition(.24f);
            Vector3 final = graphic.SegmentFractions;
            bool bounded = true, transparent = true;
            foreach (RectTransform rect in row.GetComponentsInChildren<RectTransform>(true))
            {
                if (rect == row.transform || !IsActiveWithinFoodRowHierarchy(rect, row.transform))
                    continue;
                RectTransform parent = (RectTransform)rect.parent;
                var corners = new Vector3[4];
                rect.GetLocalCorners(corners);
                Matrix4x4 local = Matrix4x4.TRS(rect.localPosition, rect.localRotation, rect.localScale);
                foreach (Vector3 corner in corners)
                {
                    Vector3 point = local.MultiplyPoint3x4(corner);
                    bounded &= point.x >= parent.rect.xMin - .01f && point.x <= parent.rect.xMax + .01f &&
                        point.y >= parent.rect.yMin - .01f && point.y <= parent.rect.yMax + .01f;
                }
            }
            foreach (Graphic child in row.GetComponentsInChildren<Graphic>(true))
                transparent &= !child.raycastTarget;
            return new JObject {
                ["initial"] = new JArray(initial.x, initial.y, initial.z),
                ["middle"] = new JArray(middle.x, middle.y, middle.z),
                ["redirectedStart"] = new JArray(redirectedStart.x, redirectedStart.y, redirectedStart.z),
                ["final"] = new JArray(final.x, final.y, final.z),
                ["themePreserved"] = graphic.FirstColor == first && graphic.SecondColor == second &&
                    graphic.ThirdColor == third && graphic.color == Color.white && UIVisualOwner.OwnsVisuals(graphic),
                ["bounded"] = bounded, ["inputTransparent"] = transparent,
                ["rowCount"] = source.parent.childCount,
                ["ordinarySliders"] = prefab.GetComponentsInChildren<Slider>(true).Length };
        }
        finally { Object.DestroyImmediate(holder); }
    }

    private static bool IsActiveWithinFoodRowHierarchy(Transform element, Transform rowRoot)
    {
        for (Transform current = element; current != null; current = current.parent)
        {
            if (!current.gameObject.activeSelf)
                return false;
            if (current == rowRoot)
                return true;
        }

        return false;
    }

    private static JObject CaptureUITextLayoutAssets(JObject args)
    {
        var rows = new JArray();
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[]
        {
            "Assets/2_Prefabs/2-1_UI", "Assets/Resources/UI"
        });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root == null)
                continue;

            InventoryItemTooltip tooltip = root.GetComponent<InventoryItemTooltip>();
            Object tooltipBody = tooltip != null
                ? new SerializedObject(tooltip).FindProperty("bodyText").objectReferenceValue
                : null;
            ReadableBookPanel book = root.GetComponent<ReadableBookPanel>();
            SerializedObject bookFields = book != null ? new SerializedObject(book) : null;
            foreach (TextMeshProUGUI text in root.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                // 只读取正式资产，不实例化面板或改动玩家当前打开的界面。
                string mode = "bounded";
                TMP_InputField input = text.GetComponentInParent<TMP_InputField>(true);
                ContentSizeFitter fitter = text.GetComponent<ContentSizeFitter>();
                Transform parent = text.transform.parent;
                ContentSizeFitter parentFitter = parent != null ? parent.GetComponent<ContentSizeFitter>() : null;
                HorizontalOrVerticalLayoutGroup parentLayout = parent != null
                    ? parent.GetComponent<HorizontalOrVerticalLayoutGroup>() : null;
                LayoutElement element = text.GetComponent<LayoutElement>();
                if (input != null && input.textComponent == text)
                    mode = "input";
                else if (text.overflowMode == TextOverflowModes.Page || text.overflowMode == TextOverflowModes.Linked ||
                    bookFields != null &&
                    (bookFields.FindProperty("leftPageText").objectReferenceValue == text ||
                     bookFields.FindProperty("rightPageText").objectReferenceValue == text))
                    mode = "pages";
                else if (tooltipBody == text ||
                    fitter != null && (fitter.verticalFit != ContentSizeFitter.FitMode.Unconstrained ||
                                       fitter.horizontalFit != ContentSizeFitter.FitMode.Unconstrained) ||
                    parentFitter != null && parentFitter.verticalFit != ContentSizeFitter.FitMode.Unconstrained &&
                    parentLayout != null && parentLayout.childControlHeight &&
                    (element == null || element.preferredHeight < 0f))
                    mode = "content";

                var row = new JObject
                {
                    ["prefab"] = path, ["name"] = text.name, ["mode"] = mode,
                    ["auto"] = text.enableAutoSizing, ["min"] = text.fontSizeMin, ["max"] = text.fontSizeMax,
                    ["overflow"] = (int)text.overflowMode,
                    ["designSize"] = new SerializedObject(text).FindProperty("m_fontSizeBase").floatValue
                };
                if (tooltipBody == text)
                    row["lineSpacing"] = text.lineSpacing;
                if (parent != null && parent.TryGetComponent(out Button button) &&
                    parent.GetComponent<LayoutGroup>() == null)
                {
                    RectTransform buttonRect = (RectTransform)button.transform;
                    if (buttonRect.rect.width > 0f)
                        row["button"] = CaptureButtonLocalTextRectangle(buttonRect, text.rectTransform);
                    else
                    {
                        // 独立伸展模板没有宿主宽度，给停用副本真实尺寸后再核对各分辨率。
                        GameObject candidate = Object.Instantiate(button.gameObject);
                        candidate.SetActive(false);
                        candidate.hideFlags = HideFlags.HideAndDontSave;
                        try
                        {
                            RectTransform rectangle = candidate.GetComponent<RectTransform>();
                            RectTransform label = (RectTransform)candidate.transform.GetChild(text.transform.GetSiblingIndex());
                            var widths = new JArray();
                            foreach (Vector2 size in new[] { new Vector2(2560, 1440), new Vector2(1920, 1080),
                                new Vector2(1600, 900), new Vector2(1280, 720), new Vector2(720, 1280) })
                            {
                                rectangle.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size.x);
                                rectangle.ForceUpdateRectTransforms();
                                JObject bounds = CaptureButtonLocalTextRectangle(rectangle, label);
                                bounds["width"] = size.x; bounds["height"] = size.y;
                                widths.Add(bounds);
                            }
                            row["button"] = widths[0].DeepClone(); row["buttonResolutions"] = widths;
                        }
                        finally { Object.DestroyImmediate(candidate); }
                    }
                }
                rows.Add(row);
            }
        }
        return new JObject { ["prefabs"] = guids.Length, ["texts"] = rows };
    }

    private static JObject CaptureButtonLocalTextRectangle(RectTransform button, RectTransform text)
    {
        // 按直接父级的局部变换测量，关闭动画的零缩放祖先不参与坐标逆变换。
        Matrix4x4 local = Matrix4x4.TRS(text.localPosition, text.localRotation, text.localScale);
        Rect rectangle = text.rect;
        var points = new JArray();
        foreach (Vector2 corner in new[] { rectangle.min, new Vector2(rectangle.xMin, rectangle.yMax),
            rectangle.max, new Vector2(rectangle.xMax, rectangle.yMin) })
        {
            Vector3 point = local.MultiplyPoint3x4(corner);
            points.Add(new JObject { ["x"] = point.x, ["y"] = point.y });
        }
        return new JObject { ["left"] = button.rect.xMin, ["right"] = button.rect.xMax,
            ["bottom"] = button.rect.yMin, ["top"] = button.rect.yMax, ["textCorners"] = points };
    }

    #endregion
}
#endif
