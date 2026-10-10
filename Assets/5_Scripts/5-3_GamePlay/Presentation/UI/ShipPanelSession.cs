using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FlatWorld.Localization;
using FlatWorld.Spaceflight;

public sealed class ShipPanelSession : IMachinePanelSession
{
    #region 控制台会话与绑定
    private const int LogCapacity = 100;
    private static readonly List<ShipPanelSession> sessions = new();
    private readonly SpaceSession session;
    private readonly string pieceId;
    private readonly List<TerminalEntry> entries = new();
    private string shipId;
    private BasePanel panel;
    private Player actor;
    private TextMeshProUGUI status, output;
    private TMP_InputField xField, yField, pressureField, commandField;
    private ScrollRect outputScroll;
    private Coroutine scrollRoutine;
    private bool outputDirty, observed, followCommand;
    private float nextRefreshTime;
    private ShipFlightPhase observedPhase;
    private bool observedIgnited, observedDriving, observedPowered;
    private string observedNavigation, observedOwner, observedDocking;
    private int observedPieces;

    private sealed class TerminalEntry
    {
        public string Time, Kind, Source;
        public object[] Arguments;
        public bool Literal;
    }

    public bool IsAlive => panel != null;
    public bool IsOpen => IsAlive && panel.IsOpen();

    public ShipPanelSession(MachineEntity entity)
        : this(SpaceSession.Current, entity.ScopeKey.Substring(5), entity.Id.ToString(CultureInfo.InvariantCulture)) { }

    private ShipPanelSession(SpaceSession session, string shipId, string pieceId)
    {
        this.session = session;
        this.shipId = shipId;
        this.pieceId = pieceId;
        GameObject prefab = GameRes.Instance.GetPrefab("UI_Ship", false);
        if (prefab == null) throw new InvalidOperationException("飞船正式面板缺失。");
        panel = UIManager.Instance.CreatePanelFromGameObject(prefab);
        panel.InitClosed();
        panel.SetGameplayInputBlocking(false);
        panel.PrepareForGamepadNavigation(closeOnCancel: true);
        status = panel.GetComponentsInChildren<TextMeshProUGUI>(true).First(value => value.name == "FWUI_ShipStatus");
        output = panel.GetComponentsInChildren<TextMeshProUGUI>(true).First(value => value.name == "FWUI_ShipOutput");
        outputScroll = panel.GetComponentsInChildren<ScrollRect>(true).First(value => value.name == "FWUI_ShipOutputScroll");
        xField = FindField("FWUI_ShipX");
        yField = FindField("FWUI_ShipY");
        pressureField = FindField("FWUI_ShipPressure");
        commandField = FindField("FWUI_ShipCommand");
        pressureField.SetTextWithoutNotify((session.GetDevice(pieceId)?.TargetPressureKPa ?? 101.325d).ToString(CultureInfo.InvariantCulture));
        commandField.characterLimit = 160;
        commandField.onSubmit.AddListener(SubmitCommand);
        status.richText = false;
        output.richText = false;
        Bind("FWUI_ShipSubmit", () => SubmitCommand(commandField.text));
        Bind("FWUI_ShipDrive", () => Execute("drive"));
        Bind("FWUI_ShipExit", () => Execute("exit"));
        Bind("FWUI_ShipIgnite", () => Execute("ignite"));
        Bind("FWUI_ShipNavigate", () => Execute($"navigate {xField.text} {yField.text}"));
        Bind("FWUI_ShipPick", () => Execute("pick"));
        Bind("FWUI_ShipCancel", () => Execute("cancel"));
        Bind("FWUI_ShipLanding", () => Execute($"land {xField.text} {yField.text}"));
        Bind("FWUI_ShipDock", () => Execute("dock"));
        Bind("FWUI_ShipUndock", () => Execute("undock"));
        Bind("FWUI_ShipPressure", () => Execute($"pressure {pressureField.text}"));
        Append("系统", "控制台已连接。输入 help 查看命令；快捷按钮也会记录执行结果。");
        FlatWorldLocalizationService.LanguageChanged += HandleLanguageChanged;
        sessions.Add(this);
    }

    private TMP_InputField FindField(string name)
        => panel.GetComponentsInChildren<TMP_InputField>(true).First(value => value.name == name);

    private void Bind(string name, Action action)
        => panel.GetComponentsInChildren<Button>(true).First(value => value.name == name).onClick.AddListener(() => action());

    public static void Open(SpaceSession session, Player player, string ship, string piece)
    {
        ShipPanelSession existing = sessions.Find(value => value.IsAlive && value.session == session && value.shipId == ship && value.pieceId == piece);
        existing ??= new ShipPanelSession(session, ship, piece);
        if (!existing.IsOpen) existing.Toggle(player);
    }

    public void Toggle(Item player)
    {
        if (!IsAlive) return;
        if (IsOpen) { Close(); return; }
        actor = player as Player;
        panel.Open();
        outputDirty = true;
        Refresh();
    }

    public void Close()
    {
        if (!IsAlive) return;
        if (scrollRoutine != null) { panel.StopCoroutine(scrollRoutine); scrollRoutine = null; }
        panel.Close();
    }

    private void HandleLanguageChanged(string _)
    {
        outputDirty = true;
        Refresh();
    }
    #endregion

    #region 命令输入与统一执行
    private void SubmitCommand(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return;
        commandField.SetTextWithoutNotify(string.Empty);
        Execute(input);
        if (IsOpen && !Application.isMobilePlatform && !EventSystemGuard.IsGamepadMode)
            panel.StartCoroutine(RestoreCommandFocus());
    }

    private IEnumerator RestoreCommandFocus()
    {
        yield return null;
        if (!IsOpen) yield break;
        commandField.Select();
        commandField.ActivateInputField();
    }

    private void Execute(string input)
    {
        if (!IsOpen) return;
        Refresh();
        if (!IsOpen) return;
        string[] parts = input.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;
        followCommand = true;
        Append("指令", input.Trim(), literal: true);
        string command = parts[0].ToLowerInvariant();
        int expected = command is "navigate" or "land" ? 3 : command == "pressure" ? 2 : 1;
        if (parts.Length != expected)
        {
            Append("拒绝", "参数数量不正确。输入 help 查看命令格式。");
            RenderOutput();
            return;
        }
        string console = session.ResolveConsole(shipId, pieceId);
        switch (command)
        {
            case "help":
                Append("系统", "help：命令帮助\nstatus：当前船况\nclear：清空反馈\ndrive / exit：进入 / 退出驾驶\nignite：点火 / 停机\nnavigate X Y：按画面坐标导航\npick：点击画面选择导航点\ncancel：取消导航\nland X Y：设置地表落点\ndock / undock：确认对接 / 解除接口\npressure kPa：设置供气目标气压");
                break;
            case "status":
                Append("系统", status.text, literal: true);
                break;
            case "clear":
                entries.Clear();
                Append("系统", "反馈记录已清空。");
                break;
            case "drive":
                Report(actor != null && session.EnterConsole(actor, console),
                    "已进入驾驶位，WASD 加速，Q/E 转动，Esc 离开。",
                    "驾驶失败：请靠近已供电的有效控制台；海面浮船无法驾驶。");
                break;
            case "exit":
                Report(session.ExitConsole(actor), "已退出驾驶。", "当前未处于驾驶状态。");
                break;
            case "ignite":
                if (!session.ToggleIgnition(console))
                    Append("拒绝", "点火失败：需要供电、有效航空燃料及足够升空推力。");
                else
                    Append("完成", session.GetFlight(shipId).Ignited ? "点火已开启。" : "引擎已停机，飞船继续按惯性运动。");
                break;
            case "navigate":
                if (TryCoordinates(parts, out Vector2 target))
                    Report(session.StartNavigation(console, target),
                        "导航目标已设置，执行进度将在反馈屏更新。",
                        "导航失败：需处于太空飞行阶段，并有已供电的控制台和光纤连接的自动导航工作方块。");
                break;
            case "pick":
                if (session.BeginPickingNavigation(console))
                {
                    Append("完成", "点击画面选择导航坐标，右键取消选点；再次打开控制台可查看导航反馈。");
                    RenderOutput();
                    Close();
                    return;
                }
                Append("拒绝", "选点失败：需要太空中有效的控制台。");
                break;
            case "cancel":
                session.CancelNavigation(shipId);
                Append("完成", "导航已取消。");
                break;
            case "land":
                if (TryCoordinates(parts, out Vector2 landing))
                {
                    session.SelectLanding(shipId, landing);
                    Append("完成", "落点已记录，实际触地时才完成降落。");
                }
                break;
            case "dock":
                Report(session.ConfirmNearbyDock(shipId), "本船已确认对接接口，双方均确认后建立连接。",
                    "对接失败：附近没有方向、位置和速度符合要求的对接接口。");
                break;
            case "undock":
                Report(session.DisconnectDock(shipId, pieceId), "已解除接口，重新连接需要双方确认。",
                    "解除失败：请在已经连接的对接接口上操作。");
                break;
            case "pressure":
                SetPressure(parts[1]);
                break;
            default:
                Append("拒绝", "未知命令。输入 help 查看可用命令。");
                break;
        }
        Refresh();
    }

    private bool TryCoordinates(string[] parts, out Vector2 point)
    {
        point = default;
        bool parsedX = float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x);
        bool parsedY = float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y);
        if (!parsedX || !parsedY || !float.IsFinite(x) || !float.IsFinite(y))
        {
            Append("拒绝", "请输入有效的 X、Y 坐标，例如 navigate 120 -40。");
            return false;
        }
        xField.SetTextWithoutNotify(x.ToString(CultureInfo.InvariantCulture));
        yField.SetTextWithoutNotify(y.ToString(CultureInfo.InvariantCulture));
        point = new Vector2(x, y);
        return true;
    }

    private void SetPressure(string input)
    {
        if (!double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out double pressure) ||
            !double.IsFinite(pressure) || pressure < 0d || pressure > 200000d)
        {
            Append("拒绝", "请输入 0 至 200000 范围内的有效气压（kPa）。");
            return;
        }
        var device = session.GetDevice(pieceId);
        if (device == null || device.Configuration.SupplyMolesPerSecond <= 0f)
        {
            Append("拒绝", "请在供气器上设置目标气压。");
            return;
        }
        device.TargetPressureKPa = pressure;
        pressureField.SetTextWithoutNotify(pressure.ToString(CultureInfo.InvariantCulture));
        Append("完成", "供气目标已设为 {0:0.###} kPa，达到目标后停止补气。", arguments: new object[] { pressure });
    }

    private void Report(bool success, string accepted, string rejected)
        => Append(success ? "完成" : "拒绝", success ? accepted : rejected);
    #endregion

    #region 双屏实时状态与反馈记录
    private static string Text(string source) => FlatWorldLocalizationService.GetUiText(source);
    private static string Format(string source, params object[] arguments) => FlatWorldLocalizationService.GetUiFormat(source, arguments);
    private static string PhaseText(ShipFlightPhase phase) => Text(PhaseSource(phase));
    private static string PhaseSource(ShipFlightPhase phase) => phase switch
    {
        ShipFlightPhase.Landed => "已落地",
        ShipFlightPhase.Floating => "海面浮船",
        ShipFlightPhase.Lifting => "发射升空",
        ShipFlightPhase.Orbit => "太空飞行",
        ShipFlightPhase.Descending => "接近星体",
        _ => "下落选址"
    };

    private void Append(string kind, string source, bool literal = false, object[] arguments = null)
    {
        // 保留原始消息和参数，语言切换时重新呈现历史，日志只在出现新反馈时重绘。
        if (entries.Count == LogCapacity) entries.RemoveAt(0);
        entries.Add(new TerminalEntry { Time = DateTime.Now.ToString("HH:mm:ss"), Kind = kind, Source = source, Literal = literal, Arguments = arguments });
        outputDirty = true;
    }

    private void RenderOutput()
    {
        if (!outputDirty || !IsOpen) return;
        bool followLatest = followCommand || outputScroll.verticalNormalizedPosition <= .05f || outputScroll.content.rect.height <= outputScroll.viewport.rect.height;
        var text = new StringBuilder();
        foreach (TerminalEntry entry in entries)
        {
            text.Append('[').Append(entry.Time).Append("] [").Append(Text(entry.Kind)).Append("] ");
            text.AppendLine(entry.Literal ? entry.Source : entry.Arguments == null ? Text(entry.Source) :
                Format(entry.Source, entry.Arguments.Select(value => value is string source ? Text(source) : value).ToArray()));
            text.AppendLine();
        }
        output.text = text.ToString();
        outputDirty = false;
        followCommand = false;
        if (followLatest && panel.gameObject.activeInHierarchy)
        {
            if (scrollRoutine != null) panel.StopCoroutine(scrollRoutine);
            scrollRoutine = panel.StartCoroutine(ScrollToLatest());
        }
    }

    private IEnumerator ScrollToLatest()
    {
        yield return null;
        if (IsOpen) outputScroll.verticalNormalizedPosition = 0f;
        scrollRoutine = null;
    }

    private void ObserveChanges(ShipState ship, ShipFlightState flight, ShipNavigationState task)
    {
        int pieces = ship.Pieces.Count(value => value.IsAlive);
        string navigation = task == null ? string.Empty : $"{task.Sequence}:{task.Status}";
        var docks = session.State.Docking.Where(value => value.Connected && (value.ShipAId == shipId || value.ShipBId == shipId)).ToArray();
        string docking = string.Join("|", docks.Select(value => value.PairId).OrderBy(value => value, StringComparer.Ordinal));
        bool driving = actor != null && session.State.Passengers.Any(value => value.ProfileId == actor.ProfileName && value.ShipId == shipId && !string.IsNullOrWhiteSpace(value.ConsolePieceId));
        bool powered = session.ResolveConsole(shipId, pieceId) != null;
        if (observed)
        {
            if (observedOwner != shipId)
                Append("系统", "船体归属已变化，控制台已连接当前船体。");
            if (observedPhase != flight.Phase)
                Append("系统", "飞行阶段：{0}", arguments: new object[] { PhaseSource(flight.Phase) });
            if (observedIgnited != flight.Ignited)
                Append("系统", flight.Ignited ? "引擎状态：已点火。" : "引擎状态：已停机。");
            if (observedNavigation != navigation && task != null)
                Append("系统", "导航反馈：{0}", arguments: new object[] { task.Status });
            if (observedPieces != pieces)
                Append("系统", "当前有效船体部件：{0}", arguments: new object[] { pieces });
            if (observedDocking != docking)
                Append("系统", "对接状态：{0} 组接口已连接。", arguments: new object[] { docks.Length });
            if (observedDriving != driving)
                Append("系统", driving ? "驾驶状态：已进入驾驶位。" : "驾驶状态：已退出驾驶位。");
            if (observedPowered != powered)
                Append("系统", powered ? "控制台供电已恢复。" : "控制台未供电，驾驶与点火指令不可用。");
        }
        observed = true;
        observedOwner = shipId;
        observedPhase = flight.Phase;
        observedIgnited = flight.Ignited;
        observedNavigation = navigation;
        observedPieces = pieces;
        observedDocking = docking;
        observedDriving = driving;
        observedPowered = powered;
    }

    public void Refresh() => RefreshScreens(true);

    private void RefreshScreens(bool immediate)
    {
        if (!IsOpen || session.State == null) return;
        if (!immediate && Time.unscaledTime < nextRefreshTime) return;
        nextRefreshTime = Time.unscaledTime + .2f;
        actor = ItemMgr.Instance?.User_Player ?? actor;
        if (!session.FindPiece(pieceId, out ShipState ship, out _)) { Close(); return; }
        shipId = ship.ShipId;
        ShipFlightState flight = session.GetFlight(shipId);
        if (flight == null) { Close(); return; }
        ShipNavigationState task = session.State.Navigation.LastOrDefault(value => value.ShipId == shipId);
        ObserveChanges(ship, flight, task);
        var text = new StringBuilder();
        text.AppendLine(Format("飞船 {0}", ship.ShipId.Substring(0, Math.Min(8, ship.ShipId.Length))));
        text.AppendLine(Format("飞行阶段  {0}", PhaseText(flight.Phase)));
        text.AppendLine(Format("引擎  {0}", Text(flight.Ignited ? "已点火" : "已停机")));
        text.AppendLine(Format("有效部件  {0}", ship.Pieces.Count(value => value.IsAlive)));
        text.AppendLine();
        text.AppendLine(Format("质量  {0:0.##} kg\n转动惯量  {1:0.##} kg·m²", ship.MassKg, ship.InertiaKgM2));
        text.AppendLine(session.DescribePropulsion(shipId));
        text.AppendLine();
        text.AppendLine(Format("速度  {0:0.##} m/s\n角速度  {1:0.##}°/s",
            Math.Sqrt(ship.VelocityX * ship.VelocityX + ship.VelocityY * ship.VelocityY), ship.AngularVelocityRadiansPerSecond * 180d / Math.PI));
        text.AppendLine(Format("参考星体  {0}", flight.Orbit.CapturedBodyId ?? flight.Orbit.ReferenceBodyId ?? Text("宇宙固定")));
        double altitude = flight.HeightMeters;
        if (flight.Phase is ShipFlightPhase.Orbit or ShipFlightPhase.Descending && session.Universe.TryGetBody(flight.Orbit.ReferenceBodyId, out BodyState body))
            altitude = (flight.Orbit.PositionMeters - body.PositionMeters).Magnitude - body.RadiusMeters;
        text.AppendLine(Format("地表高度  {0:0.##} m\n升空速度  {1:0.##} m/s", Math.Max(0d, altitude), flight.VerticalSpeed));
        if (flight.Phase is ShipFlightPhase.Descending or ShipFlightPhase.SurfaceDescending || flight.VerticalSpeed < 0d)
            text.AppendLine(Format("预计触地  {0:0.0} s", session.DescentSeconds(flight)));
        text.AppendLine(Format("落点  {0}", flight.LandingSelected ? Format("({0:0.##}, {1:0.##})", flight.LandingX, flight.LandingY) : Text("未选：实际触地时取附近位置")));
        text.AppendLine();
        text.AppendLine(Format("导航  {0}", task == null ? Text("待机") : Text(task.Status)));
        if (task != null) text.AppendLine(Format("导航目标  ({0:0.##}, {1:0.##})", task.TargetX, task.TargetY));
        foreach (ShipCompartmentState room in ship.Compartments)
        {
            var gas = new FluidInventory(room.Gas);
            text.AppendLine(Format("密闭舱  {0} 格\n气压  {1:0.##} kPa · 温度  {2:0.#}℃", room.Cells.Count,
                gas.GetPressureKPa(room.VolumeLiters, .01d), ship.NormalCabinTemperatureCelsius));
        }
        string snapshot = text.ToString();
        if (status.text != snapshot) status.text = snapshot;
        RenderOutput();
    }

    public static void RefreshOpen(SpaceSession session)
    {
        foreach (ShipPanelSession value in sessions.ToArray())
            if (!value.IsAlive) value.Dispose();
            else if (value.session == session) value.RefreshScreens(false);
    }

    public void Dispose()
    {
        FlatWorldLocalizationService.LanguageChanged -= HandleLanguageChanged;
        sessions.Remove(this);
        if (panel != null) UnityEngine.Object.Destroy(panel.gameObject);
        panel = null;
    }
    #endregion
}
