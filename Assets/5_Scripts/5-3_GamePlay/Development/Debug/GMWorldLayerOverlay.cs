using FlatWorld.WorldModel;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>GM 世界观察层模式；关闭时停止采样，温度与污染共用同一张热力图。</summary>
internal enum GmWorldLayerMode
{
    Off = 0,
    Temperature = 1,
    Contamination = 2
}

/// <summary>
/// GM 世界观察层：一张 128×128 点采样纹理覆盖当前相机视口。
/// 温度模式读取最终环境温度；污染模式读取当前地格所有已注册污染指标中的最高归一化负荷。
/// 只访问已加载的权威 ChunkTerrainData，不为调试显示触发区块加载，也不回写世界状态。
/// </summary>
[DisallowMultipleComponent]
internal sealed class GMWorldLayerOverlay : MonoBehaviour
{
    #region 显示预算与资源

    private const int TextureSize = 128;
    private const int SamplesPerFrame = 2048;
    private const float RefreshInterval = 0.25f;
    private static readonly ProfilerMarker SampleMarker = new("FlatWorld.GM.WorldLayerOverlay");
    private static readonly int OpacityId = Shader.PropertyToID("_Opacity"); // 材质整体不透明度。
    private readonly Color32[] pixels = new Color32[TextureSize * TextureSize];
    private readonly Vector3[] vertices = new Vector3[4];
    private readonly Vector2[] uvs = new Vector2[4];
    private GameObject renderRoot;
    private MeshRenderer overlayRenderer;
    private Mesh mesh;
    private Material material;
    private Texture2D texture;
    private int columns;
    private int rows;
    private int stride;
    private Vector2Int origin;
    private int sampleIndex;
    private bool sampling;
    private float nextRefreshTime;
    private long contextVersion = long.MinValue;
    private long renderedContextVersion = long.MinValue;

    public GmWorldLayerMode Mode { get; private set; }
    public bool Visible => Mode != GmWorldLayerMode.Off;
    public float Transparency { get; private set; } // 0 为不透明，1 为完全透明。

    #endregion

    #region 开关与生命周期

    private void Awake()
    {
        SetTransparency(GMConsolePreferences.WorldLayerOverlayTransparency);
        enabled = false;
    }

    /// <summary>只更新材质透明度，不触发地块重采样。</summary>
    public void SetTransparency(float transparency)
    {
        Transparency = Mathf.Clamp01(transparency);
        if (material != null)
            material.SetFloat(OpacityId, 1f - Transparency);
    }

    /// <summary>切换世界观察层；同一时刻只显示一个模式，避免两张热力图互相混色。</summary>
    public void SetMode(GmWorldLayerMode mode)
    {
        if (!System.Enum.IsDefined(typeof(GmWorldLayerMode), mode))
            mode = GmWorldLayerMode.Off;

        bool modeChanged = Mode != mode;
        Mode = mode;
        enabled = Visible;
        if (modeChanged)
            HidePendingFrame();
        nextRefreshTime = 0f;
        if (Visible && texture == null)
            CreateRenderResources();
    }

    private void LateUpdate()
    {
        if (GameManager.Instance == null || !GameManager.Instance.IsInGameWorld)
        {
            HidePendingFrame();
            return;
        }

        long version = ResolveContextVersion();
        if (renderedContextVersion != version && overlayRenderer != null)
            overlayRenderer.enabled = false;
        if (sampling && contextVersion != version)
            HidePendingFrame();

        if (!sampling)
        {
            if (Time.unscaledTime < nextRefreshTime)
                return;
            nextRefreshTime = Time.unscaledTime + RefreshInterval;
            if (!BeginSampling(Camera.main, version))
                return;
        }

        using (SampleMarker.Auto())
        {
            int end = Mathf.Min(columns * rows, sampleIndex + SamplesPerFrame);
            for (; sampleIndex < end; sampleIndex++)
            {
                int x = sampleIndex % columns;
                int y = sampleIndex / columns;
                Vector2 position = new(origin.x + (x + 0.5f) * stride, origin.y + (y + 0.5f) * stride);
                pixels[y * TextureSize + x] = TrySampleColor(position, out Color32 color)
                    ? color
                    : new Color32(0, 0, 0, 0);
            }

            if (sampleIndex == columns * rows)
                PublishTexture();
        }
    }

    /// <summary>世界纪元、观察模式和温度场版本共同组成上下文，切换世界时不显示旧纹理。</summary>
    private long ResolveContextVersion()
    {
        long epoch = ChunkMgr.ExistingInstance?.WorldRuntime?.Epoch ?? 0L;
        long mode = (long)Mode & 0xffL;
        long detail = Mode == GmWorldLayerMode.Temperature && TemperatureMgr.Instance != null
            ? (uint)TemperatureMgr.Instance.FieldContextVersion
            : 0L;
        return unchecked((epoch * 397L) ^ (mode << 48) ^ detail);
    }

    /// <summary>停止当前批次并立即隐藏旧帧。</summary>
    private void HidePendingFrame()
    {
        sampling = false;
        contextVersion = long.MinValue;
        renderedContextVersion = long.MinValue;
        if (overlayRenderer != null)
            overlayRenderer.enabled = false;
    }

    private void OnDisable() => HidePendingFrame();

    private void OnDestroy()
    {
        if (renderRoot != null) Destroy(renderRoot);
        if (mesh != null) Destroy(mesh);
        if (material != null) Destroy(material);
        if (texture != null) Destroy(texture);
    }

    #endregion

    #region 数据采样

    /// <summary>按当前模式读取一个世界位置；未加载或目录未就绪时返回透明。</summary>
    private bool TrySampleColor(Vector2 position, out Color32 color)
    {
        switch (Mode)
        {
            case GmWorldLayerMode.Temperature:
                if (TemperatureMgr.Instance != null &&
                    TemperatureMgr.Instance.TryGetAmbientTemperature(position, out float temperature))
                {
                    color = EvaluateTemperatureColor(temperature);
                    return true;
                }
                break;

            case GmWorldLayerMode.Contamination:
                if (TryGetContaminationLoad(position, out float normalizedLoad))
                {
                    color = EvaluateContaminationColor(normalizedLoad);
                    return true;
                }
                break;
        }

        color = new Color32(0, 0, 0, 0);
        return false;
    }

    /// <summary>一次定位地格后遍历注册目录，取该格最高污染负荷；MOD 污染定义自动参与。</summary>
    private static bool TryGetContaminationLoad(Vector2 position, out float normalizedLoad)
    {
        normalizedLoad = 0f;
        ChunkMgr chunkManager = ChunkMgr.ExistingInstance;
        GameRes gameRes = GameRes.ExistingInstance;
        if (chunkManager == null || gameRes == null ||
            !chunkManager.TryGetRuntimeTerrainTile(position, out RuntimeTerrainTileSample sample))
        {
            return false;
        }

        foreach (ContaminationDefinition definition in gameRes.ContaminationDefinitions.Values)
        {
            float value = ContaminationSystem.ReadValue(sample.Terrain, sample.LocalCell, definition);
            normalizedLoad = Mathf.Max(normalizedLoad, definition.Normalize(value));
            if (normalizedLoad >= 0.9999f)
                break;
        }
        return true;
    }

    /// <summary>固定 -20～60℃ 色标，确保不同区域的颜色含义不随视口改变。</summary>
    internal static Color32 EvaluateTemperatureColor(float temperature)
    {
        float band = Mathf.Clamp((temperature + 20f) / 20f, 0f, 4f);
        Color color = band < 1f ? Color.Lerp(new Color(0.08f, 0.20f, 0.95f), Color.cyan, band) :
            band < 2f ? Color.Lerp(Color.cyan, new Color(0.18f, 0.85f, 0.30f), band - 1f) :
            band < 3f ? Color.Lerp(new Color(0.18f, 0.85f, 0.30f), Color.yellow, band - 2f) :
            Color.Lerp(Color.yellow, new Color(1f, 0.08f, 0.04f), band - 3f);
        return color;
    }

    /// <summary>污染总览固定使用 0～100% 色标：绿色清洁、黄色中等、红色高负荷。</summary>
    internal static Color32 EvaluateContaminationColor(float normalizedLoad)
    {
        float value = Mathf.Clamp01(normalizedLoad);
        Color clean = new(0.10f, 0.72f, 0.35f);
        Color medium = new(0.96f, 0.78f, 0.10f);
        Color severe = new(0.95f, 0.12f, 0.08f);
        return value <= 0.5f
            ? Color.Lerp(clean, medium, value * 2f)
            : Color.Lerp(medium, severe, (value - 0.5f) * 2f);
    }

    #endregion

    #region 视口采样

    /// <summary>把当前相机视口对齐到整数世界格，超大视距自动增大采样步长。</summary>
    private bool BeginSampling(Camera camera, long version)
    {
        if (camera == null || texture == null)
            return false;

        Plane plane = new(Vector3.forward, Vector3.zero);
        Vector2 minimum = new(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 maximum = new(float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < 4; i++)
        {
            Ray ray = camera.ViewportPointToRay(new Vector3(i & 1, i >> 1, 0f));
            if (!plane.Raycast(ray, out float distance))
                return false;
            Vector2 point = ray.GetPoint(distance);
            minimum = Vector2.Min(minimum, point);
            maximum = Vector2.Max(maximum, point);
        }

        Vector2 span = maximum - minimum;
        stride = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(span.x, span.y) / (TextureSize - 4)));
        origin = new Vector2Int(
            Mathf.FloorToInt(minimum.x / stride) * stride - stride,
            Mathf.FloorToInt(minimum.y / stride) * stride - stride);
        columns = Mathf.Min(TextureSize, Mathf.CeilToInt((maximum.x - origin.x) / stride) + 1);
        rows = Mathf.Min(TextureSize, Mathf.CeilToInt((maximum.y - origin.y) / stride) + 1);
        sampleIndex = 0;
        contextVersion = version;
        sampling = true;
        return true;
    }

    /// <summary>一批完整采样结束后整体上传，避免逐行刷新和跨世界混色。</summary>
    private void PublishTexture()
    {
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        float width = columns * stride;
        float height = rows * stride;
        vertices[0] = new Vector3(origin.x, origin.y, 0f);
        vertices[1] = new Vector3(origin.x + width, origin.y, 0f);
        vertices[2] = new Vector3(origin.x + width, origin.y + height, 0f);
        vertices[3] = new Vector3(origin.x, origin.y + height, 0f);
        float u = (float)columns / TextureSize;
        float v = (float)rows / TextureSize;
        uvs[0] = Vector2.zero;
        uvs[1] = new Vector2(u, 0f);
        uvs[2] = new Vector2(u, v);
        uvs[3] = new Vector2(0f, v);
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.RecalculateBounds();
        overlayRenderer.enabled = true;
        renderedContextVersion = contextVersion;
        sampling = false;
    }

    #endregion

    #region 绘制资源

    /// <summary>创建唯一热力图纹理、材质和四边形；不同观察模式只替换像素数据。</summary>
    private void CreateRenderResources()
    {
        Shader shader = Resources.Load<Shader>("TemperatureHeatmap");
        if (shader == null)
        {
            Debug.LogError("[GMWorldLayerOverlay] 缺少 TemperatureHeatmap Shader。", this);
            SetMode(GmWorldLayerMode.Off);
            return;
        }

        texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
        {
            name = "GM World Layer Cells",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };
        material = new Material(shader) { name = "GM World Layer Overlay", hideFlags = HideFlags.DontSave };
        material.mainTexture = texture;
        material.SetFloat(OpacityId, 1f - Transparency);
        mesh = new Mesh { name = "GM World Layer Quad", hideFlags = HideFlags.DontSave };
        mesh.MarkDynamic();
        mesh.vertices = vertices;
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        renderRoot = new GameObject("World Layer Heatmap", typeof(MeshFilter), typeof(MeshRenderer));
        renderRoot.transform.SetParent(transform, false);
        renderRoot.GetComponent<MeshFilter>().sharedMesh = mesh;
        overlayRenderer = renderRoot.GetComponent<MeshRenderer>();
        overlayRenderer.sharedMaterial = material;
        overlayRenderer.sortingLayerName = "Paticle";
        overlayRenderer.sortingOrder = 32750;
        overlayRenderer.shadowCastingMode = ShadowCastingMode.Off;
        overlayRenderer.receiveShadows = false;
        overlayRenderer.enabled = false;
    }

    #endregion
}
