using UnityEngine;

/// <summary>以雪花参数驱动共享的雨雪网格与相机同步。</summary>
[DefaultExecutionOrder(10001), DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class SnowEffectController : ShaderPrecipitationEffectControllerBase
{
    #region 雪花参数

    internal const int MaximumFlakes = 1500;
    private static readonly int FieldId = Shader.PropertyToID("_SnowField");
    private static readonly int DriftId = Shader.PropertyToID("_SnowDrift");
    private static readonly int SizeId = Shader.PropertyToID("_SnowSize");

    [SerializeField, Min(1f)] private float coveragePadding = 1.1f;
    [SerializeField, Min(0f)] private float flakesPerSquareUnit = 1.5f;
    [SerializeField, Min(0f)] private float windDriftSpeed = 1.5f;

    protected override int FieldPropertyId => FieldId;
    protected override int DriftPropertyId => DriftId;
    protected override float WindDriftSpeed => windDriftSpeed;
    protected override string MissingMaterialMessage => "Shader 降雪缺少 Snowflake 材质。";
    protected override Mesh BuildEffectMesh() => BuildMesh();

    internal static Mesh BuildMesh() => BuildFixedQuadMesh(
        MaximumFlakes, 65021, true, "Shader 降雪固定网格");

    internal static Vector4 ResolveFieldSettings(Vector2 viewSize, float padding, float density, float strength) =>
        ResolveSharedFieldSettings(viewSize, padding, density, strength, 0.3f, MaximumFlakes, 0f);

    protected override Vector4 ResolveFieldSettingsForCamera(Vector2 viewSize, float strength) =>
        ResolveFieldSettings(viewSize, coveragePadding, flakesPerSquareUnit, strength);

    protected override Bounds ResolveLocalBounds(Vector4 field)
    {
        float size = EffectRenderer.sharedMaterial.GetVector(SizeId).y;
        return new Bounds(Vector3.zero, new Vector3(field.x + size, field.y + size, 0.5f));
    }

    #endregion
}
