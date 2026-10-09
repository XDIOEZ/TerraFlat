using System;
using System.Linq;
using FlatWorld.Spaceflight;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class SpaceLandingPanelSession : IDisposable
{
    #region 真实地表下落预览
    private static SpaceLandingPanelSession current;
    private readonly BasePanel panel;
    private readonly TMP_Text status;
    private readonly RawImage map;
    private readonly Texture2D texture;
    private SpaceSession session;
    private Player player;
    private string shipId, bodyId, eventKey;
    private Vector2 origin, selected;
    private float nextMapRefresh;
    private const int MapSize = 33;
    private SpaceLandingPanelSession()
    {
        GameObject prefab = GameRes.ExistingInstance.GetPrefab("UI_SpaceLanding", false);
        if (prefab == null) throw new InvalidOperationException("正式下落选址页面缺失。");
        panel = UIManager.Instance.CreatePanelFromGameObject(prefab);
        panel.SetGameplayInputBlocking(false); panel.PrepareForGamepadNavigation(closeOnCancel: true);
        status = panel.GetComponentsInChildren<TMP_Text>(true).First(t => t.name == "FWUI_LandingStatus");
        map = panel.GetComponentsInChildren<RawImage>(true).First(t => t.name == "FWUI_LandingMap");
        texture = new Texture2D(MapSize, MapSize, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        map.texture = texture;
        var picker = map.gameObject.AddComponent<SpaceLandingMapPicker>();
        picker.Selected = normalized => Select(origin + (normalized - Vector2.one * .5f) * (float)(SpaceGameplaySettings.Current.LandingChoiceRadiusMeters * 2d));
    }
    public static void Refresh(SpaceSession session, Player player)
    {
        if (session?.State == null || player == null) { current?.Dispose(); return; }
        var passenger = session.GetPassenger(player);
        var ship = session.GetShip(passenger.ShipId);
        var flight = ship != null ? session.GetFlight(ship.ShipId) : null;
        bool shipFalling = flight != null && (flight.Phase == ShipFlightPhase.SurfaceDescending || flight.Phase == ShipFlightPhase.Lifting && flight.VerticalSpeed < 0d);
        if (!shipFalling && !passenger.SurfaceDescending) { current?.Dispose(); return; }
        current ??= new SpaceLandingPanelSession();
        current.session = session; current.player = player; current.shipId = shipFalling ? ship.ShipId : null;
        current.bodyId = shipFalling ? flight.BodyId : passenger.LandingBodyId;
        string key = shipFalling ? flight.LandingEventId ?? flight.ShipId : passenger.ProfileId + ":" + passenger.LandingBodyId;
        if (current.eventKey != key)
        {
            current.eventKey = key; current.panel.Open(); current.nextMapRefresh = 0f;
        }
        current.origin = shipFalling ? new Vector2((float)flight.SurfaceX, (float)flight.SurfaceY)
            : new Vector2((float)passenger.LandingOriginX, (float)passenger.LandingOriginY);
        current.selected = shipFalling && flight.LandingSelected ? new Vector2((float)flight.LandingX, (float)flight.LandingY)
            : passenger.SurfaceDescending ? new Vector2((float)passenger.LandingX, (float)passenger.LandingY) : current.origin;
        BodyState body = session.Universe.GetBody(current.bodyId);
        double height = shipFalling ? flight.HeightMeters : passenger.SurfaceHeight;
        double velocity = shipFalling ? flight.VerticalSpeed : passenger.SurfaceVerticalSpeed;
        double g = body.SurfaceGravity;
        double seconds = g > 0d ? (velocity + Math.Sqrt(velocity * velocity + 2d * g * Math.Max(0d, height))) / g : double.PositiveInfinity;
        var ground = SpaceSurfaceQuery.GetLandingSurface(body.PlanetId, current.selected);
        current.status.text = $"目标星体：{body.DisplayName}\n剩余高度 {height:0.0} m · 下落速度 {-velocity:0.0} m/s · 预计触地 {seconds:0.0} s\n" +
            $"点击地图选择附近落点（半径 {SpaceGameplaySettings.Current.LandingChoiceRadiusMeters:0.#} m）。白色是本体占地，橙色是中心。\n" +
            $"中心坐标 ({current.selected.x:0.#}, {current.selected.y:0.#}) · {(ground.IsWater ? "海上平台" : "陆地平台")}\n" +
            (shipFalling ? "有电、有燃料且有可用引擎时，可在控制台重新点火；飞船继续按实际高度和速度下落。" : "独自下落期间停止宇航服推进，触地时由宇航服先承受冲击。");
        if (Time.unscaledTime >= current.nextMapRefresh)
        { current.nextMapRefresh = Time.unscaledTime + .5f; current.DrawMap(body.PlanetId, ship); }
    }
    private void Select(Vector2 point)
    {
        Vector2 chosen = origin + Vector2.ClampMagnitude(point - origin, (float)SpaceGameplaySettings.Current.LandingChoiceRadiusMeters);
        if (shipId != null) session.SelectLanding(shipId, chosen);
        else
        {
            var passenger = session.GetPassenger(player);
            passenger.LandingX = chosen.x; passenger.LandingY = chosen.y; passenger.LandingSelected = true;
        }
        selected = chosen; nextMapRefresh = 0f;
    }
    private void DrawMap(string planetId, ShipState ship)
    {
        float diameter = (float)(SpaceGameplaySettings.Current.LandingChoiceRadiusMeters * 2d);
        ShipAssemblyState assembly = ship != null ? session.GetConnectedAssembly(ship.ShipId) : null;
        for (int y = 0; y < MapSize; y++) for (int x = 0; x < MapSize; x++)
        {
            Vector2 point = origin + new Vector2((float)x / (MapSize - 1) - .5f, (float)y / (MapSize - 1) - .5f) * diameter;
            var sample = SpaceSurfaceQuery.GetLandingSurface(planetId, point);
            Color color = sample.IsWater ? new Color(.12f, .3f, .52f) : sample.BlockingTileId >= 0 ? new Color(.35f, .24f, .18f) : new Color(.3f, .38f, .27f);
            if (assembly != null)
            {
                foreach (string memberId in assembly.MemberShipIds)
                {
                    ShipState member = session.GetShip(memberId);
                    double mx = member.PositionX - assembly.PositionX, my = member.PositionY - assembly.PositionY;
                    ShipGeometry.Rotate(point.x - selected.x - mx, point.y - selected.y - my, -member.AngleRadians, out double dx, out double dy);
                    int cellX = (int)Math.Floor((dx + member.CenterOfMassLocalX) / member.CellSizeMeters);
                    int cellY = (int)Math.Floor((dy + member.CenterOfMassLocalY) / member.CellSizeMeters);
                    if (member.Pieces.Any(piece => piece.IsAlive && ShipGeometry.Footprint(piece).Contains(new ShipCell(cellX, cellY))))
                    { color = Color.Lerp(color, Color.white, .7f); break; }
                }
            }
            if (Vector2.Distance(point, selected) < diameter / MapSize) color = new Color(1f, .65f, .2f);
            texture.SetPixel(x, y, color);
        }
        texture.Apply(false);
    }
    public void Dispose()
    {
        if (panel != null) UnityEngine.Object.Destroy(panel.gameObject);
        if (texture != null) UnityEngine.Object.Destroy(texture);
        if (ReferenceEquals(current, this)) current = null;
    }
    #endregion
}

public sealed class SpaceLandingMapPicker : MonoBehaviour, IPointerClickHandler
{
    #region 地图指针
    public Action<Vector2> Selected;
    public void OnPointerClick(PointerEventData data)
    {
        var rect = (RectTransform)transform;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, data.position, data.pressEventCamera, out Vector2 point)) return;
        Rect area = rect.rect;
        Selected?.Invoke(new Vector2(Mathf.InverseLerp(area.xMin, area.xMax, point.x), Mathf.InverseLerp(area.yMin, area.yMax, point.y)));
    }
    #endregion
}
