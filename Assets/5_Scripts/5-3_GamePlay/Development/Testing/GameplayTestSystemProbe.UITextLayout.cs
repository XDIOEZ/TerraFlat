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
    private static void RegisterUITextLayoutRules() =>
        Register("ui_settings", "text_fitting_assets", CaptureUITextLayoutAssets);

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
