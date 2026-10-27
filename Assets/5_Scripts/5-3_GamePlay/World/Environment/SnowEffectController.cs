using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>固定雪花网格只创建一次，下落和摆动交给 Shader，CPU 只同步镜头与整层风偏移。</summary>
[DefaultExecutionOrder(10001), DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class SnowEffectController : MonoBehaviour
{
    #region 配置与资源

    internal const int MaximumFlakes = 1500;
    private static readonly int FieldId = Shader.PropertyToID("_SnowField");
    private static readonly int DriftId = Shader.PropertyToID("_SnowDrift");
    private static readonly int SizeId = Shader.PropertyToID("_SnowSize");

    [SerializeField, Min(1f)] private float coveragePadding = 1.1f;
    [SerializeField, Min(0f)] private float flakesPerSquareUnit = 1.5f;
    [SerializeField, Min(0f)] private float windDriftSpeed = 1.5f;

    private Mesh mesh;
    private MeshRenderer meshRenderer;
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

    private void Awake()
    {
        effectTransform = transform;
        meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer.sharedMaterial == null)
            throw new MissingReferenceException("Shader 降雪缺少 Snowflake 材质。");
        meshRenderer.enabled = false;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        WorldRenderingManager.Instance.ApplyRendererSorting(meshRenderer, WorldSortingManager.WorldEffectCategory);
        properties = new MaterialPropertyBlock();
        mesh = BuildMesh();
        GetComponent<MeshFilter>().sharedMesh = mesh;
    }

    private static Mesh BuildMesh()
    {
        var vertices = new Vector3[MaximumFlakes * 4];
        var uvs = new Vector2[vertices.Length];
        var seeds = new List<Vector4>(vertices.Length);
        var motions = new Vector2[vertices.Length];
        var triangles = new int[MaximumFlakes * 6];
        var random = new System.Random(65021);
        for (int i = 0; i < MaximumFlakes; i++)
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
            var motion = new Vector2((float)random.NextDouble() * Mathf.PI * 2f,
                (i + 0.5f) / MaximumFlakes);
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
        var result = new Mesh { name = "Shader 降雪固定网格", hideFlags = HideFlags.DontSave };
        result.vertices = vertices;
        result.uv = uvs;
        result.SetUVs(1, seeds);
        result.uv3 = motions;
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
        Vector3 windMovement = new Vector3(wind.x, wind.y, 0f) * Time.deltaTime;
        // 只积分整层位移，改风向不会让已落下的雪花跳到另一条轨迹。
        driftX += Vector3.Dot(windMovement - movement, right);
        driftY += Vector3.Dot(windMovement - movement, up);
        previousCameraPosition = cameraPosition;
        hasCameraPosition = true;

        float distance = Mathf.Clamp(1f, targetCamera.nearClipPlane + 0.01f,
            Mathf.Max(targetCamera.nearClipPlane + 0.01f, targetCamera.farClipPlane - 0.01f));
        float height = targetCamera.orthographic ? targetCamera.orthographicSize * 2f :
            Mathf.Tan(targetCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) * distance * 2f;
        Vector4 field = ResolveFieldSettings(new Vector2(height * targetCamera.aspect, height),
            coveragePadding, flakesPerSquareUnit, intensity);
        Vector4 drift = ResolveDrift(driftX, driftY, new Vector2(field.x, field.y));
        Vector3 position = cameraPosition + cameraTransform.forward * distance;
        if (effectTransform.position != position || effectTransform.rotation != cameraTransform.rotation)
            effectTransform.SetPositionAndRotation(position, cameraTransform.rotation);

        if (!hasSubmitted || submittedField != field || submittedDrift != drift)
        {
            properties.SetVector(FieldId, field);
            properties.SetVector(DriftId, drift);
            meshRenderer.SetPropertyBlock(properties);
            if (!hasSubmitted || submittedField.x != field.x || submittedField.y != field.y)
            {
                float size = meshRenderer.sharedMaterial.GetVector(SizeId).y;
                meshRenderer.localBounds = new Bounds(Vector3.zero,
                    new Vector3(field.x + size, field.y + size, 0.5f));
            }
            submittedField = field;
            submittedDrift = drift;
            hasSubmitted = true;
        }
        bool visible = field.z > 0f;
        if (meshRenderer.enabled != visible) meshRenderer.enabled = visible;
    }

    internal static Vector4 ResolveFieldSettings(Vector2 viewSize, float padding, float density, float strength)
    {
        float width = Mathf.Max(0.01f, viewSize.x) * Mathf.Max(1f, padding) + 0.3f;
        float height = Mathf.Max(0.01f, viewSize.y) * Mathf.Max(1f, padding) + 0.3f;
        float fraction = Mathf.Clamp01(width * height * Mathf.Max(0f, density) / MaximumFlakes);
        return new Vector4(width, height, fraction * Mathf.Clamp01(strength), 0f);
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
