using System;
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
    private readonly SpriteRenderer source;
    private readonly bool sourceWasEnabled;
    private GameObject presentationObject;
    private Mesh mesh;

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
        // 独立四边形保留原精灵大小、层级与色调，细节只交给星球材质。
        Bounds bounds = source.sprite.bounds;
        float minX = bounds.min.x, maxX = bounds.max.x;
        float minY = bounds.min.y, maxY = bounds.max.y;
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
        MeshRenderer renderer = presentationObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.sortingLayerID = source.sortingLayerID;
        renderer.sortingOrder = source.sortingOrder;
        renderer.renderingLayerMask = source.renderingLayerMask;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        var properties = new MaterialPropertyBlock();
        properties.SetColor(ColorId, source.color);
        properties.SetColor(AccentId, accent);
        properties.SetFloat(StyleId, (float)style);
        properties.SetFloat(StrengthId, Mathf.Clamp01(strength));
        properties.SetFloat(ScaleId, Mathf.Max(1f, scale));
        renderer.SetPropertyBlock(properties);
        source.enabled = false;
    }

    public void Dispose()
    {
        if (source != null) source.enabled = sourceWasEnabled;
        if (presentationObject != null) UnityEngine.Object.Destroy(presentationObject);
        if (mesh != null) UnityEngine.Object.Destroy(mesh);
        presentationObject = null;
        mesh = null;
    }
    #endregion
}
