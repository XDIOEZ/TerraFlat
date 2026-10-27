using UnityEngine;

/// <summary>以雨丝参数驱动共享的雨雪网格与相机同步。</summary>
[DefaultExecutionOrder(10001), DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class RainEffectController : ShaderPrecipitationEffectControllerBase
{
    #region 雨丝参数

    internal const int MaximumDrops = 2048;
    private static readonly int FieldId = Shader.PropertyToID("_RainField");
    private static readonly int DriftId = Shader.PropertyToID("_RainDrift");
    private static readonly int WindId = Shader.PropertyToID("_RainWind");
    private static readonly int LengthId = Shader.PropertyToID("_RainLength");
    private static readonly int WidthId = Shader.PropertyToID("_RainWidth");

    [SerializeField, Min(1f)] private float coveragePadding = 1.1f;
    [SerializeField, Min(0f)] private float dropsPerSquareUnit = 1.5f;
    [SerializeField, Min(0f)] private float windDriftSpeed = 7f;

    private float streakMargin;

    protected override int FieldPropertyId => FieldId;
    protected override int DriftPropertyId => DriftId;
    protected override int AdditionalPropertyId => WindId;
    protected override float WindDriftSpeed => windDriftSpeed;
    protected override string MissingMaterialMessage => "Shader 降雨缺少 RainStreak 材质。";
    protected override Mesh BuildEffectMesh() => BuildMesh();

    internal static Mesh BuildMesh() => BuildFixedQuadMesh(
        MaximumDrops, 78041, false, "Shader 降雨固定网格");

    private void OnValidate()
    {
        coveragePadding = Mathf.Max(1f, coveragePadding);
        dropsPerSquareUnit = Mathf.Max(0f, dropsPerSquareUnit);
        windDriftSpeed = Mathf.Max(0f, windDriftSpeed);
        MarkSubmissionDirty();
        if (EffectRenderer != null && EffectRenderer.sharedMaterial != null)
            RefreshStreakMargin();
    }

    protected override void PrepareEffectMesh() => RefreshStreakMargin();

    private void RefreshStreakMargin()
    {
        Vector4 length = EffectRenderer.sharedMaterial.GetVector(LengthId);
        Vector4 width = EffectRenderer.sharedMaterial.GetVector(WidthId);
        // 额外留出最长近景雨丝的空间，倾斜和视口外环绕都不会露出边界。
        streakMargin = (Mathf.Max(Mathf.Abs(length.x), Mathf.Abs(length.y)) +
            Mathf.Max(Mathf.Abs(width.x), Mathf.Abs(width.y))) * 1.25f;
    }

    internal static Vector4 ResolveFieldSettings(Vector2 viewSize, float padding, float density,
        float strength, float margin)
    {
        float extraCoverage = Mathf.Max(0.01f, margin) * 2f;
        return ResolveSharedFieldSettings(
            viewSize, padding, density, strength, extraCoverage, MaximumDrops, MaximumDrops);
    }

    protected override Vector4 ResolveFieldSettingsForCamera(Vector2 viewSize, float strength) =>
        ResolveFieldSettings(viewSize, coveragePadding, dropsPerSquareUnit, strength, streakMargin);

    protected override Bounds ResolveLocalBounds(Vector4 field) => new(Vector3.zero,
        new Vector3(field.x + streakMargin * 2f, field.y + streakMargin * 2f, 0.5f));

    protected override Vector4 ResolveAdditionalPropertyValue(Vector3 right, Vector3 up, Vector3 worldWind) =>
        new(Vector3.Dot(worldWind, right), Vector3.Dot(worldWind, up), 0f, 0f);

    #endregion
}
