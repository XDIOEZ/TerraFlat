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
    private readonly Quaternion originalLocalRotation;
    private readonly bool originalFlipX;
    private readonly Material originalMaterial;
    private readonly Material underwaterMaterial;
    private readonly WorldSortingMember sorting;
    private bool? isUnderwater;
    private Vector2 lastPosition;
    private float swimSwayPhase;
    private float currentSwayDegrees;
    #endregion

    #region 初始化
    public AquaticActorPresentation(Item owner, SpriteRenderer renderer)
    {
        this.owner = owner;
        this.renderer = renderer;
        visualTransform = renderer.transform;
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
        sorting = owner.GetComponent<WorldSortingMember>() ?? owner.gameObject.AddComponent<WorldSortingMember>();
    }
    #endregion

    #region 水下排序与游动表现
    public void Tick(bool underwater, float deltaTime)
    {
        SetUnderwater(underwater);
        UpdateSwimMotion(underwater, deltaTime);
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

        visualTransform.localRotation = Quaternion.AngleAxis(currentSwayDegrees, Vector3.forward) * originalLocalRotation;
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
        if (visualTransform != null) visualTransform.localRotation = originalLocalRotation;
        if (sorting != null && renderer != null) sorting.Bind(WorldSortingManager.CreatureCategory, renderer, owner);
        if (underwaterMaterial != null) UnityEngine.Object.Destroy(underwaterMaterial);
    }
    #endregion
}
