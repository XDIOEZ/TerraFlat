using System;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>通用水容器面板：显示容器容量及水质，提供饮水、倒空与手持容器转水，离开交互距离自动关闭。</summary>
public sealed class WaterVesselPanel : MonoBehaviour
{
    public const string PrefabKey = "UI_WaterVessel";
    private BasePanel panel; // 通用面板生命周期。
    private Mod_WaterVessel vessel; // 当前目标水容器。
    private Item actor; // 操作者。
    private TextMeshProUGUI title, hint; // 当前容器名称与通用操作提示。
    private TextMeshProUGUI status; // 水质、份数与提示。
    private TextMeshProUGUI transferLabel; // 转水按钮文案。
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
        BuildingPanelActions buildingActions = current.GetComponent<BuildingPanelActions>();
        if (buildingActions == null)
            throw new InvalidOperationException("水容器面板缺少 BuildingPanelActions，正式 Prefab 未完成建筑操作绑定。");
        buildingActions.Bind(target.item);
        current.panel.Open();
        current.Refresh();
    }
    /// <summary>绑定现有节点；界面层级只由 Prefab 决定。</summary>
    private void Awake()
    {
        panel = GetComponent<BasePanel>();
        title = panel.GetText("陶罐标题");
        hint = panel.GetText("说明文本");
        status = panel.GetText("水量状态");
        drink = panel.GetButton("饮水按钮");
        transfer = panel.GetButton("转水按钮");
        transferLabel = transfer.GetComponentInChildren<TextMeshProUGUI>(true);
        drink.onClick.AddListener(Drink);
        transfer.onClick.AddListener(Transfer);
        panel.GetButton("倒空按钮").onClick.AddListener(Empty);
        panel.GetButton("关闭按钮").onClick.AddListener(Close);
        panel.Closed += ClearTarget;
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
    /// <summary>取得当前手持水容器，转移方向固定为手持容器到面板中的容器。</summary>
    private Mod_WaterVessel GetHeldVessel() => actor?.GetComponentInChildren<Inventory_HotBar>()?.CurentSelectItem?
        .itemMods.GetMod_ByID<Mod_WaterVessel>(Mod_WaterVessel.ModuleId);
    /// <summary>显示可区分的水质；脏淡水允许直接饮用，烧开后变为干净饮用水，海水不能饮用。</summary>
    private void Refresh()
    {
        title.text = GameRes.Instance != null &&
                     GameRes.Instance.TryGetItemDefinition(vessel.item.itemData.IDName, out RuntimeItemDefinition definition)
            ? definition.DisplayName
            : FlatWorldLocalizationService.GetUiText("水容器");
        hint.text = FlatWorldLocalizationService.GetUiText("手持水容器对准水域使用即可装水；脏淡水可直接喝，也可烧开，海水可加热制盐。");
        transferLabel.text = FlatWorldLocalizationService.GetUiText("从手持容器倒入");

        string[] qualities = { "空容器", "脏水（可直接喝）", "饮用水", "海水（可制盐）" };
        status.text = FlatWorldLocalizationService.GetUiFormat("{0}　{1} / {2} 份\n加热进度：{3:0} 秒",
            FlatWorldLocalizationService.GetUiText(qualities[(int)vessel.Data.Quality]), vessel.Data.Amount,
            vessel.Capacity, vessel.Data.ProcessingSeconds);
        drink.interactable = (vessel.Data.Quality is VesselWaterQuality.Dirty or VesselWaterQuality.Drinkable) &&
                             vessel.Data.Amount > 0;
        Mod_WaterVessel source = GetHeldVessel();
        transfer.interactable = source != null && source != vessel && source.Data.Amount > 0 &&
            vessel.Data.Amount < vessel.Capacity && (vessel.Data.Amount == 0 || source.Data.Quality == vessel.Data.Quality);
    }
    /// <summary>完成一次饮水并即时更新余量。</summary>
    private void Drink() { vessel.Drink(actor); Refresh(); }
    /// <summary>部分转移至目标罐的剩余容量。</summary>
    private void Transfer() { GetHeldVessel()?.TransferTo(vessel, actor); Refresh(); }
    /// <summary>按用户明确点击清空当前陶罐。</summary>
    private void Empty() { vessel.Empty(actor); Refresh(); }
    /// <summary>关闭后清理玩法引用。</summary>
    private void Close() => panel.Close();
    private void ClearTarget() { vessel = null; actor = null; }
    private void OnDestroy() { if (panel != null) panel.Closed -= ClearTarget; }
}
