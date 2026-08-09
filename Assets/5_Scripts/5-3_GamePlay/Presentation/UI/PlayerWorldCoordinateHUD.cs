using TMPro;
using UnityEngine;

/// <summary>
/// 为本地玩家维护一个屏幕左上角的世界坐标 HUD。
/// 仅实例化已制作好的 UI_PlayerWorldCoordinate Prefab；每帧在角色移动完成后刷新世界坐标或经纬度。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Player))]
public sealed class PlayerWorldCoordinateHUD : MonoBehaviour
{
    #region 常量与运行时状态

    public const string ViewName = "PlayerWorldCoordinateHUD";

    private const string CoordinateTitleNodeName = "坐标标题";
    private const string CoordinateTextNodeName = "坐标文本";
    private const string CoordinateFormat = "X  {0:0.0}    Y  {1:0.0}";
    private const string GeographicFormat = "经 {0:0.00}°  纬 {1:0.00}°";
    private const string CoordinateTitle = "世界坐标 / POSITION";
    private const string GeographicTitle = "地理坐标 / GEO POSITION";

    private Player player;
    private GameObject viewObject;
    private RectTransform viewRect;
    private TextMeshProUGUI coordinateTitle;
    private TextMeshProUGUI coordinateText;
    private int lastCoordinateX = int.MinValue;
    private int lastCoordinateY = int.MinValue;
    private PlayerWorldCoordinateDisplayMode lastDisplayMode =
        (PlayerWorldCoordinateDisplayMode)(-1);
    private bool missingPrefabLogged;

    #endregion

    #region Unity 生命周期

    private void Awake()
    {
        ResolvePlayer();
    }

    private void OnEnable()
    {
        ResolvePlayer();
        if (player != null)
            player.ProfileContextChanged += HandleProfileContextChanged;

        RefreshVisibility();
    }

    /// <summary>在移动与物理更新结束后刷新，保证展示的是玩家本帧最终坐标。</summary>
    private void LateUpdate()
    {
        RefreshVisibility();
    }

    private void OnDisable()
    {
        if (player != null)
            player.ProfileContextChanged -= HandleProfileContextChanged;

        SetViewActive(false);
    }

    private void OnDestroy()
    {
        if (viewObject != null)
            Destroy(viewObject);
    }

    #endregion

    #region HUD 刷新

    /// <summary>仅本地玩家创建并显示坐标 HUD，远端玩家不会重复生成界面。</summary>
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

            return coordinateText != null;
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
        Transform titleNode = viewObject.transform.Find(CoordinateTitleNodeName);
        Transform textNode = viewObject.transform.Find(CoordinateTextNodeName);
        coordinateTitle = titleNode != null ? titleNode.GetComponent<TextMeshProUGUI>() : null;
        coordinateText = textNode != null ? textNode.GetComponent<TextMeshProUGUI>() : null;
        if (viewRect == null || coordinateTitle == null || coordinateText == null)
        {
            Debug.LogError("[PlayerWorldCoordinateHUD] 坐标 HUD Prefab 控件命名契约不完整。", viewObject);
            Destroy(viewObject);
            viewObject = null;
            viewRect = null;
            coordinateTitle = null;
            coordinateText = null;
            return false;
        }

        viewRect.SetAsFirstSibling();
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
            coordinateTitle.text = GeographicTitle;
            coordinateText.SetText(GeographicFormat, longitude, latitude);
            return;
        }

        coordinateTitle.text = CoordinateTitle;
        coordinateText.SetText(CoordinateFormat, coordinateX * 0.1f, coordinateY * 0.1f);
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

    private void SetViewActive(bool active)
    {
        if (viewObject != null && viewObject.activeSelf != active)
            viewObject.SetActive(active);
    }

    #endregion

    #region 玩家资格与辅助

    private void ResolvePlayer()
    {
        player ??= GetComponent<Player>();
    }

    private bool CanDisplay()
    {
        return isActiveAndEnabled && player != null && player.IsLocalProfile;
    }

    private void HandleProfileContextChanged()
    {
        RefreshVisibility();
    }

    #endregion
}
