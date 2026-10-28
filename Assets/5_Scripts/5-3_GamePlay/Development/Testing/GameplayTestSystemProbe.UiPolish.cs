#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
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
        Register("ui_settings", "window_drag_bounds", CaptureWindowDragBounds);
        Register("ui_settings", "window_resize_pointer", CaptureWindowResizePointer);
#if UNITY_EDITOR
        Register("ui_settings", "main_menu_scale_layout", CaptureMainMenuScaleLayout);
        Register("ui_settings", "main_menu_modal_scale_layout", CaptureMainMenuModalScaleLayout);
#endif
    }

#if UNITY_EDITOR
    /// <summary>读取四个正式主菜单面板的基准卡片，用真实安全区组件验证用户倍率。</summary>
    private static JObject CaptureMainMenuModalScaleLayout(JObject args)
    {
        string panel = Text(args, "panel", "save");
        string path = panel switch
        {
            "save" => "Assets/2_Prefabs/2-1_UI/MainMenu/Save/UI_SaveSelectionPanel.prefab",
            "new_game" => "Assets/2_Prefabs/2-1_UI/MainMenu/WorldSetup/UI_NewGame.prefab",
            "network" => "Assets/2_Prefabs/2-1_UI/MainMenu/WorldSetup/UI_NetworkMode.prefab",
            "mods" => "Assets/2_Prefabs/2-1_UI/MainMenu/Core/UI_ModManager.prefab",
            _ => throw new System.ArgumentException("未知的主菜单面板：" + panel)
        };
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        SafeAreaScaleGroup sourceGroup = prefab != null ? prefab.GetComponent<SafeAreaScaleGroup>() : null;
        if (sourceGroup == null)
            throw new System.InvalidOperationException("主菜单面板缺少安全区缩放组件：" + path);
        SerializedObject serialized = new SerializedObject(sourceGroup);
        RectTransform sourceBounds = serialized.FindProperty("boundsTarget").objectReferenceValue as RectTransform;
        SerializedProperty targets = serialized.FindProperty("scaledTargets");
        if (sourceBounds == null || targets == null || targets.arraySize == 0)
            throw new System.InvalidOperationException("主菜单面板缺少完整的缩放目标：" + path);

        bool allTargetsConsistent = false;
        for (int i = 0; i < targets.arraySize; i++)
        {
            RectTransform sourceTarget = targets.GetArrayElementAtIndex(i).objectReferenceValue as RectTransform;
            if (sourceTarget == null)
                throw new System.InvalidOperationException("主菜单面板存在空缩放目标：" + path);
            Vector3 baseScale = sourceTarget.localScale;
            bool matchesDesign = Mathf.Abs(baseScale.x - sourceBounds.localScale.x) < 0.001f &&
                                 Mathf.Abs(baseScale.y - sourceBounds.localScale.y) < 0.001f &&
                                 Mathf.Abs(baseScale.z - 1f) < 0.001f;
            if (!matchesDesign)
                throw new System.InvalidOperationException("主菜单面板的基准比例不同步：" + sourceTarget.name);
            if (sourceTarget == sourceBounds)
                allTargetsConsistent = true;
        }

        float width = (float)Number(args, "width", 1920);
        float height = (float)Number(args, "height", 1080);
        float scale = (float)Number(args, "scale", 1f);
        if (width <= 0f || height <= 0f ||
            scale < UIUserSettings.MinimumScale || scale > UIUserSettings.MaximumScale)
            throw new System.ArgumentOutOfRangeException(nameof(args), "主菜单测试的分辨率或倍率非法。");

        // 模拟正式 UIRoot 的 Expand 参考分辨率，只用临时 UI，不触碰玩家的真实设置。
        float canvasScale = Mathf.Min(width / 1920f, height / 1080f) * scale;
        GameObject fixture = new GameObject("[LuaMainMenuModalScale]", typeof(RectTransform));
        fixture.SetActive(false);
        try
        {
            RectTransform root = fixture.GetComponent<RectTransform>();
            root.sizeDelta = new Vector2(width / canvasScale, height / canvasScale);
            RectTransform card = new GameObject("FormalCardBounds", typeof(RectTransform))
                .GetComponent<RectTransform>();
            card.SetParent(root, false);
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            card.sizeDelta = sourceBounds.rect.size;
            card.localScale = sourceBounds.localScale;

            Vector2 margin = serialized.FindProperty("safeMargin").vector2Value;
            float presentationScale = serialized.FindProperty("presentationScale").floatValue;
            SafeAreaScaleGroup group = fixture.AddComponent<SafeAreaScaleGroup>();
            group.Configure(card, new[] { card }, margin, presentationScale);
            fixture.SetActive(true);
            group.ApplyScale();

            float displayedWidth = card.rect.width * card.localScale.x * canvasScale;
            float displayedHeight = card.rect.height * card.localScale.y * canvasScale;
            return new JObject
            {
                ["panel"] = panel,
                ["targets"] = targets.arraySize,
                ["containsBounds"] = allTargetsConsistent,
                ["baseScaleX"] = sourceBounds.localScale.x,
                ["baseScaleY"] = sourceBounds.localScale.y,
                ["presentationScale"] = presentationScale,
                ["widthPixels"] = displayedWidth,
                ["heightPixels"] = displayedHeight,
                ["insideSafeArea"] = displayedWidth <= width - 2f * margin.x * canvasScale + 0.2f &&
                    displayedHeight <= height - 2f * margin.y * canvasScale + 0.2f
            };
        }
        finally { Object.DestroyImmediate(fixture); }
    }

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
                ["menuPrefabScaleX"] = cardSource.localScale.x,
                ["menuPrefabScaleY"] = cardSource.localScale.y,
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

    #region 窗口拖动与缩放几何采样
    private static JObject CaptureWindowDragBounds(JObject args)
    {
        int previousOrder = BasePanel.CurrentOrder;
        GameObject root = new GameObject("[LuaWindowDrag]", typeof(RectTransform), typeof(Canvas));
        root.SetActive(false);
        root.GetComponent<Canvas>().enabled = false;
        try
        {
            RectTransform parent = new GameObject("SafeBounds", typeof(RectTransform)).GetComponent<RectTransform>();
            parent.SetParent(root.transform, false);
            parent.sizeDelta = new Vector2(1200, 900);
            parent.localScale = Vector3.one * (float)Number(args, "parentScale", 1);
            RectTransform target = new GameObject("WindowRoot", typeof(RectTransform), typeof(Image))
                .GetComponent<RectTransform>();
            target.SetParent(parent, false);
            target.sizeDelta = new Vector2(360, 240);
            target.pivot = new Vector2(.2f, .8f);
            target.anchorMin = target.anchorMax = new Vector2(.25f, .7f);
            RectTransform window = target;
            if (Flag(args, "childWindow", false))
            {
                target.anchorMin = Vector2.zero;
                target.anchorMax = Vector2.one;
                target.sizeDelta = Vector2.zero;
                window = new GameObject("VisibleWindow", typeof(RectTransform)).GetComponent<RectTransform>();
                window.SetParent(target, false);
                window.sizeDelta = new Vector2(360, 240);
                window.localScale = new Vector3(.7f, .8f, 1);
                RectTransform hit = new GameObject("DragSurface", typeof(RectTransform)).GetComponent<RectTransform>();
                hit.SetParent(target, false);
                hit.gameObject.AddComponent<UIWindowDragSurface>().Configure(window);
            }
            UI_Drag drag = target.gameObject.AddComponent<UI_Drag>();
            drag.draggableImage = target.GetComponent<Image>();
            root.SetActive(true);
            if (!Application.isPlaying) drag.Awake();
            drag.KeepVisibleWindowInsideParentBounds();
            JObject before = CaptureUiWindowBounds(window, parent);
            Vector2 start = RectTransformUtility.WorldToScreenPoint(null,
                window.TransformPoint(new Vector3(window.rect.center.x, window.rect.yMax - 2, 0)));
            var pointer = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                position = start,
                pointerCurrentRaycast = new RaycastResult { gameObject = drag.draggableImage.gameObject }
            };
            drag.OnPointerDown(pointer);
            drag.OnBeginDrag(pointer);
            JArray samples = new JArray();
            foreach (JObject step in (JArray)args["steps"])
            {
                Vector3 movement = parent.TransformVector(new Vector3(
                    (float)Number(step, "x", 0), (float)Number(step, "y", 0), 0));
                pointer.position = start + (Vector2)movement;
                drag.OnDrag(pointer);
                samples.Add(CaptureUiWindowBounds(window, parent));
            }
            drag.OnEndDrag(pointer);
            JObject animation = null;
            if (!Flag(args, "childWindow", false))
            {
                target.gameObject.AddComponent<CanvasGroup>();
                BasePanel panel = target.gameObject.AddComponent<BasePanel>();
                panel.Dragger = drag;
                panel.Open();
                SafeAreaScaleGroup scale = target.gameObject.AddComponent<SafeAreaScaleGroup>();
                scale.Configure(target, new[] { target }, Vector2.zero);
                var animationScale = typeof(SafeAreaScaleGroup).GetMethod("SetAnimationScale",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                animationScale.Invoke(scale, new object[] { .82f });
                pointer.position = RectTransformUtility.WorldToScreenPoint(null,
                    target.TransformPoint(new Vector3(target.rect.center.x, target.rect.yMax - 2, 0)));
                drag.OnPointerDown(pointer);
                drag.OnBeginDrag(pointer);
                pointer.position += (Vector2)parent.TransformVector(new Vector3(2000, 0, 0));
                drag.OnDrag(pointer);
                drag.OnEndDrag(pointer);
                animationScale.Invoke(scale, new object[] { 1f });
                bool grewOutside = !CaptureUiWindowBounds(window, parent)["inside"].Value<bool>();
                // 固定调用正式动画终态回调，验证放大完成后的再次限界。
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                object revision = typeof(BasePanel).GetField("visualTransitionRevision", flags).GetValue(panel);
                typeof(BasePanel).GetMethod("CompleteVisualTransition", flags).Invoke(panel, new[] { revision });
                animation = new JObject { ["grewOutside"] = grewOutside, ["ended"] = CaptureUiWindowBounds(window, parent) };
            }
            parent.sizeDelta = new Vector2(640, 360);
            drag.KeepVisibleWindowInsideParentBounds();
            return new JObject { ["before"] = before, ["samples"] = samples,
                ["resized"] = CaptureUiWindowBounds(window, parent),
                ["animation"] = animation,
                ["released"] = !drag.IsDragging, ["revision"] = drag.PositionRevision };
        }
        finally { Object.DestroyImmediate(root); BasePanel.CurrentOrder = previousOrder; }
    }

    private static JObject CaptureWindowResizePointer(JObject args)
    {
        GameObject root = new GameObject("[LuaWindowResize]", typeof(RectTransform));
        root.SetActive(false);
        EventSystem eventSystem = EventSystem.current;
        GameObject previousSelection = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
        try
        {
            RectTransform parent = root.GetComponent<RectTransform>();
            parent.sizeDelta = new Vector2(1200, 900);
            parent.localScale = new Vector3(.65f, 1.2f, 1);
            RectTransform target = new GameObject("Resizable", typeof(RectTransform)).GetComponent<RectTransform>();
            target.SetParent(parent, false);
            target.pivot = new Vector2((float)Number(args, "pivotX", .5), (float)Number(args, "pivotY", .5));
            if (Flag(args, "stretch", false))
            {
                target.anchorMin = new Vector2(.2f, .25f);
                target.anchorMax = new Vector2(.8f, .75f);
            }
            target.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 360);
            target.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 240);
            target.localScale = new Vector3(.6f, .8f, 1);
            UIDragResizer resize = target.gameObject.AddComponent<UIDragResizer>();
            resize.minWidth = 160; resize.maxWidth = 600;
            resize.minHeight = 100; resize.maxHeight = 450;
            root.SetActive(true);
            // 编辑态不自动执行普通 MonoBehaviour 的 Awake，只补齐固定组件的正式初始化。
            if (!Application.isPlaying)
                typeof(UIDragResizer).GetMethod("Awake", System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic).Invoke(resize, null);
            JObject before = CaptureUiWindowBounds(target, parent);
            int x = (int)Number(args, "edgeX", 1), y = (int)Number(args, "edgeY", 0);
            Vector2 start = RectTransformUtility.WorldToScreenPoint(null, target.TransformPoint(new Vector3(
                x == 0 ? target.rect.center.x : x < 0 ? target.rect.xMin + 1 : target.rect.xMax - 1,
                y == 0 ? target.rect.center.y : y < 0 ? target.rect.yMin + 1 : target.rect.yMax - 1, 0)));
            Matrix4x4 localToWorld = target.localToWorldMatrix;
            var pointer = new PointerEventData(eventSystem)
                { button = PointerEventData.InputButton.Right, position = start };
            resize.OnBeginDrag(pointer);
            JArray samples = new JArray();
            foreach (JObject step in (JArray)args["steps"])
            {
                Vector3 movement = localToWorld.MultiplyVector(new Vector3(
                    (float)Number(step, "x", 0), (float)Number(step, "y", 0), 0));
                pointer.position = start + (Vector2)movement;
                resize.OnDrag(pointer);
                samples.Add(CaptureUiWindowBounds(target, parent));
            }
            resize.OnEndDrag(pointer);
            return new JObject { ["before"] = before, ["samples"] = samples };
        }
        finally
        {
            Object.DestroyImmediate(root);
            if (eventSystem != null) eventSystem.SetSelectedGameObject(previousSelection);
        }
    }

    private static JObject CaptureUiWindowBounds(RectTransform window, RectTransform parent)
    {
        Vector3[] corners = new Vector3[4];
        window.GetWorldCorners(corners);
        Vector2 min = parent.InverseTransformPoint(corners[0]), max = min;
        for (int i = 1; i < corners.Length; i++)
        {
            Vector2 point = parent.InverseTransformPoint(corners[i]);
            min = Vector2.Min(min, point); max = Vector2.Max(max, point);
        }
        return new JObject { ["left"] = min.x, ["right"] = max.x, ["bottom"] = min.y, ["top"] = max.y,
            ["width"] = window.rect.width, ["height"] = window.rect.height,
            ["inside"] = min.x >= parent.rect.xMin - .1f && max.x <= parent.rect.xMax + .1f &&
                min.y >= parent.rect.yMin - .1f && max.y <= parent.rect.yMax + .1f };
    }
    #endregion
}
#endif
