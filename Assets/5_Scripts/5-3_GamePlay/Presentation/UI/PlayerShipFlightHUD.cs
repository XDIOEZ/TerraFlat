using System;
using System.Globalization;
using FlatWorld.Localization;
using FlatWorld.Spaceflight;
using UnityEngine;

/// <summary>本地驾驶员起降时显示真实飞行高度，落地完成凭证只短暂提示一次。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Player))]
public sealed class PlayerShipFlightHUD : MonoBehaviour
{
    #region 本地驾驶与正式 HUD
    public const string ViewName = "PlayerShipFlightHUD";
    private const string TitleSource = "飞行高度";
    private const string HeightSource = "{0} m";
    private const string NearGroundSource = "即将触地，着陆后飞船将固定";
    private const string LandedSource = "已着陆，飞船已固定";
    private readonly ShipFlightHudPresentationTracker tracker = new();
    private readonly string[] tickLabels = new string[5];
    private Player player;
    private SpaceSession boundSession;
    private ShipFlightHudView view;
    private RectTransform viewRect, coordinateHudRect, mobileMoveRect;
    private BasePanel statusPanel;
    private RectTransform statusBarsRect;
    private bool missingPrefabLogged;
    private double lastScaleMaximum = double.NaN;
    private string title, heightFormat, nearGroundHint, landedHint;
    private Vector2 lastSafeAreaSize;
    private Rect lastCoordinateRect;
    private Vector3 lastCoordinatePosition, lastCoordinateScale;
    private Rect lastMobileRect;
    private Vector3 lastMobilePosition, lastMobileScale;
    private Rect lastStatusRect;
    private Vector3 lastStatusPosition, lastStatusScale;
    private bool lastStatusVisible;
    private bool layoutDirty = true, lastCoordinateActive, lastMobile;
    private double lastReadoutHeight = double.NaN;
    private bool lastGroundContact;
    private string formattedHeight;
    #endregion

    #region 玩家生命周期
    private void Awake() => player = GetComponent<Player>();

    private void OnEnable()
    {
        player ??= GetComponent<Player>();
        if (player != null) player.ProfileContextChanged += HandleProfileContextChanged;
        FlatWorldLocalizationService.LanguageChanged += HandleLanguageChanged;
        RefreshLocalization();
        tracker.Reset();
    }

    private void OnDisable()
    {
        if (player != null) player.ProfileContextChanged -= HandleProfileContextChanged;
        FlatWorldLocalizationService.LanguageChanged -= HandleLanguageChanged;
        tracker.Reset();
        BindSession(null);
        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (view != null) Destroy(view.gameObject);
    }

    private void HandleProfileContextChanged()
    {
        tracker.Reset();
        lastScaleMaximum = double.NaN;
        SetVisible(false);
    }

    private void HandleLanguageChanged(string _) => RefreshLocalization();

    private void RefreshLocalization()
    {
        title = FlatWorldLocalizationService.GetUiText(TitleSource);
        heightFormat = FlatWorldLocalizationService.GetUiText(HeightSource);
        nearGroundHint = FlatWorldLocalizationService.GetUiText(NearGroundSource);
        landedHint = FlatWorldLocalizationService.GetUiText(LandedSource);
        lastScaleMaximum = double.NaN;
        formattedHeight = null;
    }
    #endregion

    #region 只读采样与显示
    private void LateUpdate()
    {
        bool allowed = CanDisplay();
        SpaceSession session = SpaceSession.Current;
        BindSession(session);
        ShipFlightHudSnapshot snapshot = default;
        bool available = allowed && session != null && session.TryGetShipFlightHudSnapshot(player, out snapshot);
        ShipFlightHudPresentationState presentation = tracker.Advance(snapshot, available,
            player != null ? player.gameObject.scene.name : null, Time.unscaledTimeAsDouble);
        if (!presentation.Visible || !EnsureView())
        {
            SetVisible(false);
            return;
        }
        SetVisible(true);
        RefreshLayout();
        if (lastScaleMaximum != presentation.ScaleMaximum)
        {
            lastScaleMaximum = presentation.ScaleMaximum;
            for (int i = 0; i < tickLabels.Length; i++)
                tickLabels[i] = (presentation.ScaleMaximum * i / 4d).ToString("0.##", CultureInfo.InvariantCulture);
            view.SetLabels(title, tickLabels);
        }
        if (formattedHeight == null || lastReadoutHeight != presentation.HeightMeters || lastGroundContact != presentation.GroundContact)
        {
            lastReadoutHeight = presentation.HeightMeters;
            lastGroundContact = presentation.GroundContact;
            string number = ShipFlightHudPresentationRules.FormatHeightNumber(presentation.HeightMeters, presentation.GroundContact);
            formattedHeight = string.Format(CultureInfo.InvariantCulture, heightFormat, number);
        }
        view.SetReadout(formattedHeight,
            presentation.Touchdown ? landedHint : presentation.NearGround ? nearGroundHint : string.Empty,
            (float)presentation.NormalizedHeight, presentation.Touchdown || presentation.NearGround);
    }

    private bool CanDisplay()
    {
        if (!isActiveAndEnabled || player == null || !player.IsLocalProfile || player.DestructionHandled ||
            player.IsUnloadingModules || player.itemMods == null || GameManager.Instance?.IsInGameWorld != true) return false;
        Mod_DamageReceiver health = player.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
        return health != null && health.Hp > 0f &&
            player.itemMods.GetMod_ByID<Mod_PlayerDeathState>(Mod_PlayerDeathState.ModuleId)?.IsInDyingState != true;
    }

    private void BindSession(SpaceSession session)
    {
        if (ReferenceEquals(boundSession, session)) return;
        if (!ReferenceEquals(boundSession, null))
        {
            boundSession.LocalManualConsoleExitRequested -= HandleManualConsoleExit;
            boundSession.LocalManualConsoleEnterRequested -= HandleManualConsoleEnter;
        }
        boundSession = session;
        tracker.Reset();
        if (boundSession != null)
        {
            boundSession.LocalManualConsoleExitRequested += HandleManualConsoleExit;
            boundSession.LocalManualConsoleEnterRequested += HandleManualConsoleEnter;
        }
    }

    private void HandleManualConsoleExit(string shipId)
    {
        tracker.CancelCurrentFlight(shipId);
        SetVisible(false);
    }

    private void HandleManualConsoleEnter(string shipId) => tracker.ResumeCurrentFlight(shipId);

    private bool EnsureView()
    {
        RectTransform parent = UIManager.Instance?.SafeAreaRoot;
        if (parent == null) return false;
        if (view != null)
        {
            if (viewRect.parent != parent) viewRect.SetParent(parent, false);
            return view.IsReady;
        }
        GameObject prefab = GameRes.Instance?.GetPrefab(RuntimeUIPrefabKeys.ShipFlightHud, false);
        if (prefab == null)
        {
            if (!missingPrefabLogged && GameRes.Instance != null)
            {
                Debug.LogError("[PlayerShipFlightHUD] 缺少正式 UI_ShipFlightHUD Prefab。", this);
                missingPrefabLogged = true;
            }
            return false;
        }
        GameObject instance = Instantiate(prefab, parent, false);
        instance.name = ViewName;
        view = instance.GetComponent<ShipFlightHudView>();
        viewRect = instance.GetComponent<RectTransform>();
        if (view == null || !view.IsReady || viewRect == null)
        {
            Debug.LogError("[PlayerShipFlightHUD] 正式高度尺 Prefab 缺少视觉引用。", instance);
            Destroy(instance);
            view = null;
            return false;
        }
        viewRect.SetAsFirstSibling();
        lastScaleMaximum = double.NaN;
        coordinateHudRect = null;
        mobileMoveRect = null;
        layoutDirty = true;
        return true;
    }

    private void RefreshLayout()
    {
        RectTransform safeArea = viewRect.parent as RectTransform;
        if (safeArea == null) return;
        if (coordinateHudRect == null)
            coordinateHudRect = safeArea.Find(PlayerWorldCoordinateHUD.ViewName) as RectTransform;
        bool coordinateActive = coordinateHudRect != null && coordinateHudRect.gameObject.activeInHierarchy;
        Rect coordinateRect = coordinateActive ? coordinateHudRect.rect : default;
        Vector3 coordinatePosition = coordinateActive ? coordinateHudRect.localPosition : default;
        Vector3 coordinateScale = coordinateActive ? coordinateHudRect.localScale : default;
        bool mobile = player.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller)?.IsUsingMobile == true;
        if (mobile && mobileMoveRect == null)
            mobileMoveRect = safeArea.Find(RuntimeUIPrefabKeys.MobileControls + "/玩法控制层/移动摇杆/底座") as RectTransform;
        bool moveVisible = mobile && mobileMoveRect != null && mobileMoveRect.gameObject.activeInHierarchy;
        Rect moveRect = moveVisible ? mobileMoveRect.rect : default;
        Vector3 movePosition = moveVisible ? mobileMoveRect.position : default;
        Vector3 moveScale = moveVisible ? mobileMoveRect.lossyScale : default;
        BasePanel currentStatusPanel = player.itemMods.GetMod_ByID<Mod_Food>(ModText.Food)?.panelUI;
        if (statusPanel != currentStatusPanel || statusBarsRect == null)
        {
            statusPanel = currentStatusPanel;
            statusBarsRect = statusPanel?.GetComponentInChildren<UI_Drag>(true)?.rectTransform;
        }
        RectTransform statusBars = statusBarsRect;
        bool statusVisible = statusBars != null && statusBars.gameObject.activeInHierarchy;
        Rect statusRect = statusVisible ? statusBars.rect : default;
        Vector3 statusPosition = statusVisible ? statusBars.position : default;
        Vector3 statusScale = statusVisible ? statusBars.lossyScale : default;
        Vector2 size = safeArea.rect.size;
        if (!layoutDirty && size == lastSafeAreaSize && coordinateActive == lastCoordinateActive &&
            coordinateRect == lastCoordinateRect && coordinatePosition == lastCoordinatePosition &&
            coordinateScale == lastCoordinateScale && moveVisible == lastMobile && moveRect == lastMobileRect &&
            movePosition == lastMobilePosition && moveScale == lastMobileScale &&
            statusVisible == lastStatusVisible && statusRect == lastStatusRect &&
            statusPosition == lastStatusPosition && statusScale == lastStatusScale) return;
        layoutDirty = false;
        lastSafeAreaSize = size; lastCoordinateActive = coordinateActive; lastCoordinateRect = coordinateRect;
        lastCoordinatePosition = coordinatePosition; lastCoordinateScale = coordinateScale; lastMobile = moveVisible;
        lastMobileRect = moveRect; lastMobilePosition = movePosition; lastMobileScale = moveScale;
        lastStatusVisible = statusVisible; lastStatusRect = statusRect;
        lastStatusPosition = statusPosition; lastStatusScale = statusScale;
        float top = safeArea.rect.height - 16f;
        if (coordinateActive)
        {
            Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(safeArea, coordinateHudRect);
            top = Mathf.Min(top, bounds.min.y - safeArea.rect.yMin - 16f);
        }
        // 高度尺避开左侧状态条与 Buff，并为手机摇杆的实际操作区保留边距。
        view.FitToSafeArea(safeArea, top, moveVisible ? mobileMoveRect : null,
            statusVisible ? statusBars : null);
    }

    private void SetVisible(bool visible)
    {
        if (view != null && view.gameObject.activeSelf != visible) view.gameObject.SetActive(visible);
    }
    #endregion
}
