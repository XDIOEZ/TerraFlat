using MemoryPack;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 通用物品光照模块：统一维护逻辑光照参数与 Unity Light2D 的显示参数。
/// 地块光照层由 LightLayerMgr 按此 Light2D 的实时强度、范围和距离进行计算。
/// 运行时既可作为独立模块 Prefab，也可直接挂在物品外壳上供 JSON 定义复用。
/// </summary>
public class Mod_LightSource : Module
{
    private const string EmissiveOverlayName = "Light Emissive Overlay";
    private const string EmissiveShaderResourcePath = "Shaders/TorchEmissiveOverlay";
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

    [Tooltip("光照模块的运行时/存档参数")]
    public LightSourceData Data = new LightSourceData();

    [Header("自发光 Sprite")]
    [Tooltip("仅用于火把等自身发光的 Sprite。开启后，指定高度以上额外绘制一层不受 2D 阴影影响的自发光区域。")]
    [SerializeField] private bool keepUpperSpriteEmissive;

    [Tooltip("自发光覆盖层开始绘制的 Sprite 本地 Y 坐标。")]
    [SerializeField] private float emissiveStartLocalY = 0.38f;

    private SpriteRenderer emissiveSourceRenderer;
    private SpriteRenderer emissiveOverlayRenderer;
    private MaterialPropertyBlock emissivePropertyBlock;

    private static Material sharedEmissiveMaterial;
    private static bool emissiveShaderMissingLogged;

    public float LightIntensity => Data != null ? Data.Intensity : 0f;
    public float LightRange => Data != null ? Data.Range : 0f;
    public bool IsLightEnabled => Data != null && Data.IsEnabled;

    public override void Awake()
    {
        base.Awake();
        ResolveTargetLight();
        ApplyToUnityLight();
        RefreshEmissiveOverlay();
    }

    private void OnEnable()
    {
        ResolveTargetLight();
        RefreshEmissiveOverlay();
    }

    private void OnDisable()
    {
        if (emissiveOverlayRenderer != null)
            emissiveOverlayRenderer.enabled = false;
    }

    private void OnValidate()
    {
        ModData ??= new Ex_ModData_MemoryPackable();
        ModData.ID = ModText.LightSource;
        Data ??= new LightSourceData();
        ClampData();
        ResolveTargetLight();
        ApplyToUnityLight();
    }

    #region 2D光源平面约束

    /// <summary>
    /// 玩家转身会让手持物绕 Y 轴插值旋转；Point Light2D 必须保持在 XY 平面，避免中途被投影成扁椭圆。
    /// </summary>
    private void LateUpdate()
    {
        KeepPointLightOn2DPlane();
        RefreshEmissiveOverlay();
    }

    /// <summary>只修正 Point Light2D 的世界 X/Y 旋转，保留 Z 轴朝向。</summary>
    private void KeepPointLightOn2DPlane()
    {
        if (TargetLight == null || TargetLight.lightType != Light2D.LightType.Point)
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

    public override void Load()
    {
        Data ??= new LightSourceData();
        ModData?.ReadData(ref Data);
        Data ??= new LightSourceData();
        ClampData();
        ResolveTargetLight();
        ApplyToUnityLight();
        RefreshEmissiveOverlay();
    }

    public override void Save()
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
        if (TargetLight.lightType == Light2D.LightType.Point)
        {
            TargetLight.pointLightOuterRadius = Data.Range;
            TargetLight.pointLightInnerRadius = Data.InnerRadius;
            ConfigureSolidWorldOcclusion(TargetLight);
        }

        TargetLight.enabled = Data.IsEnabled && Data.Intensity > 0f && Data.Range > 0f;
        KeepPointLightOn2DPlane();
    }

    #region 自发光 Sprite 覆盖层

    /// <summary>
    /// Blocking Tile 的 ShadowCaster2D 开启 selfShadows 后，会通过 2D 阴影模板影响所有与墙体占地重叠的 Lit Sprite。
    /// 火把本身属于发光体，因此只把 Sprite 上半部额外绘制为 Unlit；墙体自己的实体遮光规则保持不变。
    /// </summary>
    private void RefreshEmissiveOverlay()
    {
        if (!keepUpperSpriteEmissive)
        {
            if (emissiveOverlayRenderer != null)
                emissiveOverlayRenderer.enabled = false;
            return;
        }

        ResolveEmissiveSourceRenderer();
        if (emissiveSourceRenderer == null)
            return;

        EnsureEmissiveOverlay();
        if (emissiveOverlayRenderer == null || emissiveOverlayRenderer.sharedMaterial == null)
            return;

        bool shouldRender = isActiveAndEnabled &&
                            emissiveSourceRenderer.enabled &&
                            emissiveSourceRenderer.sprite != null &&
                            TargetLight != null &&
                            TargetLight.enabled &&
                            TargetLight.intensity > 0f;
        emissiveOverlayRenderer.enabled = shouldRender;
        if (!shouldRender)
            return;

        emissiveOverlayRenderer.sprite = emissiveSourceRenderer.sprite;
        emissiveOverlayRenderer.color = emissiveSourceRenderer.color;
        emissiveOverlayRenderer.flipX = emissiveSourceRenderer.flipX;
        emissiveOverlayRenderer.flipY = emissiveSourceRenderer.flipY;
        emissiveOverlayRenderer.drawMode = emissiveSourceRenderer.drawMode;
        emissiveOverlayRenderer.size = emissiveSourceRenderer.size;
        emissiveOverlayRenderer.tileMode = emissiveSourceRenderer.tileMode;
        emissiveOverlayRenderer.maskInteraction = emissiveSourceRenderer.maskInteraction;
        emissiveOverlayRenderer.spriteSortPoint = emissiveSourceRenderer.spriteSortPoint;
        emissiveOverlayRenderer.sortingLayerID = emissiveSourceRenderer.sortingLayerID;
        emissiveOverlayRenderer.sortingOrder = emissiveSourceRenderer.sortingOrder + 1;

        emissivePropertyBlock ??= new MaterialPropertyBlock();
        emissiveOverlayRenderer.GetPropertyBlock(emissivePropertyBlock);
        // 自定义 Sprite Shader 使用 MPB 时必须同步 _MainTex，否则运行时代理可能采到白纹理。
        emissivePropertyBlock.SetTexture(MainTexId, emissiveSourceRenderer.sprite.texture);
        emissivePropertyBlock.SetFloat(ClipLocalYId, emissiveStartLocalY);
        emissiveOverlayRenderer.SetPropertyBlock(emissivePropertyBlock);
    }

    private void ResolveEmissiveSourceRenderer()
    {
        if (emissiveSourceRenderer != null)
            return;

        Transform searchRoot = item != null ? item.transform : transform;
        SpriteRenderer[] renderers = searchRoot.GetComponentsInChildren<SpriteRenderer>(true);

        // 火把的动画 SpriteRenderer 自带 Animator，优先锁定它，避免误选交互描边等运行时代理。
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

        if (emissiveOverlayRenderer.sharedMaterial == null ||
            emissiveOverlayRenderer.sharedMaterial.shader == null ||
            emissiveOverlayRenderer.sharedMaterial.shader.name != "Game/2D/Torch-Emissive-Overlay")
        {
            emissiveOverlayRenderer.sharedMaterial = GetSharedEmissiveMaterial();
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
