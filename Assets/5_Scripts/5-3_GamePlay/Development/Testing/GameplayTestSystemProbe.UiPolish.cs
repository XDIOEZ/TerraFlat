#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

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
    }

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
