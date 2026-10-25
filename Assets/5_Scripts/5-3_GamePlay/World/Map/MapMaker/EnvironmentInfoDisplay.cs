using System;
using System.Collections.Generic;
using FlatWorld.Localization;
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
    public Vector2 panelSize = new(720f, 640f);
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
    private readonly List<float> displayLineHeights = new(96);
    private readonly List<int> pageStartLines = new(8);
    private readonly GUIContent measurementContent = new();
    private Camera mainCamera;
    private RuntimeTerrainTileSample hoveredSample;
    private Vector2 mouseScreenPos;
    private Vector3 mouseWorldPos;
    private Vector2Int hoveredVisualGridPos;
    private string hoveredBiomeName = "未知";
    private bool isVisible;
    private bool hasValidPointer;
    private bool isValidPosition;
    private int pageIndex;
    private int pendingPageStep;

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
        hasValidPointer = false;
        isValidPosition = false;
        if (!isVisible)
            return;

        Pointer pointer = Pointer.current;
        if (pointer == null)
            return;

        mouseScreenPos = pointer.position.ReadValue();
        UpdateMouseInfo();

        if (!hasValidPointer)
            return;

        Keyboard keyboard = Keyboard.current;
        float wheel = Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;
        if (keyboard?.upArrowKey.wasPressedThisFrame == true ||
            keyboard?.pageUpKey.wasPressedThisFrame == true || wheel > 0f)
        {
            pendingPageStep = -1;
        }
        else if (keyboard?.downArrowKey.wasPressedThisFrame == true ||
                 keyboard?.pageDownKey.wasPressedThisFrame == true || wheel < 0f)
        {
            pendingPageStep = 1;
        }
    }

    private void OnGUI()
    {
        if (!Application.isPlaying || !isVisible || !hasValidPointer)
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
        hasValidPointer = TryProjectScreenPosition(cam, mouseScreenPos, out mouseWorldPos);
        if (!hasValidPointer)
            return;

        ChunkMgr chunkManager = ChunkMgr.ExistingInstance;
        if (chunkManager == null)
            return;
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

    public static bool TryProjectScreenPosition(Camera camera, Vector2 screenPosition, out Vector3 worldPosition)
    {
        worldPosition = default;
        // 无效或已离开相机视口的指针不送进 Unity 投影，也不复用上一帧悬停位置。
        if (camera == null || !camera.isActiveAndEnabled ||
            !float.IsFinite(screenPosition.x) || !float.IsFinite(screenPosition.y) ||
            !camera.pixelRect.Contains(screenPosition))
            return false;

        Ray ray = camera.ScreenPointToRay(screenPosition);
        var worldPlane = new Plane(Vector3.forward, Vector3.zero);
        if (!worldPlane.Raycast(ray, out float distance) || !float.IsFinite(distance))
            return false;

        Vector3 projected = ray.GetPoint(distance);
        if (!float.IsFinite(projected.x) || !float.IsFinite(projected.y))
            return false;

        worldPosition = new Vector3(projected.x, projected.y, 0f);
        return true;
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

        const float screenMargin = 4f;
        const float footerGap = 10f;
        float panelWidth = Mathf.Min(Mathf.Max(320f, panelSize.x), Mathf.Max(1f, Screen.width - screenMargin * 2f));
        float lineHeight = Mathf.Max(fontSize + 6f, 20f);
        float panelHeight = Mathf.Min(
            Mathf.Max(panelSize.y, lineHeight * 8f + 40f),
            Mathf.Max(1f, Screen.height - screenMargin * 2f));

        float desiredX = mouseScreenPos.x + offset.x;
        float desiredY = Screen.height - mouseScreenPos.y + offset.y;
        float guiX = Mathf.Clamp(desiredX, screenMargin, Mathf.Max(screenMargin, Screen.width - panelWidth - screenMargin));
        float guiY = Mathf.Clamp(desiredY, screenMargin, Mathf.Max(screenMargin, Screen.height - panelHeight - screenMargin));
        var panelRect = new Rect(guiX, guiY, panelWidth, panelHeight);
        GUI.Box(panelRect, GUIContent.none, boxStyle);
        DrawOutline(panelRect, Color.white, 2f);

        if (!hasSample)
        {
            displayLines.Clear();
            displayLines.Add(Ui("<b>环境监测</b>"));
            displayLines.Add(UiFormat("鼠标世界坐标：({0:F2}, {1:F2})", mouseWorldPos.x, mouseWorldPos.y));
            displayLines.Add(Ui("当前指向位置没有已加载的 WorldModel 地块数据。"));
            displayLines.Add(Ui("移动鼠标到已加载地形上即可查看。"));
        }
        else
        {
            RefreshDisplayLines(sample);
        }

        float contentWidth = Mathf.Max(1f, panelWidth - boxStyle.padding.horizontal);
        string instructions = UiFormat("按 {0} 关闭  |  ↑↓ / PgUp/PgDn / 滚轮 翻页", toggleKey);
        string pageCount = UiFormat("第 {0}/{1} 页", displayLines.Count, displayLines.Count);
        float footerHeight = MeasureLineHeight($"{instructions}\n{pageCount}", contentWidth);
        float contentHeight = Mathf.Max(lineHeight,
            panelHeight - boxStyle.padding.vertical - footerHeight - footerGap);
        RefreshPages(contentWidth, contentHeight);
        pageIndex = Mathf.Clamp(pageIndex + pendingPageStep, 0, pageStartLines.Count - 1);
        pendingPageStep = 0;

        int startLine = pageStartLines[pageIndex];
        int endLine = pageIndex + 1 < pageStartLines.Count
            ? pageStartLines[pageIndex + 1]
            : displayLines.Count;
        string pageStatus = UiFormat("第 {0}/{1} 页", pageIndex + 1, pageStartLines.Count);
        float textX = guiX + boxStyle.padding.left;
        float textY = guiY + boxStyle.padding.top;
        for (int i = startLine; i < endLine; i++)
        {
            GUI.Label(new Rect(textX, textY, contentWidth, displayLineHeights[i]), displayLines[i], labelStyle);
            textY += displayLineHeights[i];
        }

        GUI.Label(
            new Rect(textX, guiY + panelHeight - boxStyle.padding.bottom - footerHeight, contentWidth, footerHeight),
            $"{instructions}\n{pageStatus}",
            labelStyle);
    }

    /// <summary>把总览和原始环境层整理成连续文本，再按实际文字高度分页。</summary>
    private void RefreshDisplayLines(RuntimeTerrainTileSample sample)
    {
        displayLines.Clear();
        ChunkTerrainData terrain = sample.Terrain;
        Vector2Int local = sample.LocalCell;
        TerrainCell cell = sample.Cell;

        displayLines.Add(Ui("<b>环境监测 / 地块总览</b>"));
        displayLines.Add(UiFormat("世界格：({0}, {1})  局部格：({2}, {3})",
            sample.WorldCell.x, sample.WorldCell.y, local.x, local.y));
        displayLines.Add(UiFormat("鼠标世界：({0:F2}, {1:F2})  维度：{2}",
            mouseWorldPos.x, mouseWorldPos.y, sample.Address.DimensionId));
        displayLines.Add(UiFormat("区块原点：({0}, {1})  修订：{2}",
            sample.Address.ChunkOrigin.X, sample.Address.ChunkOrigin.Y, terrain.Revision));

        displayLines.Add(UiFormat("地块：{0}", FormatTile(sample.TopTileId)));
        displayLines.Add(UiFormat("群系：{0}（ID {1}）", Ui(hoveredBiomeName), cell.BiomeId));
        displayLines.Add(UiFormat("可行走：{0}  导航代价：{1}  标记：{2}",
            Ui(terrain.IsWalkable(local.x, local.y) ? "是" : "否"), cell.NavigationCost, cell.Flags));
        displayLines.Add(UiFormat("地块层数：{0}  草层：{1}",
            terrain.GetTileLayerCount(local.x, local.y), FormatGrass(terrain.GetGrass(local.x, local.y))));

        int supportTileId = TerrainSupportLayer.GetTileId(terrain, local.x, local.y);
        if (supportTileId != 0)
            displayLines.Add(UiFormat("支撑表面：{0}", FormatTile(supportTileId)));

        AppendClimateSummary(sample);
        AppendLiquidSummary(sample);
        AppendAgricultureSummary(sample);
        AppendTileTemplateSummary(sample.TopTileId);

        displayLines.Add(string.Empty);
        displayLines.Add(Ui("<b>原始地形 / 环境层</b>"));
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
            string generated = hasGeneratedCelsius
                ? UiFormat("{0:F2}℃", generatedCelsius)
                : Ui("无");
            displayLines.Add(UiFormat("温度：环境 {0:F2}℃  生成基温 {1}", ambientTemperature, generated));
        }
        else if (hasGeneratedCelsius)
        {
            displayLines.Add(UiFormat("温度：生成基温 {0:F2}℃", generatedCelsius));
        }

        if (hasNormalizedTemperature)
            displayLines.Add(UiFormat("温度归一值：{0:F4}", normalizedTemperature));

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
            displayLines.Add(UiFormat("风场：({0:F3}, {1:F3})  角度 {2:F1}°", wind.x, wind.y, angle));
        }

        bool hasRiverKind = terrain.TryGetEnvironmentValue("riverKind", local.x, local.y, out float riverKind);
        bool hasRiverFlow = terrain.TryGetEnvironmentValue("riverFlow", local.x, local.y, out float riverFlow);
        bool hasRiverDepth = terrain.TryGetEnvironmentValue("riverDepth", local.x, local.y, out float riverDepth);
        if (hasRiverKind || hasRiverFlow || hasRiverDepth)
        {
            displayLines.Add(UiFormat("水文：kind={0:G4}  flow={1:F4}  riverDepth={2:F4}",
                riverKind, riverFlow, riverDepth));
        }
    }

    /// <summary>显示独立 Liquid 层与当前表面流向。</summary>
    private void AppendLiquidSummary(RuntimeTerrainTileSample sample)
    {
        displayLines.Add(UiFormat("液体：{0}  水量 {1}/10  类型索引 {2}",
            string.IsNullOrWhiteSpace(sample.LiquidId) ? Ui("无") : sample.LiquidId,
            sample.LiquidLevel, sample.LiquidTypeIndex));

        ChunkMgr chunkManager = ChunkMgr.ExistingInstance;
        if (chunkManager != null &&
            chunkManager.TryGetRuntimeWaterCurrent(
                new Vector2(sample.WorldCell.x + 0.5f, sample.WorldCell.y + 0.5f),
                out RuntimeWaterCurrentSample current))
        {
            displayLines.Add(UiFormat("水流：{0}  方向 ({1:F3}, {2:F3})  流量 {3:F4}",
                GetWaterCurrentName(current.Kind), current.Direction.x, current.Direction.y, current.Flow));
        }
    }

    /// <summary>只有真实耕地显示动态农业水肥，普通地块仅显示地理环境。</summary>
    private void AppendAgricultureSummary(RuntimeTerrainTileSample sample)
    {
        ChunkTerrainData terrain = sample.Terrain;
        Vector2Int local = sample.LocalCell;
        float sourceTileId = FarmlandSystem.Read(terrain, local, FarmlandSystem.SourceLayer);
        bool isFarmland = FarmlandSystem.IsFarmland(sample.Cell);
        if (!isFarmland)
            return;

        TileData_Farmland soil = FarmlandSystem.ReadSoilSnapshot(sample);
        float waterPercent = soil.maxWater > 0 ? (float)soil.waterValue / soil.maxWater * 100f : 0f;
        float fertilityPercent = soil.maxFertility > 0f ? soil.Fertility / soil.maxFertility * 100f : 0f;

        displayLines.Add(Ui("<b>农业状态</b>"));
        displayLines.Add(UiFormat("耕地：{0}  水肥状态地块 ID：{1}",
            Ui(isFarmland ? "是" : "否"), Mathf.RoundToInt(sourceTileId)));
        displayLines.Add(UiFormat("水分：{0}/{1}（{2:F1}%）", soil.waterValue, soil.maxWater, waterPercent));
        displayLines.Add(UiFormat("肥力：{0:F4}/{1:F4}（{2:F1}%）", soil.Fertility, soil.maxFertility, fertilityPercent));
    }

    /// <summary>显示当前有效地块定义中的模板信息，辅助排查配置与运行时状态差异。</summary>
    private void AppendTileTemplateSummary(int tileId)
    {
        if (!TryGetTileDefinition(tileId, out RuntimeTileDefinition definition) ||
            definition.TileDataTemplate == null)
            return;

        TileData template = definition.TileDataTemplate;
        displayLines.Add(UiFormat("定义：{0}  显示名：{1}  TileAsset：{2}",
            definition.Id, Ui(definition.DisplayName), definition.TileAssetId));
        displayLines.Add(UiFormat("模板：tag={0}  penalty={1}  walkable={2}  demolition={3:F2}",
            template.TileTag, template.Penalty, Ui(template.IsWalkable ? "是" : "否"), template.DemolitionTime));
    }

    /// <summary>完整字段放不下时移到下一页，避免换行文字与底部提示重叠。</summary>
    private void RefreshPages(float contentWidth, float contentHeight)
    {
        displayLineHeights.Clear();
        pageStartLines.Clear();
        pageStartLines.Add(0);
        float usedHeight = 0f;
        for (int i = 0; i < displayLines.Count; i++)
        {
            float height = MeasureLineHeight(displayLines[i], contentWidth);
            if (usedHeight > 0f && usedHeight + height > contentHeight)
            {
                pageStartLines.Add(i);
                usedHeight = 0f;
            }

            displayLineHeights.Add(height);
            usedHeight += height;
        }
    }

    private float MeasureLineHeight(string text, float width)
    {
        measurementContent.text = text;
        return Mathf.Max(fontSize + 6f, Mathf.Ceil(labelStyle.CalcHeight(measurementContent, width)) + 2f);
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

        DrawOutline(new Rect(x, y, width, height), hoverIndicatorColor, hoverIndicatorThickness);
    }

    private static void DrawOutline(Rect rect, Color color, float thickness)
    {
        Texture2D tex = GetOverlayPixelTexture();
        Color oldColor = GUI.color;
        GUI.color = color;
        float line = Mathf.Max(1f, thickness);
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, line), tex);
        GUI.DrawTexture(new Rect(rect.x, rect.yMax - line, rect.width, line), tex);
        GUI.DrawTexture(new Rect(rect.x, rect.y, line, rect.height), tex);
        GUI.DrawTexture(new Rect(rect.xMax - line, rect.y, line, rect.height), tex);
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

    /// <summary>把可变长度的原始地块信息整理成行，再统一交给整页显示。</summary>
    private void RefreshRawDataLines(RuntimeTerrainTileSample sample)
    {
        ChunkTerrainData terrain = sample.Terrain;
        Vector2Int local = sample.LocalCell;

        RefreshEnvironmentLayerIds(sample);
        rawDataLines.Clear();
        rawDataLines.Add(UiFormat("世界格：({0}, {1})  区块：({2}, {3})",
            sample.WorldCell.x, sample.WorldCell.y, sample.Address.ChunkOrigin.X, sample.Address.ChunkOrigin.Y));
        rawDataLines.Add(UiFormat("地表：{0}", FormatTile(sample.Cell.GroundTileId)));
        rawDataLines.Add(UiFormat("背景：{0}", FormatTile(sample.Cell.BackTileId)));
        rawDataLines.Add(UiFormat("阻挡：{0}", FormatTile(sample.Cell.BlockingTileId)));
        rawDataLines.Add(UiFormat("有效顶部：{0}", FormatTile(sample.TopTileId)));

        int layerCount = terrain.GetTileLayerCount(local.x, local.y);
        for (int i = 0; i < layerCount; i++)
            rawDataLines.Add(UiFormat("地块层堆叠[{0}]：{1}", i,
                FormatTile(terrain.GetTileIdAt(local.x, local.y, i))));

        rawDataLines.Add(UiFormat("液体：ID={0}  类型={1}  液位={2}/10",
            sample.LiquidId ?? Ui("无"), sample.LiquidTypeIndex, sample.LiquidLevel));
        rawDataLines.Add(UiFormat("环境层数量：{0}", environmentLayerIds.Count));

        for (int i = 0; i < environmentLayerIds.Count; i++)
        {
            string layerId = environmentLayerIds[i];
            if (terrain.TryGetEnvironmentValue(layerId, local.x, local.y, out float value))
                rawDataLines.Add(UiFormat("{0}：{1:G7}", layerId, value));
        }
    }

    private void AppendEnvironmentValue(
        ChunkTerrainData terrain,
        Vector2Int local,
        string layerId,
        string displayName)
    {
        if (terrain.TryGetEnvironmentValue(layerId, local.x, local.y, out float value))
            displayLines.Add(UiFormat("{0}：{1:F4}", Ui(displayName), value));
    }

    private static string FormatGrass(byte value)
    {
        return UiFormat("{0}（{1}）",
            Ui(value == ChunkTerrainData.GrassPresent ? "有" : "无"), value);
    }

    private static string FormatTile(int tileId)
    {
        if (tileId == 0)
            return Ui("无 (#0)");

        if (TryGetTileDefinition(tileId, out RuntimeTileDefinition definition))
            return UiFormat("{0} [{1}] #{2}", Ui(definition.DisplayName), definition.Id, tileId);

        return UiFormat("未知地块 #{0}", tileId);
    }

    private static string Ui(string sourceText) => FlatWorldLocalizationService.GetUiText(sourceText);

    private static string UiFormat(string sourceTemplate, params object[] arguments) =>
        FlatWorldLocalizationService.GetUiFormat(sourceTemplate, arguments);

    private static string GetWaterCurrentName(RuntimeWaterCurrentKind kind) => Ui(kind switch
    {
        RuntimeWaterCurrentKind.None => "无",
        RuntimeWaterCurrentKind.River => "河流",
        RuntimeWaterCurrentKind.Ocean => "海洋",
        RuntimeWaterCurrentKind.ExperimentalLiquid => "实验液体",
        _ => "未知"
    });

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
        boxStyle.padding = new RectOffset(14, 14, 12, 12);

        labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.normal.textColor = textColor;
        labelStyle.fontSize = fontSize;
        labelStyle.richText = true;
        labelStyle.wordWrap = true;
        labelStyle.alignment = TextAnchor.UpperLeft;
        labelStyle.padding = new RectOffset();
        labelStyle.margin = new RectOffset();
    }

    #endregion

    #region 公共接口

    public void Show()
    {
        isVisible = true;
        pageIndex = 0;
        pendingPageStep = 0;
    }

    public void Hide()
    {
        isVisible = false;
        pendingPageStep = 0;
    }

    public void Toggle()
    {
        isVisible = !isVisible;
        pendingPageStep = 0;
        if (isVisible)
            pageIndex = 0;
    }

    public void SetToggleKey(KeyCode key)
    {
        toggleKey = key;
    }

    #endregion
}
