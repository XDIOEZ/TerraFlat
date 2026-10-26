using UnityEngine;
using UnityEngine.Rendering;

/// <summary>共享四边形上的烟团全部由 Shader 推进，CPU 只提交开关、尺寸与排序。</summary>
[ExecuteAlways, DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class ShaderSmokeEffect : MonoBehaviour
{
    #region 配置与共享资源

    public const string MaterialResourcePath = "Combustion_Smoke";
    public const string ShaderName = "Game/Effects/ShaderSmoke";
    private static readonly int InstanceId = Shader.PropertyToID("_SmokeInstance");
    private static readonly int StartTimeId = Shader.PropertyToID("_SmokeStartTime");
    private static Mesh sharedMesh;

    private Material smokeMaterial;
    private Vector2 size = new(0.65f, 1.25f);
    private float density = 1f;
    private bool emitting;
    private float startTime;
    private MeshRenderer meshRenderer;
    private MaterialPropertyBlock properties;
    private Vector4 submittedInstance;
    private float submittedStartTime;
    private bool hasSubmittedInstance;

    public MeshRenderer Renderer { get { EnsureSharedResources(); return meshRenderer; } }

    public void ConfigureSmokePresentation(Vector2 dimensions, float strength = 1f, Material material = null)
    {
        size = new Vector2(FiniteClamp(dimensions.x, 0.01f, 1000f, 0.65f),
            FiniteClamp(dimensions.y, 0.01f, 1000f, 1.25f));
        density = FiniteClamp(strength, 0f, 4f, 1f);
        smokeMaterial = material;
        ApplySmokeConfiguration();
    }

    public void SetSmokeEmitting(bool active)
    {
        if (active && !emitting)
        {
            startTime = Time.time;
            hasSubmittedInstance = false;
        }
        emitting = active;
        if (!hasSubmittedInstance) ApplySmokeConfiguration();
        else ApplyRendererVisibility();
    }

    public void SetSmokeSorting(int layerId, int order)
    {
        EnsureSharedResources();
        if (meshRenderer.sortingLayerID != layerId) meshRenderer.sortingLayerID = layerId;
        if (meshRenderer.sortingOrder != order) meshRenderer.sortingOrder = order;
    }

    private void EnsureSharedResources()
    {
        if (meshRenderer != null) return;
        meshRenderer = GetComponent<MeshRenderer>();
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        if (GetComponent<ActorRenderEffectExclude>() == null)
            gameObject.AddComponent<ActorRenderEffectExclude>();
        if (sharedMesh == null) sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        GetComponent<MeshFilter>().sharedMesh = sharedMesh;
    }

    private void ApplySmokeConfiguration()
    {
        EnsureSharedResources();
        Material material = smokeMaterial != null ? smokeMaterial : Resources.Load<Material>(MaterialResourcePath);
        if (material == null)
            throw new System.InvalidOperationException("Shader 烟雾缺少 Resources/Combustion_Smoke 材质。");
        if (meshRenderer.sharedMaterial != material) meshRenderer.sharedMaterial = material;
        float phase = (GetInstanceID() & 65535) * 0.0173f;
        var instance = new Vector4(size.x, size.y, density, phase);
        if (!hasSubmittedInstance || submittedInstance != instance || submittedStartTime != startTime)
        {
            properties ??= new MaterialPropertyBlock();
            properties.SetVector(InstanceId, instance);
            properties.SetFloat(StartTimeId, startTime);
            meshRenderer.SetPropertyBlock(properties);
            // 包围盒覆盖整条上升烟柱，屏幕边缘不会按原始 Quad 提前裁掉烟团。
            meshRenderer.localBounds = new Bounds(new Vector3(0f, size.y * 0.5f, 0f),
                new Vector3(size.x, size.y, 0.05f));
            submittedInstance = instance;
            submittedStartTime = startTime;
            hasSubmittedInstance = true;
        }
        ApplyRendererVisibility();
    }

    private void ApplyRendererVisibility()
    {
        EnsureSharedResources();
        bool visible = emitting && density > 0f && isActiveAndEnabled;
        if (meshRenderer.enabled != visible) meshRenderer.enabled = visible;
    }

    private static float FiniteClamp(float value, float min, float max, float fallback)
        => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);

    #endregion

    #region 生命周期

    private void OnEnable()
    {
        startTime = Time.time;
        hasSubmittedInstance = false;
        ApplySmokeConfiguration();
    }

    private void OnDisable()
    {
        if (meshRenderer != null) meshRenderer.enabled = false;
    }

    #endregion
}
