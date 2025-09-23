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
    private RandomMapGenerator mapGenerator;
    private Camera mainCamera;
    private Grid mapGrid;
    private Tilemap targetTilemap;
    private Map map;
    private List<BiomeData> biomes;
    
    // 显示控制
    private bool isVisible = true;
    private Vector3 mouseWorldPos = Vector3.zero;
    private Vector2 mouseScreenPos = Vector2.zero;
    private Vector2Int hoveredGridPos = Vector2Int.zero;
    private EnvironmentFactors hoveredEnvFactors = new EnvironmentFactors();
    private string hoveredBiomeName = "未知";
    private TileData hoveredTileData = null;
    private bool isValidPosition = false;
    
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
        mapGenerator = GetComponent<RandomMapGenerator>();
        map = GetComponent<Map>();
        if (mapGenerator != null)
        {
            map = mapGenerator.map;
            mapGrid = mapGenerator.mapGrid;
            targetTilemap = mapGenerator.targetTilemap;
            biomes = mapGenerator.biomes;
        }
        
        // 创建悬停指示器实例
        CreateHoverIndicator();
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
    }

    private void OnGUI()
    {
        if (!Application.isPlaying || !isVisible) return;
        
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
    if (mapGenerator == null || mapGenerator.EnvFactorsGrid == null ||
        localGridPos.x < 0 || localGridPos.x >= mapGenerator.EnvFactorsGrid.GetLength(0) ||
        localGridPos.y < 0 || localGridPos.y >= mapGenerator.EnvFactorsGrid.GetLength(1))
    {
        isValidPosition = false;
        return;
    }

    // 6. 获取环境信息
    isValidPosition = true;
    hoveredGridPos = gridPos;
    hoveredEnvFactors = mapGenerator.EnvFactorsGrid[localGridPos.x, localGridPos.y];
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
        
        // 计算GUI位置（跟随鼠标，但保持在屏幕内）
        // 注意：GUI的Y轴是从上到下的，而鼠标坐标的Y轴是从下到上的
        float guiX = Mathf.Clamp(mouseScreenPos.x + offset.x, 0, Screen.width - panelSize.x);
        // 转换Y坐标：Screen.height - mouseScreenPos.y 将鼠标Y坐标转换为GUI坐标系
        float guiY = Mathf.Clamp(Screen.height - mouseScreenPos.y - panelSize.y - offset.y, 0, Screen.height - panelSize.y);
        
        // 绘制信息面板
        GUILayout.BeginArea(new Rect(guiX, guiY, panelSize.x, panelSize.y), boxStyle);
        
        GUILayout.Label($"<b>环境信息</b>", labelStyle);
        GUILayout.Label($"坐标: ({hoveredGridPos.x}, {hoveredGridPos.y})", labelStyle);
        GUILayout.Label($"生物群系: {hoveredBiomeName}", labelStyle);
        GUILayout.Label($"温度: {hoveredEnvFactors.Temperature:F2} | 湿度: {hoveredEnvFactors.Humidity:F2}", labelStyle);
        GUILayout.Label($"降水量: {hoveredEnvFactors.Precipitation:F2} | 坚固度: {hoveredEnvFactors.Solidity:F2}", labelStyle);
        GUILayout.Label($"高度: {hoveredEnvFactors.Hight:F2}", labelStyle);
        if (hoveredTileData != null)
        {
            GUILayout.Label($"瓦片: {hoveredTileData.Name_ItemName}", labelStyle);
        }
        
        GUILayout.Label($"按 {toggleKey} 键切换显示", labelStyle);
        
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
    }

    #endregion
}