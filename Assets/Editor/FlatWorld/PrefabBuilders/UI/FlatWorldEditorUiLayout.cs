using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// 两套 Prefab 构建器共用矩形定位、描边和命名节点查询。
internal static class FlatWorldEditorUiLayout
{
    #region 编辑器控件布局

    internal enum SharedButtonKind
    {
        Normal,
        Close,
        Tab,
        Primary,
        Destructive,
        Transparent
    }

    /// <summary>普通按钮只实例化公共 Prefab；业务侧仅拥有身份、文案和外部布局。</summary>
    internal static Button CreateSharedButton(
        Transform parent, string name, string caption, Vector2 position, Vector2 size,
        Vector2 pivot, SharedButtonKind kind = SharedButtonKind.Normal, string labelObjectName = null)
    {
        string key = kind switch
        {
            SharedButtonKind.Close => "UI_CloseButton",
            SharedButtonKind.Tab => "UI_TabButton",
            SharedButtonKind.Primary => "UI_PrimaryButton",
            SharedButtonKind.Destructive => "UI_DestructiveButton",
            SharedButtonKind.Transparent => "UI_TransparentButton",
            _ => "UI_Button"
        };
        string path = RuntimeUIPrefabBuilder.SharedControlsRoot + key + ".prefab";
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null)
        {
            RuntimeUIPrefabBuilder.EnsureSharedControlPrefabs();
            asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
        if (asset == null)
            throw new InvalidOperationException("缺少公共按钮资产：" + path);

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
        instance.name = name;
        RectTransform rect = instance.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = pivot;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
        LayoutElement layout = instance.GetComponent<LayoutElement>();
        if (layout != null)
        {
            layout.preferredWidth = size.x;
            layout.preferredHeight = size.y;
        }
        TextMeshProUGUI label = instance.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label == null)
            throw new InvalidOperationException(key + " 缺少公共文字节点");
        label.text = caption;
        if (!string.IsNullOrEmpty(labelObjectName))
            label.gameObject.name = labelObjectName;
        return instance.GetComponent<Button>();
    }

    /// <summary>在正式 Prefab 内原位替换遗留按钮，保持矩形、同级顺序与事件绑定。</summary>
    internal static Button ReplaceLegacyButton(
        Button existing, string caption, SharedButtonKind kind, string labelObjectName = null)
    {
        if (existing == null)
            throw new ArgumentNullException(nameof(existing));
        RectTransform oldRect = (RectTransform)existing.transform;
        Button replacement = CreateSharedButton(
            oldRect.parent, existing.name, caption, oldRect.anchoredPosition,
            oldRect.sizeDelta, oldRect.pivot, kind, labelObjectName);
        RectTransform rect = (RectTransform)replacement.transform;
        rect.anchorMin = oldRect.anchorMin;
        rect.anchorMax = oldRect.anchorMax;
        rect.localRotation = oldRect.localRotation;
        rect.localScale = oldRect.localScale;
        rect.SetSiblingIndex(oldRect.GetSiblingIndex());
        replacement.onClick = existing.onClick;
        replacement.interactable = existing.interactable;
        replacement.gameObject.SetActive(existing.gameObject.activeSelf);
        UnityEngine.Object.DestroyImmediate(existing.gameObject);
        return replacement;
    }

    internal static void AddOutline(Graphic graphic, Color color)
    {
        Outline outline = graphic.GetComponent<Outline>() ?? graphic.gameObject.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = FlatWorldUITheme.BorderOutlineDistance;
        outline.useGraphicAlpha = true;
    }

    internal static Transform FindTransform(Transform root, string name)
    {
        if (root.name == name)
            return root;
        for (int index = 0; index < root.childCount; index++)
        {
            Transform found = FindTransform(root.GetChild(index), name);
            if (found != null)
                return found;
        }
        return null;
    }

    internal static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    internal static void SetCentered(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    internal static void SetTopLeft(RectTransform rect, float left, float top, float width, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(left, -top);
        rect.sizeDelta = new Vector2(width, height);
    }

    /// <summary>使用四侧边距让矩形随父级完整伸缩。</summary>
    internal static void SetStretchWithMargins(
        RectTransform rect,
        float left,
        float bottom,
        float right,
        float top)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    /// <summary>固定高度并横向拉伸，供顶部页签栏使用。</summary>
    internal static void SetTopStretch(
        RectTransform rect,
        float left,
        float right,
        float top,
        float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(left, -top - height);
        rect.offsetMax = new Vector2(-right, -top);
    }

    internal static void SetTopStretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    // 构建入口共用字体加载和原有错误提示，不重复维护判空分支。
    internal static bool TryLoadRequiredPrefabBuilderFont(string fontPath, string diagnosticSource, out TMP_FontAsset font)
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
        if (font != null)
            return true;
        Debug.LogError($"[{diagnosticSource}] 缺少统一字体：{fontPath}");
        return false;
    }

    #endregion
}
