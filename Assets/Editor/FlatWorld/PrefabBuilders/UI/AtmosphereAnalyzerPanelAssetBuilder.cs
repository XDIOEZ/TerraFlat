using System;
using System.Collections.Generic;
using System.IO;
using FlatWorld.Localization;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.Localization;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;
using UnityEngine.UI;

/// <summary>固化大气检测仪的贴图屏幕、成分行与独立编辑态预览。</summary>
public static class AtmosphereAnalyzerPanelAssetBuilder
{
    #region 资源路径与布局
    public const string PanelPath = "Assets/2_Prefabs/2-1_UI/Gameplay/Screens/UI_AtmosphereAnalyzer.prefab";
    public const string RowPath = "Assets/2_Prefabs/2-1_UI/Gameplay/Screens/UI_AtmosphereAnalyzerGasRow.prefab";
    private const string ArtworkPath = "Assets/6_Art/Generated/Atmosphere/AtmosphereAnalyzer/AtmosphereAnalyzer_Panel.png";
    private const string FontPath = "Assets/Plugins/TextMesh Pro/Fonts/fusion-pixel-12px-monospaced-zh_hans.asset";
    private const string ButtonPath = "Assets/2_Prefabs/2-1_UI/Common/Controls/UI_Button.prefab";
    private static readonly Color ScreenInk = new Color32(210, 234, 177, 255);
    private static readonly Color SecondaryInk = new Color32(164, 193, 143, 255);
    private static readonly Dictionary<string, string> Texts = new()
    {
        ["大气检测仪"] = "Atmosphere Analyzer",
        ["星球大气总量 · 标准 m³"] = "TOTAL ATMOSPHERE · STANDARD m³",
        ["气体成分 / 占比"] = "GAS COMPOSITION / SHARE",
        ["未连接星球"] = "NO PLANET SIGNAL",
        ["星球气压  {0:0.###} kPa"] = "PLANET PRESSURE  {0:0.###} kPa",
        ["星球气压  {0} kPa"] = "PLANET PRESSURE  {0} kPa",
        ["实时测量 · 标准体积 / 摩尔占比"] = "LIVE · STANDARD VOLUME / MOLAR SHARE",
        ["当前星球没有大气"] = "THIS PLANET HAS NO ATMOSPHERE",
        ["当前区域无法测量星球大气"] = "PLANET ATMOSPHERE UNAVAILABLE HERE",
        ["无可检出的气体"] = "NO DETECTABLE GAS",
        ["请在星球上使用检测仪"] = "USE THE ANALYZER ON A PLANET"
    };
    #endregion

    #region 正式资产构建
    [MenuItem("FlatWorld/其他工具/UI/Build Atmosphere Analyzer UI")]
    public static void BuildAtmosphereAnalyzerPanelAssets()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("请退出 PlayMode 后再构建检测仪资源。");
        if (AssetDatabase.LoadAssetAtPath<Sprite>(ArtworkPath) == null)
            throw new FileNotFoundException("大气检测仪贴图尚未导入。", ArtworkPath);
        BuildGasRowPrefab();
        GameObject root = new GameObject(AtmosphereAnalyzerPanel.PrefabKey, typeof(RectTransform), typeof(CanvasGroup));
        root.SetActive(false);
        try
        {
            RectTransform rootRect = root.GetComponent<RectTransform>();
            Stretch(rootRect, Vector2.zero, Vector2.one);
            BasePanel panel = root.AddComponent<BasePanel>();
            panel.PanelName = AtmosphereAnalyzerPanel.PrefabKey;
            panel.rectTransform = rootRect;
            panel.canvasGroup = root.GetComponent<CanvasGroup>();
            panel.canvasGroup.alpha = 1f;
            RuntimeUIPrefabBuilder.ConfigureScaleAnimation(panel);
            RectTransform frame = Rect("InstrumentFrame", root.transform, new Vector2(680f, 860f));
            // 固定设计基准先放大，再由安全区等比限幅以兼顾手机竖屏。
            frame.localScale = Vector3.one * 3f;
            frame.gameObject.AddComponent<UIVisualOwner>();
            SafeAreaScaleGroup scale = root.AddComponent<SafeAreaScaleGroup>();
            scale.Configure(frame, new[] { frame }, new Vector2(20f, 20f), .82f);

            RectTransform backing = Rect("InstrumentScreenBacking", frame, Vector2.zero);
            Stretch(backing, new Vector2(.17f, .30f), new Vector2(.84f, .80f));
            Image backingImage = backing.gameObject.AddComponent<Image>();
            backingImage.color = new Color32(35, 59, 46, 255);
            backingImage.raycastTarget = false;
            RectTransform artwork = Rect("InstrumentArtwork", frame, Vector2.zero);
            Stretch(artwork, Vector2.zero, Vector2.one);
            Image artworkImage = artwork.gameObject.AddComponent<Image>();
            artworkImage.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ArtworkPath);
            artworkImage.preserveAspect = true;
            artworkImage.raycastTarget = true;

            RectTransform screen = Rect("InstrumentDisplayScreen", frame, Vector2.zero);
            Stretch(screen, new Vector2(.19f, .32f), new Vector2(.81f, .78f));
            TextMeshProUGUI title = CreateText("FWUI_AnalyzerTitle", screen, string.Empty, 26f, ScreenInk);
            SetTopRect(title.rectTransform, 0f, 0f, 27f);
            TextMeshProUGUI planet = CreateText("FWUI_PlanetName", screen, string.Empty, 20f, SecondaryInk);
            SetTopRect(planet.rectTransform, 0f, 30f, 25f);
            TextMeshProUGUI totalLabel = CreateText("FWUI_TotalLabel", screen, string.Empty, 17f, SecondaryInk);
            SetTopRect(totalLabel.rectTransform, 0f, 61f, 23f);
            TextMeshProUGUI total = CreateText("FWUI_TotalValue", screen, string.Empty, 32f, ScreenInk);
            SetTopRect(total.rectTransform, 0f, 85f, 39f);
            TextMeshProUGUI pressure = CreateText("FWUI_Pressure", screen, string.Empty, 20f, ScreenInk);
            SetTopRect(pressure.rectTransform, 0f, 127f, 28f);
            TextMeshProUGUI compositionLabel = CreateText("FWUI_CompositionLabel", screen, string.Empty, 17f, SecondaryInk);
            SetTopRect(compositionLabel.rectTransform, 0f, 166f, 24f);
            ScrollRect scroll = BuildCompositionScroll(screen);
            TextMeshProUGUI empty = CreateText("FWUI_EmptyMeasurement", screen, string.Empty, 20f, SecondaryInk);
            Stretch(empty.rectTransform, Vector2.zero, Vector2.one);
            empty.rectTransform.offsetMin = new Vector2(0f, 36f);
            empty.rectTransform.offsetMax = new Vector2(0f, -194f);
            empty.alignment = TextAlignmentOptions.Center;
            TextMeshProUGUI status = CreateText("FWUI_AnalyzerStatus", screen, string.Empty, 15f, SecondaryInk);
            status.rectTransform.anchorMin = Vector2.zero;
            status.rectTransform.anchorMax = new Vector2(1f, 0f);
            status.rectTransform.pivot = new Vector2(.5f, 0f);
            status.rectTransform.sizeDelta = new Vector2(0f, 26f);
            status.rectTransform.anchoredPosition = Vector2.zero;

            GameObject buttonAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ButtonPath);
            if (buttonAsset == null) throw new FileNotFoundException("缺少公共关闭按钮。", ButtonPath);
            GameObject closeRoot = (GameObject)PrefabUtility.InstantiatePrefab(buttonAsset, frame);
            closeRoot.name = "FWUI_AnalyzerClose";
            // 实体按键由机壳贴图提供，公共按钮只保留透明点击面和文字。
            closeRoot.GetComponent<Image>().color = Color.clear;
            RectTransform closeRect = closeRoot.GetComponent<RectTransform>();
            closeRect.anchorMin = closeRect.anchorMax = new Vector2(.67f, .175f);
            closeRect.pivot = new Vector2(.5f, .5f);
            closeRect.anchoredPosition = Vector2.zero;
            closeRect.sizeDelta = new Vector2(122.4f, 55.9f);
            LayoutElement closeLayout = closeRoot.GetComponent<LayoutElement>();
            if (closeLayout != null) closeLayout.ignoreLayout = true;
            TMP_Text closeLabel = closeRoot.GetComponentInChildren<TMP_Text>(true);
            closeLabel.text = "关闭";

            RectTransform dragRect = Rect("InstrumentDragSurface", frame, Vector2.zero);
            Stretch(dragRect, Vector2.zero, Vector2.one);
            dragRect.gameObject.AddComponent<CanvasRenderer>();
            dragRect.gameObject.AddComponent<UIWindowDragSurface>().Configure(frame, 100f, 10f);
            UI_Drag drag = root.AddComponent<UI_Drag>();
            drag.rectTransform = rootRect;
            panel.CanDrag = true;
            panel.Dragger = drag;
            closeRoot.transform.SetAsLastSibling();

            AtmosphereAnalyzerPanel controller = root.AddComponent<AtmosphereAnalyzerPanel>();
            SerializedObject fields = new SerializedObject(controller);
            SetReference(fields, "instrumentFrame", frame);
            SetReference(fields, "displayScreen", screen);
            SetReference(fields, "titleText", title);
            SetReference(fields, "planetText", planet);
            SetReference(fields, "totalLabel", totalLabel);
            SetReference(fields, "totalValue", total);
            SetReference(fields, "pressureText", pressure);
            SetReference(fields, "compositionLabel", compositionLabel);
            SetReference(fields, "statusText", status);
            SetReference(fields, "emptyText", empty);
            SetReference(fields, "compositionScroll", scroll);
            SetReference(fields, "closeButton", closeRoot.GetComponent<Button>());
            SetReference(fields, "gasRowPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(RowPath).GetComponent<AtmosphereAnalyzerGasRow>());
            fields.ApplyModifiedPropertiesWithoutUndo();
            root.SetActive(true);
            PrefabUtility.SaveAsPrefabAsset(root, PanelPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        RegisterPanelAddressable();
        SyncAnalyzerUiTexts();
    }

    private static ScrollRect BuildCompositionScroll(RectTransform parent)
    {
        RectTransform container = Rect("FWUI_CompositionScroll", parent, Vector2.zero);
        Stretch(container, Vector2.zero, Vector2.one);
        container.offsetMin = new Vector2(0f, 34f);
        container.offsetMax = new Vector2(0f, -194f);
        Image hitArea = container.gameObject.AddComponent<Image>();
        hitArea.color = Color.clear;
        hitArea.raycastTarget = true;
        ScrollRect scroll = container.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;
        RectTransform viewport = Rect("Viewport", container, Vector2.zero);
        Stretch(viewport, Vector2.zero, Vector2.one);
        viewport.gameObject.AddComponent<RectMask2D>();
        RectTransform content = Rect("GasComponents", viewport, Vector2.zero);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(.5f, 1f);
        content.sizeDelta = Vector2.zero;
        VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 3f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = viewport;
        scroll.content = content;
        return scroll;
    }

    private static void BuildGasRowPrefab()
    {
        RectTransform row = Rect("UI_AtmosphereAnalyzerGasRow", null, new Vector2(421.6f, 29f));
        row.gameObject.SetActive(false);
        try
        {
            LayoutElement size = row.gameObject.AddComponent<LayoutElement>();
            size.minHeight = size.preferredHeight = 29f;
            size.flexibleHeight = 0f;
            row.gameObject.AddComponent<UIVisualOwner>();
            RectTransform bar = Rect("ProportionFill", row, Vector2.zero);
            Stretch(bar, Vector2.zero, Vector2.one);
            Image fill = bar.gameObject.AddComponent<Image>();
            fill.color = new Color32(113, 156, 101, 75);
            fill.raycastTarget = false;
            fill.type = Image.Type.Simple;
            bar.anchorMax = new Vector2(0f, 1f);
            TextMeshProUGUI name = CreateText("GasName", row, string.Empty, 21f, ScreenInk);
            Stretch(name.rectTransform, Vector2.zero, new Vector2(.69f, 1f));
            name.rectTransform.offsetMin = new Vector2(5f, 0f);
            TextMeshProUGUI percentage = CreateText("Percentage", row, string.Empty, 21f, ScreenInk);
            Stretch(percentage.rectTransform, new Vector2(.70f, 0f), Vector2.one);
            percentage.alignment = TextAlignmentOptions.MidlineRight;
            percentage.rectTransform.offsetMax = new Vector2(-5f, 0f);
            AtmosphereAnalyzerGasRow controller = row.gameObject.AddComponent<AtmosphereAnalyzerGasRow>();
            SerializedObject fields = new SerializedObject(controller);
            SetReference(fields, "gasName", name);
            SetReference(fields, "percentage", percentage);
            SetReference(fields, "proportionFill", fill);
            fields.ApplyModifiedPropertiesWithoutUndo();
            row.gameObject.SetActive(true);
            PrefabUtility.SaveAsPrefabAsset(row.gameObject, RowPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(row.gameObject); }
    }

    private static void RegisterPanelAddressable()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) throw new InvalidOperationException("缺少 Addressables 配置。");
        string guid = AssetDatabase.AssetPathToGUID(PanelPath);
        var entry = settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
        entry.address = AtmosphereAnalyzerPanel.PrefabKey;
        entry.SetLabel("Prefab", true, true);
        EditorUtility.SetDirty(settings.DefaultGroup);
        AssetDatabase.SaveAssetIfDirty(settings.DefaultGroup);
        AssetDatabase.SaveAssetIfDirty(settings);
    }

    private static void SyncAnalyzerUiTexts()
    {
        StringTableCollection collection = LocalizationEditorSettings.GetStringTableCollection(FlatWorldLocalizationService.UiTable);
        if (collection == null) throw new InvalidOperationException("缺少 FlatWorldUI 文本表。");
        foreach (string code in new[] { "zh-CN", "en" })
        {
            StringTable table = (StringTable)collection.GetTable(new LocaleIdentifier(code));
            if (table == null) throw new InvalidOperationException("缺少大气检测仪语言表：" + code);
            foreach (KeyValuePair<string, string> text in Texts)
                table.AddEntry(FlatWorldLocalizationService.GetUiTextKey(text.Key), code == "en" ? text.Value : text.Key);
            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssetIfDirty(table);
        }
        EditorUtility.SetDirty(collection.SharedData);
        AssetDatabase.SaveAssetIfDirty(collection.SharedData);
    }
    #endregion

    #region 编辑态多分辨率预览
    public static string RenderPreview(int width = 1280, int height = 900, int gasCount = 4)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException("预览分辨率必须大于零。");
        // 模拟正式 UIRoot 的 Expand 与当前 UI 用户倍率，不能把物理像素当逻辑尺寸。
        float canvasScale = Mathf.Min(width / 1920f, height / 1080f) * UIUserSettings.Scale;
        Vector2 logicalSize = new Vector2(width / canvasScale, height / canvasScale);
        var scene = EditorSceneManager.NewPreviewScene();
        var target = new RenderTexture(width, height, 24);
        RenderTexture previous = RenderTexture.active;
        Camera camera = null;
        try
        {
            GameObject canvasRoot = new GameObject("AnalyzerPreview", typeof(RectTransform), typeof(Canvas));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvasRoot, scene);
            Canvas canvas = canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvasRoot.transform).sizeDelta = logicalSize;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPath);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.transform.SetParent(canvasRoot.transform, false);
            instance.GetComponent<CanvasGroup>().alpha = 1f;
            AtmosphereAnalyzerPanel view = instance.GetComponent<AtmosphereAnalyzerPanel>();
            FluidContentCatalogDto fluidSource = FluidCatalogLoader.Deserialize(File.ReadAllText(
                Path.Combine(Application.streamingAssetsPath, FluidCatalogLoader.RelativePath)));
            FluidCatalog names = new FluidCatalog();
            var definitions = new List<FluidDefinition>();
            foreach (FluidDefinitionDto definition in fluidSource.Fluids) definitions.Add(new FluidDefinition(definition));
            names.Register(definitions);
            view.RenderMeasurement(CreatePreviewSnapshot(gasCount), names);
            Canvas.ForceUpdateCanvases();
            instance.GetComponent<SafeAreaScaleGroup>().ApplyScale();
            GameObject cameraRoot = new GameObject("AnalyzerPreviewCamera", typeof(Camera));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraRoot, scene);
            camera = cameraRoot.GetComponent<Camera>();
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            camera.transform.position = new Vector3(0f, 0f, -1000f);
            camera.orthographic = true;
            camera.orthographicSize = logicalSize.y * .5f;
            camera.farClipPlane = 2000f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(27, 29, 30, 255);
            camera.targetTexture = target;
            canvas.worldCamera = camera;
            Canvas.ForceUpdateCanvases();
            foreach (TMP_Text text in instance.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();
            camera.Render();
            RenderTexture.active = target;
            Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                image.Apply();
                string path = Path.GetFullPath($"Library/AtmosphereAnalyzerPreview_{width}x{height}_{gasCount}.png");
                File.WriteAllBytes(path, image.EncodeToPNG());
                return path;
            }
            finally { UnityEngine.Object.DestroyImmediate(image); }
        }
        finally
        {
            RenderTexture.active = previous;
            if (camera != null) camera.targetTexture = null;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    public static AtmosphereMeasurementSnapshot CreatePreviewSnapshot(int gasCount)
    {
        AtmosphereState state = new AtmosphereState { CapacityMoles = 1000000000000m,
            ReferenceMoles = 1000000000000m, ReferencePressureKPa = 101.325d, TemperatureKelvin = 293.15d };
        string[] ids = { FluidIds.Nitrogen, FluidIds.Oxygen, FluidIds.CarbonDioxide, FluidIds.Hydrogen };
        for (int i = 0; i < Mathf.Clamp(gasCount, 0, 64); i++)
            state.Gases.Add(new AtmosphereGasAmount { FluidId = i < ids.Length ? ids[i] : "preview:trace_" + i,
                Moles = i switch { 0 => 780000000000m, 1 => 209600000000m, 2 => 10400000000m, _ => .0001m } });
        return AtmosphereMeasurementService.Capture(state, "preview", "蓝星 · Blue Planet");
    }
    #endregion

    #region 构建辅助
    private static RectTransform Rect(string name, Transform parent, Vector2 size)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        if (parent != null) rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
        return rect;
    }
    private static void Stretch(RectTransform rect, Vector2 minimum, Vector2 maximum)
    { rect.anchorMin = minimum; rect.anchorMax = maximum; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    private static void SetTopRect(RectTransform rect, float left, float top, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f); rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(.5f, 1f); rect.sizeDelta = new Vector2(-left, height);
        rect.anchoredPosition = new Vector2(left * .5f, -top);
    }
    private static TextMeshProUGUI CreateText(string name, Transform parent, string value, float size, Color color)
    {
        TextMeshProUGUI text = Rect(name, parent, Vector2.zero).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (text.font == null) throw new FileNotFoundException("缺少检测仪字体。", FontPath);
        FlatWorldUITextLayout.Configure(text, size);
        text.fontSizeMin = Mathf.Min(12f, size);
        text.color = color;
        text.raycastTarget = false;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.enableWordWrapping = false;
        text.text = value;
        return text;
    }
    private static void SetReference(SerializedObject owner, string field, UnityEngine.Object value)
    { owner.FindProperty(field).objectReferenceValue = value; }
    #endregion
}
