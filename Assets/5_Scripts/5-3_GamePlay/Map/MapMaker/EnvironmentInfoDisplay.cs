// 新建文件：EnvironmentInfoDisplay.cs

using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 环境信息显示类，用于实时显示鼠标悬停位置的环境参数
/// </summary>
public class EnvironmentInfoDisplay : MonoBehaviour
{
    #region 单例

    public static EnvironmentInfoDisplay Instance { get; private set; }

    public static EnvironmentInfoDisplay EnsureInstance()
    {
        if (Instance != null)
            return Instance;

        EnvironmentInfoDisplay existing = FindObjectOfType<EnvironmentInfoDisplay>(true);
        if (existing != null)
            return existing;

        var go = new GameObject("EnvironmentInfoDisplaySingleton");
        var display = go.AddComponent<EnvironmentInfoDisplay>();
        DontDestroyOnLoad(go);
        return display;
    }

    #endregion

    #region 字段和属性
    
    [Header("显示设置")]
    public KeyCode toggleKey = KeyCode.F3;
    public Vector2 panelSize = new Vector2(300, 150);
    public Vector2 offset = new Vector2(20, 20);
    
    [Header("悬停指示器设置")]
    public Color hoverIndicatorColor = Color.white;
    public float hoverIndicatorThickness = 2f;
    
    [Header("样式设置")]
    public Color backgroundColor = new Color(0, 0, 0, 0.7f);
    public Color textColor = Color.white;
    public int fontSize = 12;
    
    // 引用
    private Camera mainCamera;
    private Grid mapGrid;
    private Tilemap targetTilemap;
    private Map map;
    [SerializeField]
    private bool showBiomeOverlay;
    
    // 显示控制
    private bool isVisible = true;
    private Vector3 mouseWorldPos = Vector3.zero;
    private Vector2 mouseScreenPos = Vector2.zero;
    private Vector2Int hoveredGridPos = Vector2Int.zero;
    private Vector2Int hoveredLocalPos = Vector2Int.zero;
    private string hoveredBiomeName = "未知";
    private TileData hoveredTileData = null;
    private bool isValidPosition = false;

    // 翻页控制
    private int currentPage = 0; // 0: 环境信息页, 1: 瓦片信息页
    
    // GUI样式
    private GUIStyle boxStyle;
    private GUIStyle labelStyle;
    private Texture2D backgroundTexture;
    private bool stylesCreated = false;
    private const int BiomeOverlayMaxSamples = 12000;
    
    #endregion

    #region Unity生命周期

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        DontDestroyOnLoad(gameObject);

        RefreshMapContextFromCurrentMap();
        
        isVisible = false;
    }

    private void Update()
    {
        // 更新鼠标屏幕位置
        mouseScreenPos = Input.mousePosition;
        
        // 更新鼠标位置信息
        UpdateMouseInfo();
        
        // 翻页：面板可见时可切换页面；数据使用最近一次有效悬停结果
        if (isVisible)
        {
            int maxPage = (hoveredTileData != null) ? 1 : 0; // 0 或 1

            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                currentPage--;
                if (currentPage < 0) currentPage = maxPage;
            }
            else if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                currentPage++;
                if (currentPage > maxPage) currentPage = 0;
            }

            // 防御性约束，避免 hoveredTileData 变为 null 时页码越界
            if (currentPage > maxPage) currentPage = maxPage;
        }
    }

    private void OnGUI()
    {
        if (!Application.isPlaying) return;

        if (!isVisible) return;

        // 仅在显示面板时绘制调试覆盖，避免大地图 OnGUI 过载
        DrawBiomeOverlay();

        DrawHoverIndicatorGUI();
        
        // 确保GUI样式已创建
        if (!stylesCreated)
        {
            CreateGUIStyles();
            stylesCreated = true;
        }
        
        DrawInfoPanel();
    }

    private void OnDrawGizmos()
    {
        // 不再使用OnDrawGizmos，改用Prefab实例
    }

    #endregion

    #region 核心功能

    /// <summary>
    /// 绘制生物群系颜色覆盖层（基于 EnvFactorsGrid + Biomes 现算颜色）
    /// </summary>
    private void DrawBiomeOverlay()
    {
        if (!showBiomeOverlay)
            return;

        ChunkGenerator_Land landGenerator = map?.LandGenerator;
        if (map == null || map.Data == null || landGenerator?.biomes == null || landGenerator.biomes.Count == 0)
            return;

        if (!TryGetEnvironmentGridSize(out int width, out int height))
            return;

        // 需要摄像机和Tilemap来进行坐标转换
        Camera cam = GetMainCamera();
        if (cam == null)
            return;

        Texture2D tex = GetOverlayPixelTexture();
        if (tex == null)
            return;

        const float size = 6f; // 颜色块尺寸（像素）
        int totalCellCount = width * height;
        int step = 1;
        if (totalCellCount > BiomeOverlayMaxSamples)
        {
            step = Mathf.CeilToInt(Mathf.Sqrt((float)totalCellCount / BiomeOverlayMaxSamples));
            step = Mathf.Max(1, step);
        }

        Vector2Int mapOrigin = map.Data.position;

        for (int xIndex = 0; xIndex < width; xIndex += step)
        {
            for (int yIndex = 0; yIndex < height; yIndex += step)
            {
                if (!TryGetEnvironmentAtLocal(xIndex, yIndex))
                    continue;

                // 根据环境匹配生物群系，获取预览颜色
                Vector2Int worldPosition = mapOrigin + new Vector2Int(xIndex, yIndex);
                if (!landGenerator.TryGetBiomeAtWorld(worldPosition, out BiomeData resolvedBiome))
                    continue;
                Color color = resolvedBiome.PreviewColor;

                int worldX = mapOrigin.x + xIndex;
                int worldY = mapOrigin.y + yIndex;

                // 如果有 Tilemap，则可选地检查是否有实际 Tile
                if (targetTilemap != null)
                {
                    Vector3Int cellPos = new Vector3Int(worldX, worldY, 0);
                    if (!targetTilemap.HasTile(cellPos))
                        continue;
                }

                // 将格子中心转换为屏幕坐标
                Vector3 worldPos = new Vector3(worldX + 0.5f, worldY + 0.5f,
                    targetTilemap != null ? targetTilemap.transform.position.z : 0f);
                Vector3 screenPos = cam.WorldToScreenPoint(worldPos);

                // 在相机视锥外则跳过
                if (screenPos.z < 0f)
                    continue;

                float guiX = screenPos.x - size * 0.5f;
                float guiY = Screen.height - screenPos.y - size * 0.5f; // OnGUI 的 Y 轴与屏幕坐标相反

                Rect rect = new Rect(guiX, guiY, size, size);

                Color oldColor = GUI.color;
                GUI.color = color;
                GUI.DrawTexture(rect, tex);
                GUI.color = oldColor;
            }
        }
    }

    private void RefreshMapContextFromCurrentMap()
    {
        if (map == null)
            return;

        targetTilemap = map.tileMap;
        if (targetTilemap == null)
        {
            targetTilemap = map.GetComponentInChildren<Tilemap>(includeInactive: true);
        }

        mapGrid = map.GetComponentInChildren<Grid>(includeInactive: true);

        var landGen = map.LandGenerator;
    }

    /// <summary>
    /// 使用 OnGUI 在悬停格子上绘制边框指示器
    /// </summary>
    private void DrawHoverIndicatorGUI()
    {
        if (!isVisible || !isValidPosition)
            return;

        if (mapGrid == null || targetTilemap == null)
            return;

        Camera cam = GetMainCamera();
        if (cam == null)
            return;

        Vector3Int cellPos = new Vector3Int(hoveredGridPos.x, hoveredGridPos.y, 0);
        Vector3 cellWorldPos = mapGrid.CellToWorld(cellPos);
        Vector3 cellSize = mapGrid.cellSize;

        Vector3 screenMin = cam.WorldToScreenPoint(new Vector3(cellWorldPos.x, cellWorldPos.y, 0f));
        Vector3 screenMax = cam.WorldToScreenPoint(new Vector3(cellWorldPos.x + cellSize.x, cellWorldPos.y + cellSize.y, 0f));

        if (screenMin.z < 0f || screenMax.z < 0f)
            return;

        float x = Mathf.Min(screenMin.x, screenMax.x);
        float y = Mathf.Min(Screen.height - screenMin.y, Screen.height - screenMax.y);
        float width = Mathf.Abs(screenMax.x - screenMin.x);
        float height = Mathf.Abs(screenMax.y - screenMin.y);

        if (width <= 0f || height <= 0f)
            return;

        Texture2D tex = GetOverlayPixelTexture();
        Color oldColor = GUI.color;
        GUI.color = hoverIndicatorColor;

        float line = Mathf.Max(1f, hoverIndicatorThickness);
        GUI.DrawTexture(new Rect(x, y, width, line), tex); // 上
        GUI.DrawTexture(new Rect(x, y + height - line, width, line), tex); // 下
        GUI.DrawTexture(new Rect(x, y, line, height), tex); // 左
        GUI.DrawTexture(new Rect(x + width - line, y, line, height), tex); // 右

        GUI.color = oldColor;
    }

    private static Texture2D overlayPixelTexture;

    private static Texture2D GetOverlayPixelTexture()
    {
        if (overlayPixelTexture == null)
        {
            overlayPixelTexture = new Texture2D(1, 1);
            overlayPixelTexture.hideFlags = HideFlags.HideAndDontSave;
            overlayPixelTexture.SetPixel(0, 0, Color.white);
            overlayPixelTexture.Apply();
        }
        return overlayPixelTexture;
    }

/// <summary>
/// 获取主摄像机
/// </summary>
private Camera GetMainCamera()
{
    if (mainCamera == null)
    {
        mainCamera = Camera.main;
        if (mainCamera == null)
        {
            mainCamera = GameObject.FindGameObjectWithTag("MainCamera")?.GetComponent<Camera>();
        }
        if (mainCamera == null)
        {
            // 如果还找不到，尝试从子对象获取
            Camera[] childCameras = GetComponentsInChildren<Camera>(true);
            if (childCameras != null && childCameras.Length > 0)
            {
                mainCamera = childCameras[0];
            }
        }
        if (mainCamera == null)
        {
            // 最后尝试查找场景中所有的摄像机
            Camera[] cameras = FindObjectsOfType<Camera>();
            if (cameras.Length > 0)
            {
                mainCamera = cameras[0];
            }
        }
    }
    return mainCamera;
}

/// <summary>
/// 更新鼠标位置的环境信息
/// </summary>
private void UpdateMouseInfo()
{
    // 获取摄像机
    Camera cam = GetMainCamera();

    // 前置检查
    if (cam == null)
    {
        isValidPosition = false;
        return;
    }

    // 1. 鼠标屏幕坐标 → 世界坐标
    Vector3 mouseScreenPos3D = new Vector3(mouseScreenPos.x, mouseScreenPos.y, 0);
    mouseScreenPos3D.z = Mathf.Abs(cam.transform.position.z);
    mouseWorldPos = cam.ScreenToWorldPoint(mouseScreenPos3D);
    mouseWorldPos.z = 0;

    // 2. 世界坐标 → 网格整数坐标（全局）
    Vector2Int gridPos = new Vector2Int(Mathf.FloorToInt(mouseWorldPos.x), Mathf.FloorToInt(mouseWorldPos.y));

    // 3. 通过全局 Chunk 接口获取当前地图
    if (ChunkMgr.Instance == null)
    {
        isValidPosition = false;
        return;
    }

    ChunkMgr.Instance.GetChunkBy_ItemPosition(gridPos, out Chunk chunk);
    if (chunk == null || chunk.Map == null)
    {
        isValidPosition = false;
        return;
    }

    if (map != chunk.Map)
    {
        map = chunk.Map;
        RefreshMapContextFromCurrentMap();
    }

    if (map == null || map.Data == null)
    {
        isValidPosition = false;
        return;
    }

    // 如果有Tilemap，补一层视觉存在性检查
    if (targetTilemap != null)
    {
        Vector3Int cellPos = new Vector3Int(gridPos.x, gridPos.y, 0);
        if (!targetTilemap.HasTile(cellPos))
        {
            isValidPosition = false;
            return;
        }
    }

    // 4. 计算本地坐标
    Vector2Int localGridPos = gridPos - map.Data.position;

    // 5. 检测是否在有效范围内

    if (!TryGetEnvironmentGridSize(out int width, out int height) ||
        localGridPos.x < 0 || localGridPos.x >= width ||
        localGridPos.y < 0 || localGridPos.y >= height)
    {
        isValidPosition = false;
        return;
    }

    // 6. 获取环境信息
    isValidPosition = true;
    hoveredGridPos = gridPos;
    if (!TryGetEnvironmentAtLocal(localGridPos.x, localGridPos.y))
    {
        isValidPosition = false;
        return;
    }

    hoveredLocalPos = localGridPos;
    hoveredTileData = map.GetTile(gridPos);
    
    // 匹配生物群系
    hoveredBiomeName = "未知";
    if (map.LandGenerator != null && map.LandGenerator.TryGetBiomeAtWorld(gridPos, out BiomeData resolvedBiome))
        hoveredBiomeName = resolvedBiome.BiomeName;
}

    /// <summary>
    /// 绘制信息面板
    /// </summary>
    private void DrawInfoPanel()
    {
        int totalPages = (hoveredTileData != null) ? 2 : 1;
        currentPage = Mathf.Clamp(currentPage, 0, totalPages - 1);

        // 预估需要的行数（用于动态计算高度），不同页内容不同
        int lineCount = 0;
        if (currentPage == 0)
        {
            // 标题、坐标、群系、温度、基础/最终降雨、风场、高度与水文
            lineCount += 9;
        }
        else
        {
            // 标题、坐标、瓦片名、移动权重
            lineCount += (hoveredTileData != null) ? 4 : 2;
        }

        // 底部统一添加：切换显示提示 + 页码提示
        lineCount += 2;

        float lineHeight = fontSize + 4;
        float panelHeight = Mathf.Max(panelSize.y, lineCount * lineHeight + 10);
        
        // 计算GUI位置（跟随鼠标，但保持在屏幕内）
        // 注意：GUI的Y轴是从上到下的，而鼠标坐标的Y轴是从下到上的
        float guiX = Mathf.Clamp(mouseScreenPos.x + offset.x, 0, Screen.width - panelSize.x);
        // 转换Y坐标：Screen.height - mouseScreenPos.y 将鼠标Y坐标转换为GUI坐标系
        float guiY = Mathf.Clamp(Screen.height - mouseScreenPos.y - panelHeight - offset.y, 0, Screen.height - panelHeight);
        
        // 绘制信息面板
        GUILayout.BeginArea(new Rect(guiX, guiY, panelSize.x, panelHeight), boxStyle);

        if (currentPage == 0)
        {
            // 第 1 页：环境因素
            float displayTempCelsius = GetDisplayTemperatureCelsius(hoveredLocalPos.x, hoveredLocalPos.y);
            float temperature = map.Data.EnvironmentLayers.Temperature[hoveredLocalPos.x, hoveredLocalPos.y];
            float precipitation = map.Data.EnvironmentLayers.Precipitation[hoveredLocalPos.x, hoveredLocalPos.y];
            float height = map.Data.EnvironmentLayers.Height[hoveredLocalPos.x, hoveredLocalPos.y];
            Vector2 wind = map.Data.EnvironmentLayers.GetWind(hoveredLocalPos.x, hoveredLocalPos.y);
            float basePrecipitation = precipitation;
            int baseSeed = SaveDataMgr.Instance?.SaveData?.Seed ?? 1;
            DimensionManager dimensionManager = DimensionManager.Instance;
            int worldSeed = dimensionManager != null
                ? dimensionManager.GetActiveGenerationSeed(baseSeed)
                : baseSeed;
            if (map.LandGenerator != null)
            {
                ClimateSample climate = map.LandGenerator.SampleClimateAtWorld(
                    hoveredGridPos,
                    worldSeed,
                    SaveDataMgr.Instance?.GetCurrentPlanetData());
                basePrecipitation = climate.BasePrecipitation;
            }

            HydrologyCellSample hydrology = default;
            map.GetGenerator<ChunkGenerator_River>()?.TrySampleHydrologyCell(
                hoveredGridPos,
                worldSeed,
                out hydrology);
            float windAngle = Mathf.Atan2(wind.y, wind.x) * Mathf.Rad2Deg;

            GUILayout.Label($"<b>环境信息</b>", labelStyle);
            GUILayout.Label($"坐标: ({hoveredGridPos.x}, {hoveredGridPos.y})", labelStyle);
            GUILayout.Label($"生物群系: {hoveredBiomeName}", labelStyle);
            GUILayout.Label($"温度: {temperature:F2} ({displayTempCelsius:F1}℃)", labelStyle);
            GUILayout.Label($"基础降水: {basePrecipitation:F2}", labelStyle);
            GUILayout.Label($"最终降雨: {precipitation:F2}", labelStyle);
            GUILayout.Label($"风向: ({wind.x:F2}, {wind.y:F2}) {windAngle:F0}°", labelStyle);
            GUILayout.Label($"高度: {height:F2}", labelStyle);
            GUILayout.Label(
                $"水文: {hydrology.WaterKind}  汇流 {hydrology.Flow:F2}  水深 {hydrology.Depth:F2}",
                labelStyle);
        }
        else
        {
            // 第 2 页：瓦片信息
            GUILayout.Label($"<b>瓦片信息</b>", labelStyle);
            GUILayout.Label($"坐标: ({hoveredGridPos.x}, {hoveredGridPos.y})", labelStyle);

            if (hoveredTileData != null)
            {
                GUILayout.Label($"瓦片: {hoveredTileData.Name}", labelStyle);
                GUILayout.Label($"移动权重: {hoveredTileData.Penalty}", labelStyle);
            }
        }

        // 底部通用提示
        GUILayout.Label($"按 {toggleKey} 键切换显示", labelStyle);
        if (totalPages > 1)
        {
            GUILayout.Label($"第 {currentPage + 1}/{totalPages} 页（↑↓ 翻页）", labelStyle);
        }
        
        GUILayout.EndArea();
    }

    private float GetDisplayTemperatureCelsius(int x, int y)
    {
        if (map == null || map.Data == null || map.Data.EnvironmentLayers == null)
            return 0f;

        if (!map.Data.EnvironmentLayers.Contains(x, y))
            return 0f;

        return map.Data.EnvironmentLayers.TemperatureCelsius[x, y];
    }

    /// <summary>
    /// 创建GUI样式
    /// </summary>
    private void CreateGUIStyles()
    {
        // 创建背景纹理
        backgroundTexture = new Texture2D(2, 2);
        Color[] colors = new Color[4];
        for (int i = 0; i < colors.Length; i++)
        {
            colors[i] = backgroundColor;
        }
        backgroundTexture.SetPixels(colors);
        backgroundTexture.Apply();
        
        // 创建Box样式
        boxStyle = new GUIStyle(GUI.skin.box);
        boxStyle.normal.background = backgroundTexture;
        
        // 创建Label样式
        labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.normal.textColor = textColor;
        labelStyle.fontSize = fontSize;
        labelStyle.richText = true;
    }

    #endregion

    #region 公共方法

    /// <summary>
    /// 显示信息面板
    /// </summary>
    public void Show()
    {
        isVisible = true;
    }

    /// <summary>
    /// 隐藏信息面板
    /// </summary>
    public void Hide()
    {
        isVisible = false;
    }

    /// <summary>
    /// 切换显示状态
    /// </summary>
    public void Toggle()
    {
        isVisible = !isVisible;
    }

    /// <summary>
    /// 设置切换键
    /// </summary>
    public void SetToggleKey(KeyCode key)
    {
        toggleKey = key;
    }

    #endregion

    #region 清理

private void OnDestroy()
{
    if (Instance == this)
    {
        Instance = null;
    }

    if (backgroundTexture != null)
    {
        Destroy(backgroundTexture);
    }
    
    // 销毁GUI样式
    if (boxStyle != null && boxStyle.normal.background != null)
    {
        Destroy(boxStyle.normal.background);
    }
}

    #endregion

private bool TryGetEnvironmentGridSize(out int width, out int height)
{
    width = 0;
    height = 0;

    if (map == null || map.Data == null)
        return false;

    EnvironmentLayers layers = map.Data.EnvironmentLayers;
    if (layers != null && layers.Width > 0 && layers.GridHeight > 0)
    {
        width = layers.Width;
        height = layers.GridHeight;
        return true;
    }

    return false;
}

private bool TryGetEnvironmentAtLocal(int x, int y)
{
    if (map == null || map.Data == null)
        return false;

    return map.Data.IsEnvironmentLocalValid(x, y);
}

}
