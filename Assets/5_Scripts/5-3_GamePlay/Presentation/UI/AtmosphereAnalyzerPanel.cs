using System;
using System.Collections.Generic;
using System.Globalization;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>检测仪的真实星球读数嵌在仪器贴图的显示屏中。</summary>
public sealed class AtmosphereAnalyzerPanel : MonoBehaviour
{
    #region 正式仪器控件
    public const string PrefabKey = "UI_AtmosphereAnalyzer";
    [SerializeField] private RectTransform instrumentFrame;
    [SerializeField] private RectTransform displayScreen;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text planetText;
    [SerializeField] private TMP_Text totalLabel;
    [SerializeField] private TMP_Text totalValue;
    [SerializeField] private TMP_Text pressureText;
    [SerializeField] private TMP_Text compositionLabel;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text emptyText;
    [SerializeField] private ScrollRect compositionScroll;
    [SerializeField] private AtmosphereAnalyzerGasRow gasRowPrefab;
    [SerializeField] private Button closeButton;
    private readonly List<AtmosphereAnalyzerGasRow> rows = new();
    private BasePanel panel;
    private Mod_AtmosphereAnalyzer analyzer;
    private Player actor;
    private static AtmosphereAnalyzerPanel current;
    private FluidCatalog measurementCatalog;
    public static AtmosphereAnalyzerPanel Current => current;
    public AtmosphereMeasurementSnapshot RenderedMeasurement { get; private set; }
    public AtmosphereMeasurementSnapshot Snapshot => RenderedMeasurement;
    public int VisibleComponentCount { get; private set; }
    public RectTransform InstrumentFrame => instrumentFrame;
    public RectTransform DisplayScreen => displayScreen;
    public ScrollRect CompositionScroll => compositionScroll;
    #endregion

    #region 面板请求与生命周期
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterAtmosphereAnalyzerPanelRequests()
    {
        Mod_AtmosphereAnalyzer.OpenRequested -= Show;
        Mod_AtmosphereAnalyzer.OpenRequested += Show;
        Mod_AtmosphereAnalyzer.CloseRequested -= CloseTarget;
        Mod_AtmosphereAnalyzer.CloseRequested += CloseTarget;
        Mod_AtmosphereAnalyzer.PanelOpenProbe = IsShowing;
    }

    public static bool IsShowing(Mod_AtmosphereAnalyzer target) => current != null &&
        ReferenceEquals(current.analyzer, target) && current.panel != null && current.panel.IsOpen();

    private static void Show(Mod_AtmosphereAnalyzer target, Player player)
    {
        if (target == null || !target.CanMeasure(player)) return;
        if (current == null)
        {
            GameObject prefab = GameRes.Instance?.GetPrefab(PrefabKey, false);
            if (prefab == null) throw new InvalidOperationException("缺少大气检测仪正式面板：" + PrefabKey);
            current = UIManager.Instance.CreatePanelFromGameObject(prefab, initializeClosed: true)
                .GetComponent<AtmosphereAnalyzerPanel>();
            if (current == null) throw new MissingComponentException("大气检测仪 Prefab 缺少显示控制器。");
        }
        bool changedTarget = !ReferenceEquals(current.analyzer, target);
        current.analyzer = target;
        current.actor = player;
        current.RenderMeasurement(target.ReadMeasurement(player));
        if (changedTarget) current.compositionScroll.verticalNormalizedPosition = 1f;
        current.panel.Open();
    }

    private static void CloseTarget(Mod_AtmosphereAnalyzer target, Player player)
    { if (IsShowing(target)) current.panel.Close(); }

    private void Awake()
    {
        panel = GetComponent<BasePanel>();
        if (panel == null) throw new MissingComponentException("大气检测仪面板缺少 BasePanel。");
        panel.PrepareForGameplayWindow();
        panel.PrepareForGamepadNavigation(closeButton.name);
        panel.Closed += ClearMeasurementTarget;
        closeButton.onClick.AddListener(panel.Close);
        FlatWorldLocalizationService.LanguageChanged += RefreshLocalizedMeasurement;
    }

    private void Update()
    {
        if (panel == null || !panel.IsOpen()) return;
        if (analyzer == null || !analyzer.CanMeasure(actor)) { panel.Close(); return; }
        AtmosphereMeasurementSnapshot next = analyzer.ReadMeasurement(actor);
        // 大气版本不变时复用完整读数，避免每帧生成文字或重建列表。
        if (!ReferenceEquals(RenderedMeasurement, next)) RenderMeasurement(next);
    }

    private void ClearMeasurementTarget()
    { analyzer = null; actor = null; RenderedMeasurement = null; }
    private void OnDisable() => ClearMeasurementTarget();
    private void OnDestroy()
    {
        FlatWorldLocalizationService.LanguageChanged -= RefreshLocalizedMeasurement;
        if (panel != null) panel.Closed -= ClearMeasurementTarget;
        if (current == this) current = null;
    }
    #endregion

    #region 只读快照显示
    public void RenderMeasurement(AtmosphereMeasurementSnapshot measurement, FluidCatalog catalog = null)
    {
        RenderedMeasurement = measurement ?? AtmosphereMeasurementSnapshot.Unavailable;
        measurementCatalog = catalog ?? FluidCatalog.Default;
        RefreshLocalizedMeasurement();
    }

    private void RefreshLocalizedMeasurement(string _ = null)
    {
        AtmosphereMeasurementSnapshot measurement = RenderedMeasurement ?? AtmosphereMeasurementSnapshot.Unavailable;
        SetDynamicText(titleText, Ui("大气检测仪"));
        SetDynamicText(totalLabel, Ui("星球大气总量 · 标准 m³"));
        SetDynamicText(compositionLabel, Ui("气体成分 / 占比"));
        SetDynamicText(closeButton.GetComponentInChildren<TMP_Text>(true), Ui("关闭"));
        bool available = measurement.Status != AtmosphereMeasurementStatus.Unavailable;
        SetDynamicText(planetText, available && !string.IsNullOrWhiteSpace(measurement.PlanetName)
            ? measurement.PlanetName : Ui("未连接星球"));
        SetDynamicText(totalValue, available ? FormatAtmosphereAmount(measurement.TotalStandardCubicMeters) : "—");
        SetDynamicText(pressureText, available
            ? FlatWorldLocalizationService.GetUiFormat("星球气压  {0:0.###} kPa", measurement.PressureKPa)
            : FlatWorldLocalizationService.GetUiFormat("星球气压  {0} kPa", "—"));
        SetDynamicText(statusText, Ui(measurement.Status switch
        {
            AtmosphereMeasurementStatus.Measured => "实时测量 · 标准体积 / 摩尔占比",
            AtmosphereMeasurementStatus.Vacuum => "当前星球没有大气",
            _ => "当前区域无法测量星球大气"
        }));
        int count = measurement.Components.Count;
        VisibleComponentCount = count;
        EnsureMeasurementRows(count);
        for (int i = 0; i < rows.Count; i++)
        {
            bool visible = i < count;
            rows[i].gameObject.SetActive(visible);
            if (!visible) continue;
            AtmosphereMeasurementComponent component = measurement.Components[i];
            string name = measurementCatalog.TryGet(component.FluidId, out FluidDefinition definition)
                ? Ui(definition.DisplayName) : component.FluidId;
            rows[i].RenderComponent(component, name);
        }
        emptyText.gameObject.SetActive(count == 0);
        SetDynamicText(emptyText, Ui(available ? "无可检出的气体" : "请在星球上使用检测仪"));
        LayoutRebuilder.ForceRebuildLayoutImmediate(compositionScroll.content);
    }

    private void EnsureMeasurementRows(int count)
    {
        if (gasRowPrefab == null || compositionScroll == null || compositionScroll.content == null)
            throw new MissingReferenceException("大气检测仪缺少正式成分行模板或滚动区。");
        while (rows.Count < count)
        {
            AtmosphereAnalyzerGasRow row = Instantiate(gasRowPrefab, compositionScroll.content, false);
            row.name = "FWUI_AtmosphereGas_" + rows.Count;
            rows.Add(row);
        }
    }

    public static string FormatAtmosphereAmount(decimal amount) => amount.ToString(
        amount > 0m && amount < .001m ? "0.############################" : "#,0.###", CultureInfo.InvariantCulture);
    private static string Ui(string source) => FlatWorldLocalizationService.GetUiText(source);
    internal static void SetDynamicText(TMP_Text target, string value)
    {
        if (target == null) return;
        if (target.TryGetComponent(out LocalizedTextBinder binder)) binder.enabled = false;
        target.text = value;
    }
    #endregion
}
