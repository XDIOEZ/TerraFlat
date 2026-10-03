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
    private readonly List<string> displayLines = new(96);
    private Camera mainCamera;
    private RuntimeTerrainTileSample hoveredSample;
    private Vector2 mouseScreenPos;
    private Vector3 mouseWorldPos;
    private Vector2Int hoveredVisualGridPos;
    private string hoveredBiomeName = "未知";
    private bool isVisible;
    private bool isValidPosition;
    private int scrollLineOffset;

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

        int maxScroll = TryGetValidHoveredSample(out _)
            ? Mathf.Max(0, displayLines.Count - GetVisibleLineCount())
            : 0;

        Keyboard keyboard = Keyboard.current;
        if (keyboard?.upArrowKey.wasPressedThisFrame == true)
        {
            scrollLineOffset = Mathf.Max(0, scrollLineOffset - 1);
        }
        else if (keyboard?.downArrowKey.wasPressedThisFrame == true)
        {
            scrollLineOffset = Mathf.Min(maxScroll, scrollLineOffset + 1);
        }

        scrollLineOffset = Mathf.Clamp(scrollLineOffset, 0, maxScroll);
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

        float panelWidth = Mathf.Min(Mathf.Max(320f, panelSize.x), Mathf.Max(1f, Screen.width));
        float lineHeight = Mathf.Max(fontSize + 6f, 20f);
        float panelHeight = Mathf.Min(
            Mathf.Max(panelSize.y, lineHeight * 5f + 20f),
            Mathf.Max(1f, Screen.height - 4f));

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

        RefreshDisplayLines(sample);
        int visibleLineCount = GetVisibleLineCount(panelHeight);
        int maxScroll = Mathf.Max(0, displayLines.Count - visibleLineCount);
        scrollLineOffset = Mathf.Clamp(scrollLineOffset, 0, maxScroll);
        int endLine = Mathf.Min(displayLines.Count, scrollLineOffset + visibleLineCount);
        for (int i = scrollLineOffset; i < endLine; i++)
            GUILayout.Label(displayLines[i], labelStyle);

        GUILayout.FlexibleSpace();
        GUILayout.Label(
            $"按 {toggleKey} 关闭  |  ↑↓ 滚动  {scrollLineOffset + 1}-{endLine}/{displayLines.Count}",
            labelStyle);
        GUILayout.EndArea();
    }

    /// <summary>把总览和原始环境层整理成连续文本，交给上下箭头逐行滚动。</summary>
    private void RefreshDisplayLines(RuntimeTerrainTileSample sample)
    {
        displayLines.Clear();
        ChunkTerrainData terrain = sample.Terrain;
        Vector2Int local = sample.LocalCell;
        TerrainCell cell = sample.Cell;

        displayLines.Add("<b>环境监测 / 地块总览</b>");
        displayLines.Add(
            $"世界格: ({sample.WorldCell.x}, {sample.WorldCell.y})  局部格: ({local.x}, {local.y})");
        displayLines.Add(
            $"鼠标世界: ({mouseWorldPos.x:F2}, {mouseWorldPos.y:F2})  维度: {sample.Address.DimensionId}");
        displayLines.Add(
            $"区块原点: ({sample.Address.ChunkOrigin.X}, {sample.Address.ChunkOrigin.Y})  Revision: {terrain.Revision}");

        displayLines.Add($"地块: {FormatTile(sample.TopTileId)}");
        displayLines.Add($"群系: {hoveredBiomeName}  (ID {cell.BiomeId})");
        displayLines.Add(
            $"可行走: {terrain.IsWalkable(local.x, local.y)}  导航代价: {cell.NavigationCost}  Flags: {cell.Flags}");
        displayLines.Add(
            $"地块层数: {terrain.GetTileLayerCount(local.x, local.y)}  草层: {FormatGrass(terrain.GetGrass(local.x, local.y))}");

        int supportTileId = TerrainSupportLayer.GetTileId(terrain, local.x, local.y);
        if (supportTileId != 0)
            displayLines.Add($"支撑表面: {FormatTile(supportTileId)}");

        AppendClimateSummary(sample);
        AppendLiquidSummary(sample);
        AppendAgricultureSummary(sample);
        AppendTileTemplateSummary(sample.TopTileId);

        displayLines.Add(string.Empty);
        displayLines.Add("<b>原始地形 / 环境层</b>");
        RefreshRawDataLines(sample);
        displayLines.AddRange(rawDataLines);
    }

    /// <summary>显示生成气候与最终环境温度；不存在的层不伪造数值。</summary>
    private void AppendClimateSummary(RuntimeTerrainTileSample sample)
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
            displayLines.Add($"温度: 环境 {ambientTemperature:F2}℃  生成基温 {generated}");
        }
        else if (hasGeneratedCelsius)
        {
            displayLines.Add($"温度: 生成基温 {generatedCelsius:F2}℃");
        }

        if (hasNormalizedTemperature)
            displayLines.Add($"温度归一值: {normalizedTemperature:F4}");

        AppendEnvironmentValue(terrain, local, "moisture", "湿度");
        AppendEnvironmentValue(terrain, local, "precipitation", "降水");
        AppendEnvironmentValue(terrain, local, "fertility", "土壤肥力");
        AppendEnvironmentValue(terrain, local, "height", "高度");

        bool hasWindX = terrain.TryGetEnvironmentValue("windX", local.x, local.y, out float windX);
        bool hasWindY = terrain.TryGetEnvironmentValue("windY", local.x, local.y, out float windY);
        if (hasWindX || hasWindY)
        {
            var wind = new Vector2(windX, windY);
            float angle = wind.sqrMagnitude > 0.000001f
                ? Mathf.Atan2(wind.y, wind.x) * Mathf.Rad2Deg
                : 0f;
            displayLines.Add($"风场: ({wind.x:F3}, {wind.y:F3})  角度 {angle:F1}°");
        }

        bool hasRiverKind = terrain.TryGetEnvironmentValue("riverKind", local.x, local.y, out float riverKind);
        bool hasRiverFlow = terrain.TryGetEnvironmentValue("riverFlow", local.x, local.y, out float riverFlow);
        bool hasRiverDepth = terrain.TryGetEnvironmentValue("riverDepth", local.x, local.y, out float riverDepth);
        if (hasRiverKind || hasRiverFlow || hasRiverDepth)
        {
            displayLines.Add(
                $"水文: kind={riverKind:G4}  flow={riverFlow:F4}  riverDepth={riverDepth:F4}");
        }
    }

    /// <summary>显示独立 Liquid 层与当前表面流向。</summary>
    private void AppendLiquidSummary(RuntimeTerrainTileSample sample)
    {
        displayLines.Add(
            $"液体: {(string.IsNullOrWhiteSpace(sample.LiquidId) ? "无" : sample.LiquidId)}  深度 {sample.LiquidDepth:F4}  类型索引 {sample.LiquidTypeIndex}");

        ChunkMgr chunkManager = ChunkMgr.ExistingInstance;
        if (chunkManager != null &&
            chunkManager.TryGetRuntimeWaterCurrent(
                new Vector2(sample.WorldCell.x + 0.5f, sample.WorldCell.y + 0.5f),
                out RuntimeWaterCurrentSample current))
        {
            displayLines.Add(
                $"水流: {current.Kind}  方向 ({current.Direction.x:F3}, {current.Direction.y:F3})  流量 {current.Flow:F4}");
        }
    }

    /// <summary>耕地或已有农业状态时只显示可持久化的地块、水分与肥力。</summary>
    private void AppendAgricultureSummary(RuntimeTerrainTileSample sample)
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

        displayLines.Add("<b>农业状态</b>");
        displayLines.Add($"耕地: {isFarmland}  水肥状态地块ID: {Mathf.RoundToInt(sourceTileId)}");
        displayLines.Add($"水分: {soil.waterValue:F2}/{soil.maxWater:F2} ({waterPercent:F1}%)");
        displayLines.Add($"肥力: {soil.Fertility:F4}/{soil.maxFertility:F4} ({fertilityPercent:F1}%)");
    }

    /// <summary>显示当前有效地块定义中的模板信息，辅助排查配置与运行时状态差异。</summary>
    private void AppendTileTemplateSummary(int tileId)
    {
        if (!TryGetTileDefinition(tileId, out RuntimeTileDefinition definition) ||
            definition.TileDataTemplate == null)
            return;

        TileData template = definition.TileDataTemplate;
        displayLines.Add(
            $"定义: {definition.Id}  显示名: {definition.DisplayName}  TileAsset: {definition.TileAssetId}");
        displayLines.Add(
            $"模板: tag={template.TileTag}  penalty={template.Penalty}  walkable={template.IsWalkable}  demolition={template.DemolitionTime:F2}");
    }

    /// <summary>按面板高度计算一次能看到多少行，滚动只移动一行，不做分页。</summary>
    private int GetVisibleLineCount(float panelHeight = -1f)
    {
        float lineHeight = Mathf.Max(fontSize + 6f, 20f);
        float height = panelHeight > 0f
            ? panelHeight
            : Mathf.Min(
                Mathf.Max(panelSize.y, lineHeight * 5f + 20f),
                Mathf.Max(1f, Screen.height - 4f));
        float padding = boxStyle != null ? boxStyle.padding.vertical : 20f;
        float availableHeight = Mathf.Max(lineHeight, height - padding - lineHeight - 4f);
        return Mathf.Max(1, Mathf.FloorToInt(availableHeight / lineHeight));
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

    /// <summary>把可变长度的原始地块信息整理成行，再统一交给逐行滚动显示。</summary>
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

    private void AppendEnvironmentValue(
        ChunkTerrainData terrain,
        Vector2Int local,
        string layerId,
        string displayName)
    {
        if (terrain.TryGetEnvironmentValue(layerId, local.x, local.y, out float value))
            displayLines.Add($"{displayName}: {value:F4}");
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
        scrollLineOffset = 0;
    }

    public void Hide()
    {
        isVisible = false;
    }

    public void Toggle()
    {
        isVisible = !isVisible;
        if (isVisible)
            scrollLineOffset = 0;
    }

    public void SetToggleKey(KeyCode key)
    {
        toggleKey = key;
    }

    #endregion
}
