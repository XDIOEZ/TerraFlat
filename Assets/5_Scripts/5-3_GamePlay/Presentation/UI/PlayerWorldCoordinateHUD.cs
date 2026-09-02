using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// 为本地玩家维护一个屏幕左上角的坐标与 FPS 信息 HUD。
/// 仅实例化已制作好的 UI_PlayerWorldCoordinate Prefab；坐标以 10Hz 刷新，FPS 以 0.5 秒窗口采样，远端玩家不启动轮询。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Player))]
public sealed class PlayerWorldCoordinateHUD : MonoBehaviour
{
    #region 常量与运行时状态

    public const string ViewName = "PlayerWorldCoordinateHUD";

    private const string CoordinateTextNodeName = "坐标文本";
    private const string FpsTextNodeName = "FPS文本";
    private const string CoordinateFormat = "X  {0:0.0}    Y  {1:0.0}";
    private const string GeographicFormat = "经 {0:0.00}°  纬 {1:0.00}°";
    private const string FpsFormat = "FPS  {0:0}";
    private const float RefreshIntervalSeconds = 0.1f;
    private const float FpsSampleIntervalSeconds = 0.5f;

    private Player player;
    private GameObject viewObject;
    private RectTransform viewRect;
    private TextMeshProUGUI coordinateText;
    private TextMeshProUGUI fpsText;
    private int lastCoordinateX = int.MinValue;
    private int lastCoordinateY = int.MinValue;
    private int lastFpsSampleFrame = -1;
    private int lastDisplayedFps = -1;
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

        RefreshForProfileContext();
    }

    /// <summary>解除事件与刷新循环，并隐藏 HUD。</summary>
    private void OnDisable()
    {
        if (player != null)
            player.ProfileContextChanged -= HandleProfileContextChanged;
        PlayerWorldCoordinateDisplayPreferences.Changed -= HandleDisplayPreferenceChanged;

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

            return coordinateText != null && fpsText != null;
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
        if (viewRect == null || coordinateText == null || fpsText == null)
        {
            Debug.LogError("[PlayerWorldCoordinateHUD] 左上角信息 HUD Prefab 控件命名契约不完整。", viewObject);
            Destroy(viewObject);
            viewObject = null;
            viewRect = null;
            coordinateText = null;
            fpsText = null;
            return false;
        }

        viewRect.SetAsFirstSibling();
        lastCoordinateX = int.MinValue;
        lastCoordinateY = int.MinValue;
        lastDisplayMode = (PlayerWorldCoordinateDisplayMode)(-1);
        InvalidateFpsSample();
        return true;
    }

    /// <summary>仅在显示值改变时写入 TMP，避免静止状态产生无效刷新和字符串分配。</summary>
    private void RefreshCoordinateText()
    {
        Vector3 position = player.transform.position;
        int coordinateX = Mathf.RoundToInt(position.x * 10f);
        int coordinateY = Mathf.RoundToInt(position.y * 10f);
        PlayerWorldCoordinateDisplayMode displayMode =
            PlayerWorldCoordinateDisplayPreferences.Mode;
        if (coordinateX == lastCoordinateX && coordinateY == lastCoordinateY &&
            displayMode == lastDisplayMode)
            return;

        lastCoordinateX = coordinateX;
        lastCoordinateY = coordinateY;
        lastDisplayMode = displayMode;

        if (displayMode == PlayerWorldCoordinateDisplayMode.LatitudeLongitude)
        {
            GetLatitudeLongitude(position, out float longitude, out float latitude);
            coordinateText.SetText(GeographicFormat, longitude, latitude);
            return;
        }

        coordinateText.SetText(CoordinateFormat, coordinateX * 0.1f, coordinateY * 0.1f);
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

    /// <summary>以当前帧和真实时间作为下一个 FPS 采样窗口的起点。</summary>
    private void BeginFpsSample()
    {
        lastFpsSampleFrame = Time.frameCount;
        lastFpsSampleTime = Time.unscaledTime;
        lastDisplayedFps = -1;
        fpsText.SetText("FPS  --");
    }

    /// <summary>按固定真实时间窗口计算 FPS，仅在整数显示值变化时写入 TMP。</summary>
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
        int displayedFps = Mathf.Max(
            0,
            Mathf.RoundToInt((sampleFrame - lastFpsSampleFrame) / elapsed));
        lastFpsSampleFrame = sampleFrame;
        lastFpsSampleTime = sampleTime;
        if (displayedFps == lastDisplayedFps)
            return;

        lastDisplayedFps = displayedFps;
        fpsText.SetText(FpsFormat, displayedFps);
    }

    /// <summary>停止沿用旧采样窗口，避免重新显示时统计隐藏期间的帧数。</summary>
    private void InvalidateFpsSample()
    {
        lastFpsSampleFrame = -1;
        lastFpsSampleTime = -1f;
        lastDisplayedFps = -1;
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
