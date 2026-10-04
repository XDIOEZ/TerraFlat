using System;
using UnityEngine;

/// <summary>整条鱼绘制在海床之后、水面之前；不使用陆地动物的半身水线裁切。</summary>
public sealed class AquaticActorPresentation : IDisposable
{
    #region 配置与状态
    public const string SortingCategory = "underwater-creature";
    private const float FacingMovementThreshold = 0.0001f;
    private const float MovingThresholdSqr = 0.000001f;
    private const float SwimSwayDegrees = 3f;
    private const float SwimSwayCyclesPerSecond = 2.4f;
    private const float IdleReturnDegreesPerSecond = 18f;
    private readonly Item owner;
    private readonly SpriteRenderer renderer;
    private readonly Transform visualTransform;
    private readonly Vector3 originalLocalPosition;
    private readonly Quaternion originalLocalRotation;
    private readonly bool originalFlipX;
    private readonly Material originalMaterial;
    private readonly Material underwaterMaterial;
    private readonly WorldSortingMember sorting;
    private bool? isUnderwater;
    private Vector2 lastPosition;
    private float swimSwayPhase;
    private float currentSwayDegrees;
    private bool carried;
    private float carriedVisualHeight;
    private float strandedHopHeight;
    private float strandedHopTilt;
    #endregion

    #region 初始化
    public AquaticActorPresentation(Item owner, SpriteRenderer renderer)
    {
        this.owner = owner;
        this.renderer = renderer;
        visualTransform = renderer.transform;
        originalLocalPosition = visualTransform.localPosition;
        originalLocalRotation = visualTransform.localRotation;
        originalFlipX = renderer.flipX;
        lastPosition = owner.transform.position;
        originalMaterial = renderer.sharedMaterial;
        if (originalMaterial == null) throw new InvalidOperationException("水生动物缺少主体材质。");
        var config = WorldRenderingConfigCatalog.Default.sorting;
        int waterQueue = -1;
        foreach (var profile in config.batchProfiles)
            if (profile.visualLayer == "Water") waterQueue = config.batchRenderQueueBase + profile.renderQueueOffset;
        if (waterQueue < 0) throw new InvalidOperationException("世界渲染配置缺少 Water 批次。");
        underwaterMaterial = new Material(originalMaterial)
        {
            name = "AquaticActor_Submerged",
            hideFlags = HideFlags.HideAndDontSave,
            renderQueue = waterQueue - 1
        };
        ConfigureFullySubmergedMaterial(underwaterMaterial);
        sorting = owner.GetComponent<WorldSortingMember>() ?? owner.gameObject.AddComponent<WorldSortingMember>();
    }

    /// <summary>鱼整身处于水下，额外用全身水下染色避免视觉上贴在水面。</summary>
    private static void ConfigureFullySubmergedMaterial(Material material)
    {
        if (material == null || !material.HasProperty("_WaterEnabled")) return;
        material.SetFloat("_WaterEnabled", 1f);
        material.SetFloat("_WaterWorldSpace", 0f);
        material.SetFloat("_WaterSurfaceV", 1f);
        material.SetFloat("_WaterFeather", 0.005f);
        material.SetFloat("_WaterTintStrength", 0.35f);
        material.SetFloat("_WaterAlpha", 0.82f);
        material.SetFloat("_WaterLineStrength", 0f);
        material.SetFloat("_WaterWaveAmplitude", 0f);
    }
    #endregion

    #region 水下排序与游动表现
    public void Tick(bool underwater, float deltaTime)
    {
        SetUnderwater(underwater && !carried);
        if (visualTransform != null)
            visualTransform.localPosition = originalLocalPosition + Vector3.up *
                (carried ? carriedVisualHeight : underwater ? 0f : strandedHopHeight);
        UpdateSwimMotion(underwater && !carried, deltaTime);
    }

    public void SetStrandedHop(float height, float tilt)
    {
        strandedHopHeight = Mathf.Max(0f, height);
        strandedHopTilt = tilt;
    }

    /// <summary>被捕获时改用普通生物排序，并只抬高视觉节点，不改动权威地面坐标。</summary>
    public void SetCarried(bool value, float visualHeight)
    {
        carried = value;
        carriedVisualHeight = value ? Mathf.Max(0f, visualHeight) : 0f;
        if (!value && visualTransform != null)
            visualTransform.localPosition = originalLocalPosition;
    }

    public void SetUnderwater(bool value)
    {
        if (isUnderwater == value || renderer == null) return;
        isUnderwater = value;
        renderer.sharedMaterial = value ? underwaterMaterial : originalMaterial;
        sorting.Bind(value ? SortingCategory : WorldSortingManager.CreatureCategory, renderer, owner);
    }

    private void UpdateSwimMotion(bool underwater, float deltaTime)
    {
        if (renderer == null || visualTransform == null) return;

        Vector2 position = owner.transform.position;
        Vector2 movement = WorldTopologyRuntime.ShortestDelta(lastPosition, position);
        lastPosition = position;

        // 小鱼原始展示约定朝右，只按水平移动镜像，不跟随二维游动向量整体转向。
        if (Mathf.Abs(movement.x) >= FacingMovementThreshold)
            renderer.flipX = movement.x < 0f ? !originalFlipX : originalFlipX;

        bool moving = underwater && movement.sqrMagnitude >= MovingThresholdSqr && deltaTime > 0f;
        if (moving)
        {
            swimSwayPhase = Mathf.Repeat(swimSwayPhase + deltaTime * SwimSwayCyclesPerSecond, 1f);
            currentSwayDegrees = Mathf.Sin(swimSwayPhase * Mathf.PI * 2f) * SwimSwayDegrees;
        }
        else
        {
            currentSwayDegrees = Mathf.MoveTowards(currentSwayDegrees, 0f,
                IdleReturnDegreesPerSecond * Mathf.Max(0f, deltaTime));
            if (Mathf.Approximately(currentSwayDegrees, 0f)) swimSwayPhase = 0f;
        }

        float hopTilt = underwater || carried ? 0f : strandedHopTilt;
        visualTransform.localRotation = Quaternion.AngleAxis(currentSwayDegrees + hopTilt, Vector3.forward) * originalLocalRotation;
    }
    #endregion

    #region 释放
    public void Dispose()
    {
        if (renderer != null)
        {
            renderer.sharedMaterial = originalMaterial;
            renderer.flipX = originalFlipX;
        }
        if (visualTransform != null)
        {
            visualTransform.localRotation = originalLocalRotation;
            visualTransform.localPosition = originalLocalPosition;
        }
        if (sorting != null && renderer != null) sorting.Bind(WorldSortingManager.CreatureCategory, renderer, owner);
        if (underwaterMaterial != null) UnityEngine.Object.Destroy(underwaterMaterial);
    }
    #endregion
}
