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
                    var corners = new Vector3[4];
                    text.rectTransform.GetWorldCorners(corners);
                    var points = new JArray();
                    foreach (Vector3 corner in corners)
                    {
                        Vector3 point = buttonRect.InverseTransformPoint(corner);
                        points.Add(new JObject { ["x"] = point.x, ["y"] = point.y });
                    }
                    row["button"] = new JObject
                    {
                        ["left"] = buttonRect.rect.xMin, ["right"] = buttonRect.rect.xMax,
                        ["bottom"] = buttonRect.rect.yMin, ["top"] = buttonRect.rect.yMax,
                        ["textCorners"] = points
                    };
                }
                rows.Add(row);
            }
        }
        return new JObject { ["prefabs"] = guids.Length, ["texts"] = rows };
    }

    #endregion
}
#endif
