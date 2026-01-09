// 新建文件：EnvironmentInfoDisplay.cs

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 环境信息显示类，用于实时显示鼠标悬停位置的环境参数
/// </summary>
public class EnvironmentInfoDisplay : MonoBehaviour
{
    #region 字段和属性
    
    [Header("显示设置")]
    public KeyCode toggleKey = KeyCode.F3;
    public Vector2 panelSize = new Vector2(300, 150);
    public Vector2 offset = new Vector2(20, 20);
    
    [Header("Prefab设置")]
    public GameObject hoverIndicatorPrefab; // 悬停指示器Prefab
    private GameObject hoverIndicatorInstance; // 悬停指示器实例
    
    [Header("样式设置")]
    public Color backgroundColor = new Color(0, 0, 0, 0.7f);
    public Color textColor = Color.white;
    public int fontSize = 12;
    
    // 引用
    private Camera mainCamera;
    private Grid mapGrid;
    private Tilemap targetTilemap;
    private Map map;
    private List<BiomeData> biomes;
    private bool showBiomeOverlay;
    
    // 显示控制
    private bool isVisible = true;
    private Vector3 mouseWorldPos = Vector3.zero;
    private Vector2 mouseScreenPos = Vector2.zero;
    private Vector2Int hoveredGridPos = Vector2Int.zero;
    private EnvironmentFactors hoveredEnvFactors = new EnvironmentFactors();
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
    
    #endregion

    #region Unity生命周期

    private void Awake()
    {
        // 获取必要的引用
        map = GetComponent<Map>();
        if (map != null)
        {
            // Tilemap / Grid 优先从 Map 本体取，避免依赖生成器
            targetTilemap = map.tileMap;
            if (targetTilemap == null)
            {
                targetTilemap = map.GetComponentInChildren<Tilemap>(includeInactive: true);
            }

            mapGrid = map.GetComponentInChildren<Grid>(includeInactive: true);

            // 生物群系与调试开关：仅作为“配置来源”在 Awake 缓存一次
            // 如果未来你想完全移除生成器依赖，可把这些配置移动到 Map 或独立 ScriptableObject。
            var landGen = map.LandGenerator;
            biomes = landGen != null ? landGen.biomes : null;
            showBiomeOverlay = landGen != null && landGen.showBiomeOverlay;
        }
        
        // 创建悬停指示器实例
        CreateHoverIndicator();

        isVisible = false;
    }

    private void Update()
    {
        // 切换显示状态
        if (Input.GetKeyDown(toggleKey))
        {
            isVisible = !isVisible;
        }
        
        // 更新鼠标屏幕位置
        mouseScreenPos = Input.mousePosition;
        
        // 更新鼠标位置信息
        UpdateMouseInfo();
        
        // 更新悬停指示器位置
        UpdateHoverIndicator();

        // 翻页：仅在面板可见且当前有有效位置时处理
        if (isVisible && isValidPosition)
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
        else
        {
            // 无效位置或不可见时重置到第一页
            currentPage = 0;
        }
    }

    private void OnGUI()
    {
        if (!Application.isPlaying) return;

        // 先绘制生物群系覆盖调试（独立于信息面板显示开关）
        DrawBiomeOverlay();

        if (!isVisible) return;
        
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
    /// 创建悬停指示器
    /// </summary>
    private void CreateHoverIndicator()
    {
        if (hoverIndicatorPrefab != null && mapGrid != null)
        {
            hoverIndicatorInstance = Instantiate(hoverIndicatorPrefab);
            hoverIndicatorInstance.SetActive(false); // 初始隐藏
        }
    }

    /// <summary>
    /// 绘制生物群系颜色覆盖层（基于 EnvFactorsGrid + Biomes 现算颜色）
    /// </summary>
    private void DrawBiomeOverlay()
    {
        if (!showBiomeOverlay)
            return;

        if (map == null || map.Data == null || map.Data.EnvironmentData == null || biomes == null || biomes.Count == 0)
            return;

        // 需要摄像机和Tilemap来进行坐标转换
        Camera cam = GetMainCamera();
        if (cam == null)
            return;

        Texture2D tex = GetOverlayPixelTexture();
        if (tex == null)
            return;

        const float size = 6f; // 颜色块尺寸（像素）

        var envGrid = map.Data.EnvironmentData;
        int width = envGrid.GetLength(0);
        int height = envGrid.GetLength(1);
        Vector2Int mapOrigin = map.Data.position;

        for (int xIndex = 0; xIndex < width; xIndex++)
        {
            for (int yIndex = 0; yIndex < height; yIndex++)
            {
                EnvironmentFactors env = envGrid[xIndex, yIndex];

                // 根据环境匹配生物群系，获取预览颜色
                Color color = Color.clear;
                bool found = false;
                foreach (var biome in biomes)
                {
                    if (biome != null && biome.IsEnvironmentValid(env))
                    {
                        color = biome.PreviewColor;
                        found = true;
                        break;
                    }
                }

                if (!found)
                    continue;

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

    /// <summary>
    /// 更新悬停指示器位置
    /// </summary>
    private void UpdateHoverIndicator()
    {
        if (hoverIndicatorInstance == null) return;
        
        if (isValidPosition && isVisible && mapGrid != null)
        {
            hoverIndicatorInstance.SetActive(true);
            
            // 将世界坐标对齐到网格
            Vector3Int cellPos = mapGrid.WorldToCell(mouseWorldPos);
            Vector3 cellWorldPos = mapGrid.CellToWorld(cellPos);
            
            // 调整位置到格子中心
            cellWorldPos.x += 0.5f;
            cellWorldPos.y += 0.5f;
            cellWorldPos.z = 0; // 确保在最上层显示
            
            hoverIndicatorInstance.transform.position = cellWorldPos;
        }
        else
        {
            hoverIndicatorInstance.SetActive(false);
        }
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
    
    // 如果还没有mapGrid和targetTilemap，尝试从子对象获取
    if (mapGrid == null)
    {
        mapGrid = GetComponentInChildren<Grid>(true);
    }
    
    if (targetTilemap == null)
    {
        Tilemap[] childTilemaps = GetComponentsInChildren<Tilemap>(true);
        if (childTilemaps != null && childTilemaps.Length > 0)
        {
            targetTilemap = childTilemaps[0];
        }
    }
    
    // 前置检查
    if (mapGrid == null || targetTilemap == null || cam == null)
        return;

    // 1. 鼠标屏幕坐标 → 世界坐标
    Vector3 mouseScreenPos3D = new Vector3(mouseScreenPos.x, mouseScreenPos.y, 0);
    mouseScreenPos3D.z = Mathf.Abs(cam.transform.position.z - targetTilemap.transform.position.z);
    mouseWorldPos = cam.ScreenToWorldPoint(mouseScreenPos3D);
    mouseWorldPos.z = 0;

    // 2. 世界坐标 → Tilemap格子坐标
    Vector3Int cellPos = mapGrid.WorldToCell(mouseWorldPos);
    Vector2Int gridPos = new Vector2Int(cellPos.x, cellPos.y);

    // 3. 检查该格子是否存在Tile
    if (!targetTilemap.HasTile(cellPos))
    {
        isValidPosition = false;
        return;
    }

    // 4. 计算本地坐标
    Vector2Int localGridPos = gridPos - map.Data.position;

    // 5. 检测是否在有效范围内
    if (map == null || map.Data == null || map.Data.EnvironmentData == null ||
        localGridPos.x < 0 || localGridPos.x >= map.Data.EnvironmentData.GetLength(0) ||
        localGridPos.y < 0 || localGridPos.y >= map.Data.EnvironmentData.GetLength(1))
    {
        isValidPosition = false;
        return;
    }

    // 6. 获取环境信息
    isValidPosition = true;
    hoveredGridPos = gridPos;
    hoveredEnvFactors = map.Data.EnvironmentData[localGridPos.x, localGridPos.y];
    hoveredTileData = map.GetTile(gridPos);
    
    // 匹配生物群系
    hoveredBiomeName = "未知";
    if (biomes != null)
    {
        foreach (var biome in biomes)
        {
            if (biome != null && biome.IsEnvironmentValid(hoveredEnvFactors))
            {
                hoveredBiomeName = biome.BiomeName;
                break;
            }
        }
    }
}

    /// <summary>
    /// 绘制信息面板
    /// </summary>
    private void DrawInfoPanel()
    {
        if (!isValidPosition) return;

        // 当前总页数：有瓦片数据时为 2 页，否则为 1 页
        int totalPages = (hoveredTileData != null) ? 2 : 1;
        currentPage = Mathf.Clamp(currentPage, 0, totalPages - 1);

        // 预估需要的行数（用于动态计算高度），不同页内容不同
        int lineCount = 0;
        if (currentPage == 0)
        {
            // 标题、坐标、生物群系、温湿、降水坚固、高度
            lineCount += 6;
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
            GUILayout.Label($"<b>环境信息</b>", labelStyle);
            GUILayout.Label($"坐标: ({hoveredGridPos.x}, {hoveredGridPos.y})", labelStyle);
            GUILayout.Label($"生物群系: {hoveredBiomeName}", labelStyle);
            GUILayout.Label($"温度: {hoveredEnvFactors.Temperature:F2} | 湿度: {hoveredEnvFactors.Humidity:F2}", labelStyle);
            GUILayout.Label($"降水量: {hoveredEnvFactors.Precipitation:F2} | 坚固度: {hoveredEnvFactors.Solidity:F2}", labelStyle);
            GUILayout.Label($"高度: {hoveredEnvFactors.Hight:F2}", labelStyle);
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
    if (backgroundTexture != null)
    {
        Destroy(backgroundTexture);
    }
    
    if (hoverIndicatorInstance != null)
    {
        Destroy(hoverIndicatorInstance);
    }
    
    // 销毁GUI样式
    if (boxStyle != null && boxStyle.normal.background != null)
    {
        Destroy(boxStyle.normal.background);
    }
}

    #endregion
}