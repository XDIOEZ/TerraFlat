using UnityEngine;

/// <summary>静止世界物件的雪顶材质通道，复用统一 MPB 控制器，移动、拿起、回池和退出世界时撤销。</summary>
public sealed class SeasonalSnowRenderEffect : ActorRenderEffectModule
{
    private static readonly int CoverageId = Shader.PropertyToID("_SnowCoverage");
    private Item owner; // 物件身份和手持状态。
    private Vector3 lastPosition; // 排除移动中的物体。
    private float coverage; // 当前覆雪强度。
    private float nextSample; // 避免逐帧查询环境。
    /// <summary>绑定所有实际生成的物品，复用池化实例上已有组件。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        ItemMgr.RuntimeItemInstantiated -= Attach;
        ItemMgr.RuntimeItemInstantiated += Attach;
    }
    /// <summary>物件只增加行为组件，材质和模型仍来自正式资源。</summary>
    private static void Attach(Item item)
    {
        if (item is Player || item.GetComponentInChildren<Mover>(true) != null) return;
        var effect = item.GetComponent<SeasonalSnowRenderEffect>() ?? item.gameObject.AddComponent<SeasonalSnowRenderEffect>();
        effect.owner = item;
        effect.lastPosition = item.transform.position;
        effect.coverage = 0f;
        var controller = item.GetComponent<ActorRenderEffectController>() ?? item.gameObject.AddComponent<ActorRenderEffectController>();
        controller.RefreshBindings();
    }
    /// <summary>只让静止且无移动能力的世界物件积雪，不把雪刷到动物或背包物件上。</summary>
    protected override void PrepareFrame(float deltaTime)
    {
        if (owner == null || owner.InHand || owner.Owner != null) { coverage = 0f; return; }
        if (Time.unscaledTime < nextSample) return;
        nextSample = Time.unscaledTime + 0.5f;
        bool moved = (transform.position - lastPosition).sqrMagnitude > 0.0001f;
        lastPosition = transform.position;
        coverage = moved ? 0f : WeatherMgr.Instance.GetSnowCoverage(transform.position);
    }
    /// <summary>雪只影响自身表面，不扩展到外部手持物。</summary>
    protected override bool AppliesToExternalRenderer() => false;
    /// <summary>雪顶在原 Sprite 轮廓中合成，保留风摆、水体和受击通道。</summary>
    protected override void ApplyEffect(Renderer renderer, MaterialPropertyBlock block, float deltaTime) => block.SetFloat(CoverageId, coverage);
}
