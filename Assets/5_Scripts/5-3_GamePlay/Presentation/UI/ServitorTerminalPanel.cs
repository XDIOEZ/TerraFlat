using FlatWorld.Localization;
using FlatWorld.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>终端只控制当前交互的机仆，离开操作距离即关闭，不创建全局工作 HUD。</summary>
public sealed class ServitorTerminalPanel : MonoBehaviour
{
    #region 终端绑定
    public const string PrefabKey = "UI_ServitorTerminal";
    public TMP_Text StatusText;
    public TMP_Text OrderText;
    public TMP_Text HintText;
    public Slider BatteryGauge;
    public Button StandbyButton, HaulButton, StorageButton, RadiusLessButton, RadiusMoreButton, RechargeButton, CloseButton;
    private static ServitorTerminalPanel current;
    private BasePanel panel;
    private Mod_ServitorController robot;
    private Item player;
    private uint robotGeneration;
    private float nextRefresh;
    private string feedback;
    private Button[] commandButtons;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    { Mod_ServitorController.TerminalRequested -= Open; Mod_ServitorController.TerminalRequested += Open; }

    private static void Open(Mod_ServitorController target, Item actor)
    {
        if (target == null || !target.CanOperate(actor)) return;
        if (current == null)
            current = UIManager.Instance.CreatePanelFromGameObject(GameRes.Instance.GetPrefab(PrefabKey),
                initializeClosed: true).GetComponent<ServitorTerminalPanel>();
        Mod_GameController controller = actor.GetComponentInChildren<Mod_GameController>(true);
        if (controller != null && !controller.CanToggleGameplayWindow(current.panel)) return;
        current.robot = target; current.player = actor; current.robotGeneration = target.item.RuntimeGeneration;
        current.feedback = null; current.panel.Open(); current.Refresh();
    }

    private void Awake()
    {
        panel = GetComponent<BasePanel>(); panel.PrepareForGameplayWindow();
        commandButtons = new[] { StandbyButton, HaulButton, StorageButton, RadiusLessButton, RadiusMoreButton, RechargeButton };
        foreach (TMP_Text text in new[] { StatusText, OrderText, HintText })
            if (text != null && text.TryGetComponent(out LocalizedTextBinder binder)) binder.enabled = false;
        StandbyButton.onClick.AddListener(() => ApplyMode(ServitorWorkMode.Standby));
        HaulButton.onClick.AddListener(() => ApplyMode(ServitorWorkMode.Haul));
        StorageButton.onClick.AddListener(() =>
        {
            if (!CanUse()) return;
            feedback = robot.SelectNextNearbyStorage(player) ? "已指定目标容器" : "30米内没有可用的仓储容器";
            Refresh();
        });
        RadiusLessButton.onClick.AddListener(() => ChangeRadius(-2f));
        RadiusMoreButton.onClick.AddListener(() => ChangeRadius(2f));
        RechargeButton.onClick.AddListener(() => { if (CanUse()) robot.RequestRecharge(player); Refresh(); });
        CloseButton.onClick.AddListener(panel.Close);
        panel.Closed += ClearTarget;
    }

    private bool CanUse() => robot != null && robot.item != null && robot.item.RuntimeGeneration == robotGeneration && robot.CanOperate(player);
    private void ApplyMode(ServitorWorkMode mode)
    { if (CanUse()) robot.SetWorkMode(player, mode); feedback = null; Refresh(); }
    private void ChangeRadius(float delta)
    { if (CanUse()) robot.SetWorkRadius(player, robot.WorkRadius + delta); Refresh(); }

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
        BatteryGauge.SetValueWithoutNotify(robot.BatteryPercentage / 100f);
        StatusText.text = FlatWorldLocalizationService.GetUiFormat("电量 {0:0.0}%　·　{1}\n货仓：{2} 件",
            robot.BatteryPercentage, Ui(robot.Status), robot.CargoAmount);
        OrderText.text = FlatWorldLocalizationService.GetUiFormat("工作模式：{0}\n目标容器：{1}\n以容器为中心，收集半径：{2:0} 米",
            Ui(robot.WorkMode == ServitorWorkMode.Haul ? "搬运" : "待机"), robot.DestinationName, robot.WorkRadius);
        HintText.text = !GameNetwork.HasStateAuthority ? Ui("当前版本仅主机可修改机仆命令。") :
            feedback != null ? Ui(feedback) : Ui("低于20%优先找电线充电，充满后继续工作。供电充足时每秒充电1%；断电不充电。关闭终端不会取消工作。");
        bool canEdit = GameNetwork.HasStateAuthority;
        foreach (Button button in commandButtons)
            button.interactable = canEdit;
    }

    private static string Ui(string text) => FlatWorldLocalizationService.GetUiText(text);
    private void ClearTarget() { robot = null; player = null; feedback = null; }
    private void OnDisable() => ClearTarget();
    private void OnDestroy() { if (panel != null) panel.Closed -= ClearTarget; if (current == this) current = null; }
    #endregion
}
