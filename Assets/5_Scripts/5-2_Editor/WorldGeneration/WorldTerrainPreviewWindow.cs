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
/// 生成一块连续 WorldModel 预览。预览是一格一像素，支持地表或矿洞 Profile 的地形、群系、高度图和物品四种显示方式；右侧临时参数
/// 默认只作用于本次预览，也可经确认后写回 Profile 资源；运行时世界坐标缩放仍由 PlanetData 管理。
/// 默认使用快速模式跳过高成本河流和结构阶段，切换精确模式可复现完整正式结果；生成放在后台线程，
/// 最大预览边长限制为 1024，避免高分辨率水文和生态计算长时间占用编辑器内存。
/// </summary>
public sealed class WorldTerrainPreviewWindow : EditorWindow
{
    #region 类型与常量

    private enum PreviewDisplayMode
    {
        Terrain,
        Biome,
        Height,
        Ecology
    }

    /// <summary>预览生成质量；快速模式用于调参，精确模式复现完整河流和结构链路。</summary>
    private enum PreviewGenerationQuality
    {
        Fast,
        Accurate
    }

    [Serializable]
    private sealed class NumericParameterValue
    {
        public string Id;
        public double Value;
    }

    /// <summary>编辑器画布绘制用的自然物点位；坐标始终是当前预览图上的本地格坐标。</summary>
    private readonly struct PreviewNaturalItemIconPlacement
    {
        public PreviewNaturalItemIconPlacement(int guid, string itemId, float localX, float localY)
        {
            Guid = guid;
            ItemId = itemId ?? string.Empty;
            LocalX = localX;
            LocalY = localY;
        }

        public int Guid { get; }
        public string ItemId { get; }
        public float LocalX { get; }
        public float LocalY { get; }
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
        public bool FastPreview;
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
        public int EcologyRuleCount;
        public int EcologyPlacementCount;
        public int EcologyHostCount;
        public int EcologyCompanionCount;
        public int[] EcologyCounts;
        public string[] EcologyPrimaryItemIds;
        public Dictionary<string, int> EcologyItemCounts;
        public Dictionary<int, PreviewNaturalItemIconPlacement[]> NaturalItemIconPlacementsByCell;
        public long ElapsedMilliseconds;
        public bool TiledWrappedPeriod;
        public int TopologySpanX;
        public int TopologySpanY;
        public bool FastPreview;
    }

    private const string DefaultProfilePath =
        "Assets/Resources/Config/WorldModel/ChunkGenerationProfile_Surface.asset";
    private const int MinimumPreviewSize = 16;
    private const int MaximumPreviewSize = 1024;
    private const float SettingsPanelWidth = 520f;
    private const float MinimumCanvasZoom = 0.25f;
    private const float MaximumCanvasZoom = 16f;
    private const int MaximumCachedPreviewEntries = 3;
    private const int MaximumCachedPreviewPixels = 512 * 512;
    private const double ProgressRefreshIntervalSeconds = 0.15d;
    private const float MinimumNaturalItemIconCellPixels = 8f;
    private const float MaximumNaturalItemIconCellPixels = 32f;
    private const float DefaultNaturalItemIconCellPixels = 12f;
    private const int MinimumNaturalItemIconVisibleLimit = 32;
    private const int MaximumNaturalItemIconVisibleLimit = 512;
    private const int DefaultNaturalItemIconVisibleLimit = 192;
    private const string RuntimeWorldCoordinateScaleId = "world.coordinateScale";
    private const string ProfileNumericParametersPropertyName = "numericParameters";
    private const string ProfileParameterIdPropertyName = "Id";
    private const string ProfileParameterValuePropertyName = "Value";
    private static readonly string[] GenerationQualityLabels = { "快速（推荐）", "精确" };

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

    // 预览窗口之间共享纯生成器，复用已构建的水文区域；缓存键仍由种子、坐标和 Profile 指纹隔离。
    private static readonly DeterministicChunkGenerator SharedPreviewGenerator = new();
    private static readonly object PreviewResultCacheGate = new();
    private static readonly Dictionary<ulong, PreviewResult> PreviewResultCache = new();
    private static readonly Queue<ulong> PreviewResultCacheOrder = new();

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
            ["river.lakeChance"] = "内陆汇流终点形成淡水湖的确定性概率",
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
    [SerializeField] private PreviewGenerationQuality generationQuality = PreviewGenerationQuality.Fast;
    [SerializeField] private float canvasZoom = 1f;
    [SerializeField] private Vector2 canvasPan;
    [SerializeField] private List<NumericParameterValue> numericParameters = new();
    [SerializeField] private string parameterSearch = string.Empty;
    [SerializeField] private bool showAdvancedParameters;
    [SerializeField] private bool showEcologyRules;
    [SerializeField] private bool overrideEcologyMultiplier;
    [SerializeField] private float ecologyPreviewMultiplier = 1f;
    [SerializeField] private bool showNaturalItemsOverlay = true;
    [SerializeField] private float naturalItemsOverlayOpacity = 0.95f;
    [SerializeField] private bool showNaturalItemIcons = true;
    [SerializeField] private float naturalItemIconMinimumCellPixels =
        DefaultNaturalItemIconCellPixels;
    [SerializeField] private int naturalItemIconVisibleLimit =
        DefaultNaturalItemIconVisibleLimit;
    [SerializeField] private Vector2 settingsScroll;

    #endregion

    #region 运行状态

    private Texture2D previewTexture;
    private Texture2D naturalItemsOverlayTexture;
    private PreviewResult previewResult;
    private readonly Dictionary<string, Sprite> naturalItemIconCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> unresolvedNaturalItemIcons =
        new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> naturalItemSpriteAddresses;
    private string naturalItemIconCatalogError;
    private int renderedNaturalItemIconCount;
    private bool naturalItemIconsHiddenByZoom;
    private bool naturalItemIconsCapped;
    private Task<PreviewResult> generationTask;
    private CancellationTokenSource generationCancellation;
    private double lastProgressMessageAt;
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
        DestroyNaturalItemsOverlayTexture();
        ClearNaturalItemIconCache();
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
        try
        {
            var localTextureRect = new Rect(
                textureRect.x - canvasRect.x,
                textureRect.y - canvasRect.y,
                textureRect.width,
                textureRect.height);
            GUI.DrawTexture(localTextureRect, previewTexture, ScaleMode.StretchToFill, false);
            if (showNaturalItemsOverlay && naturalItemsOverlayTexture != null)
            {
                // 叠加层与底图共用同一个矩形，缩放、平移和环绕平铺无需再次换算坐标。
                Color previousGuiColor = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, naturalItemsOverlayOpacity);
                GUI.DrawTexture(localTextureRect, naturalItemsOverlayTexture,
                    ScaleMode.StretchToFill, true);
                GUI.color = previousGuiColor;
            }
            DrawVisibleNaturalItemIcons(canvasRect, textureRect);
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
        }
        finally
        {
            // 图标资源异常也必须闭合裁剪栈，避免后续 IMGUI 控件布局连锁报错。
            GUI.EndClip();
        }
        DrawNaturalItemIconHint(canvasRect);

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
            $"可行走 {((flags & TerrainCellFlags.Walkable) != 0 ? "是" : "否")}\n" +
            $"生态物品 {previewResult.EcologyCounts[index]} 个" +
            (string.IsNullOrWhiteSpace(previewResult.EcologyPrimaryItemIds[index])
                ? string.Empty
                : $"（{previewResult.EcologyPrimaryItemIds[index]}）");
        Vector2 labelSize = EditorStyles.helpBox.CalcSize(new GUIContent(hoverText));
        float labelWidth = Mathf.Min(canvasRect.width - 16f,
            Mathf.Max(260f, labelSize.x + 16f));
        float labelHeight = EditorStyles.helpBox.CalcHeight(
            new GUIContent(hoverText), labelWidth - 16f) + 8f;
        var labelRect = new Rect(
            canvasRect.xMin + 8f,
            canvasRect.yMax - labelHeight - 12f,
            labelWidth,
            labelHeight);
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
            DrawEcologySettings();
            EditorGUILayout.Space(6f);
            DrawMapOverlaySettings();
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

        int generationQualityIndex = Mathf.Clamp((int)generationQuality, 0,
            GenerationQualityLabels.Length - 1);
        generationQualityIndex = EditorGUILayout.Popup(
            "生成质量", generationQualityIndex, GenerationQualityLabels);
        generationQuality = (PreviewGenerationQuality)generationQualityIndex;
        if (generationQuality == PreviewGenerationQuality.Fast)
        {
            EditorGUILayout.HelpBox(
                "快速模式跳过河流和结构计算，但仍生成地形、群系和生态物品，适合快速调参；" +
                "切换为精确模式可复现完整世界结果。",
                MessageType.Info);
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
                "高分辨率且使用精确模式时会明显变慢；生成在后台执行，可随时取消。",
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

    /// <summary>显示地表生态或洞穴矿脉规则，并提供仅作用于地表生态的预览倍率覆盖。</summary>
    private void DrawEcologySettings()
    {
        EditorGUILayout.LabelField("物品生成", EditorStyles.boldLabel);
        if (profileAsset == null)
        {
            EditorGUILayout.HelpBox("请选择地形 Profile 后查看生态规则。", MessageType.Info);
            return;
        }

        try
        {
            ChunkGenerationProfileSnapshot snapshot = profileAsset.CreateSnapshot();
            bool cave = snapshot.Settings.Mode == ChunkGenerationMode.Cave;
            EditorGUILayout.LabelField(cave ? "矿脉规则数量" : "生态规则数量",
                cave ? snapshot.CaveResourceRules.Count.ToString() :
                snapshot.EcologyRules.Count.ToString());
            if (cave)
            {
                showEcologyRules = EditorGUILayout.Foldout(
                    showEcologyRules, "矿脉明细（只读）", true);
                if (!showEcologyRules)
                    return;

                foreach (CaveResourceRuleSnapshot rule in snapshot.CaveResourceRules)
                {
                    EditorGUILayout.LabelField(
                        $"{rule.ItemId}  阈值 {rule.VeinThreshold:0.###}  尺度 {rule.VeinScale:0.###}",
                        EditorStyles.miniLabel);
                }

                EditorGUILayout.HelpBox(
                    "预览会复用正式 CaveGenerationFeatureGenerator，显示洞壁矿脉、散落矿石和传送门点位；" +
                    "这里只读取 Profile，不会实例化 Prefab 或写入世界存档。",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Profile 全局倍率",
                snapshot.EcologyGlobalMultiplier.ToString("0.###"));

            overrideEcologyMultiplier = EditorGUILayout.Toggle(
                "覆盖本次预览倍率", overrideEcologyMultiplier);
            if (overrideEcologyMultiplier)
            {
                ecologyPreviewMultiplier = Mathf.Max(0f, EditorGUILayout.FloatField(
                    "预览倍率", ecologyPreviewMultiplier));
            }

            showEcologyRules = EditorGUILayout.Foldout(
                showEcologyRules, "规则明细（只读）", true);
            if (!showEcologyRules)
                return;

            foreach (EcologySpawnRuleSnapshot rule in snapshot.EcologyRules)
            {
                string relation = rule.CompanionOnly
                    ? $"伴生→{rule.CompanionHostTag}"
                    : "宿主/独立";
                EditorGUILayout.LabelField(
                    $"{rule.ItemId}  {rule.SpawnChance:P3}  {relation}",
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.HelpBox(
                "预览会复用正式 ChunkEcologyGenerator；这里只显示点位和统计，不实例化 Prefab，" +
                "不会写入世界存档。规则概率和环境范围请在 Profile SO 中配置。",
                MessageType.Info);
        }
        catch (Exception exception)
        {
            EditorGUILayout.HelpBox("生态规则读取失败：" + exception.Message, MessageType.Error);
        }
    }

    /// <summary>控制左侧地图的自然物品叠加层；图层纹理只在生成结果变化时重建。</summary>
    private void DrawMapOverlaySettings()
    {
        EditorGUILayout.LabelField("地图叠加层", EditorStyles.boldLabel);
        showNaturalItemsOverlay = EditorGUILayout.Toggle(
            "自然物品图层", showNaturalItemsOverlay);
        if (showNaturalItemsOverlay)
        {
            naturalItemsOverlayOpacity = Mathf.Clamp01(EditorGUILayout.Slider(
                "图层透明度", naturalItemsOverlayOpacity, 0.2f, 1f));
        }

        showNaturalItemIcons = EditorGUILayout.Toggle(
            "放大显示物品图标", showNaturalItemIcons);
        if (showNaturalItemIcons)
        {
            naturalItemIconMinimumCellPixels = EditorGUILayout.Slider(
                "显示图标的单格像素", naturalItemIconMinimumCellPixels,
                MinimumNaturalItemIconCellPixels, MaximumNaturalItemIconCellPixels);
            naturalItemIconVisibleLimit = EditorGUILayout.IntSlider(
                "单帧最多图标", naturalItemIconVisibleLimit,
                MinimumNaturalItemIconVisibleLimit, MaximumNaturalItemIconVisibleLimit);
            if (GUILayout.Button("刷新物品图标缓存"))
            {
                ClearNaturalItemIconCache();
                Repaint();
            }
        }

        if (previewResult == null)
        {
            EditorGUILayout.LabelField("自然物品", "生成预览后显示");
            return;
        }

        EditorGUILayout.LabelField("自然物品",
            $"{previewResult.EcologyPlacementCount:N0} 个点位");
        if (showNaturalItemIcons)
        {
            string iconState = naturalItemIconsHiddenByZoom
                ? "继续放大后显示"
                : naturalItemIconsCapped
                    ? $"当前显示 {renderedNaturalItemIconCount} 个，请继续放大"
                    : $"当前显示 {renderedNaturalItemIconCount} 个";
            EditorGUILayout.LabelField("放大图标", iconState);
            if (!string.IsNullOrWhiteSpace(naturalItemIconCatalogError))
            {
                EditorGUILayout.HelpBox(
                    "物品图标目录读取失败，未解析的物品会继续使用颜色叠层：" +
                    naturalItemIconCatalogError,
                    MessageType.Warning);
            }
        }
        EditorGUILayout.HelpBox(
            "缩小视野时只绘制缓存 Texture2D；单格达到设定屏幕像素后，才从可见格中绘制真实物品图标。" +
            "图标不实例化 Item，也不会写入世界存档。",
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

        if (previewResult.FastPreview)
        {
            EditorGUILayout.HelpBox(
                "当前结果来自快速预览：河流和结构阶段被跳过，生态物品用于快速观察分布趋势；" +
                "需要验证最终河流、结构占用和生态过滤时请切换为精确模式。",
                MessageType.Info);
        }

        EditorGUILayout.LabelField("高度范围",
            $"{previewResult.MinimumHeight:0.0000} ～ {previewResult.MaximumHeight:0.0000}");
        EditorGUILayout.LabelField("平均高度", previewResult.AverageHeight.ToString("0.0000"));
        EditorGUILayout.LabelField("水域占比", previewResult.WaterRatio.ToString("P2"));
        EditorGUILayout.LabelField("可行走占比", previewResult.WalkableRatio.ToString("P2"));
        EditorGUILayout.LabelField("生态规则", $"{previewResult.EcologyRuleCount} 条");
        EditorGUILayout.LabelField("生态放置", $"{previewResult.EcologyPlacementCount} 个" +
            $"（宿主 {previewResult.EcologyHostCount}，伴生 {previewResult.EcologyCompanionCount}）");
        if (previewResult.EcologyItemCounts != null && previewResult.EcologyItemCounts.Count > 0)
        {
            string itemSummary = string.Join("、", previewResult.EcologyItemCounts
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Take(6)
                .Select(pair => $"{pair.Key}×{pair.Value}"));
            EditorGUILayout.LabelField("生态物品", itemSummary);
        }
        else
        {
            EditorGUILayout.LabelField("生态物品", "当前范围未生成自然物品。");
        }

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
            try
            {
                ChunkGenerationProfileSnapshot snapshot = profileAsset.CreateSnapshot();
                AddFingerprint(ref hash, unchecked((long)snapshot.GenerationFingerprint));
                AddFingerprint(ref hash, unchecked((long)snapshot.EcologyFingerprint));
            }
            catch (Exception)
            {
                // Profile 正在编辑成非法状态时仍保持窗口可用，生成时会给出具体错误。
                AddFingerprint(ref hash, "profile.ecology.invalid");
            }
        }
        AddFingerprint(ref hash, worldSeed);
        AddFingerprint(ref hash, centerX);
        AddFingerprint(ref hash, centerY);
        AddFingerprint(ref hash, previewWidth);
        AddFingerprint(ref hash, previewHeight);
        AddFingerprint(ref hash, wrappedWorld ? 1 : 0);
        AddFingerprint(ref hash, wrappedWorldRadius);
        AddFingerprint(ref hash, (int)generationQuality);
        AddFingerprint(ref hash, overrideEcologyMultiplier ? 1 : 0);
        if (overrideEcologyMultiplier)
        {
            AddFingerprint(ref hash,
                BitConverter.DoubleToInt64Bits(ecologyPreviewMultiplier));
        }

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

    /// <summary>读取小型预览结果缓存，避免重复点击生成时再次计算相同世界区域。</summary>
    private static bool TryGetCachedPreview(ulong fingerprint, out PreviewResult result)
    {
        lock (PreviewResultCacheGate)
            return PreviewResultCache.TryGetValue(fingerprint, out result);
    }

    /// <summary>缓存有限尺寸的纯预览结果，避免编辑器为了缓存占用过多内存。</summary>
    private static void CachePreviewResult(PreviewResult result)
    {
        if (result == null ||
            (long)result.Width * result.Height > MaximumCachedPreviewPixels)
        {
            return;
        }

        lock (PreviewResultCacheGate)
        {
            if (!PreviewResultCache.ContainsKey(result.InputFingerprint))
                PreviewResultCacheOrder.Enqueue(result.InputFingerprint);
            PreviewResultCache[result.InputFingerprint] = result;

            while (PreviewResultCache.Count > MaximumCachedPreviewEntries &&
                   PreviewResultCacheOrder.Count > 0)
            {
                ulong oldestFingerprint = PreviewResultCacheOrder.Dequeue();
                PreviewResultCache.Remove(oldestFingerprint);
            }
        }
    }

    /// <summary>把纯结果绑定到编辑器画面；缓存命中和后台完成共用同一条显示路径。</summary>
    private void ApplyPreviewResult(PreviewResult result, bool fromCache)
    {
        previewResult = result;
        ResetCanvasView();
        RebuildPreviewTexture();
        if (IsPreviewOutdated())
        {
            statusMessage = "预览完成，但输入已改变；左侧画面对应任务开始时的参数。";
            statusType = MessageType.Warning;
            return;
        }

        string source = fromCache ? "缓存命中" : "生成完成";
        string quality = result.FastPreview ? "快速" : "精确";
        statusMessage = $"{source}（{quality}）：seed={result.Seed}，水域 {result.WaterRatio:P2}，可行走 {result.WalkableRatio:P2}，生态 {result.EcologyPlacementCount} 个。";
        statusType = result.WaterRatio >= 0.995d
            ? MessageType.Error
            : MessageType.Info;
    }

    /// <summary>捕获当前窗口输入并启动后台纯数据生成。</summary>
    private void StartGeneration()
    {
        if (profileAsset == null || generationTask != null)
            return;

        try
        {
            PreviewInput input = BuildPreviewInput();
            if (TryGetCachedPreview(input.InputFingerprint, out PreviewResult cachedResult))
            {
                ApplyPreviewResult(cachedResult, true);
                return;
            }

            generationCancellation = new CancellationTokenSource();
            CancellationToken token = generationCancellation.Token;
            lastProgressMessageAt = 0d;
            statusMessage = input.FastPreview
                ? "正在后台生成快速地形与生态……"
                : "正在后台生成精确地形与生态……";
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

        // 预览会把显示范围合并成一个连续大区块；天然传送门仍必须按正式 Profile 的概率格划分。
        int portalChunkWidth = numbers.TryGetValue("cave.portal.chunkWidth", out double widthValue) &&
                               widthValue > 0d
            ? Mathf.Max(1, Mathf.RoundToInt((float)widthValue))
            : source.Width;
        int portalChunkHeight = numbers.TryGetValue("cave.portal.chunkHeight", out double heightValue) &&
                                heightValue > 0d
            ? Mathf.Max(1, Mathf.RoundToInt((float)heightValue))
            : source.Height;
        numbers["cave.portal.chunkWidth"] = portalChunkWidth;
        numbers["cave.portal.chunkHeight"] = portalChunkHeight;

        bool fastPreview = generationQuality == PreviewGenerationQuality.Fast;
        if (fastPreview)
        {
            // 快速预览只关闭最高成本的水文和结构阶段，地形、群系和生态规则仍走正式生成器。
            numbers["river.enabled"] = 0d;
            numbers["structure.enabled"] = 0d;
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
        double ecologyMultiplier = source.EcologyGlobalMultiplier;
        if (overrideEcologyMultiplier)
            ecologyMultiplier = Math.Max(0d, ecologyPreviewMultiplier);
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
                StringComparer.Ordinal),
            ecologyMultiplier,
            source.EcologyRules,
            source.CaveResourceRules);
        if (profile.Settings.Mode == ChunkGenerationMode.Cave)
        {
            CavePortalPairingSnapshot pairing = CreatePreviewCavePortalPairing(
                profile, worldSeed, topology, fastPreview);
            profile = profile.WithCavePortalPairing(pairing);
        }

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
            FastPreview = fastPreview,
            Profile = profile,
            Topology = topology
        };
    }

    /// <summary>
    /// 矿洞预览读取默认地表 Profile 作为入口配对真源。
    /// 只复制当前预览修改过的 cave.portal 参数，地表高度/水文仍保留地表 Profile 自身的配置，
    /// 这样预览中的蓝色出口数量和坐标会与实际地表入口一致。
    /// </summary>
    private static CavePortalPairingSnapshot CreatePreviewCavePortalPairing(
        ChunkGenerationProfileSnapshot caveProfile, int seed,
        ChunkGenerationTopologySnapshot topology, bool fastPreview)
    {
        if (caveProfile == null || caveProfile.Settings.Mode != ChunkGenerationMode.Cave)
            return null;

        ChunkGenerationProfileSO surfaceAsset =
            AssetDatabase.LoadAssetAtPath<ChunkGenerationProfileSO>(DefaultProfilePath);
        if (surfaceAsset == null)
            return null;

        ChunkGenerationProfileSnapshot source = surfaceAsset.CreateSnapshot();
        var numbers = source.NumericParameters.ToDictionary(pair => pair.Key, pair => pair.Value,
            StringComparer.Ordinal);
        foreach (KeyValuePair<string, double> parameter in caveProfile.NumericParameters)
        {
            if (parameter.Key.StartsWith("cave.portal.", StringComparison.Ordinal))
                numbers[parameter.Key] = parameter.Value;
        }

        if (fastPreview)
        {
            // 与矿洞快速预览使用相同的低成本地表判断，避免为少数出口重复构建水文图。
            numbers["river.enabled"] = 0d;
            numbers["structure.enabled"] = 0d;
        }

        var surfaceProfile = new ChunkGenerationProfileSnapshot(
            source.ProfileId + ".portal-pair-preview",
            source.Signature,
            source.Width,
            source.Height,
            numbers,
            source.TextParameters.ToDictionary(pair => pair.Key, pair => pair.Value,
                StringComparer.Ordinal),
            source.EcologyGlobalMultiplier,
            source.EcologyRules,
            source.CaveResourceRules);
        return new CavePortalPairingSnapshot("surface", seed, surfaceProfile, topology);
    }

    /// <summary>在后台复用正式生成器，并提取绘图与全水诊断需要的纯数据。</summary>
    private static PreviewResult GeneratePreview(PreviewInput input, CancellationToken token)
    {
        var stopwatch = Stopwatch.StartNew();
        DeterministicChunkGenerator generator = SharedPreviewGenerator;
        var request = new ChunkGenerationRequest(
            1,
            new FlatWorld.WorldModel.WorldAddress(
                input.Profile.Settings.Mode == ChunkGenerationMode.Cave ? "cave" : "surface",
                new Int2(input.GenerationOriginX, input.GenerationOriginY)),
            input.Seed,
            1,
            input.Profile,
            input.Topology);
        using ChunkGenerationResult generationResult = generator.Generate(request, token);
        using ChunkTerrainData terrain = generationResult.ConsumeTerrain();
        ChunkEcologyData ecology = generationResult.ConsumeEcology();

        int cellCount = checked(input.Width * input.Height);
        var heights = new float[cellCount];
        var biomes = new byte[cellCount];
        var flags = new TerrainCellFlags[cellCount];
        var groundTileIds = new int[cellCount];
        var ecologyCounts = new int[cellCount];
        var ecologyPrimaryItemIds = new string[cellCount];
        int generationCellCount = checked(terrain.Width * terrain.Height);
        var generationEcologyCounts = new int[generationCellCount];
        var generationEcologyPrimaryItemIds = new string[generationCellCount];
        var ecologyItemCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var naturalItemIconPlacements =
            new Dictionary<int, List<PreviewNaturalItemIconPlacement>>();
        int ecologyHostCount = 0;
        int ecologyCompanionCount = 0;
        foreach (NaturalItemPlacement placement in ecology.Placements)
        {
            if (placement.LocalX < 0 || placement.LocalX >= terrain.Width ||
                placement.LocalY < 0 || placement.LocalY >= terrain.Height)
            {
                continue;
            }

            int placementIndex = placement.LocalY * terrain.Width + placement.LocalX;
            generationEcologyCounts[placementIndex]++;
            string previousItemId = generationEcologyPrimaryItemIds[placementIndex];
            if (string.IsNullOrWhiteSpace(previousItemId))
            {
                generationEcologyPrimaryItemIds[placementIndex] = placement.ItemId;
            }
            else if (!string.Equals(previousItemId, placement.ItemId,
                         StringComparison.OrdinalIgnoreCase))
            {
                generationEcologyPrimaryItemIds[placementIndex] = "多种";
            }

            if (ecologyItemCounts.TryGetValue(placement.ItemId, out int itemCount))
                ecologyItemCounts[placement.ItemId] = itemCount + 1;
            else
                ecologyItemCounts.Add(placement.ItemId, 1);

            AddNaturalItemIconPlacements(input, placement, naturalItemIconPlacements);

            if (placement.IsCompanion)
                ecologyCompanionCount++;
            else
                ecologyHostCount++;
        }
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

                if (sampleX >= 0 && sampleX < terrain.Width &&
                    sampleY >= 0 && sampleY < terrain.Height)
                {
                    int ecologyIndex = sampleY * terrain.Width + sampleX;
                    ecologyCounts[index] = generationEcologyCounts[ecologyIndex];
                    ecologyPrimaryItemIds[index] =
                        generationEcologyPrimaryItemIds[ecologyIndex];
                }
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
            EcologyRuleCount = input.Profile.Settings.Mode == ChunkGenerationMode.Cave
                ? input.Profile.CaveResourceRules.Count
                : input.Profile.EcologyRules.Count,
            EcologyPlacementCount = ecology.Count,
            EcologyHostCount = ecologyHostCount,
            EcologyCompanionCount = ecologyCompanionCount,
            EcologyCounts = ecologyCounts,
            EcologyPrimaryItemIds = ecologyPrimaryItemIds,
            EcologyItemCounts = ecologyItemCounts,
            NaturalItemIconPlacementsByCell = FreezeNaturalItemIconPlacements(
                naturalItemIconPlacements),
            ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
            TiledWrappedPeriod = input.TileWrappedPeriod,
            TopologySpanX = input.Topology.IsWrapped ? input.Topology.Span.X : 0,
            TopologySpanY = input.Topology.IsWrapped ? input.Topology.Span.Y : 0,
            FastPreview = input.FastPreview
        };
    }

    /// <summary>把正式生态点位映射到当前画布坐标；环绕 2×2 预览只在这里复制显示位置。</summary>
    private static void AddNaturalItemIconPlacements(
        PreviewInput input,
        NaturalItemPlacement placement,
        Dictionary<int, List<PreviewNaturalItemIconPlacement>> placementsByCell)
    {
        if (!input.TileWrappedPeriod)
        {
            AddNaturalItemIconPlacement(
                input.Width,
                input.Height,
                placement.LocalX,
                placement.LocalY,
                placement,
                placementsByCell);
            return;
        }

        int spanX = input.Topology.Span.X;
        int spanY = input.Topology.Span.Y;
        if (spanX <= 0 || spanY <= 0)
            return;

        int worldX = input.Topology.NormalizeX(input.GenerationOriginX + placement.LocalX);
        int worldY = input.Topology.NormalizeY(input.GenerationOriginY + placement.LocalY);
        int originX = input.Topology.NormalizeX(input.OriginX);
        int originY = input.Topology.NormalizeY(input.OriginY);
        int firstDisplayX = PositiveModulo((long)worldX - originX, spanX);
        int firstDisplayY = PositiveModulo((long)worldY - originY, spanY);
        for (int displayY = firstDisplayY; displayY < input.Height; displayY += spanY)
        {
            for (int displayX = firstDisplayX; displayX < input.Width; displayX += spanX)
            {
                AddNaturalItemIconPlacement(
                    input.Width,
                    input.Height,
                    displayX,
                    displayY,
                    placement,
                    placementsByCell);
            }
        }
    }

    /// <summary>将一个自然物点位放入显示格索引，偏移仅影响最终图标中心位置。</summary>
    private static void AddNaturalItemIconPlacement(
        int width,
        int height,
        int cellX,
        int cellY,
        NaturalItemPlacement placement,
        Dictionary<int, List<PreviewNaturalItemIconPlacement>> placementsByCell)
    {
        if ((uint)cellX >= (uint)width || (uint)cellY >= (uint)height)
            return;

        int cellIndex = cellY * width + cellX;
        if (!placementsByCell.TryGetValue(cellIndex,
                out List<PreviewNaturalItemIconPlacement> placements))
        {
            placements = new List<PreviewNaturalItemIconPlacement>();
            placementsByCell.Add(cellIndex, placements);
        }

        placements.Add(new PreviewNaturalItemIconPlacement(
            placement.Guid,
            placement.ItemId,
            cellX + placement.OffsetX,
            cellY + placement.OffsetY));
    }

    /// <summary>冻结按格索引后的图标点位，并固定同格多图标的绘制顺序。</summary>
    private static Dictionary<int, PreviewNaturalItemIconPlacement[]> FreezeNaturalItemIconPlacements(
        Dictionary<int, List<PreviewNaturalItemIconPlacement>> placementsByCell)
    {
        var frozen = new Dictionary<int, PreviewNaturalItemIconPlacement[]>(placementsByCell.Count);
        foreach (KeyValuePair<int, List<PreviewNaturalItemIconPlacement>> pair in placementsByCell)
        {
            List<PreviewNaturalItemIconPlacement> placements = pair.Value;
            placements.Sort((left, right) =>
            {
                int guidComparison = left.Guid.CompareTo(right.Guid);
                return guidComparison != 0
                    ? guidComparison
                    : StringComparer.OrdinalIgnoreCase.Compare(left.ItemId, right.ItemId);
            });
            frozen.Add(pair.Key, placements.ToArray());
        }
        return frozen;
    }

    /// <summary>返回正向模，避免负世界坐标在环绕画布中落到错误副本。</summary>
    private static int PositiveModulo(long value, int modulo)
    {
        long result = value % modulo;
        return (int)(result < 0 ? result + modulo : result);
    }

    /// <summary>由 EditorApplication.update 轮询任务，Unity 对象只在主线程创建。</summary>
    private void PollGeneration()
    {
        if (generationTask == null)
            return;

        if (!generationTask.IsCompleted)
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - lastProgressMessageAt < ProgressRefreshIntervalSeconds)
                return;

            lastProgressMessageAt = now;
            // 只按固定频率刷新窗口，不在 EditorApplication.update 中反复拼接进度字符串。
            Repaint();
            return;
        }

        Task<PreviewResult> completedTask = generationTask;
        generationTask = null;
        try
        {
            PreviewResult result = completedTask.GetAwaiter().GetResult();
            CachePreviewResult(result);
            ApplyPreviewResult(result, false);
        }
        catch (OperationCanceledException)
        {
            statusMessage = "已取消本次生成。";
            statusType = MessageType.Warning;
        }
        catch (Exception exception)
        {
            statusMessage = $"生成失败：{exception.Message}";
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
        DestroyNaturalItemsOverlayTexture();
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
        bool hasNaturalItems = previewResult.EcologyPlacementCount > 0;
        Color32[] naturalItemsOverlayColors = hasNaturalItems
            ? new Color32[colors.Length]
            : null;
        for (int i = 0; i < colors.Length; i++)
        {
            colors[i] = ResolvePreviewColor(i);
            if (hasNaturalItems)
                naturalItemsOverlayColors[i] = ResolveNaturalItemOverlayColor(i);
        }
        previewTexture.SetPixels32(colors);
        previewTexture.Apply(false, false);

        if (hasNaturalItems)
        {
            naturalItemsOverlayTexture = new Texture2D(
                previewResult.Width,
                previewResult.Height,
                TextureFormat.RGBA32,
                false,
                false)
            {
                name = $"NaturalItemsOverlay_{previewResult.Seed}",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            naturalItemsOverlayTexture.SetPixels32(naturalItemsOverlayColors);
            naturalItemsOverlayTexture.Apply(false, false);
        }
    }

    /// <summary>生成透明背景的生态图层；空格为透明，非空格仅保留自然物品标记颜色。</summary>
    private Color32 ResolveNaturalItemOverlayColor(int index)
    {
        int count = previewResult.EcologyCounts[index];
        if (count <= 0)
            return new Color32(0, 0, 0, 0);

        Color color = GetNaturalItemColor(previewResult.EcologyPrimaryItemIds[index]);
        float highlight = Mathf.Clamp01((count - 1) * 0.12f);
        color = Color.Lerp(color, Color.white, highlight);
        byte alpha = (byte)Mathf.Clamp(
            Mathf.RoundToInt(Mathf.Lerp(205f, 255f, Mathf.Clamp01(count / 4f))),
            0,
            255);
        return new Color32(
            (byte)Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255),
            alpha);
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
        if (displayMode == PreviewDisplayMode.Ecology)
            return ResolveEcologyColor(index, biomeColor);

        float shade = Mathf.Lerp(0.72f, 1.12f, height);
        return new Color32(
            (byte)Mathf.Clamp(Mathf.RoundToInt(biomeColor.r * shade), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(biomeColor.g * shade), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(biomeColor.b * shade), 0, 255),
            255);
    }

    /// <summary>把正式生态点位绘制成可读的密度图，空白格保留暗化后的地形底色。</summary>
    private Color32 ResolveEcologyColor(int index, Color32 biomeColor)
    {
        int count = previewResult.EcologyCounts[index];
        float height = Mathf.Clamp01(previewResult.Heights[index]);
        float baseShade = Mathf.Lerp(0.24f, 0.42f, height);
        Color baseColor = new Color(
            biomeColor.r / 255f * baseShade,
            biomeColor.g / 255f * baseShade,
            biomeColor.b / 255f * baseShade,
            1f);
        if (count <= 0)
        {
            TerrainCellFlags flags = previewResult.Flags[index];
            if ((flags & TerrainCellFlags.Water) != 0)
                baseColor *= 0.7f;
            return baseColor;
        }

        Color itemColor = GetNaturalItemColor(previewResult.EcologyPrimaryItemIds[index]);
        float blend = Mathf.Clamp01(0.72f + Mathf.Min(count - 1, 3) * 0.07f);
        return Color.Lerp(baseColor, itemColor, blend);
    }

    /// <summary>按物品 ID 给预览点位分配稳定的辅助颜色，不读取运行时 Prefab。</summary>
    private static Color GetNaturalItemColor(string itemId)
    {
        string value = itemId ?? string.Empty;
        if (string.Equals(value, "多种", StringComparison.Ordinal))
            return new Color(0.92f, 0.34f, 0.92f, 1f);

        if (value.IndexOf("tree", StringComparison.OrdinalIgnoreCase) >= 0 ||
            value.IndexOf("bush", StringComparison.OrdinalIgnoreCase) >= 0 ||
            value.IndexOf("coconut", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return new Color(0.23f, 0.92f, 0.36f, 1f);
        }

        if (value.IndexOf("stone", StringComparison.OrdinalIgnoreCase) >= 0 ||
            value.IndexOf("coal", StringComparison.OrdinalIgnoreCase) >= 0 ||
            value.IndexOf("copper", StringComparison.OrdinalIgnoreCase) >= 0 ||
            value.IndexOf("iron", StringComparison.OrdinalIgnoreCase) >= 0 ||
            value.IndexOf("tin", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return new Color(0.78f, 0.82f, 0.88f, 1f);
        }

        if (value.IndexOf("stick", StringComparison.OrdinalIgnoreCase) >= 0 ||
            value.IndexOf("log", StringComparison.OrdinalIgnoreCase) >= 0 ||
            value.IndexOf("wood", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return new Color(0.92f, 0.57f, 0.22f, 1f);
        }

        if (value.IndexOf("weed", StringComparison.OrdinalIgnoreCase) >= 0 ||
            value.IndexOf("twine", StringComparison.OrdinalIgnoreCase) >= 0 ||
            value.IndexOf("vine", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return new Color(0.76f, 0.89f, 0.25f, 1f);
        }

        return new Color(1f, 0.44f, 0.22f, 1f);
    }

    #region 自然物图标

    /// <summary>仅在单格足够大时，裁剪到当前画布可见范围内绘制真实物品 Sprite。</summary>
    private void DrawVisibleNaturalItemIcons(Rect canvasRect, Rect textureRect)
    {
        renderedNaturalItemIconCount = 0;
        naturalItemIconsHiddenByZoom = false;
        naturalItemIconsCapped = false;
        if (!showNaturalItemIcons || previewResult == null ||
            previewResult.NaturalItemIconPlacementsByCell == null ||
            previewResult.NaturalItemIconPlacementsByCell.Count == 0)
        {
            return;
        }

        float cellWidth = textureRect.width / previewResult.Width;
        float cellHeight = textureRect.height / previewResult.Height;
        float cellPixels = Mathf.Min(cellWidth, cellHeight);
        if (cellPixels < naturalItemIconMinimumCellPixels)
        {
            // 缩小视野时完全不查询 Sprite、不遍历点位，保留底层缓存色块即可。
            naturalItemIconsHiddenByZoom = true;
            return;
        }

        int minX = Mathf.Clamp(
            Mathf.FloorToInt((canvasRect.xMin - textureRect.xMin) / cellWidth) - 1,
            0,
            previewResult.Width - 1);
        int maxX = Mathf.Clamp(
            Mathf.CeilToInt((canvasRect.xMax - textureRect.xMin) / cellWidth) + 1,
            0,
            previewResult.Width - 1);
        int minY = Mathf.Clamp(
            Mathf.FloorToInt((textureRect.yMax - canvasRect.yMax) / cellHeight) - 1,
            0,
            previewResult.Height - 1);
        int maxY = Mathf.Clamp(
            Mathf.CeilToInt((textureRect.yMax - canvasRect.yMin) / cellHeight) + 1,
            0,
            previewResult.Height - 1);
        int visibleLimit = Mathf.Clamp(
            naturalItemIconVisibleLimit,
            MinimumNaturalItemIconVisibleLimit,
            MaximumNaturalItemIconVisibleLimit);

        Color previousGuiColor = GUI.color;
        GUI.color = Color.white;
        try
        {
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int cellIndex = y * previewResult.Width + x;
                    if (!previewResult.NaturalItemIconPlacementsByCell.TryGetValue(
                            cellIndex,
                            out PreviewNaturalItemIconPlacement[] placements))
                    {
                        continue;
                    }

                    for (int placementIndex = 0; placementIndex < placements.Length; placementIndex++)
                    {
                        if (renderedNaturalItemIconCount >= visibleLimit)
                        {
                            naturalItemIconsCapped = true;
                            return;
                        }

                        PreviewNaturalItemIconPlacement placement = placements[placementIndex];
                        if (!TryGetNaturalItemIcon(placement.ItemId, out Sprite icon))
                            continue;

                        DrawNaturalItemIcon(
                            icon,
                            placement,
                            canvasRect,
                            textureRect,
                            cellWidth,
                            cellHeight,
                            cellPixels);
                        renderedNaturalItemIconCount++;
                    }
                }
            }
        }
        finally
        {
            GUI.color = previousGuiColor;
        }
    }

    /// <summary>把一个 Sprite 按其真实切片区域绘制在自然物对应格子中心，避免读取整个图集。</summary>
    private static void DrawNaturalItemIcon(
        Sprite icon,
        PreviewNaturalItemIconPlacement placement,
        Rect canvasRect,
        Rect textureRect,
        float cellWidth,
        float cellHeight,
        float cellPixels)
    {
        if (icon == null || icon.texture == null)
            return;

        Rect spriteRect = icon.textureRect;
        if (spriteRect.width <= 0f || spriteRect.height <= 0f)
            return;

        float maximumSize = Mathf.Min(cellPixels * 0.88f, 48f);
        float aspect = spriteRect.width / spriteRect.height;
        float iconWidth = aspect >= 1f ? maximumSize : maximumSize * aspect;
        float iconHeight = aspect >= 1f ? maximumSize / aspect : maximumSize;
        float centerX = textureRect.xMin + (placement.LocalX + 0.5f) * cellWidth;
        float centerY = textureRect.yMax - (placement.LocalY + 0.5f) * cellHeight;
        var drawRect = new Rect(
            centerX - canvasRect.xMin - iconWidth * 0.5f,
            centerY - canvasRect.yMin - iconHeight * 0.5f,
            iconWidth,
            iconHeight);
        var textureCoordinates = new Rect(
            spriteRect.x / icon.texture.width,
            spriteRect.y / icon.texture.height,
            spriteRect.width / icon.texture.width,
            spriteRect.height / icon.texture.height);
        GUI.DrawTextureWithTexCoords(drawRect, icon.texture, textureCoordinates, true);
    }

    /// <summary>在画布左上角提示图标被缩放阈值或可见数量上限保护。</summary>
    private void DrawNaturalItemIconHint(Rect canvasRect)
    {
        if (!showNaturalItemIcons || previewResult == null || previewResult.EcologyPlacementCount <= 0)
            return;

        string hint = naturalItemIconsHiddenByZoom
            ? "继续放大地图以显示物品图标"
            : naturalItemIconsCapped
                ? "可见物品过多，请继续放大地图"
                : null;
        if (string.IsNullOrWhiteSpace(hint))
            return;

        var hintRect = new Rect(canvasRect.xMin + 8f, canvasRect.yMin + 8f, 188f,
            EditorGUIUtility.singleLineHeight + 6f);
        EditorGUI.DrawRect(hintRect, new Color(0f, 0f, 0f, 0.62f));
        GUI.Label(hintRect, hint, EditorStyles.whiteMiniLabel);
    }

    /// <summary>从 JSON 物品定义优先读取 Sprite，旧生态 Prefab 物品则回退到主 SpriteRenderer。</summary>
    private bool TryGetNaturalItemIcon(string itemId, out Sprite icon)
    {
        icon = null;
        string normalizedItemId = itemId?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedItemId))
            return false;

        if (naturalItemIconCache.TryGetValue(normalizedItemId, out icon) && icon != null)
            return true;
        if (unresolvedNaturalItemIcons.Contains(normalizedItemId))
            return false;

        if (EnsureNaturalItemSpriteAddresses() &&
            naturalItemSpriteAddresses.TryGetValue(normalizedItemId, out string spriteAddress))
        {
            icon = TryLoadSpriteAtAddress(spriteAddress);
        }

        icon ??= TryLoadNaturalItemPrefabIcon(normalizedItemId);
        if (icon == null)
        {
            unresolvedNaturalItemIcons.Add(normalizedItemId);
            return false;
        }

        naturalItemIconCache[normalizedItemId] = icon;
        return true;
    }

    /// <summary>只在第一次真正需要图标时解析本体 JSON 目录，避免缩小地图时引入加载成本。</summary>
    private bool EnsureNaturalItemSpriteAddresses()
    {
        if (naturalItemSpriteAddresses != null)
            return true;

        naturalItemSpriteAddresses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (ItemDefinitionDto definition in ItemDefinitionCatalogLoader.LoadBuiltInDefinitions())
            {
                if (definition == null || definition.Abstract ||
                    string.IsNullOrWhiteSpace(definition.Id) ||
                    string.IsNullOrWhiteSpace(definition.Visual?.SpriteAddress))
                {
                    continue;
                }

                naturalItemSpriteAddresses[definition.Id.Trim()] =
                    definition.Visual.SpriteAddress.Trim();
            }
            return true;
        }
        catch (Exception exception)
        {
            naturalItemIconCatalogError = exception.Message;
            Debug.LogWarning("[地形预览器] 读取物品 Sprite 目录失败：" + exception.Message);
            return false;
        }
    }

    /// <summary>为仍使用旧 Prefab 的自然物读取首选 SpriteRenderer，兼容尚未迁入 JSON 的物品。</summary>
    private static Sprite TryLoadNaturalItemPrefabIcon(string itemId)
    {
        foreach (string guid in AssetDatabase.FindAssets(itemId + " t:Prefab")
                     .OrderBy(value => value, StringComparer.Ordinal))
        {
            string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.Equals(Path.GetFileNameWithoutExtension(prefabPath), itemId,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
                continue;

            // 部分自然物（例如 Weed）只有子节点带 SpriteRenderer；不读取根节点的缺失组件。
            SpriteRenderer[] renderers = prefab.GetComponentsInChildren<SpriteRenderer>(true);
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                SpriteRenderer renderer = renderers[rendererIndex];
                if (renderer != null && renderer.sprite != null)
                    return renderer.sprite;
            }
        }

        return null;
    }

    /// <summary>把 Addressable 的“资源路径[切片名]”转换成 Editor AssetDatabase 可读取的 Sprite。</summary>
    private static Sprite TryLoadSpriteAtAddress(string address)
    {
        if (!TryParseSpriteAddress(address, out string assetPath, out string spriteName))
            return null;

        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        Sprite sprite = assets
            .OfType<Sprite>()
            .FirstOrDefault(candidate => string.IsNullOrWhiteSpace(spriteName) ||
                                         string.Equals(candidate.name, spriteName,
                                             StringComparison.Ordinal));
        return sprite ?? AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
    }

    /// <summary>解析本项目 ItemDefinition 使用的 SpriteAddress，不触碰 Addressables 运行时句柄。</summary>
    private static bool TryParseSpriteAddress(string address, out string assetPath, out string spriteName)
    {
        assetPath = string.Empty;
        spriteName = string.Empty;
        if (string.IsNullOrWhiteSpace(address))
            return false;

        string value = address.Trim();
        int nameStart = value.LastIndexOf('[');
        if (nameStart >= 0 && value.EndsWith("]", StringComparison.Ordinal))
        {
            assetPath = value.Substring(0, nameStart).Trim();
            spriteName = value.Substring(nameStart + 1, value.Length - nameStart - 2).Trim();
        }
        else
        {
            assetPath = value;
        }

        return assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>释放窗口持有的 Sprite 查询缓存；不会销毁项目资源。</summary>
    private void ClearNaturalItemIconCache()
    {
        naturalItemIconCache.Clear();
        unresolvedNaturalItemIcons.Clear();
        naturalItemSpriteAddresses = null;
        naturalItemIconCatalogError = null;
        renderedNaturalItemIconCount = 0;
        naturalItemIconsHiddenByZoom = false;
        naturalItemIconsCapped = false;
    }

    #endregion

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

    /// <summary>释放自然物品叠加纹理，避免窗口重生成或关闭时残留编辑器纹理。</summary>
    private void DestroyNaturalItemsOverlayTexture()
    {
        if (naturalItemsOverlayTexture == null)
            return;
        Object.DestroyImmediate(naturalItemsOverlayTexture);
        naturalItemsOverlayTexture = null;
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

        File.WriteAllBytes(path, EncodePreviewPng());
        statusMessage = "已导出：" + path;
        statusType = MessageType.Info;
    }

    /// <summary>导出当前可见图层；合成只在用户点击导出时执行，不增加正常预览帧耗时。</summary>
    private byte[] EncodePreviewPng()
    {
        if (!showNaturalItemsOverlay || naturalItemsOverlayTexture == null)
            return previewTexture.EncodeToPNG();

        Color32[] colors = previewTexture.GetPixels32();
        Color32[] overlayColors = naturalItemsOverlayTexture.GetPixels32();
        float opacity = Mathf.Clamp01(naturalItemsOverlayOpacity);
        for (int i = 0; i < colors.Length; i++)
        {
            float alpha = overlayColors[i].a / 255f * opacity;
            if (alpha <= 0f)
                continue;

            Color32 baseColor = colors[i];
            colors[i] = new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(
                    overlayColors[i].r * alpha + baseColor.r * (1f - alpha)), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(
                    overlayColors[i].g * alpha + baseColor.g * (1f - alpha)), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(
                    overlayColors[i].b * alpha + baseColor.b * (1f - alpha)), 0, 255),
                255);
        }

        Texture2D exportTexture = new Texture2D(
            previewTexture.width,
            previewTexture.height,
            TextureFormat.RGBA32,
            false,
            false);
        try
        {
            exportTexture.SetPixels32(colors);
            exportTexture.Apply(false, false);
            return exportTexture.EncodeToPNG();
        }
        finally
        {
            Object.DestroyImmediate(exportTexture);
        }
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
