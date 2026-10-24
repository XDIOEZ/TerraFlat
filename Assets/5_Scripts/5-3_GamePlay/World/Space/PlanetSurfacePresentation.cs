using System;
using FlatWorld.Spaceflight;
using UnityEngine;
using UnityEngine.Rendering;

public enum PlanetSurfaceStyle { Rocky, Ocean, Banded, Stellar }

internal sealed class PlanetSurfacePresentation : IDisposable
{
    #region 星球专用表现资源
    private const string MaterialPath = "Space/PlanetSurface";
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int AccentId = Shader.PropertyToID("_SurfaceAccentColor");
    private static readonly int StyleId = Shader.PropertyToID("_SurfaceStyle");
    private static readonly int StrengthId = Shader.PropertyToID("_SurfaceDetailStrength");
    private static readonly int ScaleId = Shader.PropertyToID("_SurfaceDetailScale");
    private static readonly int ExtentId = Shader.PropertyToID("_SurfaceExtent");
    private static readonly int LightId = Shader.PropertyToID("_SurfaceLightDirection");
    private static readonly int SeedId = Shader.PropertyToID("_SurfaceSeed");
    private static readonly int RotationId = Shader.PropertyToID("_SurfaceRotation");
    private readonly SpriteRenderer source;
    private readonly bool sourceWasEnabled;
    private readonly MaterialPropertyBlock properties = new();
    private GameObject presentationObject;
    private Mesh mesh;
    private MeshRenderer renderer;
    private UniverseState lightingUniverse;
    private BodyState lightingBody;
    private Vector3 lastLightDirection;
    private int lastSeed = int.MinValue;
    private double lastRotation = double.NaN;
    private PlanetSurfaceStyle style;

    private PlanetSurfacePresentation(SpriteRenderer source)
    {
        this.source = source;
        sourceWasEnabled = source.enabled;
    }

    public static PlanetSurfacePresentation Create(SpriteRenderer source, PlanetSurfaceStyle style,
        Color accent, float strength, float scale)
    {
        if (source == null || source.sprite == null) return null;
        Material material = Resources.Load<Material>(MaterialPath);
        if (material == null)
            throw new InvalidOperationException("星球专用表现材质未加载：" + MaterialPath);
        var presentation = new PlanetSurfacePresentation(source);
        try
        {
            presentation.Initialize(material, style, accent, strength, scale);
            return presentation;
        }
        catch
        {
            presentation.Dispose();
            throw;
        }
    }

    private void Initialize(Material material, PlanetSurfaceStyle style, Color accent, float strength, float scale)
    {
        // 留出大气和日冕边缘，星体实际半径仍保持原精灵大小。
        this.style = style;
        float extent = style == PlanetSurfaceStyle.Stellar ? 1.3f : 1.12f;
        Bounds bounds = source.sprite.bounds;
        float minX = bounds.center.x - bounds.extents.x * extent, maxX = bounds.center.x + bounds.extents.x * extent;
        float minY = bounds.center.y - bounds.extents.y * extent, maxY = bounds.center.y + bounds.extents.y * extent;
        mesh = new Mesh { name = "星球表面四边形" };
        mesh.vertices = new[]
        {
            new Vector3(minX, minY, 0f), new Vector3(minX, maxY, 0f),
            new Vector3(maxX, maxY, 0f), new Vector3(maxX, minY, 0f)
        };
        mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateBounds();
        mesh.UploadMeshData(true);
        presentationObject = new GameObject("清晰星球表面");
        presentationObject.layer = source.gameObject.layer;
        presentationObject.transform.SetParent(source.transform, false);
        presentationObject.SetActive(sourceWasEnabled);
        presentationObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        renderer = presentationObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.sortingLayerID = source.sortingLayerID;
        renderer.sortingOrder = source.sortingOrder;
        renderer.renderingLayerMask = source.renderingLayerMask;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        properties.SetColor(ColorId, source.color);
        properties.SetColor(AccentId, accent);
        properties.SetFloat(StyleId, (float)style);
        properties.SetFloat(StrengthId, Mathf.Clamp01(strength));
        properties.SetFloat(ScaleId, Mathf.Max(1f, scale));
        properties.SetFloat(ExtentId, extent);
        properties.SetVector(LightId, new Vector4(.65f, .35f, .8f, 0f));
        renderer.SetPropertyBlock(properties);
        source.enabled = false;
    }
    #endregion

    #region GPU 表面参数与太阳方向
    public void UpdateLighting(SpaceSession session, BodyState body)
    {
        if (renderer == null || session?.Universe == null || body == null) return;
        UniverseState universe = session.GetUniverse(body.Sector).State;
        if (!ReferenceEquals(lightingUniverse, universe))
        {
            lightingUniverse = universe;
            lightingBody = null;
            foreach (BodyState candidate in universe.Bodies)
                if (string.IsNullOrEmpty(candidate.ParentBodyId)) { lightingBody = candidate; break; }
        }
        bool changed = lastSeed != body.Seed;
        if (changed)
        {
            lastSeed = body.Seed;
            properties.SetFloat(SeedId, (body.Seed & 0xFFFF) * .0137f);
        }
        double rotation = Math.IEEERemainder(session.GetUniverse(body.Sector).GetRotationRadians(body.BodyId), Math.PI * 2d);
        if (rotation != lastRotation)
        {
            lastRotation = rotation;
            properties.SetFloat(RotationId, (float)rotation);
            changed = true;
        }
        if (style != PlanetSurfaceStyle.Stellar && lightingBody != null)
        {
            // 用宇宙双精度位置求向光方向，浮动原点和星体自转不会改变昼夜边界。
            SpaceVector2 direction = (lightingBody.PositionMeters - body.PositionMeters).Normalized;
            Vector3 localDirection = source.transform.InverseTransformDirection(
                new Vector3((float)direction.X, (float)direction.Y, .8f)).normalized;
            if ((localDirection - lastLightDirection).sqrMagnitude > .000001f)
            {
                lastLightDirection = localDirection;
                properties.SetVector(LightId, localDirection);
                changed = true;
            }
        }
        if (changed) renderer.SetPropertyBlock(properties);
    }
    #endregion

    #region 表现资源释放
    public void Dispose()
    {
        if (source != null) source.enabled = sourceWasEnabled;
        if (presentationObject != null) UnityEngine.Object.Destroy(presentationObject);
        if (mesh != null) UnityEngine.Object.Destroy(mesh);
        presentationObject = null;
        mesh = null;
        renderer = null;
    }
    #endregion
}
