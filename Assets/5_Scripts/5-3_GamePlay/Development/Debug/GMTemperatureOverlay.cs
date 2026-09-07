using UnityEngine;
using Unity.Profiling;
using UnityEngine.Rendering;

/// <summary>
/// GM 温度眼镜：一张 128×128 的点采样纹理和一个四边形覆盖可见地表，保持 UI 与操作独立。
/// 正常视距每像素对应一格，超大视距降低采样密度；最多每 0.25 秒更新，每帧最多采样 2048 格。
/// 只读已加载的权威温度场，未加载格透明；关闭后停止更新，所有图形资源随组件销毁释放。
/// </summary>
[DisallowMultipleComponent]
internal sealed class GMTemperatureOverlay : MonoBehaviour
{
    #region 显示预算与资源

    private const int TextureSize = 128;
    private const int SamplesPerFrame = 2048;
    private const float RefreshInterval = 0.25f;
    private static readonly ProfilerMarker SampleMarker = new("FlatWorld.GM.TemperatureOverlay");
    private static readonly int OpacityId = Shader.PropertyToID("_Opacity"); // 材质整体不透明度
    private readonly Color32[] pixels = new Color32[TextureSize * TextureSize];
    private readonly Vector3[] vertices = new Vector3[4];
    private readonly Vector2[] uvs = new Vector2[4];
    private GameObject renderRoot;
    private MeshRenderer overlayRenderer;
    private Mesh mesh;
    private Material material;
    private Texture2D texture;
    private TemperatureMgr temperatureManager;
    private int contextVersion = -1;
    private int renderedContextVersion = -1;
    private int columns;
    private int rows;
    private int stride;
    private Vector2Int origin;
    private int sampleIndex;
    private bool sampling;
    private float nextRefreshTime;

    public bool Visible { get; private set; }
    public float Transparency { get; private set; } // 0 为不透明，1 为完全透明

    #endregion

    #region 开关与生命周期

    private void Awake()
    {
        SetTransparency(GMConsolePreferences.TemperatureOverlayTransparency);
        enabled = false;
    }

    /// <summary>只更新材质参数，已上传纹理立即生效；未创建材质时保留设置。</summary>
    public void SetTransparency(float transparency)
    {
        Transparency = Mathf.Clamp01(transparency);
        if (material != null)
            material.SetFloat(OpacityId, 1f - Transparency);
    }

    /// <summary>GM 面板关闭不影响眼镜；只有此开关控制采样与覆盖层。</summary>
    public void SetVisible(bool visible)
    {
        Visible = visible;
        enabled = visible;
        sampling = false;
        nextRefreshTime = 0f;
        if (overlayRenderer != null)
            overlayRenderer.enabled = false;
        if (visible && texture == null)
            CreateRenderResources();
    }

    private void LateUpdate()
    {
        if (GameManager.Instance == null || !GameManager.Instance.IsInGameWorld)
        {
            HidePendingFrame();
            return;
        }

        temperatureManager = TemperatureMgr.Instance;
        int version = temperatureManager.FieldContextVersion;
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
                pixels[y * TextureSize + x] = temperatureManager.TryGetAmbientTemperature(position, out float temperature)
                    ? EvaluateColor(temperature) : new Color32(0, 0, 0, 0);
            }
            if (sampleIndex == columns * rows)
                PublishTexture();
        }
    }

    private void HidePendingFrame()
    {
        sampling = false;
        nextRefreshTime = 0f;
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

    #region 视口采样

    private bool BeginSampling(Camera camera, int version)
    {
        if (camera == null || texture == null)
            return false;

        // 与世界 z=0 平面求交，同时支持相机旋转；不依赖固定屏幕宽高或正交尺寸。
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

    /// <summary>一帧完整采样后再整体上传，避免画面出现逐行刷新和跨世界混色。</summary>
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

    /// <summary>固定 -20～60℃ 色标，避免移动到不同区域时同一种颜色代表不同温度。</summary>
    internal static Color32 EvaluateColor(float temperature)
    {
        float band = Mathf.Clamp((temperature + 20f) / 20f, 0f, 4f);
        Color color = band < 1f ? Color.Lerp(new Color(0.08f, 0.20f, 0.95f), Color.cyan, band) :
            band < 2f ? Color.Lerp(Color.cyan, new Color(0.18f, 0.85f, 0.30f), band - 1f) :
            band < 3f ? Color.Lerp(new Color(0.18f, 0.85f, 0.30f), Color.yellow, band - 2f) :
            Color.Lerp(Color.yellow, new Color(1f, 0.08f, 0.04f), band - 3f);
        return color;
    }

    #endregion

    #region 绘制资源

    private void CreateRenderResources()
    {
        Shader shader = Resources.Load<Shader>("TemperatureHeatmap");
        if (shader == null)
        {
            Debug.LogError("[GMTemperatureOverlay] 缺少 TemperatureHeatmap Shader。", this);
            SetVisible(false);
            return;
        }

        texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
        {
            name = "GM Temperature Cells",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };
        material = new Material(shader) { name = "GM Temperature Overlay", hideFlags = HideFlags.DontSave };
        material.mainTexture = texture;
        material.SetFloat(OpacityId, 1f - Transparency);
        mesh = new Mesh { name = "GM Temperature Quad", hideFlags = HideFlags.DontSave };
        mesh.MarkDynamic();
        mesh.vertices = vertices;
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        renderRoot = new GameObject("Temperature Heatmap", typeof(MeshFilter), typeof(MeshRenderer));
        renderRoot.transform.SetParent(transform, false);
        renderRoot.GetComponent<MeshFilter>().sharedMesh = mesh;
        overlayRenderer = renderRoot.GetComponent<MeshRenderer>();
        overlayRenderer.sharedMaterial = material;
        // 放在水面之后，作为透视眼镜统一覆盖地表；半透明颜色保留实体轮廓，UI 不受影响。
        overlayRenderer.sortingLayerName = "Paticle";
        overlayRenderer.sortingOrder = 32750;
        overlayRenderer.shadowCastingMode = ShadowCastingMode.Off;
        overlayRenderer.receiveShadows = false;
        overlayRenderer.enabled = false;
    }

    #endregion
}
