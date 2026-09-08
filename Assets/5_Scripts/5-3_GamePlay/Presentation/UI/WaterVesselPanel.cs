using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>正式陶罐面板：显示 8 份容量及水质，提供饮水、倒空与手持陶罐转水，离开交互距离自动关闭。</summary>
public sealed class WaterVesselPanel : MonoBehaviour
{
    public const string PrefabKey = "UI_WaterVessel";
    private BasePanel panel; // 通用面板生命周期。
    private Mod_WaterVessel vessel; // 当前目标陶罐。
    private Item actor; // 操作者。
    private TextMeshProUGUI status; // 水质、份数与提示。
    private Button drink, transfer; // 根据水质与手持状态启用。
    private static WaterVesselPanel current; // 世界 UI 下的一份面板实例。
    private float nextRefresh; // 可见时五次每秒刷新，避免逐帧生成文本。

    /// <summary>表现层监听玩法请求，不把 UI 依赖传回容器模块。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        Mod_WaterVessel.OpenRequested -= Show;
        Mod_WaterVessel.OpenRequested += Show;
    }
    /// <summary>通过资源注册表实例化正式面板，并切换当前目标。</summary>
    private static void Show(Mod_WaterVessel target, Item owner)
    {
        if (current == null)
            current = UIManager.Instance.CreatePanelFromGameObject(GameRes.Instance.GetPrefab(PrefabKey)).GetComponent<WaterVesselPanel>();
        current.vessel = target;
        current.actor = owner;
        current.panel.Open();
        current.Refresh();
    }
    /// <summary>绑定现有节点；界面层级只由 Prefab 决定。</summary>
    private void Awake()
    {
        panel = GetComponent<BasePanel>();
        status = panel.GetText("水量状态");
        drink = panel.GetButton("饮水按钮");
        transfer = panel.GetButton("转水按钮");
        drink.onClick.AddListener(Drink);
        transfer.onClick.AddListener(Transfer);
        panel.GetButton("倒空按钮").onClick.AddListener(Empty);
        panel.GetButton("关闭按钮").onClick.AddListener(Close);
    }
    /// <summary>只在面板可见时刷新，离开目标或目标销毁时收起。</summary>
    private void Update()
    {
        if (!panel.IsOpen()) return;
        if (vessel == null || !vessel.CanOperate(actor)) { Close(); return; }
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.2f;
        Refresh();
    }
    /// <summary>取得当前手持陶罐，转移方向固定为手持罐到面板中的罐。</summary>
    private Mod_WaterVessel GetHeldVessel() => actor?.GetComponentInChildren<Inventory_HotBar>()?.CurentSelectItem?
        .itemMods.GetMod_ByID<Mod_WaterVessel>(Mod_WaterVessel.ModuleId);
    /// <summary>显示可区分的水质，未经处理的淡水与海水均不开放饮水。</summary>
    private void Refresh()
    {
        string[] qualities = { "空罐", "淡水（需烧开）", "饮用水", "海水（可制盐）" };
        status.text = FlatWorldLocalizationService.GetUiFormat("{0}　{1} / {2} 份\n加热进度：{3:0} 秒",
            FlatWorldLocalizationService.GetUiText(qualities[(int)vessel.Data.Quality]), vessel.Data.Amount,
            vessel.Data.Capacity, vessel.Data.ProcessingSeconds);
        drink.interactable = vessel.Data.Quality == VesselWaterQuality.Drinkable && vessel.Data.Amount > 0;
        Mod_WaterVessel source = GetHeldVessel();
        transfer.interactable = source != null && source != vessel && source.Data.Amount > 0 &&
            vessel.Data.Amount < vessel.Data.Capacity && (vessel.Data.Amount == 0 || source.Data.Quality == vessel.Data.Quality);
    }
    /// <summary>完成一次饮水并即时更新余量。</summary>
    private void Drink() { vessel.Drink(actor); Refresh(); }
    /// <summary>部分转移至目标罐的剩余容量。</summary>
    private void Transfer() { GetHeldVessel()?.TransferTo(vessel, actor); Refresh(); }
    /// <summary>按用户明确点击清空当前陶罐。</summary>
    private void Empty() { vessel.Empty(actor); Refresh(); }
    /// <summary>关闭后清理玩法引用。</summary>
    private void Close() { panel.Close(); vessel = null; actor = null; }
}
