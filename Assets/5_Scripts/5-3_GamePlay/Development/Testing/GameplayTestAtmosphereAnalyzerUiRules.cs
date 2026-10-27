#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FlatWorld.Localization;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>从正式预制体采样检测仪布局、读数和滚动控件供 Lua 独立断言。</summary>
public static class GameplayTestAtmosphereAnalyzerUiRules
{
    #region 测试入口与正式界面采样
    private const string PanelPath = "Assets/2_Prefabs/2-1_UI/Gameplay/Screens/UI_AtmosphereAnalyzer.prefab";

    [InitializeOnLoadMethod]
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void RegisterAnalyzerUiRules() =>
        GameplayTestSystemProbe.Register("ui_settings", "atmosphere_analyzer_ui", CaptureAnalyzerUi);

    private static JObject CaptureAnalyzerUi(JObject args)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPath);
        if (prefab == null) throw new InvalidOperationException("大气检测仪正式面板 Prefab 缺失。");
        float width = (float)GameplayTestSystemProbe.Number(args, "width", 1920);
        float height = (float)GameplayTestSystemProbe.Number(args, "height", 1080);
        float userScale = (float)GameplayTestSystemProbe.Number(args, "userScale", UIUserSettings.DefaultScale);
        if (width <= 0 || height <= 0 || userScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(args), "测试画布尺寸与缩放必须大于零。");
        float canvasScale = Mathf.Min(width / 1920f, height / 1080f) * userScale;
        Vector2 logicalSize = new Vector2(width / canvasScale, height / canvasScale);
        GameObject rootObject = new GameObject("[AtmosphereAnalyzerUiRules]", typeof(RectTransform), typeof(Canvas));
        rootObject.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            RectTransform root = (RectTransform)rootObject.transform;
            root.sizeDelta = logicalSize;
            rootObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            GameObject instance = UnityEngine.Object.Instantiate(prefab, root, false);
            AtmosphereAnalyzerPanel view = instance.GetComponent<AtmosphereAnalyzerPanel>();
            if (view == null || view.InstrumentFrame == null || view.DisplayScreen == null || view.CompositionScroll == null)
                throw new InvalidOperationException("大气检测仪正式面板缺少显示控件。");
            SafeAreaScaleGroup scale = instance.GetComponent<SafeAreaScaleGroup>();
            if (scale == null) throw new InvalidOperationException("大气检测仪缺少安全区缩放。");
            FluidCatalog catalog = new FluidCatalog();
            AtmosphereMeasurementSnapshot many = CreateAnalyzerMeasurementWithTraceGases();
            view.RenderMeasurement(many, catalog);
            Canvas.ForceUpdateCanvases();
            scale.ApplyScale();
            LayoutRebuilder.ForceRebuildLayoutImmediate(view.CompositionScroll.content);
            Canvas.ForceUpdateCanvases();

            ScrollRect scroll = view.CompositionScroll;
            scroll.verticalNormalizedPosition = 1f;
            float topPosition = scroll.content.anchoredPosition.y;
            scroll.verticalNormalizedPosition = 0f;
            float bottomPosition = scroll.content.anchoredPosition.y;
            AtmosphereAnalyzerGasRow[] rows = scroll.content.GetComponentsInChildren<AtmosphereAnalyzerGasRow>(true);
            var rowSamples = new JArray();
            foreach (AtmosphereAnalyzerGasRow row in rows)
            {
                TMP_Text percentage = RequireText(row.transform, "Percentage");
                RectTransform fill = RequireRect(row.transform, "ProportionFill");
                Image image = fill.GetComponent<Image>();
                rowSamples.Add(new JObject
                {
                    ["id"] = row.FluidId,
                    ["active"] = row.gameObject.activeSelf,
                    ["fraction"] = row.Fraction,
                    ["percentage"] = percentage.text,
                    ["fill"] = fill.anchorMax.x,
                    ["fillRaycast"] = image != null && image.raycastTarget
                });
            }

            RectTransform frame = view.InstrumentFrame;
            RectTransform screen = view.DisplayScreen;
            RectTransform close = RequireRect(frame, "FWUI_AnalyzerClose");
            RectTransform artwork = RequireRect(frame, "InstrumentArtwork");
            RectTransform drag = RequireRect(frame, "InstrumentDragSurface");
            Image artworkImage = artwork.GetComponent<Image>();
            Image closeImage = close.GetComponent<Image>();
            UIWindowDragSurface dragSurface = drag.GetComponent<UIWindowDragSurface>();
            VerticalLayoutGroup layout = scroll.content.GetComponent<VerticalLayoutGroup>();
            ContentSizeFitter fitter = scroll.content.GetComponent<ContentSizeFitter>();
            var texts = new JArray();
            foreach (TMP_Text text in screen.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.rectTransform.parent != screen) continue;
                texts.Add(new JObject
                {
                    ["name"] = text.name, ["bounds"] = Bounds(text.rectTransform, screen),
                    ["auto"] = text.enableAutoSizing, ["raycast"] = text.raycastTarget
                });
            }

            var result = new JObject
            {
                ["canvas"] = Bounds(root, root), ["frame"] = Bounds(frame, root),
                ["screen"] = Bounds(screen, frame), ["close"] = Bounds(close, frame),
                ["frameLocalBounds"] = Bounds(frame, frame), ["screenLocalBounds"] = Bounds(screen, screen),
                ["texts"] = texts,
                ["artworkBound"] = artworkImage != null && artworkImage.sprite != null,
                ["dragUsesFrame"] = dragSurface != null && dragSurface.WindowRect == frame,
                ["closeIsButton"] = close.GetComponent<Button>() != null,
                ["closeRaycast"] = closeImage != null && closeImage.raycastTarget,
                ["closeAlpha"] = closeImage != null ? closeImage.color.a : -1f,
                ["vertical"] = scroll.vertical, ["horizontal"] = scroll.horizontal,
                ["hasMask"] = scroll.viewport != null && scroll.viewport.GetComponent<RectMask2D>() != null,
                ["layoutControlsHeight"] = layout != null && layout.childControlHeight && !layout.childForceExpandHeight,
                ["contentFitsHeight"] = fitter != null && fitter.verticalFit == ContentSizeFitter.FitMode.PreferredSize,
                ["viewportHeight"] = scroll.viewport.rect.height, ["contentHeight"] = scroll.content.rect.height,
                ["topPosition"] = topPosition, ["bottomPosition"] = bottomPosition,
                ["count"] = view.VisibleComponentCount, ["rows"] = rowSamples,
                ["planet"] = RequireText(screen, "FWUI_PlanetName").text,
                ["total"] = RequireText(screen, "FWUI_TotalValue").text,
                ["status"] = RequireText(screen, "FWUI_AnalyzerStatus").text,
                ["expectedStatus"] = FlatWorldLocalizationService.GetUiText("实时测量 · 标准体积 / 摩尔占比")
            };

            var vacuumState = new AtmosphereState { CapacityMoles = 100m, ReferenceMoles = 100m,
                ReferencePressureKPa = 100d, TemperatureKelvin = 293.15d };
            view.RenderMeasurement(AtmosphereMeasurementService.Capture(vacuumState, "vacuum", "真空星球"), catalog);
            result["vacuum"] = CaptureEmptyDisplay(view, rows, "当前星球没有大气", "无可检出的气体");
            view.RenderMeasurement(AtmosphereMeasurementSnapshot.Unavailable, catalog);
            result["unavailable"] = CaptureEmptyDisplay(view, rows, "当前区域无法测量星球大气", "请在星球上使用检测仪");
            view.RenderMeasurement(many, catalog);
            AtmosphereAnalyzerGasRow[] restored = scroll.content.GetComponentsInChildren<AtmosphereAnalyzerGasRow>(true);
            result["rowsReused"] = restored.Length == rows.Length && restored.Length > 0 && ReferenceEquals(restored[0], rows[0]);
            result["restoredCount"] = view.VisibleComponentCount;
            return result;
        }
        finally { UnityEngine.Object.DestroyImmediate(rootObject); }
    }
    #endregion

    #region 显示数据和几何采样
    private static AtmosphereMeasurementSnapshot CreateAnalyzerMeasurementWithTraceGases()
    {
        var state = new AtmosphereState
        {
            CapacityMoles = 2000000000000m, ReferenceMoles = 1000000000000m,
            ReferencePressureKPa = 101.325d, TemperatureKelvin = 293.15d,
            Gases = new List<AtmosphereGasAmount>
            {
                new() { FluidId = FluidIds.Nitrogen, Moles = 780000000000m },
                new() { FluidId = FluidIds.Oxygen, Moles = 209600000000m },
                new() { FluidId = FluidIds.CarbonDioxide, Moles = 10400000000m }
            }
        };
        for (int i = 0; i < 13; i++)
            state.Gases.Add(new AtmosphereGasAmount { FluidId = "probe:trace_" + i, Moles = .0001m });
        return AtmosphereMeasurementService.Capture(state, "probe", "蓝星");
    }

    private static JObject CaptureEmptyDisplay(AtmosphereAnalyzerPanel view, AtmosphereAnalyzerGasRow[] rows,
        string status, string empty)
    {
        int activeRows = 0;
        foreach (AtmosphereAnalyzerGasRow row in rows) if (row.gameObject.activeSelf) activeRows++;
        TMP_Text emptyText = RequireText(view.DisplayScreen, "FWUI_EmptyMeasurement");
        return new JObject
        {
            ["status"] = RequireText(view.DisplayScreen, "FWUI_AnalyzerStatus").text,
            ["expectedStatus"] = FlatWorldLocalizationService.GetUiText(status),
            ["planet"] = RequireText(view.DisplayScreen, "FWUI_PlanetName").text,
            ["expectedUnavailablePlanet"] = FlatWorldLocalizationService.GetUiText("未连接星球"),
            ["total"] = RequireText(view.DisplayScreen, "FWUI_TotalValue").text,
            ["empty"] = emptyText.text, ["expectedEmpty"] = FlatWorldLocalizationService.GetUiText(empty),
            ["emptyVisible"] = emptyText.gameObject.activeSelf,
            ["visibleCount"] = view.VisibleComponentCount, ["activeRows"] = activeRows
        };
    }

    private static RectTransform RequireRect(Transform parent, string name) =>
        parent.Find(name) as RectTransform ?? throw new InvalidOperationException("缺少检测仪节点：" + name);

    private static TMP_Text RequireText(Transform parent, string name) =>
        RequireRect(parent, name).GetComponent<TMP_Text>() ??
        throw new InvalidOperationException("缺少检测仪文字：" + name);

    private static JObject Bounds(RectTransform target, RectTransform relativeTo)
    {
        var corners = new Vector3[4];
        target.GetWorldCorners(corners);
        Vector3 minimum = relativeTo.InverseTransformPoint(corners[0]);
        Vector3 maximum = minimum;
        for (int i = 1; i < corners.Length; i++)
        {
            Vector3 local = relativeTo.InverseTransformPoint(corners[i]);
            minimum = Vector3.Min(minimum, local);
            maximum = Vector3.Max(maximum, local);
        }
        return new JObject { ["left"] = minimum.x, ["right"] = maximum.x,
            ["bottom"] = minimum.y, ["top"] = maximum.y,
            ["width"] = maximum.x - minimum.x, ["height"] = maximum.y - minimum.y };
    }
    #endregion
}
#endif
