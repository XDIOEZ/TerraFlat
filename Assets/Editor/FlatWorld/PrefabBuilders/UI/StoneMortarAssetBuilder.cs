using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.Localization;
using UnityEditor.SceneManagement;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;
using UnityEngine.SceneManagement;
using FlatWorld.Localization;
using UnityEngine;
using UnityEngine.UI;

/// <summary>石臼正式资源装配：导入像素精灵、构建可拖动捣棒面板和通用加工模块，并注册 Addressables。</summary>
public static class StoneMortarAssetBuilder
{
    private const string Art = "Assets/6_Art/Generated/StoneMortar/";
    private const string PanelPath = "Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/UI_StoneMortar.prefab";
    private const string ModulePath = "Assets/2_Prefabs/Gameplay/Modules/Building/Module_Mortar.prefab";
    private const string SlotPath = "Assets/2_Prefabs/2-1_UI/Gameplay/Inventory/Components/UI_Slot.prefab";

    #region 装配入口
    [MenuItem("FlatWorld/内容配置/生成石臼资源")]
    public static void Build()
    {
        Sprite icon = ImportPixelSprite(Art + "StoneMortar_Concept_HighRes.png", new RectInt(0, 0, 1280, 1280), "StoneMortar_Icon", 64, 64, 64);
        Sprite bowl = ImportPixelSprite(Art + "StoneMortar_UI_Concept.png", new RectInt(0, 0, 850, 1280), "StoneMortar_Bowl", 128, 96, 100);
        Sprite pestle = ImportPixelSprite(Art + "StoneMortar_UI_Concept.png", new RectInt(850, 0, 430, 1280), "StoneMortar_Pestle", 40, 96, 100);
        GameObject panel = BuildPanel(bowl, pestle);
        try { PrefabUtility.SaveAsPrefabAsset(panel, PanelPath); }
        finally { UnityEngine.Object.DestroyImmediate(panel); }
        GameObject module = new GameObject("Module_Mortar");
        try
        {
            Mod_Mortar mortar = module.AddComponent<Mod_Mortar>();
            mortar.Data.ID = "Mod_Mortar";
            mortar.Data.Name = "石臼";
            mortar.PanelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPath);
            PrefabUtility.SaveAsPrefabAsset(module, ModulePath);
        }
        finally { UnityEngine.Object.DestroyImmediate(module); }
        Register(PanelPath, "UI_StoneMortar", "Prefab");
        Register(ModulePath, "Module_Mortar", "Prefab");
        Register(AssetDatabase.GetAssetPath(icon), AssetDatabase.GetAssetPath(icon), "ItemSprite");
        SyncText("FlatWorldUI", FlatWorldLocalizationService.GetUiTextKey("石臼"), "石臼", "Stone Mortar");
        SyncText("FlatWorldUI", FlatWorldLocalizationService.GetUiTextKey("将材料拖入碗内，提起石棒再向下捣击"), "将材料拖入碗内，提起石棒再向下捣击", "Drag ingredients into the bowl. Lift the pestle, then pound down.");
        SyncText("FlatWorld", FlatWorldLocalizationService.GetItemLabelKey("StoneMortar"), "石臼", "Stone Mortar");
        SyncText("FlatWorld", FlatWorldLocalizationService.GetItemDescriptionKey("StoneMortar"), "将稻谷放进石碗，每捣击一次消耗一份稻谷并立即产出一份大米。手持面板中可选择放到地上，落地后交互使用或拆回；碗内原料与产物全程保留。", "Each strike turns one rice grain into one rice. Choose Place on Ground in the held mortar's panel, then interact with the placed mortar or pack it up. All ingredients and products are retained.");
        AssetDatabase.SaveAssets();
        Debug.Log("[StoneMortar] 正式精灵、交互面板、加工模块与 Addressables 已生成。");
    }
    #endregion

    /// <summary>只同步石臼新增文本，避免重建其它面板与翻译表条目。</summary>
    private static void SyncText(string collectionName, string key, string chinese, string english)
    {
        var collection = LocalizationEditorSettings.GetStringTableCollection(collectionName);
        foreach (string code in new[] { "zh-CN", "en" })
        {
            var table = (StringTable)collection.GetTable(new LocaleIdentifier(code));
            table.AddEntry(key, code == "en" ? english : chinese);
            EditorUtility.SetDirty(table);
        }
        EditorUtility.SetDirty(collection.SharedData);
    }

    /// <summary>在独立预览场景渲染正式 Prefab，不启动游戏、不改用户场景。</summary>
    [MenuItem("FlatWorld/内容配置/预览石臼面板")]
    public static void PreviewPanel()
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        RenderTexture target = new RenderTexture(680, 710, 24);
        RenderTexture previous = RenderTexture.active;
        Camera previewCamera = null;
        try
        {
            GameObject canvasObject = new GameObject("MortarPreview", typeof(Canvas));
            SceneManager.MoveGameObjectToScene(canvasObject, scene);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.GetComponent<RectTransform>().sizeDelta = new Vector2(680, 710);
            GameObject panel = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PanelPath), scene);
            panel.transform.SetParent(canvas.transform, false);
            panel.transform.localPosition = Vector3.zero;
            MortarInteractionView previewView = panel.GetComponentInChildren<MortarInteractionView>(true);
            ItemSlot_UI previewSlot = panel.GetComponentInChildren<ItemSlot_UI>(true);
            previewSlot.gameObject.SetActive(true);
            previewSlot.image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/6_Art/Generated/Rice/RiceGrain_Icon.png");
            previewSlot.image.gameObject.SetActive(true);
            previewSlot.text.text = "7";
            ((RectTransform)previewSlot.transform).anchoredPosition = new Vector2(-42, previewView.FloorAt(-42) + 26);
            ItemSlot_UI riceSlot = UnityEngine.Object.Instantiate(previewSlot, previewSlot.transform.parent);
            ((RectTransform)riceSlot.transform).anchoredPosition = new Vector2(42, previewView.FloorAt(42) + 26);
            riceSlot.image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/6_Art/Generated/Rice/Rice_Icon.png");
            riceSlot.text.text = "3";
            // 静态预览与运行时相同的图标描边，不修改玩家输入模式。
            Outline selected = riceSlot.selectionGraphic.gameObject.AddComponent<Outline>();
            selected.effectColor = FlatWorldUITheme.SelectionOutline;
            selected.effectDistance = FlatWorldUITheme.SelectionOutlineDistance;
            selected.useGraphicAlpha = false;
            GameObject cameraObject = new GameObject("PreviewCamera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.GetComponent<Camera>();
            previewCamera = camera;
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            camera.transform.position = new Vector3(0, 0, -1000);
            camera.orthographic = true;
            camera.orthographicSize = 355;
            camera.farClipPlane = 2000;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(32, 32, 32, 255);
            camera.targetTexture = target;
            canvas.worldCamera = camera;
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = target;
            var image = new Texture2D(680, 710, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 680, 710), 0, 0);
            image.Apply();
            File.WriteAllBytes("Library/StoneMortarPreview.png", image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
        }
        finally
        {
            RenderTexture.active = previous;
            if (previewCamera != null) previewCamera.targetTexture = null;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    #region 正式面板
    private static GameObject BuildPanel(Sprite bowlSprite, Sprite pestleSprite)
    {
        RectTransform root = Rect("UI_StoneMortar", null, new Vector2(680, 710), Vector2.zero);
        // 构建期间不运行 Awake，所有正式引用接线完成后再激活。
        root.gameObject.SetActive(false);
        Image background = root.gameObject.AddComponent<Image>();
        background.color = new Color32(52, 52, 52, 251);
        BasePanel panel = root.gameObject.AddComponent<BasePanel>();
        panel.PanelName = "石臼";
        panel.rectTransform = root;
        panel.canvasGroup = root.GetComponent<CanvasGroup>();
        Text("标题", root, "石臼", new Vector2(480, 50), new Vector2(-40, 300), 30);
        RectTransform close = Rect("关闭", root, new Vector2(64, 60), new Vector2(290, 300));
        Image closeImage = close.gameObject.AddComponent<Image>();
        closeImage.color = new Color32(85, 85, 85, 255);
        close.gameObject.AddComponent<Button>().targetGraphic = closeImage;
        Text("关闭文字", close, "×", new Vector2(64, 60), Vector2.zero, 30);
        Text("操作提示", root, "将材料拖入碗内，提起石棒再向下捣击", new Vector2(630, 26), new Vector2(0, 262), 22);
        RectTransform area = Rect("捣料区域", root, new Vector2(610, 460), new Vector2(0, -38));
        RectTransform bowl = Rect("石碗切面", area, new Vector2(480, 360), new Vector2(0, -86));
        Image bowlImage = bowl.gameObject.AddComponent<Image>();
        bowlImage.sprite = bowlSprite;
        bowlImage.raycastTarget = false;
        GameObject slotObject = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(SlotPath));
        slotObject.name = "碗内槽位";
        RectTransform slotRect = (RectTransform)slotObject.transform;
        slotRect.SetParent(area, false);
        slotRect.anchorMin = slotRect.anchorMax = slotRect.pivot = new Vector2(.5f, .5f);
        slotRect.anchoredPosition = new Vector2(0, -30);
        slotRect.sizeDelta = new Vector2(72, 64);
        ItemSlot_UI slot = slotObject.GetComponent<ItemSlot_UI>();
        slot.image.rectTransform.sizeDelta = new Vector2(64, 58);
        slot.image.rectTransform.anchorMin = slot.image.rectTransform.anchorMax = new Vector2(.5f, .5f);
        slot.image.rectTransform.anchoredPosition = Vector2.zero;
        slot.text.rectTransform.anchorMin = slot.text.rectTransform.anchorMax = new Vector2(.5f, .5f);
        slot.text.rectTransform.anchoredPosition = new Vector2(30, -22);
        slot.selectionGraphic = slot.image;
        slotObject.GetComponent<Button>().transition = Selectable.Transition.None;
        slot.image.raycastTarget = false;
        slot.image.gameObject.SetActive(false);
        slot.text.text = string.Empty;
        foreach (Image decoration in slotObject.GetComponentsInChildren<Image>(true))
            if (decoration.gameObject != slotObject && decoration != slot.image)
                decoration.enabled = false;
        Image slotBackground = slotObject.GetComponent<Image>();
        slotBackground.sprite = null;
        // 背景仅保留矩形射线命中区域，玩家只看到材料与数量。
        slotBackground.color = Color.clear;
        foreach (Outline outline in slotObject.GetComponentsInChildren<Outline>(true))
            if (outline.GetComponent<TMP_Text>() == null) outline.enabled = false;
        RectTransform stone = Rect("石棒", area, new Vector2(220, 310), new Vector2(0, -35));
        stone.pivot = new Vector2(.5f, 0);
        Image stoneImage = stone.gameObject.AddComponent<Image>();
        stoneImage.sprite = pestleSprite;
        stoneImage.preserveAspect = true;
        stoneImage.raycastTarget = true;
        MortarInteractionView view = stone.gameObject.AddComponent<MortarInteractionView>();
        view.BowlSlot = slot;
        view.DragArea = area;
        view.BowlShape = bowl;
        MortarBowlDropRegion region = slotObject.AddComponent<MortarBowlDropRegion>();
        region.View = view;
        region.Slot = slot;
        RectTransform previous = Rect("上一页", root, new Vector2(64, 60), new Vector2(-110, -305));
        previous.gameObject.AddComponent<Image>().color = new Color32(85, 85, 85, 255);
        view.PreviousPage = previous.gameObject.AddComponent<Button>();
        Text("上一页文字", previous, "‹", new Vector2(64, 60), Vector2.zero, 30);
        RectTransform next = Rect("下一页", root, new Vector2(64, 60), new Vector2(110, -305));
        next.gameObject.AddComponent<Image>().color = new Color32(85, 85, 85, 255);
        view.NextPage = next.gameObject.AddComponent<Button>();
        Text("下一页文字", next, "›", new Vector2(64, 60), Vector2.zero, 30);
        view.PageText = Text("库存页码", root, "", new Vector2(140, 35), new Vector2(0, -305), 20);
        previous.gameObject.SetActive(false);
        next.gameObject.SetActive(false);
        slotObject.SetActive(false);
        PortableBuildingPanelBuilder.ConfigureMortar(root.gameObject);
        root.gameObject.SetActive(true);
        return root.gameObject;
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        if (parent != null) rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        rect.localScale = Vector3.one;
        return rect;
    }

    private static TMP_Text Text(string name, Transform parent, string value, Vector2 size, Vector2 position, int fontSize)
    {
        TextMeshProUGUI text = Rect(name, parent, size, position).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Plugins/TextMesh Pro/Fonts/fusion-pixel-12px-monospaced-zh_hans.asset");
        text.text = value;
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.color = new Color32(238, 238, 238, 255);
        text.raycastTarget = false;
        return text;
    }
    #endregion

    #region 像素资源导入
    /// <summary>生成源按已确认的分区裁切、最近邻采样及十二色石材量化，输出硬透明运行时 PNG。</summary>
    private static Sprite ImportPixelSprite(string source, RectInt region, string name, int width, int height, float ppu)
    {
        var input = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        input.LoadImage(File.ReadAllBytes(source));
        Color32[] pixels = input.GetPixels32();
        int minX = region.xMax, minY = region.yMax, maxX = region.xMin, maxY = region.yMin;
        for (int y = region.yMin; y < Math.Min(region.yMax, input.height); y++)
        for (int x = region.xMin; x < Math.Min(region.xMax, input.width); x++)
        {
            if (pixels[y * input.width + x].a < 128) continue;
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }
        if (minX > maxX || minY > maxY) throw new InvalidOperationException("石臼源图没有可见像素。");
        var output = new Texture2D(width, height, TextureFormat.RGBA32, false);
        Color32[] result = new Color32[width * height];
        float scale = Math.Min((width - 4f) / (maxX - minX + 1), (height - 4f) / (maxY - minY + 1));
        int targetWidth = Mathf.RoundToInt((maxX - minX + 1) * scale);
        int targetHeight = Mathf.RoundToInt((maxY - minY + 1) * scale);
        for (int y = 0; y < targetHeight; y++)
        for (int x = 0; x < targetWidth; x++)
        {
            Color32 c = pixels[(minY + Math.Min(maxY - minY, (int)(y / scale))) * input.width + minX + Math.Min(maxX - minX, (int)(x / scale))];
            if (c.a < 128) continue;
            int shade = Mathf.Clamp(Mathf.RoundToInt(((c.r + c.g + c.b) / 3f - 28) / 18), 0, 11);
            byte v = (byte)(28 + shade * 18);
            result[(y + (height - targetHeight) / 2) * width + x + (width - targetWidth) / 2] = new Color32(v, (byte)Math.Max(0, v - 2), (byte)Math.Max(0, v - 7), 255);
        }
        output.SetPixels32(result);
        output.Apply();
        string path = Art + name + ".png";
        File.WriteAllBytes(path, output.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(input);
        UnityEngine.Object.DestroyImmediate(output);
        AssetDatabase.ImportAsset(path);
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = ppu;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.alphaIsTransparency = true;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static void Register(string path, string address, string label)
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(path), settings.DefaultGroup);
        entry.address = address;
        entry.SetLabel(label, true, true);
        EditorUtility.SetDirty(settings);
    }
    #endregion
}
