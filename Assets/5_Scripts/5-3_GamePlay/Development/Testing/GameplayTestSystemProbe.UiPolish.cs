#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

public static partial class GameplayTestSystemProbe
{
    #region 界面整理规则采样
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterUiPolishRuntime() => RegisterUiPolishRules();
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterUiPolishEditor() => RegisterUiPolishRules();
#endif
    private static void RegisterUiPolishRules()
    {
        Register("ui_settings", "log_filters", CaptureLogFilters);
        Register("ui_settings", "world_settings_pages", CaptureWorldSettingsPages);
        Register("ui_settings", "bag_hotbar_layout", CaptureBagHotbarLayout);
        Register("ui_settings", "hover_and_mod_selection", CaptureHoverAndModSelection);
        Register("ui_settings", "pressure_hud_display_cache", CapturePressureHudDisplayCache);
#if UNITY_EDITOR
        Register("ui_settings", "main_menu_scale_layout", CaptureMainMenuScaleLayout);
#endif
    }

#if UNITY_EDITOR
    /// <summary>从正式主菜单 Prefab 取得尺寸，并在独立布局中检查不同用户倍率的响应。</summary>
    private static JObject CaptureMainMenuScaleLayout(JObject args)
    {
        const string prefabPath = "Assets/2_Prefabs/2-1_UI/MainMenu/Core/UI_MainMenu.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        RectTransform brandSource = prefab != null
            ? prefab.transform.Find("品牌区") as RectTransform : null;
        RectTransform cardSource = prefab != null
            ? prefab.transform.Find("旅程菜单卡") as RectTransform : null;
        RectTransform settingsSource = prefab != null
            ? prefab.transform.Find(GameManager.MainMenuSettingsButtonKey) as RectTransform : null;
        if (prefab == null || prefab.GetComponent<MainMenuResponsiveLayout>() == null ||
            brandSource == null || cardSource == null || settingsSource == null)
            throw new System.InvalidOperationException("正式主菜单的响应式布局或节点引用缺失。");

        string[] buttonNames =
        {
            GameManager.MainMenuContinueButtonKey, GameManager.MainMenuNewGameButtonKey,
            GameManager.MainMenuMultiplayerButtonKey, GameManager.MainMenuModsButtonKey
        };
        float buttonHeight = -1f;
        foreach (string name in buttonNames)
        {
            RectTransform button = cardSource.Find(name) as RectTransform;
            if (button == null || button.GetComponent<Button>() == null || button.rect.height <= 0f)
                throw new System.InvalidOperationException("主菜单缺少旅程入口：" + name);
            if (buttonHeight > 0f && !Mathf.Approximately(buttonHeight, button.rect.height))
                throw new System.InvalidOperationException("主菜单四个旅程入口高度不一致。");
            buttonHeight = button.rect.height;
        }

        float width = (float)Number(args, "width", 1920);
        float height = (float)Number(args, "height", 1080);
        float userScale = (float)Number(args, "scale", UIUserSettings.DefaultScale);
        if (width <= 0f || height <= 0f ||
            userScale < UIUserSettings.MinimumScale || userScale > UIUserSettings.MaximumScale)
            throw new System.ArgumentOutOfRangeException(nameof(args), "主菜单测试的分辨率或倍率非法。");

        GameObject fixture = new GameObject("[LuaMainMenuScaleLayout]", typeof(RectTransform));
        fixture.SetActive(false);
        try
        {
            RectTransform root = fixture.GetComponent<RectTransform>();
            float canvasScale = Mathf.Min(width / 1920f, height / 1080f) * userScale;
            root.sizeDelta = new Vector2(width / canvasScale, height / canvasScale);
            RectTransform brand = CreateMainMenuReferenceRect(root, brandSource);
            RectTransform card = CreateMainMenuReferenceRect(root, cardSource);
            RectTransform settings = CreateMainMenuReferenceRect(root, settingsSource);
            MainMenuResponsiveLayout layout = fixture.AddComponent<MainMenuResponsiveLayout>();
            layout.Configure(brand, card, settings);
            fixture.SetActive(true);
            layout.RefreshLayout();
            root.ForceUpdateRectTransforms();

            Vector3[] brandCorners = new Vector3[4];
            Vector3[] cardCorners = new Vector3[4];
            Vector3[] settingsCorners = new Vector3[4];
            brand.GetWorldCorners(brandCorners);
            card.GetWorldCorners(cardCorners);
            settings.GetWorldCorners(settingsCorners);
            float brandBottom = root.InverseTransformPoint(brandCorners[0]).y;
            float cardTop = root.InverseTransformPoint(cardCorners[2]).y;
            float brandRight = root.InverseTransformPoint(brandCorners[2]).x;
            float settingsLeft = root.InverseTransformPoint(settingsCorners[0]).x;

            return new JObject
            {
                ["buttons"] = buttonNames.Length,
                ["buttonPixels"] = buttonHeight * card.localScale.y * canvasScale,
                ["brandPixels"] = brand.rect.height * brand.localScale.y * canvasScale,
                ["settingsPixels"] = settings.rect.height * settings.localScale.y * canvasScale,
                ["leftMarginPixels"] = brand.anchoredPosition.x * canvasScale,
                ["topMarginPixels"] = -brand.anchoredPosition.y * canvasScale,
                ["layoutFactor"] = brand.localScale.y,
                ["menuToBrandRatio"] = card.localScale.y / brand.localScale.y,
                ["insideSafeArea"] = MainMenuRectInside(root, brand) &&
                    MainMenuRectInside(root, card) && MainMenuRectInside(root, settings),
                ["brandCardSeparated"] = brandBottom >= cardTop - 0.1f,
                ["brandSettingsSeparated"] = settingsLeft >= brandRight - 0.1f
            };
        }
        finally { Object.DestroyImmediate(fixture); }
    }

    private static RectTransform CreateMainMenuReferenceRect(RectTransform root, RectTransform source)
    {
        RectTransform rect = new GameObject(source.name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(root, false);
        rect.anchorMin = source.anchorMin;
        rect.anchorMax = source.anchorMax;
        rect.pivot = source.pivot;
        rect.anchoredPosition = source.anchoredPosition;
        rect.sizeDelta = source.sizeDelta;
        return rect;
    }

    private static bool MainMenuRectInside(RectTransform root, RectTransform child)
    {
        Vector3[] corners = new Vector3[4];
        child.GetWorldCorners(corners);
        foreach (Vector3 point in corners)
        {
            Vector3 local = root.InverseTransformPoint(point);
            if (local.x < root.rect.xMin - 0.1f || local.x > root.rect.xMax + 0.1f ||
                local.y < root.rect.yMin - 0.1f || local.y > root.rect.yMax + 0.1f)
                return false;
        }
        return true;
    }
#endif

    private static JObject CapturePressureHudDisplayCache(JObject args)
    {
        var baseline = new PlayerWorldCoordinateHUD.PressureDisplayKey(
            101.21d, 80.01f, 120.01f, 10.11f, 8.11f, 12.41d, false);
        var unchanged = new PlayerWorldCoordinateHUD.PressureDisplayKey(
            101.23d, 80.02f, 120.02f, 10.12f, 8.12f, 12.44d, false);
        bool withinOneDecimal = baseline.Equals(unchanged);
        bool pressureChanged = !baseline.Equals(new PlayerWorldCoordinateHUD.PressureDisplayKey(
            101.26d, 80.01f, 120.01f, 10.11f, 8.11f, 12.41d, false));
        bool safeRangeChanged = !baseline.Equals(new PlayerWorldCoordinateHUD.PressureDisplayKey(
            101.21d, 80.06f, 120.01f, 10.11f, 8.11f, 12.41d, false));
        bool extensionChanged = !baseline.Equals(new PlayerWorldCoordinateHUD.PressureDisplayKey(
            101.21d, 80.01f, 120.01f, 10.16f, 8.11f, 12.41d, false));
        bool highExtensionChanged = !baseline.Equals(new PlayerWorldCoordinateHUD.PressureDisplayKey(
            101.21d, 80.01f, 120.01f, 10.11f, 8.16f, 12.41d, false));
        bool bufferChanged = !baseline.Equals(new PlayerWorldCoordinateHUD.PressureDisplayKey(
            101.21d, 80.01f, 120.01f, 10.11f, 8.11f, 12.46d, false));
        bool helmetChanged = !baseline.Equals(new PlayerWorldCoordinateHUD.PressureDisplayKey(
            101.21d, 80.01f, 120.01f, 10.11f, 8.11f, 12.41d, true));
        var low = new PlayerWorldCoordinateHUD.PressureDisplayKey(
            79.98d, 80f, 120f, 0f, 0f, 1d, false);
        var safe = new PlayerWorldCoordinateHUD.PressureDisplayKey(
            80.02d, 80f, 120f, 0f, 0f, 1d, false);
        bool dangerChanged = low.Pressure == safe.Pressure && low.Danger == -1 &&
            safe.Danger == 0 && !low.Equals(safe);
        var high = new PlayerWorldCoordinateHUD.PressureDisplayKey(
            120.02d, 80f, 120f, 0f, 0f, 1d, false);
        var upperSafe = new PlayerWorldCoordinateHUD.PressureDisplayKey(
            119.98d, 80f, 120f, 0f, 0f, 1d, false);
        bool highDangerChanged = high.Pressure == upperSafe.Pressure && high.Danger == 1 &&
            upperSafe.Danger == 0 && !high.Equals(upperSafe);
        return new JObject
        {
            ["unchanged"] = withinOneDecimal,
            ["pressureChanged"] = pressureChanged,
            ["safeRangeChanged"] = safeRangeChanged,
            ["extensionChanged"] = extensionChanged,
            ["highExtensionChanged"] = highExtensionChanged,
            ["bufferChanged"] = bufferChanged,
            ["helmetChanged"] = helmetChanged,
            ["dangerChanged"] = dangerChanged,
            ["highDangerChanged"] = highDangerChanged,
            ["roundingCorrect"] = baseline.Pressure == 101.2d && baseline.Minimum == 80d &&
                baseline.Buffer == 12.4d
        };
    }

    private static JObject CaptureHoverAndModSelection(JObject args)
    {
        var original = new ModProfile
        {
            EnabledMods = new System.Collections.Generic.List<string> { "mod.a", "mod.b" },
            LoadOrder = new System.Collections.Generic.List<string> { "mod.a", "mod.b" }
        };
        var equivalent = new ModProfile
        {
            EnabledMods = new System.Collections.Generic.List<string> { "MOD.B", "MOD.A" },
            LoadOrder = new System.Collections.Generic.List<string> { "MOD.A", "MOD.B" }
        };
        bool equivalentSelection = original.HasSameSelection(equivalent);
        equivalent.LoadOrder.Reverse();
        bool reorderedChanged = !original.HasSameSelection(equivalent);
        equivalent.LoadOrder.Reverse();
        equivalent.DisabledMods.Add("mod.c");
        bool disabledChanged = !original.HasSameSelection(equivalent);
        equivalent.DisabledMods.Clear();
        equivalent.AutoEnableNewMods = true;
        return new JObject
        {
            ["earlyHidden"] = !InventoryItemTooltip.HasCompletedMouseHover(10d, 10.299d),
            ["delayCompletedVisible"] = InventoryItemTooltip.HasCompletedMouseHover(10d, 10.3d),
            ["reenteredHidden"] = !InventoryItemTooltip.HasCompletedMouseHover(10.4d, 10.6d),
            ["equivalentSelection"] = equivalentSelection,
            ["reorderedChanged"] = reorderedChanged,
            ["disabledChanged"] = disabledChanged,
            ["autoEnableChanged"] = !original.HasSameSelection(equivalent)
        };
    }

    private static JObject CaptureLogFilters(JObject args)
    {
        bool logs = args.Value<bool?>("logs") ?? true;
        bool warnings = args.Value<bool?>("warnings") ?? true;
        bool errors = args.Value<bool?>("errors") ?? true;
        return new JObject
        {
            ["log"] = GameLogManager.MatchesRuntimeLogFilter(LogType.Log, logs, warnings, errors),
            ["warning"] = GameLogManager.MatchesRuntimeLogFilter(LogType.Warning, logs, warnings, errors),
            ["error"] = GameLogManager.MatchesRuntimeLogFilter(LogType.Error, logs, warnings, errors),
            ["exception"] = GameLogManager.MatchesRuntimeLogFilter(LogType.Exception, logs, warnings, errors),
            ["assert"] = GameLogManager.MatchesRuntimeLogFilter(LogType.Assert, logs, warnings, errors)
        };
    }

    private static JObject CaptureWorldSettingsPages(JObject args)
    {
        GameObject root = new GameObject("[LuaWorldSettingsPages]", typeof(RectTransform));
        root.SetActive(false);
        try
        {
            GameObject basic = new GameObject("Basic", typeof(RectTransform));
            GameObject advanced = new GameObject("Advanced", typeof(RectTransform));
            basic.transform.SetParent(root.transform, false);
            advanced.transform.SetParent(root.transform, false);
            NewWorldSettingsPagination pager = root.AddComponent<NewWorldSettingsPagination>();
            pager.Configure(basic, advanced, null, null);
            bool initialBasic = basic.activeSelf && !advanced.activeSelf;
            Transform preserved = advanced.transform;
            pager.ShowAdvanced();
            bool advancedVisible = !basic.activeSelf && advanced.activeSelf;
            pager.ShowBasic();
            return new JObject
            {
                ["initialBasic"] = initialBasic,
                ["advancedVisible"] = advancedVisible,
                ["returnedBasic"] = basic.activeSelf && !advanced.activeSelf,
                ["instancesRetained"] = preserved == advanced.transform && root.transform.childCount == 2
            };
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static JObject CaptureBagHotbarLayout(JObject args)
    {
        GameObject root = new GameObject("[LuaBagHotbarLayout]", typeof(RectTransform));
        root.SetActive(false);
        try
        {
            RectTransform parent = root.GetComponent<RectTransform>();
            parent.sizeDelta = new Vector2((float)Number(args, "width", 1920), (float)Number(args, "height", 1080));
            RectTransform bag = new GameObject("Bag", typeof(RectTransform)).GetComponent<RectTransform>();
            bag.SetParent(parent, false);
            bag.sizeDelta = new Vector2(1004, 560);
            bag.localScale = Vector3.one * (float)Number(args, "scale", 0.6);
            RectTransform hotbar = new GameObject("Hotbar", typeof(RectTransform)).GetComponent<RectTransform>();
            hotbar.SetParent(parent, false);
            hotbar.anchorMin = hotbar.anchorMax = new Vector2(0.5f, 0);
            hotbar.pivot = new Vector2(0.5f, 0);
            hotbar.sizeDelta = new Vector2(982, 104);
            hotbar.anchoredPosition = new Vector2(0, 24);
            InventoryPanelLayout.ApplyDefaultBagPosition(bag, hotbar);
            Vector3[] corners = new Vector3[4];
            bag.GetWorldCorners(corners);
            float left = parent.InverseTransformPoint(corners[0]).x;
            float bottom = parent.InverseTransformPoint(corners[0]).y;
            float top = parent.InverseTransformPoint(corners[2]).y;
            hotbar.GetWorldCorners(corners);
            float hotbarTop = parent.InverseTransformPoint(corners[1]).y;
            return new JObject
            {
                ["gap"] = bottom - hotbarTop,
                ["leftGap"] = left - parent.rect.xMin,
                ["insideTop"] = top <= parent.rect.yMax + 0.1f
            };
        }
        finally { Object.DestroyImmediate(root); }
    }
    #endregion
}
#endif
