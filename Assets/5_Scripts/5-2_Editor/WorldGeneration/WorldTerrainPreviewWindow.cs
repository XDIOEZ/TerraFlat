#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlatWorld.WorldModel;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

/// <summary>
/// 在不进入游戏场景的情况下，复用正式 ChunkGenerationProfileSO 与 DeterministicChunkGenerator
/// 生成一块连续地表预览。预览是一格一像素，支持地形、群系和高度图三种显示方式；右侧临时参数
/// 默认只作用于本次预览，也可经确认后写回 Profile 资源；运行时世界坐标缩放仍由 PlanetData 管理。
/// 生成放在后台线程，最大预览边长限制为 1024，避免高分辨率水文计算长时间占用编辑器内存。
/// </summary>
public sealed class WorldTerrainPreviewWindow : EditorWindow
{
    #region 类型与常量

    private enum PreviewDisplayMode
    {
        Terrain,
        Biome,
        Height
    }

    [Serializable]
    private sealed class NumericParameterValue
    {
        public string Id;
        public double Value;
    }

    private sealed class PreviewInput
    {
        public ulong InputFingerprint;
        public int Seed;
        public int OriginX;
        public int OriginY;
        public int Width;
        public int Height;
        public int GenerationOriginX;
        public int GenerationOriginY;
        public bool TileWrappedPeriod;
        public ChunkGenerationProfileSnapshot Profile;
        public ChunkGenerationTopologySnapshot Topology;
    }

    private sealed class PreviewResult
    {
        public ulong InputFingerprint;
        public int Seed;
        public int OriginX;
        public int OriginY;
        public int Width;
        public int Height;
        public float[] Heights;
        public byte[] Biomes;
        public TerrainCellFlags[] Flags;
        public int[] GroundTileIds;
        public double MinimumHeight;
        public double MaximumHeight;
        public double AverageHeight;
        public double WaterRatio;
        public double WalkableRatio;
        public long ElapsedMilliseconds;
        public bool TiledWrappedPeriod;
        public int TopologySpanX;
        public int TopologySpanY;
    }

    private const string DefaultProfilePath =
        "Assets/Resources/Config/WorldModel/ChunkGenerationProfile_Surface.asset";
    private const int MinimumPreviewSize = 16;
    private const int MaximumPreviewSize = 1024;
    private const float SettingsPanelWidth = 520f;
    private const float MinimumCanvasZoom = 0.25f;
    private const float MaximumCanvasZoom = 16f;
    private const string RuntimeWorldCoordinateScaleId = "world.coordinateScale";
    private const string ProfileNumericParametersPropertyName = "numericParameters";
    private const string ProfileParameterIdPropertyName = "Id";
    private const string ProfileParameterValuePropertyName = "Value";

    private static readonly HashSet<string> CommonParameterIds = new(StringComparer.Ordinal)
    {
        "world.coordinateScale",
        "terrain.seaLevel",
        "terrain.beachLevel",
        "terrain.mountainLevel",
        "terrain.height.coordScale",
        "terrain.height.frequency",
        "terrain.height.octaves",
        "terrain.height.lacunarity",
        "terrain.height.persistence",
        "terrain.height.offsetX",
        "terrain.height.offsetY",
        "terrain.height.secondaryBoostEnabled",
        "terrain.height.secondaryBoostStrength",
        "climate.precipitation.coordScale",
        "climate.temperature.coordScale",
        "river.enabled",
        "structure.enabled"
    };

    /// <summary>Profile 数值参数的通俗作用说明；未知的 MOD 参数仍显示原始 ID。</summary>
    private static readonly IReadOnlyDictionary<string, string> ParameterDescriptions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["world.coordinateScale"] = "越大地貌越密集、越碎；越小地貌越舒展",
            ["terrain.groundTileId"] = "普通陆地默认使用的 Tile 数字编号",
            ["terrain.waterTileId"] = "河流和淡水使用的 Tile 数字编号",
            ["terrain.waterThreshold"] = "旧配置兼容项，当前纯地表生成器不读取",
            ["terrain.saltWaterTileId"] = "海洋使用的咸水 Tile 数字编号",
            ["terrain.sandTileId"] = "沙滩、沙漠和冲积带使用的 Tile 数字编号",
            ["terrain.stoneTileId"] = "山地石地使用的 Tile 数字编号",
            ["terrain.snowTileId"] = "雪地使用的 Tile 数字编号",
            ["terrain.seaLevel"] = "越高水域越多，越低陆地越多",
            ["terrain.beachLevel"] = "越高海岸边的沙滩带越宽",
            ["terrain.mountainLevel"] = "越低石质山地越多，越高山地越少",
            ["terrain.noiseScale"] = "简化地形模式的起伏密度；越大变化越快",
            ["terrain.octaves"] = "简化地形模式的细节层数；越高越细、计算越慢",
            ["climate.noiseScale"] = "简化气候模式的区域密度；越大冷热干湿变化越快",
            ["climate.octaves"] = "简化气候模式的细节层数；越高气候边界越细碎",
            ["terrain.height.coordScale"] = "高度图坐标倍率；越大山海分布越密集",
            ["terrain.height.frequency"] = "高度图基础频率；越大起伏切换越快",
            ["terrain.height.octaves"] = "高度图叠加的细节层数；越高细节越多、生成越慢",
            ["terrain.height.lacunarity"] = "每层细节缩小的速度；越大细节尺度跨度越大",
            ["terrain.height.persistence"] = "后续小细节保留的强度；越大地表越粗糙",
            ["terrain.height.offsetX"] = "沿 X 方向平移整张高度噪声图",
            ["terrain.height.offsetY"] = "沿 Y 方向平移整张高度噪声图",
            ["terrain.height.secondaryBoostEnabled"] = "是否再次拉开高地和低地的高度差",
            ["terrain.height.secondaryBoostStrength"] = "越大高低差越明显，海陆分界更强",
            ["climate.precipitation.coordScale"] = "降水图坐标倍率；越大干湿区域越密集",
            ["climate.precipitation.frequency"] = "降水图基础频率；越大干湿变化越快",
            ["climate.precipitation.octaves"] = "降水图细节层数；越高局部雨量变化越细",
            ["climate.precipitation.lacunarity"] = "降水每层细节缩小的速度",
            ["climate.precipitation.persistence"] = "降水小细节保留强度；越大雨量分布越碎",
            ["climate.precipitation.offsetX"] = "沿 X 方向平移整张降水噪声图",
            ["climate.precipitation.offsetY"] = "沿 Y 方向平移整张降水噪声图",
            ["climate.temperature.coordScale"] = "温度图坐标倍率；越大冷热区域越密集",
            ["climate.temperature.frequency"] = "温度图基础频率；越大冷热变化越快",
            ["climate.temperature.octaves"] = "温度图细节层数；越高局部温差越细碎",
            ["climate.temperature.lacunarity"] = "温度每层细节缩小的速度",
            ["climate.temperature.persistence"] = "温度小细节保留强度；越大温度分布越碎",
            ["climate.temperature.offsetX"] = "沿 X 方向平移整张温度噪声图",
            ["climate.temperature.offsetY"] = "沿 Y 方向平移整张温度噪声图",
            ["climate.temperature.celsiusMin"] = "归一化最低温对应的摄氏温度，仅影响环境温度数值",
            ["climate.temperature.celsiusMax"] = "归一化最高温对应的摄氏温度，仅影响环境温度数值",
            ["climate.wind.regionSize"] = "一块稳定风向区域的大小；越大风向变化越缓",
            ["climate.wind.seedSalt"] = "改变风场排列，不改变世界种子和高度图",
            ["climate.orographic.sampleDistance"] = "向上风方向检查山体的距离；越大雨影影响更远",
            ["climate.orographic.sampleCount"] = "迎风坡采样次数；越高判断更细、计算更多",
            ["climate.orographic.windwardGain"] = "迎风坡增雨强度；越大山前越湿",
            ["climate.orographic.leewardLoss"] = "背风坡减雨强度；越大山后越干",
            ["river.enabled"] = "是否按照正式地势和降水结果生成河流",
            ["river.hydrologyRegionSize"] = "水文计算分区边长；越大跨区河网更完整但更耗时",
            ["river.runoffCellSize"] = "汇总降水并寻找河源的网格大小；越小潜在河源越密",
            ["river.runoffSampleStride"] = "径流网格内部的采样间隔；越小越精确也越慢",
            ["river.maxTraceSteps"] = "单条河最多向下游追踪多少格；越大河可能更长",
            ["river.minimumVisibleCourseLength"] = "短于该长度的零碎河段整条隐藏",
            ["river.infiltrationFloor"] = "低于该降水量时水被地面吸收；越高河流越少",
            ["river.startFlow"] = "形成成熟可见主河所需的累计水量；越高主河越少",
            ["river.tributaryStartFlow"] = "细支流接入主河所需水量；越低支流越多",
            ["river.fullWidthFlow"] = "河流长到最大宽度所需水量；越低大河越早变宽",
            ["river.maxWidth"] = "河道允许达到的最大格宽",
            ["river.meanderTieTolerance"] = "旧版水文在近似等高路线间的轻微选路扰动",
            ["river.meanderStrength"] = "河道连续转弯强度；越大越弯，但仍必须向下坡",
            ["river.meanderScale"] = "河弯的尺度；越大弯道越长、越舒缓",
            ["river.valleyDetailWeight"] = "河流贴着细小谷底走的倾向；越大越贴地形",
            ["river.lookAheadWeight"] = "选择下游时参考前方谷地的强度；越大越少短视锯齿",
            ["river.lookAheadDistance"] = "选择下游方向时向前查看的格数",
            ["river.floodplainStartFlow"] = "开始生成河岸冲积平原所需水量；越高冲积带越少",
            ["river.floodplainMaxRadius"] = "冲积平原向河道两侧扩展的最大格数",
            ["river.floodplainMaxSlope"] = "允许生成宽冲积平原的最大坡度；越高陡坡也会铺开",
            ["river.alluvialTileThreshold"] = "冲积强度超过该值才换成沙土 Tile",
            ["river.depthMin"] = "小河的最浅深度表现值",
            ["river.depthMax"] = "大河的最深深度表现值",
            ["river.minLakeCells"] = "旧版水文中盆地至少多大才显示为湖泊",
            ["river.maxLakeCells"] = "旧版水文中单个湖泊允许扩张的最大格数",
            ["river.maxLakeLevelRise"] = "旧版湖面相对洼地最多抬高多少",
            ["river.lakeMinFlow"] = "旧版盆地形成湖泊所需的最低累计水量",
            ["river.maxCachedRegions"] = "旧版水文最多缓存多少个区域；越大越占内存",
            ["grass.density"] = "合适陆地长草的基础概率；越大草越密",
            ["structure.enabled"] = "是否按种子放置遗迹等简化结构",
            ["structure.regionSize"] = "结构候选网格大小；越大结构通常越稀疏",
            ["structure.spawnChance"] = "每个候选区域出现结构的概率",
            ["structure.radius"] = "简化结构从中心向外覆盖的格数",
            ["structure.groundTileId"] = "结构覆盖地面时使用的 Tile 数字编号",
            ["terrain.biomeCount"] = "旧配置兼容项，当前群系编号固定为 0～7",
            ["biome.desert.minimumHeight"] = "低于该高度不判定沙漠，避免沙漠贴进海里",
            ["biome.desert.maximumPrecipitation"] = "降水高于该值不判定沙漠；越低沙漠越少",
            ["biome.grassland.minimumTemperature"] = "温度低于该值不判定温带草原",
            ["biome.grassland.maximumTemperature"] = "温度高于该值不判定温带草原",
            ["biome.grassland.minimumPrecipitation"] = "降水低于该值不判定温带草原",
            ["biome.grassland.maximumPrecipitation"] = "降水高于该值不判定温带草原，通常转森林",
            ["navigation.defaultCost"] = "普通地面的寻路代价；越大角色越不愿经过"
        };

    #endregion

    #region 序列化状态

    [SerializeField] private ChunkGenerationProfileSO profileAsset;
    [SerializeField] private int worldSeed = -329089282;
    [SerializeField] private int centerX;
    [SerializeField] private int centerY;
    [SerializeField] private int previewWidth = 256;
    [SerializeField] private int previewHeight = 256;
    [SerializeField] private bool wrappedWorld;
    [SerializeField] private int wrappedWorldRadius = PlanetData.DefaultRadius;
    [SerializeField] private PreviewDisplayMode displayMode;
    [SerializeField] private float canvasZoom = 1f;
    [SerializeField] private Vector2 canvasPan;
    [SerializeField] private List<NumericParameterValue> numericParameters = new();
    [SerializeField] private string parameterSearch = string.Empty;
    [SerializeField] private bool showAdvancedParameters;
    [SerializeField] private Vector2 settingsScroll;

    #endregion

    #region 运行状态

    private Texture2D previewTexture;
    private PreviewResult previewResult;
    private Task<PreviewResult> generationTask;
    private CancellationTokenSource generationCancellation;
    private double generationStartedAt;
    private string statusMessage = "请选择 Profile 并点击右下角“生成预览”。";
    private MessageType statusType = MessageType.Info;
    private GUIStyle advancedParameterLabelStyle;

    #endregion

    #region 菜单与生命周期

    [MenuItem("FlatWorld/世界生成/地形预览器", priority = 10)]
    private static void OpenWindow()
    {
        WorldTerrainPreviewWindow window = GetWindow<WorldTerrainPreviewWindow>("地形预览器");
        window.minSize = new Vector2(900f, 600f);
        window.Show();
    }

    private void OnEnable()
    {
        minSize = new Vector2(900f, 600f);
        if (profileAsset == null)
            profileAsset = AssetDatabase.LoadAssetAtPath<ChunkGenerationProfileSO>(DefaultProfilePath);
        if (profileAsset != null && numericParameters.Count == 0)
            ResetParametersFromProfile();
        EditorApplication.update += PollGeneration;
    }

    private void OnDisable()
    {
        EditorApplication.update -= PollGeneration;
        CancelGeneration();
        DestroyPreviewTexture();
    }

    #endregion

    #region 界面

    private void OnGUI()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawPreviewPanel();
            DrawSettingsPanel();
        }
    }

    /// <summary>绘制左侧预览画布、状态和悬停格子信息。</summary>
    private void DrawPreviewPanel()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("地形画布", EditorStyles.boldLabel);
                GUILayout.Label("滚轮缩放 · 右键拖动", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(previewTexture == null))
                {
                    GUILayout.Label($"{canvasZoom * 100f:0}%", EditorStyles.miniLabel,
                        GUILayout.Width(48f));
                    if (GUILayout.Button("适应", EditorStyles.toolbarButton, GUILayout.Width(48f)))
                        ResetCanvasView();
                    if (GUILayout.Button("导出 PNG", EditorStyles.toolbarButton, GUILayout.Width(74f)))
                        ExportPreviewPng();
                }
            }

            Rect canvasRect = GUILayoutUtility.GetRect(
                320f,
                10000f,
                320f,
                10000f,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(canvasRect, new Color(0.105f, 0.11f, 0.12f));
            DrawPreviewTexture(canvasRect);

            if (previewResult != null)
            {
                string staleSuffix = IsPreviewOutdated()
                    ? "  【输入已修改，当前仍是上次结果】"
                    : string.Empty;
                string wrappedSuffix = previewResult.TiledWrappedPeriod
                    ? $"  循环平铺 {previewResult.TopologySpanX}×{previewResult.TopologySpanY}"
                    : string.Empty;
                EditorGUILayout.LabelField(
                    $"范围 X[{previewResult.OriginX}, {previewResult.OriginX + previewResult.Width - 1}]  " +
                    $"Y[{previewResult.OriginY}, {previewResult.OriginY + previewResult.Height - 1}]  " +
                    $"耗时 {previewResult.ElapsedMilliseconds} ms{wrappedSuffix}{staleSuffix}",
                    EditorStyles.miniLabel);
            }
        }
    }

    /// <summary>绘制可缩放、可右键拖动的 Texture2D，并显示鼠标当前格子的生成数据。</summary>
    private void DrawPreviewTexture(Rect canvasRect)
    {
        if (previewTexture == null)
        {
            GUI.Label(canvasRect, "尚未生成预览", CenteredLabelStyle());
            return;
        }

        Rect fitRect = CalculateFitRect(canvasRect, previewTexture.width, previewTexture.height);
        HandleCanvasInput(canvasRect, fitRect);
        ClampCanvasPan(canvasRect, fitRect);
        Rect textureRect = CalculateZoomedTextureRect(fitRect);

        // 放大后的图片可能超出画布，只在左侧图片区域内绘制，避免覆盖右侧参数面板。
        GUI.BeginClip(canvasRect);
        var localTextureRect = new Rect(
            textureRect.x - canvasRect.x,
            textureRect.y - canvasRect.y,
            textureRect.width,
            textureRect.height);
        GUI.DrawTexture(localTextureRect, previewTexture, ScaleMode.StretchToFill, false);
        EditorGUI.DrawRect(
            new Rect(localTextureRect.xMin, localTextureRect.yMin, localTextureRect.width, 1f),
            Color.black);
        EditorGUI.DrawRect(
            new Rect(localTextureRect.xMin, localTextureRect.yMax - 1f, localTextureRect.width, 1f),
            Color.black);
        EditorGUI.DrawRect(
            new Rect(localTextureRect.xMin, localTextureRect.yMin, 1f, localTextureRect.height),
            Color.black);
        EditorGUI.DrawRect(
            new Rect(localTextureRect.xMax - 1f, localTextureRect.yMin, 1f, localTextureRect.height),
            Color.black);
        GUI.EndClip();

        Event current = Event.current;
        if (previewResult == null || !canvasRect.Contains(current.mousePosition) ||
            !textureRect.Contains(current.mousePosition))
            return;

        float normalizedX = Mathf.Clamp01((current.mousePosition.x - textureRect.xMin) / textureRect.width);
        float normalizedY = Mathf.Clamp01((current.mousePosition.y - textureRect.yMin) / textureRect.height);
        int pixelX = Mathf.Min(previewResult.Width - 1,
            Mathf.FloorToInt(normalizedX * previewResult.Width));
        int pixelY = Mathf.Min(previewResult.Height - 1,
            Mathf.FloorToInt((1f - normalizedY) * previewResult.Height));
        int index = pixelY * previewResult.Width + pixelX;
        TerrainCellFlags flags = previewResult.Flags[index];
        string hoverText =
            $"世界格 ({previewResult.OriginX + pixelX}, {previewResult.OriginY + pixelY})\n" +
            $"高度 {previewResult.Heights[index]:0.0000}  " +
            $"群系 {SurfaceBiomeClassifier.GetLegacyName(previewResult.Biomes[index])}  " +
            $"Tile {previewResult.GroundTileIds[index]}\n" +
            $"水 {((flags & TerrainCellFlags.Water) != 0 ? "是" : "否")}  " +
            $"可行走 {((flags & TerrainCellFlags.Walkable) != 0 ? "是" : "否")}";
        Vector2 labelSize = EditorStyles.helpBox.CalcSize(new GUIContent(hoverText));
        var labelRect = new Rect(
            canvasRect.xMin + 8f,
            canvasRect.yMax - labelSize.y - 12f,
            Mathf.Min(canvasRect.width - 16f, Mathf.Max(260f, labelSize.x + 16f)),
            labelSize.y + 8f);
        GUI.Box(labelRect, hoverText, EditorStyles.helpBox);
        Repaint();
    }

    /// <summary>绘制右侧参数、诊断统计与右下角生成按钮。</summary>
    private void DrawSettingsPanel()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox,
                   GUILayout.Width(SettingsPanelWidth), GUILayout.ExpandHeight(true)))
        {
            settingsScroll = EditorGUILayout.BeginScrollView(settingsScroll);
            DrawSourceSettings();
            EditorGUILayout.Space(6f);
            DrawPreviewSettings();
            EditorGUILayout.Space(6f);
            DrawCommonTerrainSettings();
            EditorGUILayout.Space(6f);
            DrawAdvancedParameters();
            EditorGUILayout.Space(6f);
            DrawResultSummary();
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox(statusMessage, statusType);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (generationTask != null)
                {
                    if (GUILayout.Button("取消", GUILayout.Height(32f), GUILayout.Width(82f)))
                        CancelGeneration();
                }
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(profileAsset == null || generationTask != null))
                {
                    if (GUILayout.Button("生成预览", GUILayout.Height(32f), GUILayout.Width(126f)))
                        StartGeneration();
                }
            }
        }
    }

    private void DrawSourceSettings()
    {
        EditorGUILayout.LabelField("生成源", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        ChunkGenerationProfileSO selected = (ChunkGenerationProfileSO)EditorGUILayout.ObjectField(
            "地形 Profile", profileAsset, typeof(ChunkGenerationProfileSO), false);
        if (EditorGUI.EndChangeCheck())
        {
            profileAsset = selected;
            ResetParametersFromProfile();
        }

        worldSeed = EditorGUILayout.IntField("世界种子", worldSeed);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(EditorGUIUtility.labelWidth);
            if (GUILayout.Button("随机种子", GUILayout.Width(88f)))
                worldSeed = Guid.NewGuid().GetHashCode();
            if (GUILayout.Button("复制种子", GUILayout.Width(88f)))
                EditorGUIUtility.systemCopyBuffer = worldSeed.ToString();
        }
    }

    private void DrawPreviewSettings()
    {
        EditorGUILayout.LabelField("预览范围", EditorStyles.boldLabel);
        centerX = EditorGUILayout.IntField("中心 X", centerX);
        centerY = EditorGUILayout.IntField("中心 Y", centerY);
        previewWidth = Mathf.Clamp(EditorGUILayout.IntField("画面宽度（格/像素）", previewWidth),
            MinimumPreviewSize, MaximumPreviewSize);
        previewHeight = Mathf.Clamp(EditorGUILayout.IntField("画面高度（格/像素）", previewHeight),
            MinimumPreviewSize, MaximumPreviewSize);

        EditorGUI.BeginChangeCheck();
        PreviewDisplayMode selectedMode = (PreviewDisplayMode)EditorGUILayout.EnumPopup(
            "显示方式", displayMode);
        if (EditorGUI.EndChangeCheck())
        {
            displayMode = selectedMode;
            RebuildPreviewTexture();
        }

        wrappedWorld = EditorGUILayout.Toggle("环绕世界", wrappedWorld);
        if (wrappedWorld)
        {
            wrappedWorldRadius = Mathf.Clamp(EditorGUILayout.IntField("星球半径", wrappedWorldRadius),
                1, 1000000);
            DrawWrappedWorldPreviewHelpers();
        }

        long pixels = (long)previewWidth * previewHeight;
        EditorGUILayout.LabelField($"预计采样：{pixels:N0} 格（1 格 = 1 像素）", EditorStyles.miniLabel);
        if (pixels > 512L * 512L)
        {
            EditorGUILayout.HelpBox(
                "高分辨率且启用河流时会明显变慢；生成在后台执行，可随时取消。",
                MessageType.Warning);
        }
    }

    private void DrawCommonTerrainSettings()
    {
        EditorGUILayout.LabelField("常用地形参数", EditorStyles.boldLabel);
        if (numericParameters.Count == 0)
        {
            EditorGUILayout.HelpBox("当前 Profile 没有可编辑数值参数。", MessageType.Warning);
            return;
        }

        float previousLabelWidth = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 365f;
        try
        {
            DrawDoubleField("world.coordinateScale", "世界坐标缩放");
            DrawSlider("terrain.seaLevel", "海平面", 0f, 1f);
            DrawSlider("terrain.beachLevel", "沙滩上限", 0f, 1f);
            DrawSlider("terrain.mountainLevel", "山地阈值", 0f, 1f);

            EditorGUILayout.Space(3f);
            EditorGUILayout.LabelField("高度噪声", EditorStyles.miniBoldLabel);
            DrawDoubleField("terrain.height.coordScale", "坐标倍率");
            DrawDoubleField("terrain.height.frequency", "基础频率");
            DrawIntegerSlider("terrain.height.octaves", "八度", 1, 12);
            DrawDoubleField("terrain.height.lacunarity", "频率增幅");
            DrawDoubleField("terrain.height.persistence", "振幅衰减");
            DrawDoubleField("terrain.height.offsetX", "偏移 X");
            DrawDoubleField("terrain.height.offsetY", "偏移 Y");
            DrawToggle("terrain.height.secondaryBoostEnabled", "二次增强");
            DrawDoubleField("terrain.height.secondaryBoostStrength", "增强强度");

            EditorGUILayout.Space(3f);
            EditorGUILayout.LabelField("气候与附加层", EditorStyles.miniBoldLabel);
            DrawDoubleField("climate.precipitation.coordScale", "降水坐标倍率");
            DrawDoubleField("climate.temperature.coordScale", "温度坐标倍率");
            DrawToggle("river.enabled", "生成河流");
            DrawToggle("structure.enabled", "生成结构");
        }
        finally
        {
            EditorGUIUtility.labelWidth = previousLabelWidth;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("从 Profile 重新读取"))
                ResetParametersFromProfile();

            using (new EditorGUI.DisabledScope(profileAsset == null || generationTask != null))
            {
                if (GUILayout.Button("应用当前参数到 Profile SO"))
                    ApplyCurrentParametersToProfile();
            }
        }
        EditorGUILayout.HelpBox(
            "“应用”会直接保存所选 Profile SO，并支持撤销。世界坐标缩放属于当前 PlanetData，" +
            "进入世界时会覆盖 Profile，因此不会写入。",
            MessageType.Info);
    }

    private void DrawAdvancedParameters()
    {
        showAdvancedParameters = EditorGUILayout.Foldout(
            showAdvancedParameters, "高级参数（Profile 全量）", true);
        if (!showAdvancedParameters)
            return;

        parameterSearch = EditorGUILayout.TextField("筛选", parameterSearch);
        IEnumerable<NumericParameterValue> parameters = numericParameters
            .Where(parameter => parameter != null &&
                                (string.IsNullOrWhiteSpace(parameterSearch) ||
                                 parameter.Id.IndexOf(parameterSearch,
                                     StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 GetParameterDescription(parameter.Id).IndexOf(
                                     parameterSearch, StringComparison.OrdinalIgnoreCase) >= 0))
            .OrderBy(parameter => CommonParameterIds.Contains(parameter.Id) ? 1 : 0)
            .ThenBy(parameter => parameter.Id, StringComparer.Ordinal);

        advancedParameterLabelStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            wordWrap = true,
            alignment = TextAnchor.MiddleLeft
        };
        foreach (NumericParameterValue parameter in parameters)
        {
            string description = GetParameterDescription(parameter.Id);
            string displayName = string.IsNullOrWhiteSpace(description)
                ? parameter.Id
                : $"{parameter.Id}（{description}）";
            var content = new GUIContent(displayName, description);
            float labelHeight = Mathf.Max(
                EditorGUIUtility.singleLineHeight,
                advancedParameterLabelStyle.CalcHeight(content, 365f));
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(content, advancedParameterLabelStyle,
                    GUILayout.Height(labelHeight), GUILayout.Width(365f));
                parameter.Value = EditorGUILayout.DoubleField(parameter.Value, GUILayout.Width(112f));
            }
        }
    }

    private void DrawResultSummary()
    {
        EditorGUILayout.LabelField("生成诊断", EditorStyles.boldLabel);
        if (previewResult == null)
        {
            EditorGUILayout.LabelField("尚无结果", EditorStyles.miniLabel);
            return;
        }

        if (IsPreviewOutdated())
        {
            EditorGUILayout.HelpBox(
                "右侧输入已经改变，左侧仍是上一次生成结果。请点击右下角“生成预览”后再判断地图。",
                MessageType.Warning);
        }

        EditorGUILayout.LabelField("高度范围",
            $"{previewResult.MinimumHeight:0.0000} ～ {previewResult.MaximumHeight:0.0000}");
        EditorGUILayout.LabelField("平均高度", previewResult.AverageHeight.ToString("0.0000"));
        EditorGUILayout.LabelField("水域占比", previewResult.WaterRatio.ToString("P2"));
        EditorGUILayout.LabelField("可行走占比", previewResult.WalkableRatio.ToString("P2"));

        if (previewResult.WaterRatio >= 0.995d)
        {
            EditorGUILayout.HelpBox(
                "该范围几乎全是水。优先检查世界坐标缩放、海平面和高度噪声参数。",
                MessageType.Error);
        }
        else if (previewResult.WalkableRatio <= 0.01d)
        {
            EditorGUILayout.HelpBox("该范围几乎没有可行走格。", MessageType.Warning);
        }
    }

    #endregion

    #region 参数操作

    /// <summary>把 Profile 参数复制为窗口私有值，并补上运行时星球才提供的世界坐标缩放。</summary>
    private void ResetParametersFromProfile()
    {
        numericParameters.Clear();
        if (profileAsset == null)
            return;

        try
        {
            ChunkGenerationProfileSnapshot snapshot = profileAsset.CreateSnapshot();
            foreach (KeyValuePair<string, double> pair in snapshot.NumericParameters
                         .OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                numericParameters.Add(new NumericParameterValue { Id = pair.Key, Value = pair.Value });
            }

            if (FindParameter(RuntimeWorldCoordinateScaleId) == null)
            {
                numericParameters.Add(new NumericParameterValue
                {
                    Id = RuntimeWorldCoordinateScaleId,
                    Value = snapshot.Settings.WorldCoordinateScale
                });
            }
            statusMessage = $"已读取 Profile：{snapshot.ProfileId}，参数 {numericParameters.Count} 项。";
            statusType = MessageType.Info;
        }
        catch (Exception exception)
        {
            statusMessage = "读取 Profile 失败：" + exception.Message;
            statusType = MessageType.Error;
        }
    }

    /// <summary>经确认后把窗口中的有效数值覆盖到所选 Profile，并立即保存资源。</summary>
    private void ApplyCurrentParametersToProfile()
    {
        if (profileAsset == null || generationTask != null)
            return;

        string assetPath = AssetDatabase.GetAssetPath(profileAsset);
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            statusMessage = "应用失败：当前对象不是可保存的项目资源。";
            statusType = MessageType.Error;
            return;
        }

        try
        {
            Dictionary<string, double> values = numericParameters
                .Where(parameter => parameter != null &&
                                    !string.IsNullOrWhiteSpace(parameter.Id) &&
                                    !string.Equals(parameter.Id, RuntimeWorldCoordinateScaleId,
                                        StringComparison.Ordinal) &&
                                    !double.IsNaN(parameter.Value) &&
                                    !double.IsInfinity(parameter.Value))
                .GroupBy(parameter => parameter.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last().Value,
                    StringComparer.Ordinal);

            var serializedProfile = new SerializedObject(profileAsset);
            serializedProfile.Update();
            SerializedProperty serializedParameters =
                serializedProfile.FindProperty(ProfileNumericParametersPropertyName);
            if (serializedParameters == null || !serializedParameters.isArray)
                throw new InvalidOperationException("Profile 数值参数列表无法序列化。");

            int matchedCount = 0;
            int changedCount = 0;
            for (int index = 0; index < serializedParameters.arraySize; index++)
            {
                SerializedProperty element = serializedParameters.GetArrayElementAtIndex(index);
                SerializedProperty idProperty =
                    element.FindPropertyRelative(ProfileParameterIdPropertyName);
                SerializedProperty valueProperty =
                    element.FindPropertyRelative(ProfileParameterValuePropertyName);
                if (idProperty == null || valueProperty == null ||
                    !values.TryGetValue(idProperty.stringValue, out double value))
                {
                    continue;
                }

                matchedCount++;
                if (!valueProperty.doubleValue.Equals(value))
                    changedCount++;
            }

            if (matchedCount == 0)
                throw new InvalidOperationException("窗口参数与所选 Profile 没有匹配项。");

            string confirmation =
                $"确定把当前窗口参数应用到“{profileAsset.name}”吗？\n\n" +
                $"资源：{assetPath}\n匹配参数：{matchedCount} 项\n将修改：{changedCount} 项\n\n" +
                "世界坐标缩放不会写入，它由当前世界的 PlanetData 管理。";
            if (!EditorUtility.DisplayDialog("应用地形参数到 Profile SO", confirmation,
                    "应用并保存", "取消"))
            {
                return;
            }

            if (changedCount == 0)
            {
                statusMessage = $"{profileAsset.name} 已经与当前可保存参数一致，无需修改。";
                statusType = MessageType.Info;
                return;
            }

            Undo.RecordObject(profileAsset, "应用地形预览参数到 Profile SO");
            serializedProfile.Update();
            serializedParameters = serializedProfile.FindProperty(ProfileNumericParametersPropertyName);
            for (int index = 0; index < serializedParameters.arraySize; index++)
            {
                SerializedProperty element = serializedParameters.GetArrayElementAtIndex(index);
                SerializedProperty idProperty =
                    element.FindPropertyRelative(ProfileParameterIdPropertyName);
                SerializedProperty valueProperty =
                    element.FindPropertyRelative(ProfileParameterValuePropertyName);
                if (idProperty != null && valueProperty != null &&
                    values.TryGetValue(idProperty.stringValue, out double value))
                {
                    valueProperty.doubleValue = value;
                }
            }

            serializedProfile.ApplyModifiedProperties();
            EditorUtility.SetDirty(profileAsset);
            AssetDatabase.SaveAssetIfDirty(profileAsset);
            statusMessage =
                $"已应用并保存 {profileAsset.name}：更新 {changedCount} 项；世界坐标缩放未写入。";
            statusType = MessageType.Info;
            Debug.Log($"[地形预览器] 已应用 {changedCount} 项参数到 {assetPath}。", profileAsset);
        }
        catch (Exception exception)
        {
            statusMessage = "应用 Profile 失败：" + exception.Message;
            statusType = MessageType.Error;
            Debug.LogException(exception, profileAsset);
        }
    }

    private NumericParameterValue FindParameter(string id)
    {
        return numericParameters.FirstOrDefault(parameter =>
            parameter != null && string.Equals(parameter.Id, id, StringComparison.Ordinal));
    }

    /// <summary>显示环绕周期，并提供一周期和 2×2 重复验证范围快捷设置。</summary>
    private void DrawWrappedWorldPreviewHelpers()
    {
        if (profileAsset == null || profileAsset.ChunkWidth <= 0 || profileAsset.ChunkHeight <= 0)
            return;

        int halfX = AlignUp(wrappedWorldRadius, profileAsset.ChunkWidth);
        int halfY = AlignUp(wrappedWorldRadius, profileAsset.ChunkHeight);
        int spanX = checked(halfX * 2);
        int spanY = checked(halfY * 2);
        EditorGUILayout.HelpBox(
            $"实际循环周期：{spanX} × {spanY} 格。环绕表示右边界的下一格接左边界，" +
            "边缘两列不会是相同像素。要直观看重复，请设置 2×2 后重新生成。",
            MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            bool canShowOnePeriod = spanX <= MaximumPreviewSize && spanY <= MaximumPreviewSize;
            using (new EditorGUI.DisabledScope(!canShowOnePeriod))
            {
                if (GUILayout.Button("设置为完整一周期"))
                    SetWrappedPreviewCopies(halfX, halfY, spanX, spanY, 1);
            }

            bool canShowFourCopies = spanX <= MaximumPreviewSize / 2 &&
                                     spanY <= MaximumPreviewSize / 2;
            using (new EditorGUI.DisabledScope(!canShowFourCopies))
            {
                if (GUILayout.Button("设置为 2×2 循环验证"))
                    SetWrappedPreviewCopies(halfX, halfY, spanX, spanY, 2);
            }
        }
    }

    /// <summary>让预览范围从环绕世界最小边界开始，整齐显示指定数量的周期副本。</summary>
    private void SetWrappedPreviewCopies(int halfX, int halfY, int spanX, int spanY, int copies)
    {
        previewWidth = checked(spanX * copies);
        previewHeight = checked(spanY * copies);
        centerX = checked(-halfX + previewWidth / 2);
        centerY = checked(-halfY + previewHeight / 2);
    }

    private void DrawDoubleField(string id, string label)
    {
        NumericParameterValue parameter = FindParameter(id);
        if (parameter != null)
            parameter.Value = EditorGUILayout.DoubleField(BuildParameterLabel(id, label), parameter.Value);
    }

    private void DrawSlider(string id, string label, float minimum, float maximum)
    {
        NumericParameterValue parameter = FindParameter(id);
        if (parameter != null)
        {
            parameter.Value = EditorGUILayout.Slider(
                BuildParameterLabel(id, label), (float)parameter.Value, minimum, maximum);
        }
    }

    private void DrawIntegerSlider(string id, string label, int minimum, int maximum)
    {
        NumericParameterValue parameter = FindParameter(id);
        if (parameter != null)
        {
            parameter.Value = EditorGUILayout.IntSlider(
                BuildParameterLabel(id, label), Mathf.RoundToInt((float)parameter.Value),
                minimum, maximum);
        }
    }

    private void DrawToggle(string id, string label)
    {
        NumericParameterValue parameter = FindParameter(id);
        if (parameter != null)
        {
            parameter.Value = EditorGUILayout.Toggle(
                BuildParameterLabel(id, label), parameter.Value > 0.5d) ? 1d : 0d;
        }
    }

    /// <summary>把常用参数的中文名称和通俗作用拼成“名称（作用）”。</summary>
    private static string BuildParameterLabel(string id, string displayName)
    {
        string description = GetParameterDescription(id);
        return string.IsNullOrWhiteSpace(description)
            ? displayName
            : $"{displayName}（{description}）";
    }

    /// <summary>读取参数说明；MOD 自定义键没有内置说明时返回空文本。</summary>
    private static string GetParameterDescription(string id)
    {
        return !string.IsNullOrWhiteSpace(id) &&
               ParameterDescriptions.TryGetValue(id, out string description)
            ? description
            : string.Empty;
    }

    /// <summary>判断右侧生成输入是否已经不同于左侧画面对应的输入。</summary>
    private bool IsPreviewOutdated()
    {
        return previewResult != null &&
               previewResult.InputFingerprint != CalculateCurrentInputFingerprint();
    }

    /// <summary>为所有会改变生成结果的窗口输入计算轻量指纹，不包含显示模式和画布缩放。</summary>
    private ulong CalculateCurrentInputFingerprint()
    {
        ulong hash = 14695981039346656037UL;
        AddFingerprint(ref hash, profileAsset != null ? profileAsset.GetInstanceID() : 0);
        if (profileAsset != null)
        {
            AddFingerprint(ref hash, profileAsset.ProfileId);
            AddFingerprint(ref hash, profileAsset.GenerationSignature);
            AddFingerprint(ref hash, profileAsset.ChunkWidth);
            AddFingerprint(ref hash, profileAsset.ChunkHeight);
        }
        AddFingerprint(ref hash, worldSeed);
        AddFingerprint(ref hash, centerX);
        AddFingerprint(ref hash, centerY);
        AddFingerprint(ref hash, previewWidth);
        AddFingerprint(ref hash, previewHeight);
        AddFingerprint(ref hash, wrappedWorld ? 1 : 0);
        AddFingerprint(ref hash, wrappedWorldRadius);

        foreach (NumericParameterValue parameter in numericParameters
                     .Where(parameter => parameter != null)
                     .OrderBy(parameter => parameter.Id, StringComparer.Ordinal))
        {
            AddFingerprint(ref hash, parameter.Id);
            AddFingerprint(ref hash, BitConverter.DoubleToInt64Bits(parameter.Value));
        }
        return hash;
    }

    private static void AddFingerprint(ref ulong hash, string value)
    {
        if (value == null)
        {
            AddFingerprint(ref hash, -1L);
            return;
        }

        for (int i = 0; i < value.Length; i++)
            AddFingerprint(ref hash, value[i]);
        AddFingerprint(ref hash, 0L);
    }

    private static void AddFingerprint(ref ulong hash, long value)
    {
        unchecked
        {
            for (int byteIndex = 0; byteIndex < sizeof(long); byteIndex++)
            {
                hash ^= (byte)(value >> (byteIndex * 8));
                hash *= 1099511628211UL;
            }
        }
    }

    #endregion

    #region 生成流程

    /// <summary>捕获当前窗口输入并启动后台纯数据生成。</summary>
    private void StartGeneration()
    {
        if (profileAsset == null || generationTask != null)
            return;

        try
        {
            PreviewInput input = BuildPreviewInput();
            generationCancellation = new CancellationTokenSource();
            CancellationToken token = generationCancellation.Token;
            generationStartedAt = EditorApplication.timeSinceStartup;
            statusMessage = "正在后台生成地形……";
            statusType = MessageType.Info;
            generationTask = Task.Run(() => GeneratePreview(input, token), token);
        }
        catch (Exception exception)
        {
            statusMessage = "无法开始生成：" + exception.Message;
            statusType = MessageType.Error;
            Debug.LogException(exception);
        }
    }

    private PreviewInput BuildPreviewInput()
    {
        int width = Mathf.Clamp(previewWidth, MinimumPreviewSize, MaximumPreviewSize);
        int height = Mathf.Clamp(previewHeight, MinimumPreviewSize, MaximumPreviewSize);
        int originX = checked(centerX - width / 2);
        int originY = checked(centerY - height / 2);
        ChunkGenerationProfileSnapshot source = profileAsset.CreateSnapshot();

        Dictionary<string, double> numbers = source.NumericParameters.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
        foreach (NumericParameterValue parameter in numericParameters)
        {
            if (parameter == null || string.IsNullOrWhiteSpace(parameter.Id) ||
                double.IsNaN(parameter.Value) || double.IsInfinity(parameter.Value))
            {
                continue;
            }
            numbers[parameter.Id] = parameter.Value;
        }

        ChunkGenerationTopologySnapshot topology = default;
        if (wrappedWorld)
        {
            int halfX = AlignUp(wrappedWorldRadius, source.Width);
            int halfY = AlignUp(wrappedWorldRadius, source.Height);
            topology = new ChunkGenerationTopologySnapshot(
                new Int2(-halfX, -halfY),
                new Int2(checked(halfX * 2), checked(halfY * 2)));
        }

        int generationOriginX = originX;
        int generationOriginY = originY;
        int generationWidth = width;
        int generationHeight = height;
        bool tileWrappedPeriod = topology.IsWrapped &&
                                 (width > topology.Span.X || height > topology.Span.Y) &&
                                 topology.Span.X <= MaximumPreviewSize &&
                                 topology.Span.Y <= MaximumPreviewSize;
        if (tileWrappedPeriod)
        {
            // 2×2 等循环验证只生成一份正式周期，再按规范坐标平铺，速度和内存不随副本数暴涨。
            generationOriginX = topology.Min.X;
            generationOriginY = topology.Min.Y;
            generationWidth = topology.Span.X;
            generationHeight = topology.Span.Y;
        }

        // 预览用一个连续大区块完成，避免逐个 16x16 区块重复构建水文图；每格仍走正式生成器。
        var profile = new ChunkGenerationProfileSnapshot(
            source.ProfileId + ".editor-preview",
            source.Signature,
            generationWidth,
            generationHeight,
            numbers,
            source.TextParameters.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal));

        return new PreviewInput
        {
            InputFingerprint = CalculateCurrentInputFingerprint(),
            Seed = worldSeed,
            OriginX = originX,
            OriginY = originY,
            Width = width,
            Height = height,
            GenerationOriginX = generationOriginX,
            GenerationOriginY = generationOriginY,
            TileWrappedPeriod = tileWrappedPeriod,
            Profile = profile,
            Topology = topology
        };
    }

    /// <summary>在后台复用正式生成器，并提取绘图与全水诊断需要的纯数据。</summary>
    private static PreviewResult GeneratePreview(PreviewInput input, CancellationToken token)
    {
        var stopwatch = Stopwatch.StartNew();
        var generator = new DeterministicChunkGenerator();
        var request = new ChunkGenerationRequest(
            1,
            new FlatWorld.WorldModel.WorldAddress(
                "surface", new Int2(input.GenerationOriginX, input.GenerationOriginY)),
            input.Seed,
            1,
            input.Profile,
            input.Topology);
        using ChunkGenerationResult generationResult = generator.Generate(request, token);
        using ChunkTerrainData terrain = generationResult.ConsumeTerrain();

        int cellCount = checked(input.Width * input.Height);
        var heights = new float[cellCount];
        var biomes = new byte[cellCount];
        var flags = new TerrainCellFlags[cellCount];
        var groundTileIds = new int[cellCount];
        double minimumHeight = double.MaxValue;
        double maximumHeight = double.MinValue;
        double totalHeight = 0d;
        int waterCount = 0;
        int walkableCount = 0;

        for (int y = 0; y < input.Height; y++)
        {
            for (int x = 0; x < input.Width; x++)
            {
                int index = y * input.Width + x;
                if ((index & 255) == 0)
                    token.ThrowIfCancellationRequested();

                int sampleX = x;
                int sampleY = y;
                if (input.TileWrappedPeriod)
                {
                    int worldX = input.Topology.NormalizeX(input.OriginX + x);
                    int worldY = input.Topology.NormalizeY(input.OriginY + y);
                    sampleX = worldX - input.GenerationOriginX;
                    sampleY = worldY - input.GenerationOriginY;
                }

                TerrainCell cell = terrain.GetCell(sampleX, sampleY);
                float heightValue = terrain.TryGetEnvironmentValue(
                    "height", sampleX, sampleY, out float sampledHeight)
                    ? sampledHeight
                    : 0f;
                heights[index] = heightValue;
                biomes[index] = (byte)Math.Max(byte.MinValue, Math.Min(byte.MaxValue, cell.BiomeId));
                flags[index] = cell.Flags;
                groundTileIds[index] = cell.GroundTileId;
                minimumHeight = Math.Min(minimumHeight, heightValue);
                maximumHeight = Math.Max(maximumHeight, heightValue);
                totalHeight += heightValue;
                if ((cell.Flags & TerrainCellFlags.Water) != 0)
                    waterCount++;
                if (terrain.IsWalkable(sampleX, sampleY))
                    walkableCount++;
            }
        }

        stopwatch.Stop();
        return new PreviewResult
        {
            InputFingerprint = input.InputFingerprint,
            Seed = input.Seed,
            OriginX = input.OriginX,
            OriginY = input.OriginY,
            Width = input.Width,
            Height = input.Height,
            Heights = heights,
            Biomes = biomes,
            Flags = flags,
            GroundTileIds = groundTileIds,
            MinimumHeight = minimumHeight,
            MaximumHeight = maximumHeight,
            AverageHeight = totalHeight / cellCount,
            WaterRatio = waterCount / (double)cellCount,
            WalkableRatio = walkableCount / (double)cellCount,
            ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
            TiledWrappedPeriod = input.TileWrappedPeriod,
            TopologySpanX = input.Topology.IsWrapped ? input.Topology.Span.X : 0,
            TopologySpanY = input.Topology.IsWrapped ? input.Topology.Span.Y : 0
        };
    }

    /// <summary>由 EditorApplication.update 轮询任务，Unity 对象只在主线程创建。</summary>
    private void PollGeneration()
    {
        if (generationTask == null)
            return;

        if (!generationTask.IsCompleted)
        {
            statusMessage = $"正在后台生成地形……{EditorApplication.timeSinceStartup - generationStartedAt:0.0} 秒";
            Repaint();
            return;
        }

        Task<PreviewResult> completedTask = generationTask;
        generationTask = null;
        try
        {
            previewResult = completedTask.GetAwaiter().GetResult();
            ResetCanvasView();
            RebuildPreviewTexture();
            if (IsPreviewOutdated())
            {
                statusMessage = "生成完成，但生成期间输入又被修改；左侧画面对应的是任务开始时的参数。";
                statusType = MessageType.Warning;
            }
            else
            {
                statusMessage =
                    $"生成完成：seed={previewResult.Seed}，水域 {previewResult.WaterRatio:P2}，" +
                    $"可行走 {previewResult.WalkableRatio:P2}。";
                statusType = previewResult.WaterRatio >= 0.995d
                    ? MessageType.Error
                    : MessageType.Info;
            }
        }
        catch (OperationCanceledException)
        {
            statusMessage = "已取消本次生成。";
            statusType = MessageType.Warning;
        }
        catch (Exception exception)
        {
            statusMessage = "生成失败：" + exception.Message;
            statusType = MessageType.Error;
            Debug.LogException(exception);
        }
        finally
        {
            generationCancellation?.Dispose();
            generationCancellation = null;
            Repaint();
        }
    }

    private void CancelGeneration()
    {
        if (generationCancellation == null)
            return;
        generationCancellation.Cancel();
        statusMessage = "正在取消……";
        statusType = MessageType.Warning;
    }

    #endregion

    #region 纹理与颜色

    /// <summary>从已采样结果重建 Texture2D；切换显示模式无需重新生成地形。</summary>
    private void RebuildPreviewTexture()
    {
        if (previewResult == null)
            return;

        DestroyPreviewTexture();
        previewTexture = new Texture2D(
            previewResult.Width,
            previewResult.Height,
            TextureFormat.RGBA32,
            false,
            false)
        {
            name = $"TerrainPreview_{previewResult.Seed}",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        var colors = new Color32[previewResult.Width * previewResult.Height];
        for (int i = 0; i < colors.Length; i++)
            colors[i] = ResolvePreviewColor(i);
        previewTexture.SetPixels32(colors);
        previewTexture.Apply(false, false);
    }

    private Color32 ResolvePreviewColor(int index)
    {
        float height = Mathf.Clamp01(previewResult.Heights[index]);
        if (displayMode == PreviewDisplayMode.Height)
        {
            byte gray = (byte)Mathf.RoundToInt(height * 255f);
            return new Color32(gray, gray, gray, 255);
        }

        Color32 biomeColor = GetBiomeColor((SurfaceBiomeKind)previewResult.Biomes[index]);
        if (displayMode == PreviewDisplayMode.Biome)
            return biomeColor;

        float shade = Mathf.Lerp(0.72f, 1.12f, height);
        return new Color32(
            (byte)Mathf.Clamp(Mathf.RoundToInt(biomeColor.r * shade), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(biomeColor.g * shade), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(biomeColor.b * shade), 0, 255),
            255);
    }

    private static Color32 GetBiomeColor(SurfaceBiomeKind biome)
    {
        return biome switch
        {
            SurfaceBiomeKind.Ocean => new Color32(31, 82, 143, 255),
            SurfaceBiomeKind.River => new Color32(52, 151, 207, 255),
            SurfaceBiomeKind.Beach => new Color32(219, 201, 132, 255),
            SurfaceBiomeKind.Desert => new Color32(204, 166, 78, 255),
            SurfaceBiomeKind.Grassland => new Color32(91, 154, 77, 255),
            SurfaceBiomeKind.Forest => new Color32(38, 103, 62, 255),
            SurfaceBiomeKind.Snow => new Color32(226, 235, 237, 255),
            SurfaceBiomeKind.Stone => new Color32(112, 116, 121, 255),
            _ => new Color32(217, 74, 187, 255)
        };
    }

    private static Rect CalculateFitRect(Rect outer, int textureWidth, int textureHeight)
    {
        float scale = Mathf.Min(outer.width / textureWidth, outer.height / textureHeight);
        float width = textureWidth * scale;
        float height = textureHeight * scale;
        return new Rect(
            outer.x + (outer.width - width) * 0.5f,
            outer.y + (outer.height - height) * 0.5f,
            width,
            height);
    }

    /// <summary>处理画布滚轮缩放、右键拖动和右键菜单事件。</summary>
    private void HandleCanvasInput(Rect canvasRect, Rect fitRect)
    {
        Event current = Event.current;
        int controlId = GUIUtility.GetControlID(
            "WorldTerrainPreviewCanvas".GetHashCode(), FocusType.Passive, canvasRect);
        EditorGUIUtility.AddCursorRect(canvasRect, MouseCursor.Pan);

        if (current.type == EventType.ScrollWheel && canvasRect.Contains(current.mousePosition))
        {
            float previousZoom = canvasZoom;
            float nextZoom = Mathf.Clamp(
                previousZoom * Mathf.Pow(1.12f, -current.delta.y),
                MinimumCanvasZoom,
                MaximumCanvasZoom);
            if (!Mathf.Approximately(previousZoom, nextZoom))
            {
                Rect previousRect = CalculateZoomedTextureRect(fitRect);
                float ratio = nextZoom / previousZoom;
                Vector2 nextCenter = current.mousePosition +
                                     (previousRect.center - current.mousePosition) * ratio;
                canvasZoom = nextZoom;
                canvasPan = nextCenter - fitRect.center;
                ClampCanvasPan(canvasRect, fitRect);
                Repaint();
            }
            current.Use();
            return;
        }

        switch (current.GetTypeForControl(controlId))
        {
            case EventType.MouseDown when current.button == 1 &&
                                          canvasRect.Contains(current.mousePosition):
                GUIUtility.hotControl = controlId;
                current.Use();
                break;
            case EventType.MouseDrag when current.button == 1 && GUIUtility.hotControl == controlId:
                canvasPan += current.delta;
                ClampCanvasPan(canvasRect, fitRect);
                current.Use();
                Repaint();
                break;
            case EventType.MouseUp when current.button == 1 && GUIUtility.hotControl == controlId:
                GUIUtility.hotControl = 0;
                current.Use();
                break;
            case EventType.ContextClick when canvasRect.Contains(current.mousePosition):
                current.Use();
                break;
        }
    }

    /// <summary>根据适应窗口尺寸、缩放倍率和平移量计算图片最终矩形。</summary>
    private Rect CalculateZoomedTextureRect(Rect fitRect)
    {
        float width = fitRect.width * canvasZoom;
        float height = fitRect.height * canvasZoom;
        Vector2 center = fitRect.center + canvasPan;
        return new Rect(center.x - width * 0.5f, center.y - height * 0.5f, width, height);
    }

    /// <summary>限制平移量，确保放大后的图片始终覆盖画布，不露出额外空白。</summary>
    private void ClampCanvasPan(Rect canvasRect, Rect fitRect)
    {
        float maximumPanX = Mathf.Max(0f, (fitRect.width * canvasZoom - canvasRect.width) * 0.5f);
        float maximumPanY = Mathf.Max(0f, (fitRect.height * canvasZoom - canvasRect.height) * 0.5f);
        canvasPan.x = Mathf.Clamp(canvasPan.x, -maximumPanX, maximumPanX);
        canvasPan.y = Mathf.Clamp(canvasPan.y, -maximumPanY, maximumPanY);
    }

    /// <summary>恢复到完整图片适应左侧画布的初始视图。</summary>
    private void ResetCanvasView()
    {
        canvasZoom = 1f;
        canvasPan = Vector2.zero;
        Repaint();
    }

    private static GUIStyle CenteredLabelStyle()
    {
        return new GUIStyle(EditorStyles.centeredGreyMiniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 14
        };
    }

    private void DestroyPreviewTexture()
    {
        if (previewTexture == null)
            return;
        Object.DestroyImmediate(previewTexture);
        previewTexture = null;
    }

    private void ExportPreviewPng()
    {
        string path = EditorUtility.SaveFilePanel(
            "导出地形预览 PNG",
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            previewTexture != null ? previewTexture.name + ".png" : "TerrainPreview.png",
            "png");
        if (string.IsNullOrWhiteSpace(path) || previewTexture == null)
            return;

        File.WriteAllBytes(path, previewTexture.EncodeToPNG());
        statusMessage = "已导出：" + path;
        statusType = MessageType.Info;
    }

    private static int AlignUp(int value, int alignment)
    {
        if (alignment <= 0)
            throw new ArgumentOutOfRangeException(nameof(alignment));
        long result = ((long)value + alignment - 1L) / alignment * alignment;
        if (result > int.MaxValue / 2)
            throw new OverflowException("环绕世界半径过大。");
        return (int)result;
    }

    #endregion
}
#endif
