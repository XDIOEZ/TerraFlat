using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FlatWorld.Spaceflight;

public sealed class ShipPanelSession : IMachinePanelSession
{
    #region 整船状态与操作面板
    private static readonly List<ShipPanelSession> sessions = new();
    private readonly SpaceSession session;
    private string shipId;
    private readonly string pieceId;
    private BasePanel panel;
    private Player actor;
    private TextMeshProUGUI status;
    private TMP_InputField xField, yField, pressureField;
    private string message;
    public bool IsAlive => panel != null;
    public bool IsOpen => IsAlive && panel.IsOpen();
    public ShipPanelSession(MachineEntity entity)
        : this(SpaceSession.Current, entity.ScopeKey.Substring(5), entity.Id.ToString(CultureInfo.InvariantCulture)) { }
    private ShipPanelSession(SpaceSession session, string shipId, string pieceId)
    {
        this.session = session; this.shipId = shipId; this.pieceId = pieceId;
        GameObject prefab = GameRes.Instance.GetPrefab("UI_Ship", false);
        if (prefab == null) throw new InvalidOperationException("飞船正式面板缺失。");
        panel = UIManager.Instance.CreatePanelFromGameObject(prefab);
        panel.InitClosed(); panel.SetGameplayInputBlocking(false); panel.PrepareForGamepadNavigation(closeOnCancel: true);
        status = panel.GetComponentsInChildren<TextMeshProUGUI>(true).First(value => value.name == "FWUI_ShipStatus");
        xField = panel.GetComponentsInChildren<TMP_InputField>(true).First(value => value.name == "FWUI_ShipX");
        yField = panel.GetComponentsInChildren<TMP_InputField>(true).First(value => value.name == "FWUI_ShipY");
        pressureField = panel.GetComponentsInChildren<TMP_InputField>(true).First(value => value.name == "FWUI_ShipPressure");
        pressureField.text = (session.GetDevice(pieceId)?.TargetPressureKPa ?? 101.325d).ToString(CultureInfo.InvariantCulture);
        Bind("FWUI_ShipExit", () => { session.ExitConsole(actor); message = "已退出驾驶"; });
        Bind("FWUI_ShipPick", () => { message = session.BeginPickingNavigation(session.ResolveConsole(shipId, pieceId)) ? "点击画面选择导航坐标，右键取消选点" : "选点需要太空中有效的控制台"; if (message.StartsWith("点击")) Close(); });
        Bind("FWUI_ShipPressure", () =>
        {
            var device = session.GetDevice(pieceId);
            if (device?.Configuration.SupplyMolesPerSecond > 0f && double.TryParse(pressureField.text, NumberStyles.Float, CultureInfo.InvariantCulture, out double pressure) && double.IsFinite(pressure) && pressure >= 0d && pressure <= 200000d)
            { device.TargetPressureKPa = pressure; message = "已设置实际供气目标气压，达到后停止，降低后补气"; }
            else message = "请在供气器上输入有效气压";
        });
        Bind("FWUI_ShipDrive", () => { message = session.EnterConsole(actor, session.ResolveConsole(shipId, pieceId)) ? "已进入驾驶位，WASD 加速，Q/E 转动，Esc 离开" : "需要有效且已供电的控制台；浮船暂时不能驾驶"; });
        Bind("FWUI_ShipIgnite", () => { message = session.ToggleIgnition(session.ResolveConsole(shipId, pieceId)) ? "点火状态已切换" : "点火需要供电、有效航空燃料及足够升空推力"; });
        Bind("FWUI_ShipNavigate", () =>
        {
            if (!Coordinates(out Vector2 point)) return;
            message = session.StartNavigation(session.ResolveConsole(shipId, pieceId), point) ? "导航目标已设置" : "需要同船的控制台和光纤连接的自动导航工作方块";
        });
        Bind("FWUI_ShipCancel", () => { session.CancelNavigation(shipId); message = "导航已取消"; });
        Bind("FWUI_ShipLanding", () => { if (Coordinates(out Vector2 point)) { session.SelectLanding(shipId, point); message = "落点已记录，实际触地时才完成降落"; } });
        Bind("FWUI_ShipDock", () => { message = session.ConfirmNearbyDock(shipId) ? "本船已确认同一对接接口" : "附近没有方向、位置和速度符合要求的对接接口"; });
        Bind("FWUI_ShipUndock", () => { message = session.DisconnectDock(shipId, pieceId) ? "已解除接口，重新连接需要双方确认" : "请在已经连接的对接接口上解除"; });
        sessions.Add(this);
    }
    private void Bind(string name, Action action)
    {
        Button button = panel.GetComponentsInChildren<Button>(true).First(value => value.name == name);
        button.onClick.AddListener(() => { action(); Refresh(); });
    }
    private bool Coordinates(out Vector2 point)
    {
        point = default;
        float x = 0f, y = 0f;
        bool parsed = float.TryParse(xField.text, NumberStyles.Float, CultureInfo.InvariantCulture, out x) &&
            float.TryParse(yField.text, NumberStyles.Float, CultureInfo.InvariantCulture, out y);
        if (!parsed) { message = "请输入有效的 X、Y 坐标"; return false; }
        if (!float.IsFinite(x) || !float.IsFinite(y)) { message = "坐标超出可用范围"; return false; }
        point = new Vector2(x, y); return true;
    }
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
        panel.Open(); Refresh();
    }
    public void Close() { if (IsAlive) panel.Close(); }
    public void Refresh()
    {
        if (!IsOpen || session.State == null) return;
        actor = ItemMgr.Instance?.User_Player;
        if (session.FindPiece(pieceId, out ShipState owner, out _)) shipId = owner.ShipId;
        ShipState ship = session.GetShip(shipId);
        if (ship == null) { Close(); return; }
        ShipFlightState flight = session.GetFlight(shipId);
        var text = new StringBuilder();
        string phase = flight.Phase switch { ShipFlightPhase.Landed => "已落地", ShipFlightPhase.Floating => "海面浮船", ShipFlightPhase.Lifting => "发射升空", ShipFlightPhase.Orbit => "太空飞行", ShipFlightPhase.Descending => "接近星体", _ => "下落选址" };
        text.AppendLine($"飞船 {ship.ShipId.Substring(0, Math.Min(8, ship.ShipId.Length))} · {phase} · 部件 {ship.Pieces.Count(value => value.IsAlive)}");
        text.AppendLine($"质量 {ship.MassKg:0.##} kg · 转动惯量 {ship.InertiaKgM2:0.##} kg·m²");
        text.AppendLine(session.DescribePropulsion(shipId));
        text.AppendLine($"速度 {Math.Sqrt(ship.VelocityX * ship.VelocityX + ship.VelocityY * ship.VelocityY):0.##} m/s · 角速度 {ship.AngularVelocityRadiansPerSecond * 180d / Math.PI:0.##}°/s");
        text.AppendLine($"当前参考星体 {flight.Orbit.CapturedBodyId ?? flight.Orbit.ReferenceBodyId ?? "宇宙固定"}");
        double altitude = flight.HeightMeters;
        if (flight.Phase is ShipFlightPhase.Orbit or ShipFlightPhase.Descending && session.Universe.TryGetBody(flight.Orbit.ReferenceBodyId, out BodyState body))
            altitude = (flight.Orbit.PositionMeters - body.PositionMeters).Magnitude - body.RadiusMeters;
        text.AppendLine($"相对地表高度 {Math.Max(0d, altitude):0.##} m · 升空速度 {flight.VerticalSpeed:0.##} m/s");
        if (flight.Phase == ShipFlightPhase.Descending || flight.VerticalSpeed < 0d)
            text.AppendLine($"预计触地 {session.DescentSeconds(flight):0.0} s · 落点 {(flight.LandingSelected ? $"{flight.LandingX:0.##}, {flight.LandingY:0.##}" : "未选：实际触地时取附近位置")}");
        ShipNavigationState task = session.State.Navigation.LastOrDefault(value => value.ShipId == shipId);
        if (task != null) text.AppendLine($"导航：{task.Status} · 目标相对坐标 ({task.TargetX:0.##}, {task.TargetY:0.##})");
        foreach (ShipCompartmentState room in ship.Compartments)
        {
            var gas = new FluidInventory(room.Gas);
            text.AppendLine($"密闭舱 {room.Cells.Count} 格 · {gas.GetPressureKPa(room.VolumeLiters, .01d):0.##} kPa · {ship.NormalCabinTemperatureCelsius:0.#}℃");
        }
        text.AppendLine("坐标框用于设置太空当前画面坐标的导航点，或目标星球地表的下落位置。");
        if (!string.IsNullOrWhiteSpace(message)) text.AppendLine(message);
        status.text = text.ToString();
    }
    public static void RefreshOpen(SpaceSession session)
    {
        foreach (ShipPanelSession value in sessions.ToArray())
            if (!value.IsAlive) { value.Dispose(); continue; }
            else if (value.session == session) value.Refresh();
    }
    public void Dispose()
    {
        sessions.Remove(this);
        if (panel != null) UnityEngine.Object.Destroy(panel.gameObject);
        panel = null;
    }
    #endregion
}
