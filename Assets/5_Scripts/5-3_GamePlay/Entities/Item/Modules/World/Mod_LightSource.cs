using MemoryPack;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 通用物品光照模块：统一维护逻辑光照参数与 Unity Light2D 的显示参数。
/// 地块光照层由 LightLayerMgr 按此 Light2D 的实时强度、范围和距离进行计算。
/// 运行时既可作为独立模块 Prefab，也可直接挂在物品外壳上供 JSON 定义复用。
/// </summary>
public class Mod_LightSource : Module, ICombustionStateReceiver
{
    private const string EmissiveOverlayName = "Light Emissive Overlay";
    private const string EmissiveShaderResourcePath = "Shaders/EmissiveSpriteOverlay";
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int ClipLocalYId = Shader.PropertyToID("_ClipLocalY");

    public Ex_ModData_MemoryPackable ModData = new Ex_ModData_MemoryPackable
    {
        ID = ModText.LightSource
    };

    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = (Ex_ModData_MemoryPackable)value;
    }

    [Tooltip("可以不手动赋值，模块会自动查找物品子对象中的 Point Light2D")]
    public Light2D TargetLight;

    [SerializeField, Tooltip("光源颜色；具体设备通过 JSON 配置，不在模块里硬编码。")]
    private Color lightColor = Color.white;

    [Tooltip("接收局部光照的 Sorting Layer 名称；空列表沿用光源 Prefab 原配置。不是物理碰撞层或真实高度。")]
    public string[] TargetSortingLayers = System.Array.Empty<string>();

    [Tooltip("开启后，只有同一物品的燃烧模块处于活动状态时才输出光照。")]
    public bool followCombustionState;

    [Tooltip("光照模块的运行时/存档参数")]
    public LightSourceData Data = new LightSourceData();

    [Header("自发光 Sprite")]
    [Tooltip("用于自身发光的 Sprite。开启后，指定高度以上额外绘制一层不受 2D 阴影影响的自发光区域。")]
    [SerializeField] private bool keepUpperSpriteEmissive;

    [Tooltip("开启后让整个源 Sprite 使用不受 2D 阴影压暗的覆盖层；仍保留比自身基准更亮的光照结果，也不改变它对世界投射的阴影。")]
    [SerializeField] private bool ignore2DShadowsOnSourceSprite;

    [Tooltip("自发光覆盖层开始绘制的 Sprite 本地 Y 坐标。")]
    [SerializeField] private float emissiveStartLocalY = 0.38f;

    private SpriteRenderer emissiveSourceRenderer;
    private SpriteRenderer emissiveOverlayRenderer;
    private MaterialPropertyBlock emissivePropertyBlock;
    private bool hasEmissiveVisualState;
    private EmissiveVisualState lastEmissiveVisualState;
    private SpriteRenderer syncedEmissiveOverlay;
    private Material syncedEmissiveMaterial;
    private bool combustionActive = true;
    private Light2D sortingLayerSourceLight;
    private int[] originalSortingLayerIds;

    private static Material sharedEmissiveMaterial;
    private static bool emissiveShaderMissingLogged;

    public float LightIntensity => Data != null ? Data.Intensity : 0f;
    public float LightRange => Data != null ? Data.Range : 0f;
    public bool IsLightEnabled =>
        Data != null &&
        Data.IsEnabled &&
        (!followCombustionState || combustionActive);

    public override void Awake()
    {
        base.Awake();
        ResolveTargetLight();
        ApplyTargetSortingLayers();
        ApplyToUnityLight();
        RefreshEmissiveOverlay();
    }

    private void OnEnable()
    {
        hasEmissiveVisualState = false;
        ResolveTargetLight();
        ApplyTargetSortingLayers();
        RefreshEmissiveOverlay();
    }

    private void OnDisable()
    {
        if (emissiveOverlayRenderer != null)
            emissiveOverlayRenderer.enabled = false;
        hasEmissiveVisualState = false;
    }

    private void OnValidate()
    {
        ModData ??= new Ex_ModData_MemoryPackable();
        ModData.ID = ModText.LightSource;
        Data ??= new LightSourceData();
        ClampData();
        ResolveTargetLight();
        ApplyTargetSortingLayers();
        ApplyToUnityLight();
    }

    #region 2D光源平面约束

    /// <summary>
    /// 玩家转身会让手持物绕 Y 轴插值旋转；Point Light2D 必须保持在 XY 平面，避免中途被投影成扁椭圆。
    /// </summary>
    private void LateUpdate()
    {
        KeepPointLightOn2DPlane();
        // 未配置自发光且未创建过覆盖层时，无需进入逐帧 Sprite 表现检查。
        if (keepUpperSpriteEmissive || ignore2DShadowsOnSourceSprite || emissiveOverlayRenderer != null)
            RefreshEmissiveOverlay();
    }

    /// <summary>只修正 Point Light2D 的世界 X/Y 旋转，保留 Z 轴朝向。</summary>
    private void KeepPointLightOn2DPlane()
    {
        if (TargetLight == null || TargetLight.lightType != Light2D.LightType.Point)
            return;

        // 已经绕世界 Z 轴旋转的光源直接通过四元数分量判定，避免每帧转换欧拉角。
        Quaternion worldRotation = TargetLight.transform.rotation;
        if (Mathf.Abs(worldRotation.x) < 0.000001f && Mathf.Abs(worldRotation.y) < 0.000001f)
            return;

        Vector3 worldEuler = TargetLight.transform.eulerAngles;
        if (Mathf.Abs(Mathf.DeltaAngle(worldEuler.x, 0f)) < 0.01f &&
            Mathf.Abs(Mathf.DeltaAngle(worldEuler.y, 0f)) < 0.01f)
        {
            return;
        }

        TargetLight.transform.rotation = Quaternion.Euler(0f, 0f, worldEuler.z);
    }

    #endregion

    protected override void OnLoad()
    {
        Data ??= new LightSourceData();
        ModData?.ReadData(ref Data);
        Data ??= new LightSourceData();
        ClampData();
        ResolveTargetLight();
        ApplyTargetSortingLayers();
        ApplyToUnityLight();
        RefreshEmissiveOverlay();
    }

    protected override void OnSave()
    {
        ClampData();
        ModData?.WriteData(Data);
    }

    /// <summary>
    /// 设置光照强度。值越高，Unity 灯光和地块光照层的亮度都越高。
    /// </summary>
    public void SetLightIntensity(float intensity)
    {
        Data ??= new LightSourceData();
        Data.Intensity = Mathf.Max(0f, intensity);
        ApplyToUnityLight();
    }

    /// <summary>
    /// 设置光照半径，并同步到 Point Light2D 的外半径。
    /// </summary>
    public void SetLightRange(float range)
    {
        Data ??= new LightSourceData();
        Data.Range = Mathf.Max(0f, range);
        Data.InnerRadius = Mathf.Clamp(Data.InnerRadius, 0f, Data.Range);
        ApplyToUnityLight();
    }

    public void SetInnerRadius(float innerRadius)
    {
        Data ??= new LightSourceData();
        Data.InnerRadius = Mathf.Clamp(innerRadius, 0f, Mathf.Max(0f, Data.Range));
        ApplyToUnityLight();
    }

    public void SetLightEnabled(bool isEnabled)
    {
        Data ??= new LightSourceData();
        Data.IsEnabled = isEnabled;
        ApplyToUnityLight();
    }

    /// <summary>燃烧状态只作为运行时门控，不改写光源自身的持久化启用配置。</summary>
    public void SetCombustionActive(bool active)
    {
        combustionActive = active;
        ApplyToUnityLight();
        RefreshEmissiveOverlay();
    }

    public void SetLightParameters(float intensity, float range, float innerRadius, bool isEnabled = true)
    {
        Data ??= new LightSourceData();
        Data.Intensity = Mathf.Max(0f, intensity);
        Data.Range = Mathf.Max(0f, range);
        Data.InnerRadius = Mathf.Clamp(innerRadius, 0f, Data.Range);
        Data.IsEnabled = isEnabled;
        ApplyToUnityLight();
    }

    private void ResolveTargetLight()
    {
        if (TargetLight != null)
            return;

        Transform searchRoot = item != null ? item.transform : transform;
        TargetLight = searchRoot.GetComponentInChildren<Light2D>(true);
    }

    #region 光照接收层与建筑遮挡契约

    /// <summary>MOD 可覆盖此入口提供自身光源；读取真实 Light2D 状态，兼容燃料模块直接熄灯。</summary>
    public virtual bool TryGetActiveOcclusionLight(out Light2D light)
    {
        light = TargetLight;
        return isActiveAndEnabled && light != null && light.isActiveAndEnabled &&
               light.lightType == Light2D.LightType.Point && light.intensity > 0f &&
               light.pointLightOuterRadius > 0f;
    }

    /// <summary>配置局部光照的目标层并立即应用；空列表恢复 Prefab 基线，不改变 Sprite 排序与物理碰撞。</summary>
    public void SetTargetSortingLayers(string[] layerNames)
    {
        // 在修改配置前检查名称，避免无效 MOD 参数留下半应用状态。
        Light2DSortingLayerUtility.ResolveLayerIds(layerNames);
        TargetSortingLayers = layerNames != null ? (string[])layerNames.Clone() : System.Array.Empty<string>();
        ResolveTargetLight();
        ApplyTargetSortingLayers();
    }

    /// <summary>加载配置时解析名称，不在光照强度更新热路径中重复分配层数组。</summary>
    private void ApplyTargetSortingLayers()
    {
        if (TargetLight == null)
            return;
        if (sortingLayerSourceLight != TargetLight)
        {
            sortingLayerSourceLight = TargetLight;
            originalSortingLayerIds = Light2DSortingLayerUtility.GetLightLayers(TargetLight)
                ?? Light2DSortingLayerUtility.ResolveLayerIds(null);
        }

        int[] targetLayers = TargetSortingLayers == null || TargetSortingLayers.Length == 0
            ? originalSortingLayerIds
            : Light2DSortingLayerUtility.ResolveLayerIds(TargetSortingLayers);
        Light2DSortingLayerUtility.SetLightLayers(TargetLight, targetLayers);
    }

    #endregion

    private void ClampData()
    {
        if (Data == null)
            return;

        Data.Intensity = Mathf.Max(0f, Data.Intensity);
        Data.Range = Mathf.Max(0f, Data.Range);
        Data.InnerRadius = Mathf.Clamp(Data.InnerRadius, 0f, Data.Range);
    }

    private void ApplyToUnityLight()
    {
        if (TargetLight == null || Data == null)
            return;

        TargetLight.intensity = Data.Intensity;
        TargetLight.color = lightColor;
        if (TargetLight.lightType == Light2D.LightType.Point)
        {
            TargetLight.pointLightOuterRadius = Data.Range;
            TargetLight.pointLightInnerRadius = Data.InnerRadius;
            ConfigureSolidWorldOcclusion(TargetLight);
        }

        TargetLight.enabled = IsLightEnabled && Data.Intensity > 0f && Data.Range > 0f;
        KeepPointLightOn2DPlane();
    }

    #region 自发光 Sprite 覆盖层

    // 每帧只读取源贴图状态，真正变化时才写回 Renderer 与 MPB。
    private readonly struct EmissiveVisualState
    {
        public readonly Sprite Sprite;
        public readonly Texture Texture;
        public readonly Color Color;
        public readonly bool FlipX, FlipY;
        public readonly SpriteDrawMode DrawMode;
        public readonly Vector2 Size;
        public readonly SpriteTileMode TileMode;
        public readonly SpriteMaskInteraction Mask;
        public readonly SpriteSortPoint SortPoint;
        public readonly int Layer, Order;
        public readonly float ClipLocalY;

        public EmissiveVisualState(SpriteRenderer source, float clipLocalY)
        {
            Sprite = source.sprite;
            Texture = Sprite != null ? Sprite.texture : null;
            Color = source.color;
            FlipX = source.flipX;
            FlipY = source.flipY;
            DrawMode = source.drawMode;
            Size = source.size;
            TileMode = source.tileMode;
            Mask = source.maskInteraction;
            SortPoint = source.spriteSortPoint;
            Layer = source.sortingLayerID;
            Order = source.sortingOrder + 1;
            ClipLocalY = clipLocalY;
        }

        public bool Matches(in EmissiveVisualState other) =>
            Sprite == other.Sprite && Texture == other.Texture && Color.Equals(other.Color) &&
            FlipX == other.FlipX && FlipY == other.FlipY &&
            DrawMode == other.DrawMode && Size.Equals(other.Size) &&
            TileMode == other.TileMode && Mask == other.Mask &&
            SortPoint == other.SortPoint && Layer == other.Layer &&
            Order == other.Order && ClipLocalY == other.ClipLocalY;
    }

    /// <summary>
    /// Blocking Tile 的 ShadowCaster2D 开启 selfShadows 后，会通过 2D 阴影模板影响所有与墙体占地重叠的 Lit Sprite。
    /// 发光体可按配置只覆盖上半部，或覆盖整个源 Sprite；覆盖层只抬高被阴影压暗的结果，仍保留更亮的 Lit 结果。
    /// 墙体自己的实体遮光规则保持不变。
    /// </summary>
    private void RefreshEmissiveOverlay()
    {
        if (!keepUpperSpriteEmissive && !ignore2DShadowsOnSourceSprite)
        {
            if (emissiveOverlayRenderer != null)
                emissiveOverlayRenderer.enabled = false;
            hasEmissiveVisualState = false;
            return;
        }

        ResolveEmissiveSourceRenderer();
        bool shouldRender = emissiveSourceRenderer != null && isActiveAndEnabled &&
                            emissiveSourceRenderer.enabled &&
                            emissiveSourceRenderer.sprite != null &&
                            TargetLight != null &&
                            TargetLight.enabled &&
                            TargetLight.intensity > 0f;
        if (!shouldRender)
        {
            if (emissiveOverlayRenderer != null && emissiveOverlayRenderer.enabled)
                emissiveOverlayRenderer.enabled = false;
            hasEmissiveVisualState = false;
            return;
        }

        EnsureEmissiveOverlay();
        if (emissiveOverlayRenderer == null || emissiveOverlayRenderer.sharedMaterial == null)
            return;
        if (!emissiveOverlayRenderer.enabled)
            emissiveOverlayRenderer.enabled = true;

        float clipLocalY = ignore2DShadowsOnSourceSprite
            ? emissiveSourceRenderer.sprite.bounds.min.y - Mathf.Epsilon
            : emissiveStartLocalY;
        var visual = new EmissiveVisualState(emissiveSourceRenderer, clipLocalY);
        bool firstSync = !hasEmissiveVisualState;
        if (!firstSync && visual.Matches(lastEmissiveVisualState))
            return;

        if (firstSync || visual.Sprite != lastEmissiveVisualState.Sprite)
            emissiveOverlayRenderer.sprite = visual.Sprite;
        if (firstSync || !visual.Color.Equals(lastEmissiveVisualState.Color))
            emissiveOverlayRenderer.color = visual.Color;
        if (firstSync || visual.FlipX != lastEmissiveVisualState.FlipX)
            emissiveOverlayRenderer.flipX = visual.FlipX;
        if (firstSync || visual.FlipY != lastEmissiveVisualState.FlipY)
            emissiveOverlayRenderer.flipY = visual.FlipY;
        if (firstSync || visual.DrawMode != lastEmissiveVisualState.DrawMode)
            emissiveOverlayRenderer.drawMode = visual.DrawMode;
        if (firstSync || !visual.Size.Equals(lastEmissiveVisualState.Size))
            emissiveOverlayRenderer.size = visual.Size;
        if (firstSync || visual.TileMode != lastEmissiveVisualState.TileMode)
            emissiveOverlayRenderer.tileMode = visual.TileMode;
        if (firstSync || visual.Mask != lastEmissiveVisualState.Mask)
            emissiveOverlayRenderer.maskInteraction = visual.Mask;
        if (firstSync || visual.SortPoint != lastEmissiveVisualState.SortPoint)
            emissiveOverlayRenderer.spriteSortPoint = visual.SortPoint;
        if (firstSync || visual.Layer != lastEmissiveVisualState.Layer)
            emissiveOverlayRenderer.sortingLayerID = visual.Layer;
        if (firstSync || visual.Order != lastEmissiveVisualState.Order)
            emissiveOverlayRenderer.sortingOrder = visual.Order;

        if (firstSync || visual.Sprite != lastEmissiveVisualState.Sprite ||
            visual.Texture != lastEmissiveVisualState.Texture ||
            visual.ClipLocalY != lastEmissiveVisualState.ClipLocalY)
        {
            emissivePropertyBlock ??= new MaterialPropertyBlock();
            emissiveOverlayRenderer.GetPropertyBlock(emissivePropertyBlock);
            // 只在贴图或裁剪位置变化时提交 Shader 参数，避免静态光源逐帧访问 MPB。
            emissivePropertyBlock.SetTexture(MainTexId, visual.Texture);
            emissivePropertyBlock.SetFloat(ClipLocalYId, visual.ClipLocalY);
            emissiveOverlayRenderer.SetPropertyBlock(emissivePropertyBlock);
        }
        lastEmissiveVisualState = visual;
        hasEmissiveVisualState = true;
    }

    private void ResolveEmissiveSourceRenderer()
    {
        if (emissiveSourceRenderer != null)
            return;

        Transform searchRoot = item != null ? item.transform : transform;
        SpriteRenderer[] renderers = searchRoot.GetComponentsInChildren<SpriteRenderer>(true);

        // 动画 SpriteRenderer 自带 Animator，优先锁定它，避免误选交互描边等运行时代理。
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer candidate = renderers[i];
            if (candidate == null || candidate.gameObject.name == EmissiveOverlayName)
                continue;
            if (candidate.GetComponent<Animator>() != null)
            {
                emissiveSourceRenderer = candidate;
                return;
            }
        }

        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer candidate = renderers[i];
            if (candidate != null && candidate.gameObject.name != EmissiveOverlayName)
            {
                emissiveSourceRenderer = candidate;
                return;
            }
        }
    }

    private void EnsureEmissiveOverlay()
    {
        if (emissiveSourceRenderer == null)
            return;

        if (emissiveOverlayRenderer == null)
        {
            Transform existing = emissiveSourceRenderer.transform.Find(EmissiveOverlayName);
            if (existing != null)
                emissiveOverlayRenderer = existing.GetComponent<SpriteRenderer>();
        }

        if (emissiveOverlayRenderer == null)
        {
            var overlayObject = new GameObject(EmissiveOverlayName);
            overlayObject.layer = emissiveSourceRenderer.gameObject.layer;
            overlayObject.transform.SetParent(emissiveSourceRenderer.transform, false);
            overlayObject.transform.localPosition = Vector3.zero;
            overlayObject.transform.localRotation = Quaternion.identity;
            overlayObject.transform.localScale = Vector3.one;
            emissiveOverlayRenderer = overlayObject.AddComponent<SpriteRenderer>();
        }

        if (syncedEmissiveOverlay != emissiveOverlayRenderer)
        {
            syncedEmissiveOverlay = emissiveOverlayRenderer;
            syncedEmissiveMaterial = null;
            hasEmissiveVisualState = false;
        }
        Material currentMaterial = emissiveOverlayRenderer.sharedMaterial;
        if (currentMaterial == null || currentMaterial != syncedEmissiveMaterial)
        {
            if (currentMaterial == null || currentMaterial.shader == null ||
                currentMaterial.shader.name != "Game/2D/Emissive-Sprite-Overlay")
            {
                currentMaterial = GetSharedEmissiveMaterial();
                emissiveOverlayRenderer.sharedMaterial = currentMaterial;
            }
            syncedEmissiveMaterial = currentMaterial;
            hasEmissiveVisualState = false;
        }
    }

    private static Material GetSharedEmissiveMaterial()
    {
        if (sharedEmissiveMaterial != null)
            return sharedEmissiveMaterial;

        Shader shader = Resources.Load<Shader>(EmissiveShaderResourcePath);
        if (shader == null)
        {
            if (!emissiveShaderMissingLogged)
            {
                Debug.LogError($"[Mod_LightSource] 缺少自发光覆盖 Shader：{EmissiveShaderResourcePath}");
                emissiveShaderMissingLogged = true;
            }
            return null;
        }

        emissiveShaderMissingLogged = false;
        sharedEmissiveMaterial = new Material(shader)
        {
            name = "Light Emissive Overlay (Runtime)",
            hideFlags = HideFlags.HideAndDontSave
        };
        return sharedEmissiveMaterial;
    }

    #endregion

    /// <summary>
    /// 世界实体 Point Light 必须把 Blocking Tile 视为不透光实体。
    /// 阴影强度 1 只移除当前局部光，昼夜全局光仍会正常作用于阴影区域。
    /// </summary>
    private static void ConfigureSolidWorldOcclusion(Light2D targetLight)
    {
        targetLight.shadowsEnabled = true;
        targetLight.shadowIntensity = 1f;

        if (!targetLight.volumeIntensityEnabled)
            return;

        targetLight.volumetricShadowsEnabled = true;
        targetLight.shadowVolumeIntensity = 1f;
    }
}

[MemoryPackable]
[System.Serializable]
public partial class LightSourceData
{
    [Tooltip("是否开启光源")]
    public bool IsEnabled = true;

    [Tooltip("光照强度；同时驱动 Unity Light2D.intensity 和地块光照层")]
    public float Intensity = 1f;

    [Tooltip("光照外半径；同时驱动 Point Light2D.pointLightOuterRadius")]
    public float Range = 8f;

    [Tooltip("光照内半径，内半径内保持最大强度")]
    public float InnerRadius = 0.1f;
}
