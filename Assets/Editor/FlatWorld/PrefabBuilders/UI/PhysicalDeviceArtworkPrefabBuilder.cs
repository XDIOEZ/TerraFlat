using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>原位装配器物区，复用业务节点并保留复杂控件的独立布局。</summary>
public static class PhysicalDeviceArtworkPrefabBuilder
{
    #region 正式面板与重建入口
    public const string CatalogPath = "Assets/Resources/UI/PhysicalDeviceArtworkCatalog.asset";

    [MenuItem("FlatWorld/其他工具/UI/同步全部器物美术界面")]
    public static void SynchronizeAllPhysicalDeviceArtwork()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("请退出 PlayMode 后同步正式器物界面。");
        var catalog = AssetDatabase.LoadAssetAtPath<PhysicalDeviceArtworkCatalog>(CatalogPath);
        foreach (var layout in catalog.Panels)
        {
            string path = AssetDatabase.FindAssets(layout.PanelId + " t:Prefab", new[] { "Assets/2_Prefabs/2-1_UI/Gameplay" })
                .Select(AssetDatabase.GUIDToAssetPath).Single(value => System.IO.Path.GetFileNameWithoutExtension(value) == layout.PanelId);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                RemoveBeforeRebuildingControls(root);
                Configure(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }

    public static void Configure(GameObject root)
    {
        var catalog = AssetDatabase.LoadAssetAtPath<PhysicalDeviceArtworkCatalog>(CatalogPath);
        if (catalog == null) return;
        var layout = Array.Find(catalog.Panels, value => value.PanelId == root.name);
        if (layout == null || root.GetComponentInChildren<PhysicalDeviceArtworkView>(true) != null) return;
        foreach (var appearance in catalog.Appearances)
            if (appearance.Layers.Length > PhysicalDeviceArtworkCatalog.LayerCapacity)
                throw new InvalidOperationException("器物外观超过预制图层容量：" + appearance.ItemIds[0]);

        bool active = root.activeSelf;
        root.SetActive(false);
        try
        {
            Transform[] nodes = root.GetComponentsInChildren<Transform>(true);
            RectTransform host = nodes.FirstOrDefault(value => value.name == "CraftingContent") as RectTransform
                ?? (RectTransform)root.transform;
            float width = host.rect.width, height = host.rect.height;
            Vector2 artworkPosition, artworkSize;
            if (layout.Dedicated)
            {
                host.sizeDelta = new Vector2(800, 680);
                foreach (Image image in root.GetComponentsInChildren<Image>(true))
                    if (image.GetComponentInParent<Button>(true) == null && image.GetComponentInParent<ItemSlot_UI>(true) == null)
                    { Color color = image.color; color.a = 0; image.color = color; image.raycastTarget = false; }
                foreach (Transform node in nodes)
                    if (node.name.StartsWith("FWUI_Section", StringComparison.Ordinal) || node.name.StartsWith("FWUI_FlowArrow", StringComparison.Ordinal))
                        node.gameObject.SetActive(false);
                Place(nodes, host, "输入_1", -140, -160, 96, 96);
                Place(nodes, host, "输出_1", 140, -160, 96, 96);
                Place(nodes, host, "合成按钮", 210, -280, 180, 60);
                Place(nodes, host, "关闭", 340, 292, 60, 60);
                Place(nodes, host, "放置建筑按钮", -210, -280, 180, 60);
                Place(nodes, host, "拆回建筑按钮", -210, -280, 180, 60);
                Place(nodes, host, "FWUI_FooterHint", 0, -220, 720, 44);
                Place(nodes, host, "FWUI_标题", -210, 292, 300, 42);
                artworkPosition = new Vector2(0, 55); artworkSize = new Vector2(600, 470);
            }
            else
            {
                // 冻结原控件区的几何，新增器物区不会拉伸已有配方、库存或状态列表。
                foreach (RectTransform child in host)
                {
                    Vector2 size = child.rect.size;
                    Vector2 position = child.anchoredPosition + new Vector2(
                        (Mathf.Lerp(child.anchorMin.x, child.anchorMax.x, child.pivot.x) - .5f) * width,
                        (Mathf.Lerp(child.anchorMin.y, child.anchorMax.y, child.pivot.y) - .5f) * height);
                    child.anchorMin = child.anchorMax = new Vector2(.5f, .5f);
                    child.sizeDelta = size;
                    child.anchoredPosition = position + new Vector2(layout.ArtworkWidth * .5f, 0);
                }
                host.sizeDelta = new Vector2(width + layout.ArtworkWidth, height);
                artworkPosition = new Vector2(-width * .5f, 0);
                artworkSize = new Vector2(layout.ArtworkWidth - 16f, Mathf.Min(470, Mathf.Max(300, height - 70)));
            }
            GameObject art = new GameObject("FWUI_DeviceArtwork", typeof(RectTransform), typeof(UIVisualOwner));
            art.layer = root.layer;
            var artRect = (RectTransform)art.transform;
            artRect.SetParent(host, false);
            artRect.sizeDelta = new Vector2(440, 470);
            artRect.anchoredPosition = artworkPosition;
            artRect.localScale = Vector3.one * Mathf.Min(artworkSize.x / 440, artworkSize.y / 470);
            var images = new Image[PhysicalDeviceArtworkCatalog.LayerCapacity];
            for (int i = 0; i < images.Length; i++)
            {
                GameObject part = new GameObject("ArtworkLayer_" + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                part.layer = root.layer;
                part.transform.SetParent(artRect, false);
                images[i] = part.GetComponent<Image>();
                images[i].raycastTarget = false; images[i].preserveAspect = true; images[i].useSpriteMesh = true;
                part.SetActive(false);
            }
            var view = art.AddComponent<PhysicalDeviceArtworkView>();
            var fields = new SerializedObject(view);
            fields.FindProperty("catalog").objectReferenceValue = catalog;
            fields.FindProperty("defaultAppearance").stringValue = layout.DefaultAppearance;
            fields.FindProperty("controlDesignSize").vector2Value = new Vector2(width, height);
            fields.FindProperty("artworkWidth").floatValue = layout.ArtworkWidth;
            var layers = fields.FindProperty("layers"); layers.arraySize = images.Length;
            for (int i = 0; i < images.Length; i++) layers.GetArrayElementAtIndex(i).objectReferenceValue = images[i];
            fields.FindProperty("actionButton").objectReferenceValue = root.GetComponentsInChildren<Button>(true)
                .FirstOrDefault(value => value.name is "合成按钮" or "FWUI_Action" or "FWUI_Toggle" or "FWUI_SendCargo");
            fields.ApplyModifiedPropertiesWithoutUndo();
            var mechanical = root.GetComponent<MechanicalPanelView>();
            if (mechanical != null) mechanical.PreservePrefabLayout = true;
            var safeArea = root.GetComponent<SafeAreaScaleGroup>() ?? root.AddComponent<SafeAreaScaleGroup>();
            safeArea.Configure(host, new[] { host }, new Vector2(24, 24));
            artRect.SetAsLastSibling();
            if (layout.Dedicated)
                foreach (Transform node in host.Cast<Transform>().ToArray())
                    if (node.GetComponent<Button>() != null || node.GetComponent<ItemSlot_UI>() != null) node.SetAsLastSibling();
        }
        finally { root.SetActive(active); }
    }

    public static void RemoveBeforeRebuildingControls(GameObject root)
    {
        var artwork = root.GetComponentInChildren<PhysicalDeviceArtworkView>(true);
        if (artwork == null) return;
        var fields = new SerializedObject(artwork);
        Vector2 original = fields.FindProperty("controlDesignSize").vector2Value;
        float width = fields.FindProperty("artworkWidth").floatValue;
        var host = (RectTransform)artwork.transform.parent;
        if (width > 0f)
            foreach (RectTransform child in host)
                if (child != artwork.transform) child.anchoredPosition -= new Vector2(width * .5f, 0);
        host.sizeDelta = original;
        UnityEngine.Object.DestroyImmediate(artwork.gameObject);
        if (root.TryGetComponent(out MechanicalPanelView mechanical)) mechanical.PreservePrefabLayout = false;
    }

    private static void Place(Transform[] nodes, RectTransform host, string name, float x, float y, float width, float height)
    {
        var rect = nodes.FirstOrDefault(value => value.name == name) as RectTransform;
        if (rect == null) return;
        rect.SetParent(host, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(width, height);
    }
    #endregion
}
