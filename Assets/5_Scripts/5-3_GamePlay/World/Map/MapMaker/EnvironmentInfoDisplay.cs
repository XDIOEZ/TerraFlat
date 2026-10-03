using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// F3 环境监测面板：直接读取当前 WorldModel 已加载区块的权威地形数据，
/// 跟随鼠标显示格子、气候、水文、液体、农业状态以及全部原始环境层。
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

    #region 显示设置

    [Header("显示设置")]
    public KeyCode toggleKey = KeyCode.F3;
    public Vector2 panelSize = new(560f, 240f);
    public Vector2 offset = new(20f, 20f);

    [Header("悬停指示器设置")]
    public Color hoverIndicatorColor = Color.white;
    public float hoverIndicatorThickness = 2f;

    [Header("样式设置")]
    public Color backgroundColor = new(0f, 0f, 0f, 0.78f);
    public Color textColor = Color.white;
    public int fontSize = 16;

    #endregion

    #region 运行时状态

    private readonly List<string> environmentLayerIds = new(24);
    private readonly List<string> rawDataLines = new(48);
    private Camera mainCamera;
    private RuntimeTerrainTileSample hoveredSample;
    private Vector2 mouseScreenPos;
    private Vector3 mouseWorldPos;
    private Vector2Int hoveredVisualGridPos;
    private string hoveredBiomeName = "未知";
    private bool isVisible;
    private bool isValidPosition;
    private int currentPage;

    private GUIStyle boxStyle;
    private GUIStyle labelStyle;
    private Texture2D backgroundTexture;
    private bool stylesCreated;
    private static Texture2D overlayPixelTexture;

    #endregion

    #region Unity 生命周期

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        isVisible = false;
    }

    private void Update()
    {
        Pointer pointer = Pointer.current;
        if (pointer == null)
        {
            isValidPosition = false;
            return;
        }

        mouseScreenPos = pointer.position.ReadValue();
        UpdateMouseInfo();

        if (!isVisible)
            return;

        int maxPage = 0;
        if (TryGetValidHoveredSample(out RuntimeTerrainTileSample sample))
        {
            RefreshRawDataLines(sample);
            maxPage = GetTotalPageCount(GetRawRowsPerPage()) - 1;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard?.upArrowKey.wasPressedThisFrame == true)
        {
            currentPage--;
            if (currentPage < 0)
                currentPage = maxPage;
        }
        else if (keyboard?.downArrowKey.wasPressedThisFrame == true)
        {
            currentPage++;
            if (currentPage > maxPage)
                currentPage = 0;
        }

        currentPage = Mathf.Clamp(currentPage, 0, maxPage);
    }

    private void OnGUI()
    {
        if (!Application.isPlaying || !isVisible)
            return;

        if (!stylesCreated)
        {
            CreateGUIStyles();
            stylesCreated = true;
        }

        DrawHoverIndicatorGUI();
        DrawInfoPanel();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        if (backgroundTexture != null)
            Destroy(backgroundTexture);
    }

    #endregion

    #region 鼠标采样

    /// <summary>把当前指针投影到世界 Z=0 平面，并读取新版区块的权威单格数据。</summary>
    private void UpdateMouseInfo()
    {
        Camera cam = GetMainCamera();
        ChunkMgr chunkManager = ChunkMgr.ExistingInstance;
        if (cam == null || chunkManager == null)
        {
            isValidPosition = false;
            return;
        }

        Ray ray = cam.ScreenPointToRay(mouseScreenPos);
        var worldPlane = new Plane(Vector3.forward, Vector3.zero);
        if (!worldPlane.Raycast(ray, out float distance))
        {
            isValidPosition = false;
            return;
        }

        mouseWorldPos = ray.GetPoint(distance);
        mouseWorldPos.z = 0f;
        hoveredVisualGridPos = new Vector2Int(
            Mathf.FloorToInt(mouseWorldPos.x),
            Mathf.FloorToInt(mouseWorldPos.y));

        if (!chunkManager.TryGetRuntimeTerrainTile(mouseWorldPos, out RuntimeTerrainTileSample sample))
        {
            isValidPosition = false;
            return;
        }

        hoveredSample = sample;
        hoveredBiomeName = chunkManager.TryGetRuntimeBiomeName(mouseWorldPos, out string biomeName)
            ? biomeName
            : $"Biome#{sample.Cell.BiomeId}";
        isValidPosition = true;
    }

    /// <summary>确认 Update 与 OnGUI 之间区块没有被卸载。</summary>
    private bool TryGetValidHoveredSample(out RuntimeTerrainTileSample sample)
    {
        sample = hoveredSample;
        if (!isValidPosition || sample.Terrain == null || sample.Terrain.IsDisposed)
            return false;

        Vector2Int local = sample.LocalCell;
        return (uint)local.x < (uint)sample.Terrain.Width &&
               (uint)local.y < (uint)sample.Terrain.Height;
    }

    private Camera GetMainCamera()
    {
        if (mainCamera != null)
            return mainCamera;

        mainCamera = Camera.main;
        if (mainCamera != null)
            return mainCamera;

        GameObject taggedCamera = GameObject.FindGameObjectWithTag("MainCamera");
        if (taggedCamera != null)
            mainCamera = taggedCamera.GetComponent<Camera>();

        if (mainCamera == null)
        {
            Camera[] cameras = FindObjectsOfType<Camera>();
            if (cameras.Length > 0)
                mainCamera = cameras[0];
        }

        return mainCamera;
    }

    #endregion

    #region 面板绘制

    /// <summary>绘制跟随鼠标的调试面板；默认位于指针右下方，靠近屏幕边缘时自动收回屏内。</summary>
    private void DrawInfoPanel()
    {
        bool hasSample = TryGetValidHoveredSample(out RuntimeTerrainTileSample sample);
        int rawRowsPerPage = 1;
        int totalPages = 1;
        if (hasSample)
        {
            RefreshRawDataLines(sample);
            rawRowsPerPage = GetRawRowsPerPage();
            totalPages = GetTotalPageCount(rawRowsPerPage);
        }

        currentPage = Mathf.Clamp(currentPage, 0, totalPages - 1);

        int lineCount;
        if (!hasSample)
        {
            lineCount = 5;
        }
        else if (currentPage == 0)
        {
            lineCount = EstimateOverviewLineCount(sample);
        }
        else
        {
            int rawPageIndex = currentPage - 1;
            int startLine = rawPageIndex * rawRowsPerPage;
            int visibleLines = Mathf.Min(rawRowsPerPage, rawDataLines.Count - startLine);
            lineCount = 1 + Mathf.Max(0, visibleLines);
        }

        float panelWidth = Mathf.Min(Mathf.Max(320f, panelSize.x), Mathf.Max(1f, Screen.width));
        float lineHeight = Mathf.Max(fontSize + 6f, 20f);
        float wantedHeight = Mathf.Max(panelSize.y, (lineCount + (hasSample ? 1 : 0)) * lineHeight + 20f);
        float panelHeight = Mathf.Min(wantedHeight, Mathf.Max(1f, Screen.height - 4f));

        float desiredX = mouseScreenPos.x + offset.x;
        float desiredY = Screen.height - mouseScreenPos.y + offset.y;
        float guiX = Mathf.Clamp(desiredX, 0f, Mathf.Max(0f, Screen.width - panelWidth));
        float guiY = Mathf.Clamp(desiredY, 0f, Mathf.Max(0f, Screen.height - panelHeight));

        GUILayout.BeginArea(new Rect(guiX, guiY, panelWidth, panelHeight), boxStyle);

        if (!hasSample)
        {
            GUILayout.Label("<b>环境监测</b>", labelStyle);
            GUILayout.Label($"鼠标世界坐标: ({mouseWorldPos.x:F2}, {mouseWorldPos.y:F2})", labelStyle);
            GUILayout.Label("当前指向位置没有已加载的 WorldModel 地块数据。", labelStyle);
            GUILayout.Label("移动鼠标到已加载地形上即可查看。", labelStyle);
            GUILayout.Label($"按 {toggleKey} 关闭", labelStyle);
            GUILayout.EndArea();
            return;
        }

        if (currentPage == 0)
            DrawOverviewPage(sample);
        else
            DrawRawDataPage(currentPage - 1, rawRowsPerPage);

        GUILayout.Label(
            $"按 {toggleKey} 关闭  |  第 {currentPage + 1}/{totalPages} 页（↑↓ 翻页）",
            labelStyle);
        GUILayout.EndArea();
    }

    /// <summary>第一页显示开发时最常用的地块、气候、水文和农业信息。</summary>
    private void DrawOverviewPage(RuntimeTerrainTileSample sample)
    {
        ChunkTerrainData terrain = sample.Terrain;
        Vector2Int local = sample.LocalCell;
        TerrainCell cell = sample.Cell;

        GUILayout.Label("<b>环境监测 / 地块总览</b>", labelStyle);
        GUILayout.Label(
            $"世界格: ({sample.WorldCell.x}, {sample.WorldCell.y})  局部格: ({local.x}, {local.y})",
            labelStyle);
        GUILayout.Label(
            $"鼠标世界: ({mouseWorldPos.x:F2}, {mouseWorldPos.y:F2})  维度: {sample.Address.DimensionId}",
            labelStyle);
        GUILayout.Label(
            $"区块原点: ({sample.Address.ChunkOrigin.X}, {sample.Address.ChunkOrigin.Y})  Revision: {terrain.Revision}",
            labelStyle);

        GUILayout.Label($"地块: {FormatTile(sample.TopTileId)}", labelStyle);
        GUILayout.Label($"群系: {hoveredBiomeName}  (ID {cell.BiomeId})", labelStyle);
        GUILayout.Label(
            $"可行走: {terrain.IsWalkable(local.x, local.y)}  导航代价: {cell.NavigationCost}  Flags: {cell.Flags}",
            labelStyle);
        GUILayout.Label(
            $"地块层数: {terrain.GetTileLayerCount(local.x, local.y)}  草层: {FormatGrass(terrain.GetGrass(local.x, local.y))}",
            labelStyle);

        int supportTileId = TerrainSupportLayer.GetTileId(terrain, local.x, local.y);
        if (supportTileId != 0)
            GUILayout.Label($"支撑表面: {FormatTile(supportTileId)}", labelStyle);

        DrawClimateSummary(sample);
        DrawLiquidSummary(sample);
        DrawAgricultureSummary(sample);
        DrawTileTemplateSummary(sample.TopTileId);
    }

    /// <summary>原始数据按屏幕可容纳行数拆页，避免调试面板依赖滚轮。</summary>
    private void DrawRawDataPage(int rawPageIndex, int rowsPerPage)
    {
        GUILayout.Label("<b>原始地形 / 环境层</b>", labelStyle);
        int startLine = Mathf.Max(0, rawPageIndex) * Mathf.Max(1, rowsPerPage);
        int endLine = Mathf.Min(rawDataLines.Count, startLine + Mathf.Max(1, rowsPerPage));
        for (int i = startLine; i < endLine; i++)
            GUILayout.Label(rawDataLines[i], labelStyle);
    }

    /// <summary>显示生成气候与最终环境温度；不存在的层不伪造数值。</summary>
    private void DrawClimateSummary(RuntimeTerrainTileSample sample)
    {
        ChunkTerrainData terrain = sample.Terrain;
        Vector2Int local = sample.LocalCell;

        bool hasGeneratedCelsius = terrain.TryGetEnvironmentValue(
            "temperature.celsius", local.x, local.y, out float generatedCelsius);
        bool hasNormalizedTemperature = terrain.TryGetEnvironmentValue(
            "temperature", local.x, local.y, out float normalizedTemperature);

        if (TemperatureMgr.Instance.TryGetAmbientTemperature(
                new Vector2(sample.WorldCell.x + 0.5f, sample.WorldCell.y + 0.5f),
                out float ambientTemperature))
        {
            string generated = hasGeneratedCelsius ? $"{generatedCelsius:F2}℃" : "无";
            GUILayout.Label($"温度: 环境 {ambientTemperature:F2}℃  生成基温 {generated}", labelStyle);
        }
        else if (hasGeneratedCelsius)
        {
            GUILayout.Label($"温度: 生成基温 {generatedCelsius:F2}℃", labelStyle);
        }

        if (hasNormalizedTemperature)
            GUILayout.Label($"温度归一值: {normalizedTemperature:F4}", labelStyle);

        DrawEnvironmentValue(terrain, local, "moisture", "湿度");
        DrawEnvironmentValue(terrain, local, "precipitation", "降水");
        DrawEnvironmentValue(terrain, local, "fertility", "土壤肥力");
        DrawEnvironmentValue(terrain, local, "height", "高度");

        bool hasWindX = terrain.TryGetEnvironmentValue("windX", local.x, local.y, out float windX);
        bool hasWindY = terrain.TryGetEnvironmentValue("windY", local.x, local.y, out float windY);
        if (hasWindX || hasWindY)
        {
            var wind = new Vector2(windX, windY);
            float angle = wind.sqrMagnitude > 0.000001f
                ? Mathf.Atan2(wind.y, wind.x) * Mathf.Rad2Deg
                : 0f;
            GUILayout.Label($"风场: ({wind.x:F3}, {wind.y:F3})  角度 {angle:F1}°", labelStyle);
        }

        bool hasRiverKind = terrain.TryGetEnvironmentValue("riverKind", local.x, local.y, out float riverKind);
        bool hasRiverFlow = terrain.TryGetEnvironmentValue("riverFlow", local.x, local.y, out float riverFlow);
        bool hasRiverDepth = terrain.TryGetEnvironmentValue("riverDepth", local.x, local.y, out float riverDepth);
        if (hasRiverKind || hasRiverFlow || hasRiverDepth)
        {
            GUILayout.Label(
                $"水文: kind={riverKind:G4}  flow={riverFlow:F4}  riverDepth={riverDepth:F4}",
                labelStyle);
        }
    }

    /// <summary>显示独立 Liquid 层与当前表面流向。</summary>
    private void DrawLiquidSummary(RuntimeTerrainTileSample sample)
    {
        GUILayout.Label(
            $"液体: {(string.IsNullOrWhiteSpace(sample.LiquidId) ? "无" : sample.LiquidId)}  深度 {sample.LiquidDepth:F4}  类型索引 {sample.LiquidTypeIndex}",
            labelStyle);

        ChunkMgr chunkManager = ChunkMgr.ExistingInstance;
        if (chunkManager != null &&
            chunkManager.TryGetRuntimeWaterCurrent(
                new Vector2(sample.WorldCell.x + 0.5f, sample.WorldCell.y + 0.5f),
                out RuntimeWaterCurrentSample current))
        {
            GUILayout.Label(
                $"水流: {current.Kind}  方向 ({current.Direction.x:F3}, {current.Direction.y:F3})  流量 {current.Flow:F4}",
                labelStyle);
        }
    }

    /// <summary>耕地或已有农业状态时只显示可持久化的地块、水分与肥力。</summary>
    private void DrawAgricultureSummary(RuntimeTerrainTileSample sample)
    {
        ChunkTerrainData terrain = sample.Terrain;
        Vector2Int local = sample.LocalCell;
        float sourceTileId = FarmlandSystem.Read(terrain, local, FarmlandSystem.SourceLayer);
        bool hasAgricultureState = sourceTileId > 0f;
        bool isFarmland = FarmlandSystem.IsFarmland(sample.Cell);
        if (!isFarmland && !hasAgricultureState)
            return;

        TileData_Farmland soil = FarmlandSystem.ReadSoilSnapshot(sample);
        float waterPercent = soil.maxWater > 0f ? soil.waterValue / soil.maxWater * 100f : 0f;
        float fertilityPercent = soil.maxFertility > 0f ? soil.Fertility / soil.maxFertility * 100f : 0f;

        GUILayout.Label("<b>农业状态</b>", labelStyle);
        GUILayout.Label(
            $"耕地: {isFarmland}  水肥状态地块ID: {Mathf.RoundToInt(sourceTileId)}",
            labelStyle);
        GUILayout.Label(
            $"水分: {soil.waterValue:F2}/{soil.maxWater:F2} ({waterPercent:F1}%)",
            labelStyle);
        GUILayout.Label(
            $"肥力: {soil.Fertility:F4}/{soil.maxFertility:F4} ({fertilityPercent:F1}%)",
            labelStyle);
    }

    /// <summary>显示当前有效地块定义中的模板信息，辅助排查配置与运行时状态差异。</summary>
    private void DrawTileTemplateSummary(int tileId)
    {
        if (!TryGetTileDefinition(tileId, out RuntimeTileDefinition definition) ||
            definition.TileDataTemplate == null)
            return;

        TileData template = definition.TileDataTemplate;
        GUILayout.Label(
            $"定义: {definition.Id}  显示名: {definition.DisplayName}  TileAsset: {definition.TileAssetId}",
            labelStyle);
        GUILayout.Label(
            $"模板: tag={template.TileTag}  penalty={template.Penalty}  walkable={template.IsWalkable}  demolition={template.DemolitionTime:F2}",
            labelStyle);
    }

    private int EstimateOverviewLineCount(RuntimeTerrainTileSample sample)
    {
        int count = 21;
        if (TerrainSupportLayer.GetTileId(sample.Terrain, sample.LocalCell.x, sample.LocalCell.y) != 0)
            count++;

        float source = FarmlandSystem.Read(sample.Terrain, sample.LocalCell, FarmlandSystem.SourceLayer);
        if (FarmlandSystem.IsFarmland(sample.Cell) || source > 0f)
            count += 4;

        return count;
    }

    /// <summary>按当前屏幕高度计算原始数据每页最多显示多少行。</summary>
    private int GetRawRowsPerPage()
    {
        float lineHeight = Mathf.Max(fontSize + 6f, 20f);
        float contentHeight = Mathf.Max(1f, Screen.height - 24f);
        int maxVisibleLines = Mathf.Max(3, Mathf.FloorToInt(contentHeight / lineHeight));
        return Mathf.Max(1, maxVisibleLines - 2);
    }

    private int GetTotalPageCount(int rawRowsPerPage)
    {
        int rows = Mathf.Max(1, rawRowsPerPage);
        int rawPageCount = Mathf.Max(1, Mathf.CeilToInt(rawDataLines.Count / (float)rows));
        return 1 + rawPageCount;
    }

    #endregion

    #region 悬停框

    /// <summary>直接按 1×1 世界格绘制悬停框，不再依赖旧 Map/Tilemap 对象。</summary>
    private void DrawHoverIndicatorGUI()
    {
        if (!TryGetValidHoveredSample(out _))
            return;

        Camera cam = GetMainCamera();
        if (cam == null)
            return;

        Vector3 minWorld = new(hoveredVisualGridPos.x, hoveredVisualGridPos.y, 0f);
        Vector3 maxWorld = new(hoveredVisualGridPos.x + 1f, hoveredVisualGridPos.y + 1f, 0f);
        Vector3 screenMin = cam.WorldToScreenPoint(minWorld);
        Vector3 screenMax = cam.WorldToScreenPoint(maxWorld);
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
        GUI.DrawTexture(new Rect(x, y, width, line), tex);
        GUI.DrawTexture(new Rect(x, y + height - line, width, line), tex);
        GUI.DrawTexture(new Rect(x, y, line, height), tex);
        GUI.DrawTexture(new Rect(x + width - line, y, line, height), tex);
        GUI.color = oldColor;
    }

    private static Texture2D GetOverlayPixelTexture()
    {
        if (overlayPixelTexture != null)
            return overlayPixelTexture;

        overlayPixelTexture = new Texture2D(1, 1)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        overlayPixelTexture.SetPixel(0, 0, Color.white);
        overlayPixelTexture.Apply();
        return overlayPixelTexture;
    }

    #endregion

    #region 数据格式化

    private void RefreshEnvironmentLayerIds(RuntimeTerrainTileSample sample)
    {
        environmentLayerIds.Clear();
        foreach (string layerId in sample.Terrain.EnvironmentLayerIds)
        {
            if (!string.IsNullOrWhiteSpace(layerId))
                environmentLayerIds.Add(layerId);
        }

        environmentLayerIds.Sort(StringComparer.Ordinal);
    }

    /// <summary>把可变长度的原始地块信息整理成行，再统一交给翻页逻辑显示。</summary>
    private void RefreshRawDataLines(RuntimeTerrainTileSample sample)
    {
        ChunkTerrainData terrain = sample.Terrain;
        Vector2Int local = sample.LocalCell;

        RefreshEnvironmentLayerIds(sample);
        rawDataLines.Clear();
        rawDataLines.Add(
            $"世界格: ({sample.WorldCell.x}, {sample.WorldCell.y})  Chunk: ({sample.Address.ChunkOrigin.X}, {sample.Address.ChunkOrigin.Y})");
        rawDataLines.Add($"Ground: {FormatTile(sample.Cell.GroundTileId)}");
        rawDataLines.Add($"Back: {FormatTile(sample.Cell.BackTileId)}");
        rawDataLines.Add($"Blocking: {FormatTile(sample.Cell.BlockingTileId)}");
        rawDataLines.Add($"EffectiveTop: {FormatTile(sample.TopTileId)}");

        int layerCount = terrain.GetTileLayerCount(local.x, local.y);
        for (int i = 0; i < layerCount; i++)
            rawDataLines.Add($"TileStack[{i}]: {FormatTile(terrain.GetTileIdAt(local.x, local.y, i))}");

        rawDataLines.Add(
            $"Liquid: id={sample.LiquidId ?? "none"}  type={sample.LiquidTypeIndex}  depth={sample.LiquidDepth:F4}");
        rawDataLines.Add($"环境层数量: {environmentLayerIds.Count}");

        for (int i = 0; i < environmentLayerIds.Count; i++)
        {
            string layerId = environmentLayerIds[i];
            if (terrain.TryGetEnvironmentValue(layerId, local.x, local.y, out float value))
                rawDataLines.Add($"{layerId}: {value:G7}");
        }
    }

    private void DrawEnvironmentValue(
        ChunkTerrainData terrain,
        Vector2Int local,
        string layerId,
        string displayName)
    {
        if (terrain.TryGetEnvironmentValue(layerId, local.x, local.y, out float value))
            GUILayout.Label($"{displayName}: {value:F4}", labelStyle);
    }

    private static string FormatGrass(byte value)
    {
        return value == ChunkTerrainData.GrassPresent ? $"有 ({value})" : $"无 ({value})";
    }

    private static string FormatTile(int tileId)
    {
        if (tileId == 0)
            return "无 (#0)";

        if (TryGetTileDefinition(tileId, out RuntimeTileDefinition definition))
            return $"{definition.DisplayName} [{definition.Id}] #{tileId}";

        return $"未知地块 #{tileId}";
    }

    private static bool TryGetTileDefinition(int tileId, out RuntimeTileDefinition definition)
    {
        definition = null;
        GameRes resources = GameRes.ExistingInstance;
        return tileId > 0 && resources != null && resources.TryGetTileDefinition(tileId, out definition);
    }

    #endregion

    #region GUI 样式

    private void CreateGUIStyles()
    {
        backgroundTexture = new Texture2D(2, 2)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        var colors = new Color[4];
        for (int i = 0; i < colors.Length; i++)
            colors[i] = backgroundColor;
        backgroundTexture.SetPixels(colors);
        backgroundTexture.Apply();

        boxStyle = new GUIStyle(GUI.skin.box);
        boxStyle.normal.background = backgroundTexture;
        boxStyle.padding = new RectOffset(12, 12, 10, 10);

        labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.normal.textColor = textColor;
        labelStyle.fontSize = fontSize;
        labelStyle.richText = true;
        labelStyle.wordWrap = false;
    }

    #endregion

    #region 公共接口

    public void Show()
    {
        isVisible = true;
    }

    public void Hide()
    {
        isVisible = false;
    }

    public void Toggle()
    {
        isVisible = !isVisible;
    }

    public void SetToggleKey(KeyCode key)
    {
        toggleKey = key;
    }

    #endregion
}
