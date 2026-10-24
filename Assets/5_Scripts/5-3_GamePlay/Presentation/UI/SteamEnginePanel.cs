using FlatWorld.Localization;
using TMPro;
using UnityEngine;

/// <summary>蒸汽机手持配置页只修改引擎自身状态，不持有矿车或燃料库存。</summary>
public sealed class SteamEnginePanel : MonoBehaviour
{
    #region 配置页面
    public const string PrefabKey = "UI_SteamEngine";
    private static SteamEnginePanel current;
    private BasePanel panel;
    private Mod_SteamEngine engine;
    private Player actor;
    private TMP_Text status, hint;
    private float nextRefresh;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    { Mod_SteamEngine.OpenRequested -= Open; Mod_SteamEngine.OpenRequested += Open; }

    public static void Open(Mod_SteamEngine target, Player player)
    {
        if (target == null || player == null) return;
        if (current == null)
            current = UIManager.Instance.CreatePanelFromGameObject(GameRes.Instance.GetPrefab(PrefabKey),
                initializeClosed: true).GetComponent<SteamEnginePanel>();
        current.engine = target; current.actor = player; current.panel.Open(); current.Refresh();
    }
    private void Awake()
    {
        panel = GetComponent<BasePanel>(); panel.PrepareForGameplayWindow();
        status = DynamicText("FWUI_Status"); hint = DynamicText("FWUI_Hint");
        panel.GetButton("FWUI_Close").onClick.AddListener(panel.Close);
        panel.GetButton("FWUI_Toggle").onClick.AddListener(() => { if (CanUse()) Configure(!engine.State.Enabled, engine.State.OutputLevel); });
        for (int i = 1; i <= 3; i++)
        { int level = i; panel.GetButton("FWUI_Level" + i).onClick.AddListener(() => { if (CanUse()) Configure(engine.State.Enabled, level); }); }
        panel.Closed += ClearTarget;
    }
    private TMP_Text DynamicText(string name)
    {
        TMP_Text text = panel.GetText(name);
        if (text != null && text.TryGetComponent(out LocalizedTextBinder binder)) binder.enabled = false;
        return text;
    }
    private bool CanUse() => engine != null && actor != null && engine.item != null &&
        engine.item.InHand && engine.item.Owner == actor && actor.IsLocalProfile;
    private void Configure(bool enabled, int level)
    { if (CanUse()) { engine.Configure(enabled, level); Refresh(); } }
    private void Update()
    {
        if (panel == null || !panel.IsOpen()) return;
        if (!CanUse()) { panel.Close(); return; }
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + .2f; Refresh();
    }
    private void Refresh()
    {
        if (!CanUse()) return;
        SteamEngineState state = engine.State;
        status.text = FlatWorldLocalizationService.GetUiFormat("{0} · 输出档位 {1}/3 · 动力 {2:0.##}",
            Ui(state.Enabled ? "已开启" : "已关闭"), state.OutputLevel, engine.MaximumPower * state.OutputLevel / 3f);
        hint.text = FlatWorldLocalizationService.GetUiFormat(
            "满档耗水 {0:0.###} 份/秒；每单位燃料热值可运行 {1:0.#} 秒。\n水容器和燃料放入矿车货仓，安装到引擎槽后自动取用。低档按比例消耗。",
            engine.WaterPerSecond, engine.FuelSecondsPerUnit);
        Caption("FWUI_Close", "关闭"); Caption("FWUI_Toggle", state.Enabled ? "关闭引擎" : "开启引擎");
        Caption("FWUI_Level1", "低档 33%"); Caption("FWUI_Level2", "中档 67%"); Caption("FWUI_Level3", "高档 100%");
    }
    private void Caption(string name, string source)
    {
        TMP_Text label = panel.GetButton(name).GetComponentInChildren<TMP_Text>(true);
        if (label != null && label.TryGetComponent(out LocalizedTextBinder binder)) binder.enabled = false;
        if (label != null) label.text = Ui(source);
    }
    private static string Ui(string value) => FlatWorldLocalizationService.GetUiText(value);
    private void ClearTarget() { engine = null; actor = null; }
    private void OnDisable() => ClearTarget();
    private void OnDestroy() { if (panel != null) panel.Closed -= ClearTarget; if (current == this) current = null; }
    #endregion
}
