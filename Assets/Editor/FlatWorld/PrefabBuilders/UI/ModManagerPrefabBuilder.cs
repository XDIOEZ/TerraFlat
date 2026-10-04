using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>定向维护 MOD 管理页的标题栏、操作区和拖拽视图。</summary>
public static class ModManagerPrefabBuilder
{
    #region MOD 管理页布局

    private const string PrefabPath = "Assets/2_Prefabs/2-1_UI/MainMenu/Core/UI_ModManager.prefab";

    [MenuItem("FlatWorld/UI/同步 MOD 管理页布局")]
    public static void SyncLayout()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            root.SetActive(false);
            var card = (RectTransform)root.transform.Find("MOD管理卡片");
            if (card == null) throw new InvalidOperationException("MOD 管理页缺少内容卡片。");
            Remove(card, "上移");
            Remove(card, "下移");
            Remove(card, "代码授权");
            Transform codeHint = card.Find("代码风险");
            if (codeHint != null) codeHint.name = "代码说明";

            RectTransform header = EnsureRect(card, "标题栏");
            Place(header, Vector2.zero, new Vector2(1480f, 92f));
            header.SetAsFirstSibling();
            Image headerImage = header.GetComponent<Image>() ?? header.gameObject.AddComponent<Image>();
            headerImage.color = new Color32(46, 46, 46, 255);
            headerImage.raycastTarget = false;

            Transform title = card.Find("页面标题") ?? header.Find("页面标题");
            title.SetParent(header, false);
            Place((RectTransform)title, new Vector2(80f, -24f), new Vector2(1100f, 50f));

            Transform close = card.Find("关闭") ?? header.Find("关闭");
            close.SetParent(header, false);
            Place((RectTransform)close, new Vector2(1336f, -16f), new Vector2(120f, 60f));
            Remove(close, "关闭_眉题");
            Remove(close, "关闭_箭头");
            TMP_Text closeText = close.Find("关闭_标题").GetComponent<TMP_Text>();
            closeText.text = "关闭";
            closeText.alignment = TextAlignmentOptions.Center;
            Fill((RectTransform)closeText.transform, 8f);
            Place((RectTransform)card.Find("加载状态"), new Vector2(80f, -104f), new Vector2(600f, 28f));
            Place((RectTransform)card.Find("目录路径"), new Vector2(730f, -104f), new Vector2(650f, 28f));

            RectTransform previewArea = EnsureRect(card, "MOD展示图区域");
            Place(previewArea, new Vector2(730f, -150f), new Vector2(650f, 280f));
            Image previewAreaImage = previewArea.GetComponent<Image>() ?? previewArea.gameObject.AddComponent<Image>();
            previewAreaImage.color = new Color32(43, 43, 43, 255);
            previewAreaImage.raycastTarget = false;

            RectTransform modPreview = card.Find("MOD展示图") as RectTransform ?? EnsureRect(previewArea, "MOD展示图");
            if (modPreview.parent != previewArea) modPreview.SetParent(previewArea, false);
            Fill(modPreview, 8f);
            Image modPreviewImage = modPreview.GetComponent<Image>() ?? modPreview.gameObject.AddComponent<Image>();
            modPreviewImage.color = Color.clear;
            modPreviewImage.raycastTarget = false;
            modPreviewImage.preserveAspect = true;

            Transform placeholder = previewArea.Find("MOD展示图占位");
            if (placeholder == null)
            {
                placeholder = UnityEngine.Object.Instantiate(card.Find("代码说明").gameObject, previewArea).transform;
                placeholder.name = "MOD展示图占位";
                var binder = placeholder.GetComponent<FlatWorld.Localization.LocalizedTextBinder>();
                if (binder != null) UnityEngine.Object.DestroyImmediate(binder);
            }
            Fill((RectTransform)placeholder, 12f);
            TMP_Text placeholderText = placeholder.GetComponent<TMP_Text>();
            placeholderText.text = "暂无展示图";
            placeholderText.fontSize = 20f;
            placeholderText.alignment = TextAlignmentOptions.Center;
            placeholderText.raycastTarget = false;

            RectTransform details = (RectTransform)card.Find("详情内容");
            Place(details, new Vector2(730f, -446f), new Vector2(650f, 152f));
            TMP_Text detailsText = details.GetComponent<TMP_Text>();
            detailsText.fontSize = 19f;
            detailsText.raycastTarget = false;
            Place((RectTransform)card.Find("代码说明"), new Vector2(730f, -606f), new Vector2(650f, 26f));

            foreach (string key in new[] { "上一页", "下一页", "启用切换", "刷新", "打开目录", "应用重载" })
            {
                Transform button = card.Find(key);
                Remove(button, key + "_眉题");
                RectTransform label = (RectTransform)button.Find(key + "_标题");
                Place(label, new Vector2(16f, -10f), new Vector2(((RectTransform)button).rect.width - 60f, 50f));
            }
            Place((RectTransform)card.Find("启用切换"), new Vector2(730f, -644f), new Vector2(300f, 60f));
            Place((RectTransform)card.Find("刷新"), new Vector2(1070f, -644f), new Vector2(300f, 60f));
            Place((RectTransform)card.Find("打开目录"), new Vector2(730f, -714f), new Vector2(300f, 60f));
            Place((RectTransform)card.Find("应用重载"), new Vector2(1070f, -714f), new Vector2(300f, 60f));

            RectTransform bounds = EnsureRect(card, "MOD列表范围");
            Place(bounds, new Vector2(80f, -150f), new Vector2(600f, 520f));
            for (int slot = 0; slot < 6; slot++)
            {
                Place((RectTransform)card.Find($"MOD条目_{slot + 1}"), new Vector2(80f, -150f - slot * 88f), new Vector2(600f, 80f));
                card.Find($"MOD条目_{slot + 1}/MOD条目_{slot + 1}_说明").GetComponent<TMP_Text>().text = string.Empty;
            }
            Transform sortHint = card.Find("排序提示");
            if (sortHint == null)
            {
                sortHint = UnityEngine.Object.Instantiate(card.Find("代码说明").gameObject, card).transform;
                sortHint.name = "排序提示";
            }
            Place((RectTransform)sortHint, new Vector2(80f, -676f), new Vector2(600f, 20f));
            TMP_Text sortText = sortHint.GetComponent<TMP_Text>();
            sortText.text = "拖拽条目排序；拖到翻页按钮上停留可跨页。";
            sortText.fontSize = 17f;
            RectTransform preview = card.Find("拖拽预览") as RectTransform;
            if (preview == null)
            {
                preview = (RectTransform)UnityEngine.Object.Instantiate(card.Find("MOD条目_1").gameObject, card).transform;
                preview.name = "拖拽预览";
                UnityEngine.Object.DestroyImmediate(preview.GetComponent<Button>());
                Remove(preview, "MOD条目_1_箭头");
                preview.Find("MOD条目_1_序号").name = "拖拽序号";
                preview.Find("MOD条目_1_标题").name = "拖拽标题";
                preview.Find("MOD条目_1_说明").name = "拖拽说明";
            }
            foreach (Graphic graphic in preview.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            preview.GetComponent<Image>().color = new Color32(92, 85, 64, 240);
            foreach (TMP_Text text in preview.GetComponentsInChildren<TMP_Text>(true))
            {
                text.text = string.Empty;
                var binder = text.GetComponent<FlatWorld.Localization.LocalizedTextBinder>();
                if (binder != null) UnityEngine.Object.DestroyImmediate(binder);
            }
            preview.gameObject.SetActive(false);

            RectTransform line = EnsureRect(card, "排序落点线");
            line.anchorMin = line.anchorMax = line.pivot = new Vector2(0.5f, 0.5f);
            line.sizeDelta = new Vector2(600f, 3f);
            Image lineImage = line.GetComponent<Image>() ?? line.gameObject.AddComponent<Image>();
            lineImage.color = FlatWorldUITheme.Accent;
            lineImage.raycastTarget = false;
            line.gameObject.SetActive(false);

            ModMenuDragController controller = card.GetComponent<ModMenuDragController>() ?? card.gameObject.AddComponent<ModMenuDragController>();
            var serialized = new SerializedObject(controller);
            SerializedProperty rows = serialized.FindProperty("rows");
            rows.arraySize = 6;
            for (int slot = 0; slot < 6; slot++)
                rows.GetArrayElementAtIndex(slot).objectReferenceValue = card.Find($"MOD条目_{slot + 1}").GetComponent<Button>();
            SetReference(serialized, "listBounds", bounds);
            SetReference(serialized, "dragPreview", preview);
            SetReference(serialized, "previewNumber", preview.Find("拖拽序号").GetComponent<TMP_Text>());
            SetReference(serialized, "previewTitle", preview.Find("拖拽标题").GetComponent<TMP_Text>());
            SetReference(serialized, "previewDescription", preview.Find("拖拽说明").GetComponent<TMP_Text>());
            SetReference(serialized, "insertionLine", line);
            SetReference(serialized, "previousPage", card.Find("上一页").GetComponent<Button>());
            SetReference(serialized, "nextPage", card.Find("下一页").GetComponent<Button>());
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // 只维护当前面板，保存前统一边框和按钮状态。
            FlatWorldUITheme.ApplyBorderThickness(root.transform);
            FlatWorldUITheme.ApplySelectionColors(root.transform);
            root.SetActive(true);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void SetReference(SerializedObject target, string key, UnityEngine.Object value)
        => target.FindProperty(key).objectReferenceValue = value;

    private static RectTransform EnsureRect(Transform parent, string name)
    {
        if (parent.Find(name) is RectTransform existing) return existing;
        var created = new GameObject(name, typeof(RectTransform));
        created.layer = parent.gameObject.layer;
        created.transform.SetParent(parent, false);
        return (RectTransform)created.transform;
    }

    private static void Remove(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null) UnityEngine.Object.DestroyImmediate(child.gameObject);
    }

    private static void Place(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
    }

    private static void Fill(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.one * inset;
        rect.offsetMax = -Vector2.one * inset;
    }

    #endregion
}
