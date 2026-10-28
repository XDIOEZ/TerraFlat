using System.Collections.Generic;
using FlatWorld.AIECS;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// 统一管理玩家、生物、落地植物与显式配置的世界物品的接触阴影。
/// 地表接触阴影共用 BRG，投影世界按域复用原生网格，按可见轮廓定位脚点并绘制扁椭圆。
/// 通过完整注册和结构变化事件维护绑定；水态和昼夜光照只改变表现，不写回物品状态。
/// </summary>
public sealed class ActorShadowManager : SingletonMono<ActorShadowManager>
{
    #region 阴影配置与运行状态

    private const int MechanicalShadowQueue = 2993; // 机械主体为 2994，底影必须先于主体绘制。
    private static WorldRenderingConfig.ContactShadow Defaults => WorldRenderingConfigCatalog.Default.shadows.contact;

    private readonly Dictionary<Item, ShadowBinding> bindings = new Dictionary<Item, ShadowBinding>();
    private readonly List<KeyValuePair<Item, ShadowBinding>> cleanupBindings =
        new List<KeyValuePair<Item, ShadowBinding>>();
    private ContactShadowBatchRenderer shadowBatch; // 所有 Item 共用的原生绘制批次。
    private ContactShadowBatchRenderer mechanicalShadowBatch; // 机械数据节点共用的单独排序批次。
    private readonly Dictionary<WorldPresentationSortingDomain, DomainShadowBucket> domainShadowBatches = new();
    private int lightingFrame = -1;
    private int lightingSceneHandle = int.MinValue;
    private int lightingSceneNameHandle = int.MinValue;
    private string lightingSceneName = string.Empty;
    private float cachedShadowOpacity; // 本帧光照与晨昏进度合成后的透明度。

    private sealed class DomainShadowBucket
    {
        private readonly ActorShadowManager owner;
        private readonly WorldPresentationSortingDomain domain;
        private AiecsShadowRenderer actors, machines;

        internal DomainShadowBucket(ActorShadowManager owner, WorldPresentationSortingDomain domain)
        { this.owner = owner; this.domain = domain; domain.Disposing += Release; }

        private void Release() => owner.ReleaseDomainShadowBucket(domain);
        internal void Begin() { actors?.Begin(); machines?.Begin(); }
        internal void End() { actors?.End(); machines?.End(); }
        internal void Hide() { actors?.Hide(); machines?.Hide(); }

        internal void Append(bool mechanical, Vector2 center, float width, float height, float opacity)
        {
            Transform root = domain.Root;
            if (root == null) return;
            AiecsShadowRenderer renderer = mechanical ? machines : actors;
            if (renderer == null)
            {
                WorldSortingManager.GetResourceSortingKey(WorldSortingManager.GroundShadowCategory, out int layer, out int order);
                renderer = new AiecsShadowRenderer(root.gameObject.scene, layer, order, root,
                    mechanical ? MechanicalShadowQueue : 2995, 0);
                renderer.SetSortingKeyCallbacks(ApplySortingKey, RemoveSortingKey);
                if (mechanical) machines = renderer; else actors = renderer;
                renderer.Begin();
            }
            Vector3 local = root.InverseTransformPoint(center);
            float localWidth = root.InverseTransformVector(Vector3.right * width).magnitude;
            float localHeight = root.InverseTransformVector(Vector3.up * height).magnitude;
            renderer.Append(local, new Vector4(0f, 0f, localWidth, localHeight), false, opacity);
        }

        private void ApplySortingKey(Renderer target, int layer, int order, int queue, float footY)
            => domain.ApplyLocalRendererSortingKey(target, layer, order, queue, footY);
        private void RemoveSortingKey(Renderer target) => domain.RemoveLocalSortingKey(target);

        internal void Dispose()
        {
            domain.Disposing -= Release;
            try { actors?.Dispose(); }
            finally { machines?.Dispose(); }
        }
    }

    #endregion

    #region Unity 生命周期

    /// <summary>统一入口分发物品生命周期，启用时恢复接触阴影模块。</summary>
    private void OnEnable()
    {
        RenderPipelineManager.beginContextRendering -= PresentActorShadows;
        RenderPipelineManager.beginContextRendering += PresentActorShadows;
        SunShadowParametersProvider.PublishContactAppearance();
        WorldRenderingManager.ExistingInstance?.AttachContactShadowRenderingBackend(this);
    }

    /// <summary>每个渲染上下文开始时同步阴影位置、尺寸和光照透明度。</summary>
    private void PresentActorShadows(ScriptableRenderContext context, List<Camera> cameras)
    {
        foreach (DomainShadowBucket bucket in domainShadowBatches.Values) bucket.Begin();
        if (bindings.Count == 0 && MechanicalShadowRegistry.Count == 0)
        {
            shadowBatch?.Hide();
            mechanicalShadowBatch?.Hide();
            foreach (DomainShadowBucket bucket in domainShadowBatches.Values) bucket.End();
            return;
        }

        if (bindings.Count > 0)
        {
            int localScene = SceneManager.GetActiveScene().handle;
            shadowBatch ??= new ContactShadowBatchRenderer();
            shadowBatch.BeginFrame();
            cleanupBindings.Clear();
            foreach (KeyValuePair<Item, ShadowBinding> pair in bindings)
            {
                Item item = pair.Key;
                ShadowBinding binding = pair.Value;
                if (item == null || binding == null ||
                    !item.gameObject.activeInHierarchy || IsItemInPool(item))
                {
                    cleanupBindings.Add(pair);
                    continue;
                }

                string worldKey = GameplayWorldRegistry.GetItemWorldKey(item);
                bool projected = WorldPresentationTransformRegistry.TryGet(worldKey, out Matrix4x4 transform, out bool visible);
                WorldPresentationTransformRegistry.TryGetSortingDomain(worldKey, out WorldPresentationSortingDomain domain);
                if (WorldPresentationProjection.TryGetItemSortingProjection(item, out WorldPresentationSortingDomain itemDomain,
                    out Matrix4x4 itemTransform, out bool itemVisible))
                {
                    projected = true;
                    transform = itemTransform;
                    visible = itemVisible;
                    domain = itemDomain;
                }
                if (projected ? !visible : item.gameObject.scene.handle != localScene) continue;
                UpdateBinding(item, binding, projected, transform, domain);
            }

            for (int i = 0; i < cleanupBindings.Count; i++)
            {
                KeyValuePair<Item, ShadowBinding> pair = cleanupBindings[i];
                RemoveBinding(pair.Key);
            }
            shadowBatch.EndFrame();
        }
        else shadowBatch?.Hide();

        if (MechanicalShadowRegistry.Count > 0)
        {
            mechanicalShadowBatch ??= new ContactShadowBatchRenderer(MechanicalShadowQueue);
            mechanicalShadowBatch.BeginFrame();
            AppendMechanicalShadows();
            mechanicalShadowBatch.EndFrame();
        }
        else mechanicalShadowBatch?.Hide();
        foreach (DomainShadowBucket bucket in domainShadowBatches.Values) bucket.End();
    }

    /// <summary>解除事件并释放 BRG 原生资源。</summary>
    protected override void OnDestroy()
    {
        RenderPipelineManager.beginContextRendering -= PresentActorShadows;
        ClearBindings();
        ReleaseBatches();

        base.OnDestroy();
    }

    /// <summary>脚本重载可能不调用 OnDestroy，停用时必须释放原生 BRG 而不是只隐藏。</summary>
    private void OnDisable()
    {
        RenderPipelineManager.beginContextRendering -= PresentActorShadows;
        ClearBindings();
        ReleaseBatches();
    }

    private void ReleaseBatches()
    {
        ContactShadowBatchRenderer actors = shadowBatch;
        ContactShadowBatchRenderer machines = mechanicalShadowBatch;
        shadowBatch = null;
        mechanicalShadowBatch = null;
        try { actors?.Dispose(); }
        finally
        {
            try { machines?.Dispose(); }
            finally
            {
                foreach (DomainShadowBucket bucket in domainShadowBatches.Values) bucket.Dispose();
                domainShadowBatches.Clear();
            }
        }
    }

    #endregion

    #region 对外生命周期接口

    /// <summary>为符合条件的角色、落地植物或世界物品建立轻量阴影绑定。</summary>
    public void RegisterActor(Item item)
    {
        if (this == null || !isActiveAndEnabled)
            return;

        if (item == null || bindings.ContainsKey(item) ||
            !TryClassifyShadowCaster(item, out bool isRootedPlant,
                out ItemShadowVisualDefinitionDto shadowVisual))
            return;

        shadowBatch ??= new ContactShadowBatchRenderer();
        bindings.Add(item, new ShadowBinding(
            isRootedPlant ? null : VisualGroundOffsetResolver.FindProvider(item),
            isRootedPlant, shadowVisual));
    }

    /// <summary>移除实体对应的阴影绑定。</summary>
    public void UnregisterActor(Item item)
    {
        if (item == null || !bindings.ContainsKey(item))
            return;

        RemoveBinding(item);
    }

    /// <summary>由水体地块切换实体阴影的显示状态。</summary>
    public void SetActorInWater(Item item, bool inWater)
    {
        if (item == null || !bindings.TryGetValue(item, out ShadowBinding binding))
            return;

        binding.InWater = inWater;
    }

    internal bool IsItemShadowBound(Item item) => item != null && bindings.ContainsKey(item);
    internal bool IsItemInWater(Item item) =>
        item != null && bindings.TryGetValue(item, out ShadowBinding binding) && binding.InWater;

    internal void ClearWorldRendering() => ClearBindings();

    #endregion

    #region 阴影同步

    /// <summary>已显示机械共用一个位于机械主体下方的 BRG 椭圆批次。</summary>
    private void AppendMechanicalShadows()
    {
        int localScene = SceneManager.GetActiveScene().handle;
        WorldRenderingConfig.ContactShadow defaults = Defaults;
        foreach (MechanicalShadowRegistry.Entry entry in MechanicalShadowRegistry.Entries)
        {
            if (!entry.Active || !entry.Scene.IsValid() || !entry.Scene.isLoaded || entry.ContactWidth <= 0f) continue;
            bool projected = WorldPresentationTransformRegistry.TryGet(entry.Scene.name, out Matrix4x4 transform, out bool visible);
            if (projected ? !visible : entry.Scene.handle != localScene) continue;
            float opacity = GetShadowOpacity(projected ? SceneManager.GetActiveScene() : entry.Scene);
            if (opacity <= 0.001f) continue;
            Vector3 foot = projected ? transform.MultiplyPoint3x4(entry.Foot) : entry.Foot;
            float width = projected ? entry.ContactWidth * transform.MultiplyVector(Vector3.right).magnitude : entry.ContactWidth;
            WorldPresentationTransformRegistry.TryGetSortingDomain(entry.Scene.name, out WorldPresentationSortingDomain domain);
            AppendContactShadow(domain, true, new Vector2(foot.x, foot.y + defaults.groundOffset),
                width, width * defaults.heightRatio, opacity);
        }
    }

    /// <summary>同步单个实体的脚底位置、贴图占地尺寸和当前光照透明度。</summary>
    private void UpdateBinding(Item item, ShadowBinding binding, bool projected, Matrix4x4 projection,
        WorldPresentationSortingDomain sortingDomain)
    {
        // 太空玩家不画地面阴影，舱内人工重力也不改变这条表现规则。
        if (item is Player && SpaceMgr.IsSpaceScene(SceneManager.GetActiveScene().name))
            return;

        SpriteRenderer sourceRenderer = binding.SourceRenderer;
        if (sourceRenderer == null || sourceRenderer.sprite == null)
        {
            sourceRenderer = FindSourceRenderer(item);
            binding.SourceRenderer = sourceRenderer;
        }

        if (sourceRenderer == null || sourceRenderer.sprite == null ||
            !sourceRenderer.enabled || (projected
                ? (WorldPresentationProjection.TryGetProjectedRendererOriginalForceRenderingOff(sourceRenderer, out bool forceOff)
                    ? forceOff : GameplayWorldRegistry.GetPresentationOriginalForceRenderingOff(sourceRenderer))
                : sourceRenderer.forceRenderingOff && !BuildingDepthMeshBridge.IsProjected(sourceRenderer)) ||
            !sourceRenderer.gameObject.activeInHierarchy)
            return;

        if (binding.IsRootedPlant && !IsGroundedStaticPlant(item))
            return;

        float contactWidth = binding.ShadowVisual?.ContactWidth ?? 0f;
        if (contactWidth > 0f && (item.InHand || item.Owner != null))
            return;

        float alpha = GetShadowOpacity(projected ? SceneManager.GetActiveScene() : item.gameObject.scene);
        float visualOpacity = sourceRenderer.color.a * alpha;
        if (binding.InWater || visualOpacity <= 0.001f)
            return;

        Bounds footprint = ShadowFootprintResolver.MeasureVisibleWorldBounds(sourceRenderer);
        if (!binding.IsRootedPlant)
            footprint.center -= VisualGroundOffsetResolver.Resolve(binding.GroundOffsetProvider,
                sourceRenderer.transform);
        Vector3 footprintAnchor = ShadowFootprintResolver.ResolveFoot(item, footprint,
            binding.ShadowVisual?.FootLocalPosition, 0f, binding.ShadowVisual?.FootOverlap);
        if (contactWidth > 0f)
        {
            binding.ShadowWidth = Mathf.Clamp(contactWidth * Mathf.Abs(item.transform.lossyScale.x),
                Defaults.minimumWidth, Defaults.maximumWidth);
        }
        else if (binding.IsRootedPlant)
        {
            ResolveRootedPlantFootprint(item, footprintAnchor, footprint.size.x,
                out Vector3 rootAnchor, out float rootedPlantWidth);
            footprintAnchor = rootAnchor;
            footprintAnchor.y = Mathf.Max(footprintAnchor.y,
                ShadowFootprintResolver.ResolveFoot(item, footprint, null).y);
            binding.ShadowWidth = Mathf.Clamp(rootedPlantWidth,
                Defaults.minimumWidth, Defaults.maximumWidth);
        }

        // 角色锁定首次宽度避免动画抖动；植物则跟随成长尺寸更新。
        if (!binding.IsRootedPlant && binding.ShadowWidth <= 0f)
        {
            float visualFootprintWidth = Mathf.Max(footprint.size.x,
                footprint.size.y * Defaults.heightToWidthRatio);
            binding.ShadowWidth = Mathf.Clamp(visualFootprintWidth * Defaults.widthRatio,
                Defaults.minimumWidth, Defaults.maximumWidth);
        }

        if (projected) footprintAnchor = projection.MultiplyPoint3x4(footprintAnchor);
        float projectedWidth = projected ? binding.ShadowWidth * projection.MultiplyVector(Vector3.right).magnitude : binding.ShadowWidth;
        AppendContactShadow(sortingDomain, false, new Vector2(footprintAnchor.x,
                footprintAnchor.y + Defaults.groundOffset),
            projectedWidth, projectedWidth * Defaults.heightRatio,
            visualOpacity);
    }

    private void AppendContactShadow(WorldPresentationSortingDomain domain, bool mechanical, Vector2 center,
        float width, float height, float opacity)
    {
        if (domain == null) { (mechanical ? mechanicalShadowBatch : shadowBatch).Append(center, width, height, opacity); return; }
        if (!domainShadowBatches.TryGetValue(domain, out DomainShadowBucket bucket))
            domainShadowBatches.Add(domain, bucket = new DomainShadowBucket(this, domain));
        bucket.Append(mechanical, center, width, height, opacity);
    }

    private void ReleaseDomainShadowBucket(WorldPresentationSortingDomain domain)
    {
        if (domainShadowBatches.Remove(domain, out DomainShadowBucket bucket)) bucket.Dispose();
    }

    /// <summary>植物以根部碰撞体定位，宽度同时受实际植株贴图限制。</summary>
    private static void ResolveRootedPlantFootprint(Item item, Vector3 visualFoot,
        float visualWidth, out Vector3 rootAnchor, out float shadowWidth)
    {
        item.TryGetComponent(out Collider2D rootCollider);
        if (rootCollider != null && rootCollider.enabled)
        {
            Bounds bodyBounds = rootCollider.bounds;
            rootAnchor = bodyBounds.center;
            shadowWidth = Mathf.Min(bodyBounds.size.x,
                visualWidth * Defaults.rootedPlantVisualWidthLimit) * Defaults.rootedPlantBodyWidthRatio;
            return;
        }

        rootAnchor = visualFoot;
        shadowWidth = visualWidth * Defaults.rootedPlantVisualWidthLimit;
    }

    /// <summary>获取实体当前使用的主 SpriteRenderer，兼容玩家和 AI 动画模块。</summary>
    private static SpriteRenderer FindSourceRenderer(Item item)
    {
        if (item == null)
            return null;

        if (item.Sprite != null && item.Sprite.sprite != null)
            return item.Sprite;

        SpriteRenderer[] renderers = item.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && renderers[i].sprite != null)
                return renderers[i];
        }

        return null;
    }

    /// <summary>向旧对象与 ECS 表现提供同一场景的阴影透明度；每场景每帧缓存光照查询。</summary>
    public float GetShadowOpacity(Scene actorScene)
    {
        if (!isActiveAndEnabled) return 0f;
        return GetCachedSolarOpacity(actorScene);
    }

    /// <summary>每场景每帧只计算一次太阳日照，供所有接触阴影复用。</summary>
    private float GetCachedSolarOpacity(Scene actorScene)
    {
        Scene resolvedScene = actorScene.IsValid() && actorScene.isLoaded
            ? actorScene
            : SceneManager.GetActiveScene();
        int sceneHandle = resolvedScene.handle;

        // Scene.name 是 native -> managed 字符串桥；先用整数句柄命中缓存，避免每个实体每帧分配字符串。
        if (lightingFrame == Time.frameCount && lightingSceneHandle == sceneHandle)
            return cachedShadowOpacity;

        lightingFrame = Time.frameCount;
        lightingSceneHandle = sceneHandle;
        cachedShadowOpacity = 0f;
        if (lightingSceneNameHandle != sceneHandle)
        {
            lightingSceneNameHandle = sceneHandle;
            lightingSceneName = resolvedScene.name;
        }
        string sceneName = lightingSceneName;

        DayTimeSystem dayTimeSystem = DayTimeSystem.GetInstance();
        if (dayTimeSystem == null ||
            !dayTimeSystem.TryGetResolvedTimeData(sceneName, out _, out TimeData timeData) || timeData == null)
        {
            return cachedShadowOpacity;
        }

        cachedShadowOpacity = SunShadowParametersProvider.ResolveOpacity(
            dayTimeSystem.GetSunLighting(sceneName));
        return cachedShadowOpacity;
    }

    #endregion

    #region 注册与清理

    /// <summary>识别角色、根植植物或显式声明底座阴影的世界物品。</summary>
    private static bool TryClassifyShadowCaster(Item item, out bool isRootedPlant,
        out ItemShadowVisualDefinitionDto shadowVisual)
    {
        isRootedPlant = false;
        shadowVisual = null;
        if (item == null || !item.gameObject.activeInHierarchy || IsItemInPool(item))
            return false;
        if (item.itemMods?.GetMod_ByID<Mod_Building>(ModText.Building)?.IsGroundFacility == true)
            return false;

        GameRes resources = GameRes.ExistingInstance;
        if (resources != null && item.itemData != null &&
            resources.TryGetItemDefinition(item.itemData.IDName, out RuntimeItemDefinition definition))
            shadowVisual = definition.Visual?.Shadows;
        if (shadowVisual?.Enabled == false)
            return false;

        if (item is Player || RuntimeAiEntityUtility.IsAiEntity(item))
            return true;

        if ((shadowVisual?.ContactWidth ?? 0f) > 0f && item.Owner == null && !item.InHand)
            return true;

        bool groundedPlant = IsGroundedStaticPlant(item);
        bool hasPlantTag = groundedPlant && item.itemData.Tags != null &&
                           (item.itemData.Tags.ContainsTag(Tag.Tree) ||
                            item.itemData.Tags.ContainsTag(Tag.Plant));
        MonoBehaviour[] behaviours = item.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IAIActor)
                return true;
            if (groundedPlant && behaviours[i] is IPlantableCrop)
                isRootedPlant = true;
        }

        isRootedPlant |= hasPlantTag;
        return isRootedPlant;
    }

    /// <summary>只让留在地面、不可拾取的植物保有根部阴影。</summary>
    private static bool IsGroundedStaticPlant(Item item)
    {
        return item != null && item.Owner == null && item.itemData?.Stack != null &&
               !item.InHand && !item.itemData.Stack.CanBePickedUp;
    }

    /// <summary>判断对象是否已经回收到对象池。</summary>
    private static bool IsItemInPool(Item item)
    {
        return item != null && item.PoolMarker.InPool;
    }

    /// <summary>清理所有阴影绑定。</summary>
    private void ClearBindings()
    {
        bindings.Clear();
        shadowBatch?.Hide();
        mechanicalShadowBatch?.Hide();
        foreach (DomainShadowBucket bucket in domainShadowBatches.Values) bucket.Hide();
    }

    /// <summary>移除物品绑定；下一帧批量上传不会再包含它。</summary>
    private void RemoveBinding(Item item)
    {
        bindings.Remove(item);
    }

    #endregion

    /// <summary>实体与批量阴影间的轻量运行时绑定。</summary>
    private sealed class ShadowBinding
    {
        public readonly IVisualGroundOffset GroundOffsetProvider;
        public readonly bool IsRootedPlant;
        public readonly ItemShadowVisualDefinitionDto ShadowVisual;
        public SpriteRenderer SourceRenderer;
        public float ShadowWidth;
        public bool InWater;

        public ShadowBinding(IVisualGroundOffset groundOffsetProvider, bool isRootedPlant,
            ItemShadowVisualDefinitionDto shadowVisual)
        {
            GroundOffsetProvider = groundOffsetProvider;
            IsRootedPlant = isRootedPlant;
            ShadowVisual = shadowVisual;
        }
    }

}
