using System;
using System.Linq;
using FlatWorld.Localization;
using FlatWorld.Spaceflight;
using FlatWorld.Networking;
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
    private string bodyId, eventKey;
    private Vector2 origin, selected;
    private float nextMapRefresh;
    private bool remotePreviewPending, remotePreviewReady, remoteSelectedWater;
    private byte[] remotePreview;
    private Vector2 remoteOrigin, remoteSelected;
    private string remoteBody;
    private int remotePreviewGeneration;
    private string landingHeaderTemplate;
    private string mapHintTemplate;
    private string coordinatesTemplate;
    private string landingSafetyText;
    private string waterLandingText;
    private string landLandingText;
    private const int MapSize = SpaceNetworkBridge.LandingPreviewGridSize;
    private SpaceLandingPanelSession()
    {
        GameObject prefab = GameRes.ExistingInstance.GetPrefab("UI_SpaceLanding", false);
        if (prefab == null) throw new InvalidOperationException("正式下落选址页面缺失。");
        panel = UIManager.Instance.CreatePanelFromGameObject(prefab);
        panel.SetGameplayInputBlocking(false); panel.PrepareForGamepadNavigation(closeOnCancel: true);
        status = panel.GetComponentsInChildren<TMP_Text>(true).First(t => t.name == "FWUI_LandingStatus");
        if (status.TryGetComponent(out LocalizedTextBinder binder)) binder.enabled = false;
        FlatWorldLocalizationService.LanguageChanged += HandleLanguageChanged;
        HandleLanguageChanged(null);
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
        // 船上乘员通过真实驾驶调整落点，选址地图只服务独自下落的玩家。
        if (session.GetShip(passenger.ShipId) != null || !passenger.SurfaceDescending) { current?.Dispose(); return; }
        // 临时场景面板销毁后释放旧缓存，下次刷新重新创建选址界面。
        if (current != null && current.panel == null) current.Dispose();
        current ??= new SpaceLandingPanelSession();
        current.session = session; current.player = player;
        current.bodyId = passenger.LandingBodyId;
        string key = passenger.ProfileId + ":" + passenger.LandingBodyId;
        if (current.eventKey != key)
        {
            current.eventKey = key; current.panel.Open(); current.nextMapRefresh = 0f;
        }
        current.origin = new Vector2((float)passenger.LandingOriginX, (float)passenger.LandingOriginY);
        current.selected = new Vector2((float)passenger.LandingX, (float)passenger.LandingY);
        BodyState body = session.Universe.GetBody(current.bodyId);
        double height = passenger.SurfaceHeight;
        double velocity = passenger.SurfaceVerticalSpeed;
        double g = body.SurfaceGravity;
        double seconds = g > 0d ? (velocity + Math.Sqrt(velocity * velocity + 2d * g * Math.Max(0d, height))) / g : double.PositiveInfinity;
        bool remote = GameNetwork.Session.IsClient && !GameNetwork.Session.IsServer;
        SurfaceQueryStatus groundStatus;
        bool isWater;
        if (remote)
        {
            current.MaintainRemotePreview();
            groundStatus = current.remotePreviewReady ? SurfaceQueryStatus.Ready : SurfaceQueryStatus.Pending;
            isWater = current.remoteSelectedWater;
        }
        else
        {
            groundStatus = SpaceSurfaceQuery.QueryLandingSurface(body.PlanetId, current.selected, out var ground);
            isWater = ground.IsWater;
        }
        current.status.text = string.Format(current.landingHeaderTemplate, body.DisplayName, height, -velocity, seconds) + "\n" +
            string.Format(current.mapHintTemplate, SpaceGameplaySettings.Current.LandingChoiceRadiusMeters) + "\n" +
            string.Format(current.coordinatesTemplate, current.selected.x, current.selected.y,
                groundStatus == SurfaceQueryStatus.Ready ? isWater ? current.waterLandingText : current.landLandingText
                    : FlatWorldLocalizationService.GetUiText("正在加载地表")) + "\n" +
            current.landingSafetyText;
        if (Time.unscaledTime >= current.nextMapRefresh)
        { current.nextMapRefresh = Time.unscaledTime + .5f; current.DrawMap(body.PlanetId); }
    }
    private void Select(Vector2 point)
    {
        Vector2 chosen = origin + Vector2.ClampMagnitude(point - origin, (float)SpaceGameplaySettings.Current.LandingChoiceRadiusMeters);
        var passenger = session.GetPassenger(player);
        if (session.GetShip(passenger.ShipId) != null || !passenger.SurfaceDescending) return;
        passenger.LandingX = chosen.x; passenger.LandingY = chosen.y; passenger.LandingSelected = true;
        selected = chosen; nextMapRefresh = 0f;
    }
    private void DrawMap(string planetId)
    {
        float diameter = (float)(SpaceGameplaySettings.Current.LandingChoiceRadiusMeters * 2d);
        for (int y = 0; y < MapSize; y++) for (int x = 0; x < MapSize; x++)
        {
            Vector2 point = origin + new Vector2((float)x / (MapSize - 1) - .5f, (float)y / (MapSize - 1) - .5f) * diameter;
            Color color;
            if (GameNetwork.Session.IsClient && !GameNetwork.Session.IsServer)
            {
                byte kind = remotePreviewReady ? remotePreview[y * MapSize + x] : (byte)0;
                color = kind == 0 ? new Color(.2f, .2f, .2f) : kind == 1 ? new Color(.12f, .3f, .52f) :
                    kind == 2 ? new Color(.35f, .24f, .18f) : new Color(.3f, .38f, .27f);
            }
            else
            {
                SurfaceQueryStatus sampleStatus = SpaceSurfaceQuery.QueryLandingSurface(planetId, point, out var sample);
                color = sampleStatus != SurfaceQueryStatus.Ready ? new Color(.2f, .2f, .2f) :
                    sample.IsWater ? new Color(.12f, .3f, .52f) : sample.BlockingTileId >= 0 ? new Color(.35f, .24f, .18f) : new Color(.3f, .38f, .27f);
            }
            if (Vector2.Distance(point, selected) < diameter / MapSize) color = new Color(1f, .65f, .2f);
            texture.SetPixel(x, y, color);
        }
        texture.Apply(false);
    }

    // 下落地图只请求服务端确认的落点范围，旧选址回复不会覆盖新选择。
    private void MaintainRemotePreview()
    {
        if (remoteBody != bodyId || remoteOrigin != origin || remoteSelected != selected)
        {
            SpaceNetworkBridge.LandingPreviewCancel?.Invoke();
            remoteBody = bodyId; remoteOrigin = origin; remoteSelected = selected;
            remotePreviewPending = remotePreviewReady = false; remotePreview = null; remotePreviewGeneration++;
        }
        if (remotePreviewReady || remotePreviewPending || SpaceNetworkBridge.LandingPreviewRequester == null) return;
        int generation = remotePreviewGeneration;
        remotePreviewPending = SpaceNetworkBridge.LandingPreviewRequester(bodyId, origin, selected, (grid, water) =>
        {
            if (generation != remotePreviewGeneration || !ReferenceEquals(current, this)) return;
            if (grid?.Length != MapSize * MapSize) { remotePreviewPending = false; return; }
            remotePreview = grid; remoteSelectedWater = water; remotePreviewReady = true; remotePreviewPending = false;
            nextMapRefresh = 0f;
        });
    }
    private void HandleLanguageChanged(string _)
    {
        // 模板仅在语言切换时读取，实时下落数值刷新不反复查表。
        landingHeaderTemplate = FlatWorldLocalizationService.GetUiText("目标星体：{0}\n剩余高度 {1:0.0} m · 下落速度 {2:0.0} m/s · 预计触地 {3:0.0} s");
        mapHintTemplate = FlatWorldLocalizationService.GetUiText("点击地图选择附近落点（半径 {0:0.#} m）。橙色是落点中心。");
        coordinatesTemplate = FlatWorldLocalizationService.GetUiText("中心坐标 ({0:0.#}, {1:0.#}) · {2}");
        landingSafetyText = FlatWorldLocalizationService.GetUiText("独自下落期间停止宇航服推进，触地时由宇航服先承受冲击。");
        waterLandingText = FlatWorldLocalizationService.GetUiText("海上平台");
        landLandingText = FlatWorldLocalizationService.GetUiText("陆地平台");
    }
    public void Dispose()
    {
        remotePreviewGeneration++;
        if (ReferenceEquals(current, this)) SpaceNetworkBridge.LandingPreviewCancel?.Invoke();
        FlatWorldLocalizationService.LanguageChanged -= HandleLanguageChanged;
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
