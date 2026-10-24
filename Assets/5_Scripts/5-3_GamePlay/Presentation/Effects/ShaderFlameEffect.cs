using UnityEngine;
using UnityEngine.Rendering;

/// <summary>可独立复用的网格火焰；火舌和火星只使用 GPU 时间，不做逐帧 CPU 模拟。</summary>
[ExecuteAlways, DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class ShaderFlameEffect : MonoBehaviour
{
    #region 配置与共享资源

    public const string MaterialResourcePath = "Combustion_Flame";
    public const string ShaderName = "Game/Effects/ShaderFlame";
    private static readonly int InstanceId = Shader.PropertyToID("_FlameInstance");
    private static Mesh sharedMesh;

    [SerializeField] private Material flameMaterial;
    [SerializeField] private Vector2 size = new(0.22f, 0.36f);
    [SerializeField, Range(0f, 4f)] private float intensity = 1f;
    [SerializeField] private bool burning = true;
    [SerializeField] private int sortingLayerId;
    [SerializeField] private int sortingOrder;

    private MeshRenderer meshRenderer;
    private MaterialPropertyBlock properties;
    private Vector4 submittedInstance;
    private bool hasSubmittedInstance;

    public MeshRenderer Renderer { get { EnsureResources(); return meshRenderer; } }

    // 只有配置变化才提交实例参数，同一材质不会因每个火源而被复制。
    public void Configure(Vector2 dimensions, float strength = 1f, Material material = null)
    {
        size = new Vector2(FiniteClamp(dimensions.x, 0.001f, 1000f, 0.22f),
            FiniteClamp(dimensions.y, 0.001f, 1000f, 0.36f));
        intensity = FiniteClamp(strength, 0f, 4f, 1f);
        flameMaterial = material;
        ApplyConfiguration();
    }

    public void SetBurning(bool active)
    {
        burning = active;
        EnsureResources();
        bool visible = active && intensity > 0f && isActiveAndEnabled;
        if (meshRenderer.enabled != visible) meshRenderer.enabled = visible;
    }

    public void SetSorting(int layerId, int order)
    {
        sortingLayerId = layerId;
        sortingOrder = order;
        EnsureResources();
        if (meshRenderer.sortingLayerID != layerId) meshRenderer.sortingLayerID = layerId;
        if (meshRenderer.sortingOrder != order) meshRenderer.sortingOrder = order;
    }

    private void EnsureResources()
    {
        if (meshRenderer != null) return;
        meshRenderer = GetComponent<MeshRenderer>();
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        if (GetComponent<ActorRenderEffectExclude>() == null)
            gameObject.AddComponent<ActorRenderEffectExclude>();
        // 借用 Unity 内置四顶点网格，域重载或销毁未激活对象时也没有自有 Mesh 泄漏。
        if (sharedMesh == null) sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        GetComponent<MeshFilter>().sharedMesh = sharedMesh;
    }

    private void ApplyConfiguration()
    {
        EnsureResources();
        Material material = flameMaterial != null ? flameMaterial : Resources.Load<Material>(MaterialResourcePath);
        if (material == null)
            throw new System.InvalidOperationException("Shader 火焰缺少 Resources/Combustion_Flame 材质。");
        if (meshRenderer.sharedMaterial != material) meshRenderer.sharedMaterial = material;

        // 相位不消费游戏随机数，也不随物品移动而跳变。
        float phase = (GetInstanceID() & 65535) * 0.0173f;
        var instance = new Vector4(size.x, size.y, intensity, phase);
        if (!hasSubmittedInstance || submittedInstance != instance)
        {
            properties ??= new MaterialPropertyBlock();
            properties.SetVector(InstanceId, instance);
            meshRenderer.SetPropertyBlock(properties);
            // Shader 缩放顶点时同步 CPU 裁剪边界，防止大火焰在屏幕边缘提前消失。
            meshRenderer.localBounds = new Bounds(new Vector3(0f, size.y * 0.5f, 0f),
                new Vector3(size.x, size.y, 0.05f));
            submittedInstance = instance;
            hasSubmittedInstance = true;
        }
        SetSorting(sortingLayerId, sortingOrder);
        SetBurning(burning);
    }

    private static float FiniteClamp(float value, float min, float max, float fallback)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);
    }

    #endregion

    #region 生命周期

    private void OnEnable()
    {
        hasSubmittedInstance = false;
        ApplyConfiguration();
    }

    private void OnDisable()
    {
        if (meshRenderer != null) meshRenderer.enabled = false;
    }

    private void OnDestroy()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall -= ApplyInspectorConfiguration;
#endif
    }

    private void OnValidate()
    {
        size.x = FiniteClamp(size.x, 0.001f, 1000f, 0.22f);
        size.y = FiniteClamp(size.y, 0.001f, 1000f, 0.36f);
        intensity = FiniteClamp(intensity, 0f, 4f, 1f);
#if UNITY_EDITOR
        // OnValidate 可能来自导入线程，延后到主线程更新编辑器预览。
        UnityEditor.EditorApplication.delayCall -= ApplyInspectorConfiguration;
        UnityEditor.EditorApplication.delayCall += ApplyInspectorConfiguration;
#endif
    }

#if UNITY_EDITOR
    private void ApplyInspectorConfiguration()
    {
        if (this != null && isActiveAndEnabled && gameObject.scene.IsValid()) ApplyConfiguration();
    }
#endif

    #endregion
}
