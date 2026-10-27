using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>固定雨丝网格交给 Shader 推进下落，CPU 只同步镜头、雨量和整层风偏移。</summary>
[DefaultExecutionOrder(10001), DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class RainEffectController : MonoBehaviour
{
    #region 配置与资源

    internal const int MaximumDrops = 2048;
    private static readonly int FieldId = Shader.PropertyToID("_RainField");
    private static readonly int DriftId = Shader.PropertyToID("_RainDrift");
    private static readonly int WindId = Shader.PropertyToID("_RainWind");
    private static readonly int LengthId = Shader.PropertyToID("_RainLength");
    private static readonly int WidthId = Shader.PropertyToID("_RainWidth");

    [SerializeField, Min(1f)] private float coveragePadding = 1.1f;
    [SerializeField, Min(0f)] private float dropsPerSquareUnit = 1.5f;
    [SerializeField, Min(0f)] private float windDriftSpeed = 7f;

    private Mesh mesh;
    private MeshRenderer meshRenderer;
    private Transform effectTransform;
    private MaterialPropertyBlock properties;
    private Camera targetCamera;
    private float intensity;
    private float streakMargin;
    private Vector2 wind;
    private double driftX;
    private double driftY;
    private Vector3 previousCameraPosition;
    private bool hasCameraPosition;
    private bool hasSubmitted;
    private Vector4 submittedField;
    private Vector4 submittedDrift;
    private Vector4 submittedWind;

    private void Awake()
    {
        effectTransform = transform;
        meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer.sharedMaterial == null)
            throw new MissingReferenceException("Shader 降雨缺少 RainStreak 材质。");
        meshRenderer.enabled = false;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        WorldRenderingManager.Instance.ApplyRendererSorting(meshRenderer, WorldSortingManager.WorldEffectCategory);
        properties = new MaterialPropertyBlock();
        RefreshStreakMargin();
        mesh = BuildMesh();
        GetComponent<MeshFilter>().sharedMesh = mesh;
    }

    private void OnValidate()
    {
        coveragePadding = Mathf.Max(1f, coveragePadding);
        dropsPerSquareUnit = Mathf.Max(0f, dropsPerSquareUnit);
        windDriftSpeed = Mathf.Max(0f, windDriftSpeed);
        hasSubmitted = false;
        if (meshRenderer != null && meshRenderer.sharedMaterial != null)
            RefreshStreakMargin();
    }

    private void RefreshStreakMargin()
    {
        Vector4 length = meshRenderer.sharedMaterial.GetVector(LengthId);
        Vector4 width = meshRenderer.sharedMaterial.GetVector(WidthId);
        // 额外留出最长近景雨丝的空间，倾斜和视口外环绕都不会露出边界。
        streakMargin = (Mathf.Max(Mathf.Abs(length.x), Mathf.Abs(length.y)) +
            Mathf.Max(Mathf.Abs(width.x), Mathf.Abs(width.y))) * 1.25f;
    }

    private static Mesh BuildMesh()
    {
        var vertices = new Vector3[MaximumDrops * 4];
        var uvs = new Vector2[vertices.Length];
        var seeds = new List<Vector4>(vertices.Length);
        var thresholds = new Vector2[vertices.Length];
        var triangles = new int[MaximumDrops * 6];
        var random = new System.Random(78041);
        for (int i = 0; i < MaximumDrops; i++)
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
            for (int corner = 0; corner < 4; corner++)
            {
                seeds.Add(seed);
                thresholds[v + corner] = new Vector2((i + 0.5f) / MaximumDrops, 0f);
            }
            triangles[t] = v;
            triangles[t + 1] = v + 1;
            triangles[t + 2] = v + 2;
            triangles[t + 3] = v;
            triangles[t + 4] = v + 2;
            triangles[t + 5] = v + 3;
        }
        var result = new Mesh { name = "Shader 降雨固定网格", hideFlags = HideFlags.DontSave };
        result.vertices = vertices;
        result.uv = uvs;
        result.SetUVs(1, seeds);
        result.uv3 = thresholds;
        result.triangles = triangles;
        result.UploadMeshData(true);
        return result;
    }

    #endregion

    #region 天气输入与相机同步

    public void SyncToCamera(Camera camera, float strength)
    {
        if (targetCamera != camera) hasCameraPosition = false;
        targetCamera = camera;
        intensity = Mathf.Clamp01(strength);
    }

    public void SetWind(Vector2 velocity) => wind = velocity * windDriftSpeed;

    private void LateUpdate()
    {
        if (targetCamera == null || !targetCamera.isActiveAndEnabled)
        {
            targetCamera = Camera.main;
            hasCameraPosition = false;
        }
        if (targetCamera == null || intensity <= 0.001f)
        {
            if (meshRenderer.enabled) meshRenderer.enabled = false;
            return;
        }

        Transform cameraTransform = targetCamera.transform;
        Vector3 cameraPosition = cameraTransform.position;
        Vector3 right = cameraTransform.right, up = cameraTransform.up;
        Vector3 movement = hasCameraPosition ? cameraPosition - previousCameraPosition : Vector3.zero;
        Vector3 worldWind = new Vector3(wind.x, wind.y, 0f);
        // 积分风偏移并抵消镜头移动，转风不会跳轨，雨丝也不会粘在屏幕上。
        Vector3 displacement = worldWind * Time.deltaTime - movement;
        driftX += Vector3.Dot(displacement, right);
        driftY += Vector3.Dot(displacement, up);
        previousCameraPosition = cameraPosition;
        hasCameraPosition = true;

        float distance = Mathf.Clamp(1f, targetCamera.nearClipPlane + 0.01f,
            Mathf.Max(targetCamera.nearClipPlane + 0.01f, targetCamera.farClipPlane - 0.01f));
        float height = targetCamera.orthographic ? targetCamera.orthographicSize * 2f :
            Mathf.Tan(targetCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) * distance * 2f;
        Vector4 field = ResolveFieldSettings(new Vector2(height * targetCamera.aspect, height),
            coveragePadding, dropsPerSquareUnit, intensity, streakMargin);
        Vector4 drift = ResolveDrift(driftX, driftY, new Vector2(field.x, field.y));
        var localWind = new Vector4(Vector3.Dot(worldWind, right), Vector3.Dot(worldWind, up), 0f, 0f);
        Vector3 position = cameraPosition + cameraTransform.forward * distance;
        if (effectTransform.position != position || effectTransform.rotation != cameraTransform.rotation)
            effectTransform.SetPositionAndRotation(position, cameraTransform.rotation);

        if (!hasSubmitted || submittedField != field || submittedDrift != drift || submittedWind != localWind)
        {
            properties.SetVector(FieldId, field);
            properties.SetVector(DriftId, drift);
            properties.SetVector(WindId, localWind);
            meshRenderer.SetPropertyBlock(properties);
            if (!hasSubmitted || submittedField.x != field.x || submittedField.y != field.y)
                meshRenderer.localBounds = new Bounds(Vector3.zero,
                    new Vector3(field.x + streakMargin * 2f, field.y + streakMargin * 2f, 0.5f));
            submittedField = field;
            submittedDrift = drift;
            submittedWind = localWind;
            hasSubmitted = true;
        }
        bool visible = field.z > 0f;
        if (meshRenderer.enabled != visible) meshRenderer.enabled = visible;
    }

    internal static Vector4 ResolveFieldSettings(Vector2 viewSize, float padding, float density,
        float strength, float margin)
    {
        float edge = Mathf.Max(0.01f, margin) * 2f;
        float width = Mathf.Max(0.01f, viewSize.x) * Mathf.Max(1f, padding) + edge;
        float height = Mathf.Max(0.01f, viewSize.y) * Mathf.Max(1f, padding) + edge;
        float fraction = Mathf.Clamp01(width * height * Mathf.Max(0f, density) / MaximumDrops);
        return new Vector4(width, height, fraction * Mathf.Clamp01(strength), MaximumDrops);
    }

    internal static Vector4 ResolveDrift(double x, double y, Vector2 fieldSize) =>
        new((float)(x % fieldSize.x), (float)(y % fieldSize.y), 0f, 0f);

    #endregion

    #region 停用与释放

    private void OnEnable() => hasSubmitted = false;

    private void OnDisable()
    {
        if (meshRenderer != null) meshRenderer.enabled = false;
        intensity = 0f;
        wind = Vector2.zero;
        driftX = driftY = 0d;
        hasCameraPosition = false;
    }

    private void OnDestroy()
    {
        if (mesh == null) return;
        GetComponent<MeshFilter>().sharedMesh = null;
        Destroy(mesh);
    }

    #endregion
}
