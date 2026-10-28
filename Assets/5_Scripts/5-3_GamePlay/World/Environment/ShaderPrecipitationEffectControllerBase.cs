using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>统一雨雪网格的相机覆盖、风偏移、属性提交和资源生命周期。</summary>
public abstract class ShaderPrecipitationEffectControllerBase : MonoBehaviour
{
    #region 共用网格与配置

    private Mesh effectMesh;
    private MeshFilter meshFilter;
    private Transform effectTransform;
    private MaterialPropertyBlock properties;
    private Camera targetCamera;
    private float intensity;
    private Vector2 wind;
    private double driftX;
    private double driftY;
    private Vector3 previousCameraPosition;
    private bool hasCameraPosition;
    private bool hasSubmitted;
    private Vector4 submittedField;
    private Vector4 submittedDrift;
    private Vector4 submittedAdditional;

    protected MeshRenderer EffectRenderer { get; private set; }
    protected abstract int FieldPropertyId { get; }
    protected abstract int DriftPropertyId { get; }
    protected virtual int AdditionalPropertyId => 0;
    protected abstract float WindDriftSpeed { get; }
    protected abstract string MissingMaterialMessage { get; }
    protected abstract Mesh BuildEffectMesh();
    protected abstract Vector4 ResolveFieldSettingsForCamera(Vector2 viewSize, float strength);
    protected abstract Bounds ResolveLocalBounds(Vector4 field);
    protected virtual Vector4 ResolveAdditionalPropertyValue(Vector3 right, Vector3 up, Vector3 worldWind) => Vector4.zero;
    protected virtual void PrepareEffectMesh() { }

    protected virtual void Awake()
    {
        effectTransform = transform;
        EffectRenderer = GetComponent<MeshRenderer>();
        if (EffectRenderer.sharedMaterial == null)
            throw new MissingReferenceException(MissingMaterialMessage);
        EffectRenderer.enabled = false;
        EffectRenderer.shadowCastingMode = ShadowCastingMode.Off;
        EffectRenderer.receiveShadows = false;
        EffectRenderer.lightProbeUsage = LightProbeUsage.Off;
        EffectRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        WorldRenderingManager.ApplyAvailableRendererSorting(EffectRenderer, WorldSortingManager.WorldEffectCategory);
        properties = new MaterialPropertyBlock();
        PrepareEffectMesh();
        effectMesh = BuildEffectMesh();
        effectMesh.UploadMeshData(true);
        meshFilter = GetComponent<MeshFilter>();
        meshFilter.sharedMesh = effectMesh;
    }

    protected static Mesh BuildFixedQuadMesh(int sampleCount, int randomSeed, bool swayPhaseInUv3X, string meshName)
    {
        var vertices = new Vector3[sampleCount * 4];
        var uvs = new Vector2[vertices.Length];
        var seeds = new List<Vector4>(vertices.Length);
        var motions = new Vector2[vertices.Length];
        var triangles = new int[sampleCount * 6];
        var random = new System.Random(randomSeed);
        for (int i = 0; i < sampleCount; i++)
        {
            int v = i * 4, t = i * 6;
            vertices[v] = new Vector3(-0.5f, -0.5f);
            vertices[v + 1] = new Vector3(0.5f, -0.5f);
            vertices[v + 2] = new Vector3(0.5f, 0.5f);
            vertices[v + 3] = new Vector3(-0.5f, 0.5f);
            uvs[v] = Vector2.zero;
            uvs[v + 1] = Vector2.right;
            uvs[v + 2] = Vector2.one;
            uvs[v + 3] = Vector2.up;
            var seed = new Vector4((float)random.NextDouble(), (float)random.NextDouble(),
                (float)random.NextDouble(), (float)random.NextDouble());
            float threshold = (i + 0.5f) / sampleCount;
            Vector2 motion = swayPhaseInUv3X
                ? new Vector2((float)random.NextDouble() * Mathf.PI * 2f, threshold)
                : new Vector2(threshold, 0f);
            for (int corner = 0; corner < 4; corner++)
            {
                seeds.Add(seed);
                motions[v + corner] = motion;
            }
            triangles[t] = v;
            triangles[t + 1] = v + 1;
            triangles[t + 2] = v + 2;
            triangles[t + 3] = v;
            triangles[t + 4] = v + 2;
            triangles[t + 5] = v + 3;
        }
        var result = new Mesh { name = meshName, hideFlags = HideFlags.DontSave };
        result.vertices = vertices;
        result.uv = uvs;
        result.SetUVs(1, seeds);
        result.uv3 = motions;
        result.triangles = triangles;
        return result;
    }

    internal static Vector4 ResolveDrift(double x, double y, Vector2 fieldSize) =>
        new((float)(x % fieldSize.x), (float)(y % fieldSize.y), 0f, 0f);

    protected static Vector4 ResolveSharedFieldSettings(Vector2 viewSize, float padding, float density,
        float strength, float extraCoverage, int sampleCount, float shaderBudget)
    {
        float width = Mathf.Max(0.01f, viewSize.x) * Mathf.Max(1f, padding) + extraCoverage;
        float height = Mathf.Max(0.01f, viewSize.y) * Mathf.Max(1f, padding) + extraCoverage;
        float fraction = Mathf.Clamp01(width * height * Mathf.Max(0f, density) / sampleCount);
        return new Vector4(width, height, fraction * Mathf.Clamp01(strength), shaderBudget);
    }

    protected void MarkSubmissionDirty() => hasSubmitted = false;

    #endregion

    #region 天气输入与相机同步

    public void SyncToCamera(Camera camera, float strength)
    {
        if (targetCamera != camera) hasCameraPosition = false;
        targetCamera = camera;
        intensity = Mathf.Clamp01(strength);
    }

    public void SetWind(Vector2 velocity) => wind = velocity * WindDriftSpeed;

    protected virtual void LateUpdate()
    {
        if (targetCamera == null || !targetCamera.isActiveAndEnabled)
        {
            targetCamera = Camera.main;
            hasCameraPosition = false;
        }
        if (targetCamera == null || intensity <= 0.001f)
        {
            if (EffectRenderer.enabled) EffectRenderer.enabled = false;
            return;
        }

        Transform cameraTransform = targetCamera.transform;
        Vector3 cameraPosition = cameraTransform.position;
        Vector3 right = cameraTransform.right, up = cameraTransform.up;
        Vector3 movement = hasCameraPosition ? cameraPosition - previousCameraPosition : Vector3.zero;
        Vector3 worldWind = new(wind.x, wind.y, 0f);
        Vector3 displacement = worldWind * Time.deltaTime - movement;
        driftX += Vector3.Dot(displacement, right);
        driftY += Vector3.Dot(displacement, up);
        previousCameraPosition = cameraPosition;
        hasCameraPosition = true;

        float distance = Mathf.Clamp(1f, targetCamera.nearClipPlane + 0.01f,
            Mathf.Max(targetCamera.nearClipPlane + 0.01f, targetCamera.farClipPlane - 0.01f));
        float height = targetCamera.orthographic ? targetCamera.orthographicSize * 2f :
            Mathf.Tan(targetCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) * distance * 2f;
        Vector4 field = ResolveFieldSettingsForCamera(
            new Vector2(height * targetCamera.aspect, height), intensity);
        Vector4 drift = ResolveDrift(driftX, driftY, new Vector2(field.x, field.y));
        Vector4 additional = ResolveAdditionalPropertyValue(right, up, worldWind);
        Vector3 position = cameraPosition + cameraTransform.forward * distance;
        if (effectTransform.position != position || effectTransform.rotation != cameraTransform.rotation)
            effectTransform.SetPositionAndRotation(position, cameraTransform.rotation);

        if (!hasSubmitted || submittedField != field || submittedDrift != drift || submittedAdditional != additional)
        {
            properties.SetVector(FieldPropertyId, field);
            properties.SetVector(DriftPropertyId, drift);
            if (AdditionalPropertyId != 0)
                properties.SetVector(AdditionalPropertyId, additional);
            EffectRenderer.SetPropertyBlock(properties);
            if (!hasSubmitted || submittedField.x != field.x || submittedField.y != field.y)
                EffectRenderer.localBounds = ResolveLocalBounds(field);
            submittedField = field;
            submittedDrift = drift;
            submittedAdditional = additional;
            hasSubmitted = true;
        }
        bool visible = field.z > 0f;
        if (EffectRenderer.enabled != visible) EffectRenderer.enabled = visible;
    }

    #endregion

    #region 停用与释放

    protected virtual void OnEnable() => hasSubmitted = false;

    protected virtual void OnDisable()
    {
        if (EffectRenderer != null) EffectRenderer.enabled = false;
        intensity = 0f;
        wind = Vector2.zero;
        driftX = driftY = 0d;
        hasCameraPosition = false;
    }

    protected virtual void OnDestroy()
    {
        if (effectMesh == null) return;
        if (meshFilter != null) meshFilter.sharedMesh = null;
        Destroy(effectMesh);
    }

    #endregion
}
