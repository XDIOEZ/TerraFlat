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
            bag.sizeDelta = new Vector2(920, 640);
            bag.localScale = Vector3.one * (float)Number(args, "scale", 0.6);
            RectTransform hotbar = new GameObject("Hotbar", typeof(RectTransform)).GetComponent<RectTransform>();
            hotbar.SetParent(parent, false);
            hotbar.anchorMin = hotbar.anchorMax = new Vector2(0.5f, 0);
            hotbar.pivot = new Vector2(0.5f, 0);
            hotbar.sizeDelta = new Vector2(800, 96);
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
