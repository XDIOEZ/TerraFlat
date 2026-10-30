using System;
using System.Collections;
using FlatWorld.Localization;
using FlatWorld.Diagnostics;
using FlatWorld.Spaceflight;
using TMPro;
using UnityEngine;

/// <summary>
/// 为本地玩家维护屏幕左上角的坐标、FPS、TPS 和脚下地块环境温度 HUD。
/// 坐标与环境温度以 10Hz 采样，FPS 与 TPS 共用 0.5 秒真实时间窗口。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Player))]
public sealed class PlayerWorldCoordinateHUD : MonoBehaviour
{
    #region 性能分段
    // HUD 只在真实刷新与读数边界计时，协程等待不进入性能作用域。
    private static readonly PerformanceDebugPoint HudSampleMarker = new("FlatWorld.HUD.Sample");
    private static readonly PerformanceDebugPoint TemperatureSampleMarker = new("FlatWorld.HUD.TemperatureSample");
    private static readonly PerformanceDebugPoint PressureFormatMarker = new("FlatWorld.HUD.PressureFormat");
    #endregion

    #region 常量与运行时状态

    public const string ViewName = "PlayerWorldCoordinateHUD";
    public const string AmbientTemperatureTextNodeName = "环境温度文本";
    public const string AmbientTemperatureFormat = "环境温度  {0:0.0}℃";
    public const string AmbientTemperatureUnavailableText = "环境温度  --℃";
    private const string PressureFormat =
        "气压 {0:0.#} kPa · {1}\n安全 {2:0.#}～{3:0.#} kPa · 低压 +{4:0.#} / 高压 +{5:0.#}\n呼吸 {6} · 缓冲 {7:0.#}秒";
    public const string SeasonTextNodeName = "季节文本";
    public const string SeasonFormat = "第 {0} 年 · {1} · 第 {2} 天 · {3:00}时";
    private static readonly string[] SeasonNames = { "春季", "夏季", "秋季", "冬季" };

    private const string CoordinateTextNodeName = "坐标文本";
    private const string FpsTextNodeName = "FPS文本";
    private const string CoordinateFormat = "X  {0:0.0}    Y  {1:0.0}";
    private const string GeographicFormat = "经 {0:0.00}°  纬 {1:0.00}°";
    private const string UniverseCoordinateFormat = "宇宙  X {0:0.000}  Y {1:0.000} km";
    private const string UniverseCoordinateUnavailableText = "宇宙  X --  Y -- km";
    private const string FpsFormat = "FPS  {0:0}    TPS  {1:0}";
    private const float RefreshIntervalSeconds = 0.1f;
    private const float FpsSampleIntervalSeconds = 0.5f;

    private Player player;
    private GameObject viewObject;
    private RectTransform viewRect;
    private TextMeshProUGUI coordinateText;
    private TextMeshProUGUI fpsText;
    private TextMeshProUGUI ambientTemperatureText; // 当前地块的环境读数，与体温模块的当前体温独立
    private TextMeshProUGUI seasonText; // 当前年份与季节
    private int lastSeasonYear = -1;
    private int lastSeasonIndex = -1;
    private int lastSeasonDay = -1;
    private int lastSeasonHour = -1;
    private string localizedTemperatureFormat;
    private string localizedTemperatureUnavailableText;
    private string localizedGeographicFormat;
    private string localizedPressureFormat;
    private string localizedPressureSafe, localizedPressureLowDanger, localizedPressureHighDanger;
    private string localizedPressureTank, localizedPressureAtmosphere;
    private int lastTemperatureTenths = int.MinValue;
    private bool temperatureDisplayInitialized;
    private string lastPressurePresentation;
    private PressureDisplayKey lastPressureKey;
    private bool hasPressureKey;
    private int lastCoordinateX = int.MinValue;
    private int lastCoordinateY = int.MinValue;
    private bool lastCoordinatesAreSpace;
    private double lastUniverseCoordinateX = double.NaN;
    private double lastUniverseCoordinateY = double.NaN;
    private string localizedUniverseCoordinateFormat;
    private string localizedUniverseCoordinateUnavailableText;
    private int lastFpsSampleFrame = -1;
    private ulong lastTpsSampleTick;
    private int lastDisplayedFps = -1;
    private int lastDisplayedTps = -1;
    private float lastFpsSampleTime = -1f;
    private PlayerWorldCoordinateDisplayMode lastDisplayMode =
        (PlayerWorldCoordinateDisplayMode)(-1);
    private bool missingPrefabLogged;
    private Coroutine refreshCoroutine;

    /// <summary>Profiler 可读取的 HUD 刷新节拍次数。</summary>
    public int RefreshTickCount { get; private set; }

    #endregion

    #region Unity 生命周期

    /// <summary>缓存当前 HUD 所属玩家。</summary>
    private void Awake()
    {
        ResolvePlayer();
    }

    /// <summary>订阅本地玩家资格与显示偏好，并恢复 HUD。</summary>
    private void OnEnable()
    {
        ResolvePlayer();
        if (player != null)
            player.ProfileContextChanged += HandleProfileContextChanged;
        PlayerWorldCoordinateDisplayPreferences.Changed += HandleDisplayPreferenceChanged;
        FlatWorldLocalizationService.LanguageChanged += HandleLanguageChanged;
        RefreshHudLocalization();

        RefreshForProfileContext();
    }

    /// <summary>解除事件与刷新循环，并隐藏 HUD。</summary>
    private void OnDisable()
    {
        if (player != null)
            player.ProfileContextChanged -= HandleProfileContextChanged;
        PlayerWorldCoordinateDisplayPreferences.Changed -= HandleDisplayPreferenceChanged;
        FlatWorldLocalizationService.LanguageChanged -= HandleLanguageChanged;

        StopRefreshLoop();
        SetViewActive(false);
    }

    /// <summary>销毁由本组件实例化的 HUD 视图。</summary>
    private void OnDestroy()
    {
        if (viewObject != null)
            Destroy(viewObject);
    }

    #endregion

    #region HUD 刷新

    /// <summary>仅本地玩家创建并显示左上角信息 HUD，远端玩家不会重复生成界面。</summary>
    private void RefreshVisibility()
    {
        using var refreshScope = HudSampleMarker.Measure(1);
        ResolvePlayer();
        if (!CanDisplay())
        {
            SetViewActive(false);
            return;
        }

        if (!EnsureView())
            return;

        SetViewActive(true);
        RefreshCoordinateText();
        RefreshFpsDisplay();
        RefreshAmbientTemperatureText();
        RefreshSeasonText();
    }

    /// <summary>实例化已有视觉 Prefab，并让常驻 HUD 位于普通弹窗的下方。</summary>
    private bool EnsureView()
    {
        Transform panelRoot = UIManager.Instance?.panelRoot;
        RectTransform rootRect = panelRoot as RectTransform ?? panelRoot?.GetComponent<RectTransform>();
        if (rootRect == null)
            return false;

        if (viewObject != null)
        {
            if (viewRect != null && viewRect.parent != rootRect)
            {
                viewRect.SetParent(rootRect, false);
                viewRect.SetAsFirstSibling();
            }

            return coordinateText != null && fpsText != null && ambientTemperatureText != null && seasonText != null;
        }

        GameObject prefab = GameRes.Instance?.GetPrefab(RuntimeUIPrefabKeys.PlayerWorldCoordinate, false);
        if (prefab == null)
        {
            if (!missingPrefabLogged && GameRes.Instance != null)
            {
                Debug.LogError("[PlayerWorldCoordinateHUD] 缺少 UI_PlayerWorldCoordinate Prefab。", this);
                missingPrefabLogged = true;
            }

            return false;
        }

        viewObject = Instantiate(prefab, rootRect, false);
        viewObject.name = ViewName;
        viewRect = viewObject.GetComponent<RectTransform>();
        Transform textNode = viewObject.transform.Find(CoordinateTextNodeName);
        coordinateText = textNode != null ? textNode.GetComponent<TextMeshProUGUI>() : null;
        Transform fpsNode = viewObject.transform.Find(FpsTextNodeName);
        fpsText = fpsNode != null ? fpsNode.GetComponent<TextMeshProUGUI>() : null;
        Transform temperatureNode = viewObject.transform.Find(AmbientTemperatureTextNodeName);
        ambientTemperatureText = temperatureNode != null ? temperatureNode.GetComponent<TextMeshProUGUI>() : null;
        seasonText = viewObject.transform.Find(SeasonTextNodeName)?.GetComponent<TextMeshProUGUI>();
        if (viewRect == null || coordinateText == null || fpsText == null || ambientTemperatureText == null || seasonText == null)
        {
            Debug.LogError("[PlayerWorldCoordinateHUD] 左上角信息 HUD Prefab 控件命名契约不完整。", viewObject);
            Destroy(viewObject);
            viewObject = null;
            viewRect = null;
            coordinateText = null;
            fpsText = null;
            ambientTemperatureText = null;
            seasonText = null;
            return false;
        }

        viewRect.SetAsFirstSibling();
        lastCoordinateX = int.MinValue;
        lastCoordinateY = int.MinValue;
        InvalidateUniverseCoordinates();
        lastDisplayMode = (PlayerWorldCoordinateDisplayMode)(-1);
        InvalidateFpsSample();
        temperatureDisplayInitialized = false;
        lastSeasonYear = -1;
        lastSeasonHour = -1;
        return true;
    }

    /// <summary>基础 HUD 只显示整点时间，并仅在日历显示值变化时更新文字。</summary>
    private void RefreshSeasonText()
    {
        if (seasonText == null || DayTimeSystem.Instance == null ||
            !DayTimeSystem.Instance.TryGetCurrentSeason(out SeasonSnapshot season) ||
            !DayTimeSystem.Instance.TryGetActiveTimeData(out TimeData time))
            return;
        int day = Mathf.FloorToInt(season.ElapsedDays) + 1;
        float dayLength = Mathf.Max(1f, time.DayLength);
        int hour = Mathf.Clamp(
            Mathf.FloorToInt(Mathf.Repeat(time.CurrentTime, dayLength) / dayLength * 24f),
            0,
            23);
        if (lastSeasonYear == season.Year && lastSeasonIndex == (int)season.Season &&
            lastSeasonDay == day && lastSeasonHour == hour)
            return;
        lastSeasonYear = season.Year;
        lastSeasonIndex = (int)season.Season;
        lastSeasonDay = day;
        lastSeasonHour = hour;
        seasonText.text = FlatWorldLocalizationService.GetUiFormat(SeasonFormat,
            season.Year, FlatWorldLocalizationService.GetUiText(SeasonNames[lastSeasonIndex]), day, hour);
    }

    /// <summary>复用热力图的逐格温度入口，未加载时显示空读数；四舍五入到 0.1℃ 后去重刷新。</summary>
    private void RefreshAmbientTemperatureText()
    {
        bool available;
        float temperature;
        using (TemperatureSampleMarker.Measure(1))
            available = TemperatureMgr.Instance.TryGetAmbientTemperature(player.transform.position, out temperature);
        int tenths = available ? Mathf.RoundToInt(temperature * 10f) : int.MinValue;
        string pressurePresentation = BuildPressurePresentation();
        if (temperatureDisplayInitialized && tenths == lastTemperatureTenths && pressurePresentation == lastPressurePresentation)
            return;

        temperatureDisplayInitialized = true;
        lastTemperatureTenths = tenths;
        lastPressurePresentation = pressurePresentation;
        string temperaturePresentation = available ? string.Format(localizedTemperatureFormat, tenths * .1f) : localizedTemperatureUnavailableText;
        ambientTemperatureText.text = temperaturePresentation + pressurePresentation;
    }

    private string BuildPressurePresentation()
    {
        using var pressureScope = PressureFormatMarker.Measure(1);
        if (player == null || player.itemMods.GetMod_ByID<Mod_Pressure>(Mod_Pressure.ModuleId) is not Mod_Pressure pressure)
        {
            hasPressureKey = false;
            return string.Empty;
        }
        pressure.GetSafePressureRange(out float min, out float max);
        double current = AtmosphereService.TryGetForWorld(player.gameObject.scene.name, out var atmosphere) ? AtmosphereService.PressureKPa(atmosphere) : 0d;
        bool sealedHelmet = SpacesuitSystem.TryGetEquipped(player, out var suit) && suit.IsHelmetSealed;
        var key = new PressureDisplayKey(current, min, max, pressure.SafePressureMinKPa - min,
            max - pressure.SafePressureMaxKPa, pressure.State.RemainingBufferSeconds, sealedHelmet);
        if (hasPressureKey && lastPressureKey.Equals(key))
            return lastPressurePresentation;

        hasPressureKey = true;
        lastPressureKey = key;
        string danger = key.Danger < 0 ? localizedPressureLowDanger :
            key.Danger > 0 ? localizedPressureHighDanger : localizedPressureSafe;
        string source = sealedHelmet ? localizedPressureTank : localizedPressureAtmosphere;
        try
        {
            return "\n" + string.Format(localizedPressureFormat, key.Pressure, danger, key.Minimum,
                key.Maximum, key.LowExtension, key.HighExtension, source, key.Buffer);
        }
        catch (FormatException)
        {
            // 译文占位符不合法时沿用 UI 本地化服务的原文回退约定。
            try
            {
                return "\n" + string.Format(PressureFormat, key.Pressure, danger, key.Minimum,
                    key.Maximum, key.LowExtension, key.HighExtension, source, key.Buffer);
            }
            catch (FormatException) { return "\n" + PressureFormat; }
        }
    }

    /// <summary>按 HUD 的一位小数精度比较，数值和安全等级均未变化就不分配字符串。</summary>
    internal readonly struct PressureDisplayKey : IEquatable<PressureDisplayKey>
    {
        public readonly double Pressure, Minimum, Maximum, LowExtension, HighExtension, Buffer;
        public readonly int Danger;
        public readonly bool SealedHelmet;

        public PressureDisplayKey(double pressure, float minimum, float maximum, float lowExtension,
            float highExtension, double buffer, bool sealedHelmet)
        {
            Pressure = Round(pressure);
            Minimum = Round(minimum);
            Maximum = Round(maximum);
            LowExtension = Round(lowExtension);
            HighExtension = Round(highExtension);
            Buffer = Round(buffer);
            Danger = pressure < minimum ? -1 : pressure > maximum ? 1 : 0;
            SealedHelmet = sealedHelmet;
        }

        private static double Round(double value) =>
            double.IsFinite(value) ? Math.Round(value, 1, MidpointRounding.AwayFromZero) : value;

        public bool Equals(PressureDisplayKey other) =>
            Pressure.Equals(other.Pressure) && Minimum.Equals(other.Minimum) && Maximum.Equals(other.Maximum) &&
            LowExtension.Equals(other.LowExtension) && HighExtension.Equals(other.HighExtension) &&
            Buffer.Equals(other.Buffer) && Danger == other.Danger && SealedHelmet == other.SealedHelmet;
    }

    /// <summary>只在语言变化时查询 HUD 文案，低频采样循环只负责数值。</summary>
    private void RefreshHudLocalization()
    {
        localizedTemperatureFormat = FlatWorldLocalizationService.GetUiText(AmbientTemperatureFormat);
        localizedTemperatureUnavailableText = FlatWorldLocalizationService.GetUiText(AmbientTemperatureUnavailableText);
        localizedGeographicFormat = FlatWorldLocalizationService.GetUiText(GeographicFormat);
        localizedPressureFormat = FlatWorldLocalizationService.GetUiText(PressureFormat);
        localizedPressureSafe = FlatWorldLocalizationService.GetUiText("安全");
        localizedPressureLowDanger = FlatWorldLocalizationService.GetUiText("低压危险");
        localizedPressureHighDanger = FlatWorldLocalizationService.GetUiText("高压危险");
        localizedPressureTank = FlatWorldLocalizationService.GetUiText("罐内供氧");
        localizedPressureAtmosphere = FlatWorldLocalizationService.GetUiText("星球大气");
        hasPressureKey = false;
        localizedUniverseCoordinateFormat = FlatWorldLocalizationService.GetUiText(UniverseCoordinateFormat);
        localizedUniverseCoordinateUnavailableText = FlatWorldLocalizationService.GetUiText(UniverseCoordinateUnavailableText);
        InvalidateUniverseCoordinates();
        lastCoordinateX = lastCoordinateY = int.MinValue;
        lastDisplayMode = (PlayerWorldCoordinateDisplayMode)(-1);
        temperatureDisplayInitialized = false;
    }

    private void HandleLanguageChanged(string localeCode)
    {
        RefreshHudLocalization();
        lastSeasonYear = -1;
        lastSeasonHour = -1;
        RefreshSeasonText();
        if (CanDisplay() && coordinateText != null)
            RefreshCoordinateText();
        if (CanDisplay() && ambientTemperatureText != null)
            RefreshAmbientTemperatureText();
    }

    /// <summary>仅在显示值改变时写入 TMP，避免静止状态产生无效刷新和字符串分配。</summary>
    private void RefreshCoordinateText()
    {
        SpaceSession session = SpaceSession.Current;
        if (session?.IsSpaceView == true)
        {
            RefreshUniverseCoordinateText(session);
            return;
        }

        // 先还原星球逻辑坐标，避免 HUD 显示循环世界的 Unity 表现镜像位置。
        Vector3 position = WorldLocalPresentation.ToLogical(player.transform.position);
        int coordinateX = Mathf.RoundToInt(position.x * 10f);
        int coordinateY = Mathf.RoundToInt(position.y * 10f);
        PlayerWorldCoordinateDisplayMode displayMode =
            PlayerWorldCoordinateDisplayPreferences.Mode;
        if (!lastCoordinatesAreSpace && coordinateX == lastCoordinateX && coordinateY == lastCoordinateY &&
            displayMode == lastDisplayMode)
            return;

        lastCoordinateX = coordinateX;
        lastCoordinateY = coordinateY;
        lastDisplayMode = displayMode;
        lastCoordinatesAreSpace = false;

        if (displayMode == PlayerWorldCoordinateDisplayMode.LatitudeLongitude)
        {
            GetLatitudeLongitude(position, out float longitude, out float latitude);
            coordinateText.SetText(localizedGeographicFormat, longitude, latitude);
            return;
        }

        coordinateText.SetText(CoordinateFormat, coordinateX * 0.1f, coordinateY * 0.1f);
    }

    /// <summary>太空位置只读取双精度权威乘员状态，不使用随浮动原点移动的 Unity 坐标。</summary>
    private void RefreshUniverseCoordinateText(SpaceSession session)
    {
        if (!TryGetUniversePosition(session, out SpaceVector2 position))
        {
            if (!lastCoordinatesAreSpace || !double.IsNaN(lastUniverseCoordinateX))
                coordinateText.text = localizedUniverseCoordinateUnavailableText;
            lastCoordinatesAreSpace = true;
            lastUniverseCoordinateX = lastUniverseCoordinateY = double.NaN;
            return;
        }

        // 显示千米并保留三位小数，换原点不会改变读数，米级变化才重写文本。
        double coordinateX = Math.Round(position.X);
        double coordinateY = Math.Round(position.Y);
        if (lastCoordinatesAreSpace && coordinateX == lastUniverseCoordinateX &&
            coordinateY == lastUniverseCoordinateY)
            return;

        lastCoordinatesAreSpace = true;
        lastUniverseCoordinateX = coordinateX;
        lastUniverseCoordinateY = coordinateY;
        coordinateText.text = string.Format(localizedUniverseCoordinateFormat,
            coordinateX / 1000d, coordinateY / 1000d);
    }

    /// <summary>乘船时由船姿态和乘员本地位置还原宇宙位置，离船时读取自由移动位置。</summary>
    private bool TryGetUniversePosition(SpaceSession session, out SpaceVector2 position)
    {
        position = default;
        if (session.State == null)
            return false;

        SpacePassengerState passenger = null;
        foreach (SpacePassengerState candidate in session.State.Passengers)
        {
            if (candidate.ProfileId != player.ProfileName)
                continue;
            passenger = candidate;
            break;
        }
        if (passenger == null)
            return false;

        ShipState ship = session.GetShip(passenger.ShipId);
        if (passenger.Supported && ship != null)
        {
            ShipGeometry.LocalToWorld(ship, passenger.LocalX, passenger.LocalY, out double x, out double y);
            position = new SpaceVector2(x, y);
        }
        else
        {
            position = passenger.FreeMotion.PositionMeters;
        }
        return position.IsFinite;
    }

    /// <summary>重新绑定界面或切换语言时强制刷新宇宙坐标。</summary>
    private void InvalidateUniverseCoordinates()
    {
        lastCoordinatesAreSpace = false;
        lastUniverseCoordinateX = lastUniverseCoordinateY = double.NaN;
    }

    /// <summary>根据持久化开关控制 FPS 文本，并仅在开启时采样刷新。</summary>
    private void RefreshFpsDisplay()
    {
        bool shouldShow = PlayerWorldCoordinateDisplayPreferences.ShowFps;
        if (!shouldShow)
        {
            if (fpsText.gameObject.activeSelf)
                fpsText.gameObject.SetActive(false);
            InvalidateFpsSample();
            return;
        }

        if (!fpsText.gameObject.activeSelf)
        {
            fpsText.gameObject.SetActive(true);
            BeginFpsSample();
            return;
        }

        RefreshFpsText();
    }

    /// <summary>以当前画面帧、固定步和真实时间作为采样窗口起点。</summary>
    private void BeginFpsSample()
    {
        lastFpsSampleFrame = Time.frameCount;
        lastTpsSampleTick = GameplayFixedStepSimulation.TickIndex;
        lastFpsSampleTime = Time.unscaledTime;
        lastDisplayedFps = -1;
        lastDisplayedTps = -1;
        fpsText.SetText("FPS  --    TPS  --");
    }

    /// <summary>按同一真实时间窗口计算 FPS 和实际 TPS，变化时才写入 TMP。</summary>
    private void RefreshFpsText()
    {
        if (lastFpsSampleFrame < 0 || lastFpsSampleTime < 0f)
        {
            BeginFpsSample();
            return;
        }

        float sampleTime = Time.unscaledTime;
        float elapsed = sampleTime - lastFpsSampleTime;
        if (elapsed < FpsSampleIntervalSeconds)
            return;

        int sampleFrame = Time.frameCount;
        ulong sampleTick = GameplayFixedStepSimulation.TickIndex;
        int displayedFps = Mathf.Max(
            0,
            Mathf.RoundToInt((sampleFrame - lastFpsSampleFrame) / elapsed));
        int displayedTps = Mathf.RoundToInt((sampleTick - lastTpsSampleTick) / elapsed);
        lastFpsSampleFrame = sampleFrame;
        lastTpsSampleTick = sampleTick;
        lastFpsSampleTime = sampleTime;
        if (displayedFps == lastDisplayedFps && displayedTps == lastDisplayedTps)
            return;

        lastDisplayedFps = displayedFps;
        lastDisplayedTps = displayedTps;
        fpsText.SetText(FpsFormat, displayedFps, displayedTps);
    }

    /// <summary>停止沿用旧采样窗口，避免重新显示时统计隐藏期间的帧数。</summary>
    private void InvalidateFpsSample()
    {
        lastFpsSampleFrame = -1;
        lastTpsSampleTick = 0;
        lastFpsSampleTime = -1f;
        lastDisplayedFps = -1;
        lastDisplayedTps = -1;
    }

    /// <summary>
    /// 将二维世界位置投影为经纬度。有限循环世界按实际边界映射到完整的 360°/180°；
    /// 无限世界以当前星球半径作为本地地理参考尺度，保证所有世界都能显示稳定读数。
    /// </summary>
    public static void GetLatitudeLongitude(
        Vector2 worldPosition,
        out float longitude,
        out float latitude)
    {
        if (WorldTopologyRuntime.TryGetActiveBounds(out WorldTopologyBounds bounds))
        {
            Vector2 normalized = bounds.NormalizePosition(worldPosition);
            longitude = Mathf.Lerp(
                -180f,
                180f,
                Mathf.InverseLerp(bounds.Min.x, bounds.MaxExclusive.x, normalized.x));
            latitude = Mathf.Lerp(
                -90f,
                90f,
                Mathf.InverseLerp(bounds.Min.y, bounds.MaxExclusive.y, normalized.y));
            return;
        }

        float radius = ResolveGeographicReferenceRadius();
        longitude = Mathf.Repeat(worldPosition.x / radius * 180f + 180f, 360f) - 180f;
        latitude = Mathf.Clamp(worldPosition.y / radius * 90f, -90f, 90f);
    }

    /// <summary>无限世界没有边界时，复用当前星球半径作为局部经纬度参考。</summary>
    private static float ResolveGeographicReferenceRadius()
    {
        SaveDataMgr saveDataManager = SaveDataMgr.Instance;
        if (saveDataManager != null &&
            saveDataManager.TryGetActivePlanetData(out PlanetData planetData) &&
            planetData != null)
        {
            return Mathf.Max(1f, planetData.Radius);
        }

        return PlanetData.DefaultRadius;
    }

    /// <summary>切换本地 HUD 根节点显隐。</summary>
    private void SetViewActive(bool active)
    {
        if (!active)
            InvalidateFpsSample();

        if (viewObject != null && viewObject.activeSelf != active)
            viewObject.SetActive(active);
    }

    #endregion

    #region 玩家资格与辅助

    /// <summary>缓存当前组件所属的玩家。</summary>
    private void ResolvePlayer()
    {
        player ??= GetComponent<Player>();
    }

    /// <summary>仅允许已启用的本地玩家维护屏幕 HUD。</summary>
    private bool CanDisplay()
    {
        return isActiveAndEnabled && player != null && player.IsLocalProfile;
    }

    /// <summary>玩家本地资格变化后重建 HUD 刷新状态。</summary>
    private void HandleProfileContextChanged()
    {
        RefreshForProfileContext();
    }

    /// <summary>显示偏好变化后立即刷新坐标格式与 FPS 显隐。</summary>
    private void HandleDisplayPreferenceChanged()
    {
        lastDisplayMode = (PlayerWorldCoordinateDisplayMode)(-1);
        if (CanDisplay())
            RefreshVisibility();
    }

    /// <summary>资格变化时只为本地玩家启动低频刷新循环。</summary>
    private void RefreshForProfileContext()
    {
        if (!CanDisplay())
        {
            StopRefreshLoop();
            SetViewActive(false);
            return;
        }

        RefreshVisibility();
        if (refreshCoroutine == null)
            refreshCoroutine = StartCoroutine(RefreshHudCoroutine());
    }

    /// <summary>以低频真实时间节拍刷新左上角动态信息。</summary>
    private IEnumerator RefreshHudCoroutine()
    {
        WaitForSecondsRealtime wait = new WaitForSecondsRealtime(RefreshIntervalSeconds);
        while (CanDisplay())
        {
            yield return wait;
            if (!CanDisplay())
                break;

            RefreshTickCount++;
            RefreshVisibility();
        }

        refreshCoroutine = null;
    }

    /// <summary>停止本地 HUD 的低频刷新循环。</summary>
    private void StopRefreshLoop()
    {
        if (refreshCoroutine == null)
            return;

        StopCoroutine(refreshCoroutine);
        refreshCoroutine = null;
    }

    #endregion
}
