using System.Collections.Generic;
using UnityEngine;

/// <summary>通过统一材质合成轻抖身体，不修改动画节点、逻辑根、刚体或受击盒。</summary>
[DisallowMultipleComponent]
public sealed class ActorRenderHitShakeEffect : ActorRenderEffectModule
{
    #region 受击视觉位移
    internal static readonly int OffsetId = Shader.PropertyToID("_CombatHitOffset");
    private readonly Dictionary<SpriteRenderer, Bounds> expandedRenderers = new();
    private MaterialPropertyBlock cleanupBlock;
    private Item owner;
    private uint ownerGeneration;
    private bool hasOwner;
    private float elapsed, duration, distance;
    private Vector2 direction, offset;

    public override bool RequiresContinuousRendering => false;
    public override bool RequiresSpriteBodyRange => false;
    internal Vector2 CurrentOffset => offset;

    public static void Play(Item target, Vector2 worldDirection, float seconds, float amplitude)
    {
        if (target == null || !target.gameObject.activeInHierarchy ||
            CombatHitFeedback.ShakeDuration(seconds) <= 0f || CombatHitFeedback.ShakeDistance(amplitude) <= 0f) return;
        var controller = target.GetComponentInChildren<ActorRenderEffectController>(true);
        if (controller == null) controller = target.gameObject.AddComponent<ActorRenderEffectController>();
        var effect = controller.GetComponent<ActorRenderHitShakeEffect>();
        if (effect == null)
        {
            effect = controller.gameObject.AddComponent<ActorRenderHitShakeEffect>();
            controller.RefreshBindings();
        }
        effect.owner = target;
        effect.ownerGeneration = target.RuntimeGeneration;
        effect.hasOwner = true;
        effect.PlayShake(worldDirection, seconds, amplitude);
    }

    /// <summary>连续命中重新起振但不叠加幅度，始终在同一视觉基准附近衰减。</summary>
    internal void PlayShake(Vector2 worldDirection, float seconds, float amplitude)
    {
        duration = CombatHitFeedback.ShakeDuration(seconds);
        distance = CombatHitFeedback.ShakeDistance(amplitude);
        elapsed = 0f;
        direction = CombatHitFeedback.ShakeDirection(worldDirection);
        offset = CombatHitFeedback.EvaluateShake(0f, duration, distance, direction);
        enabled = true;
        MarkRenderStateDirty();
    }

    protected override bool AppliesToExternalRenderer() => false;
    protected override bool AppliesTo(Renderer renderer) => renderer is SpriteRenderer &&
        renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty(OffsetId);

    protected override void PrepareFrame(float deltaTime)
    {
        if (duration <= 0f) return;
        if (hasOwner && (owner == null || owner.RuntimeGeneration != ownerGeneration))
        {
            ClearShake();
            return;
        }
        elapsed += Mathf.Max(0f, deltaTime);
        Vector2 next = CombatHitFeedback.EvaluateShake(elapsed, duration, distance, direction);
        if (next != offset) { offset = next; MarkRenderStateDirty(); }
        if (elapsed >= duration) ClearShake();
    }

    protected override void ApplyEffect(Renderer renderer, MaterialPropertyBlock block, float deltaTime)
    {
        block.SetVector(OffsetId, new Vector4(offset.x, offset.y, 0f, 0f));
        if (duration <= 0f || distance <= 0f) return;
        var spriteRenderer = (SpriteRenderer)renderer;
        if (!expandedRenderers.TryGetValue(spriteRenderer, out Bounds bounds))
        {
            bounds = spriteRenderer.localBounds;
            expandedRenderers.Add(spriteRenderer, bounds);
        }
        // 动画换帧仍按当前图扩边，避免 Shader 轻抖在屏幕边缘被原包围盒裁掉。
        if (spriteRenderer.drawMode == SpriteDrawMode.Simple && spriteRenderer.sprite != null)
        {
            bounds = spriteRenderer.sprite.bounds;
            Vector3 center = bounds.center;
            if (spriteRenderer.flipX) center.x = -center.x;
            if (spriteRenderer.flipY) center.y = -center.y;
            bounds.center = center;
        }
        Vector3 x = spriteRenderer.transform.InverseTransformVector(Vector3.right * distance);
        Vector3 y = spriteRenderer.transform.InverseTransformVector(Vector3.up * distance);
        bounds.Expand(new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x), Mathf.Abs(x.y) + Mathf.Abs(y.y),
            Mathf.Abs(x.z) + Mathf.Abs(y.z)) * 2f);
        spriteRenderer.localBounds = bounds;
    }

    private void OnDisable() => ClearShake();
    private void OnDestroy() => ClearShake();

    /// <summary>完成、停用和回池清除本通道并恢复自动边界，不覆盖水体、染色等其它材质参数。</summary>
    private void ClearShake()
    {
        duration = 0f;
        offset = Vector2.zero;
        MarkRenderStateDirty();
        foreach (var entry in expandedRenderers)
        {
            SpriteRenderer renderer = entry.Key;
            if (renderer == null) continue;
            cleanupBlock ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(cleanupBlock);
            cleanupBlock.SetVector(OffsetId, Vector4.zero);
            renderer.SetPropertyBlock(cleanupBlock);
            renderer.ResetLocalBounds();
        }
        expandedRenderers.Clear();
    }
    #endregion
}
