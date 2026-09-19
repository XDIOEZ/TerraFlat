using FlatWorld.Networking;
using UnityEngine;
using static WorldItemWaterRules;

/// <summary>
/// 单个散落物的水中临时运行态。状态由当前位置、物品重量/体积与液体浮力推导，
/// 不写入库存 ItemData；对象池复用或离水时统一清理。漂浮稳定后仅按低频节奏检查水流与离水状态。
/// </summary>
public sealed class WorldItemWaterRuntime : MonoBehaviour, IItemPoolLifecycle
{
    private const string WaterEffectMaterialAddress = "Assets/9_Shaders/Material/Sprite-Lit-Master.mat";

    private Item waterItem;
    private ActorRenderEffectController waterRenderEffects;
    private WaterImmersionRenderEffect waterImmersionEffect;
    private bool isSinking;
    private bool isFloating;
    private bool floatSettled;
    private float sinkElapsed;
    private float resolvedSinkDuration;
    private float floatElapsed;
    private float floatTargetDepth;
    private float settledTickElapsed;
    private float submergedElapsed;
    private Transform submergedVisual;
    private Vector3 originalVisualScale;

    public bool IsActive => waterItem != null && (isSinking || isFloating);

    /// <summary>从当前位置建立浮沉状态；重复调用同一活动实例不会重置进度。</summary>
    public void Activate(Item targetItem)
    {
        if (targetItem == null || targetItem.itemData?.Stack == null)
            return;
        if (IsActive && waterItem == targetItem)
            return;

        ResetRuntimeState(resetVisual: true);
        waterItem = targetItem;
        enabled = true;

        float ratio = ResolveWeightVolumeRatio(targetItem.itemData.Stack);
        float effectiveThreshold = ResolveSinkRatioThreshold(targetItem.transform.position);
        if (ShouldSink(ratio, effectiveThreshold))
            BeginSink(targetItem, ratio);
        else
            BeginFloat(targetItem, ResolveFloatingDepth(ratio));
    }

    private void Update()
    {
        if (!IsActive)
            return;

        float deltaTime = Mathf.Max(0f, Time.deltaTime);
        if (isSinking)
        {
            UpdateSink(deltaTime);
            return;
        }

        if (!isFloating)
            return;

        if (floatSettled)
        {
            settledTickElapsed += deltaTime;
            if (settledTickElapsed < SettledTickInterval)
                return;

            deltaTime = settledTickElapsed;
            settledTickElapsed = 0f;
        }

        UpdateFloat(deltaTime);
    }

    /// <summary>显式退出水体状态并恢复原始水线表现。</summary>
    public void Deactivate()
    {
        ResetRuntimeState(resetVisual: true);
        enabled = false;
    }

    private void BeginSink(Item targetItem, float ratio)
    {
        EnsureWaterVisual(targetItem);
        WorldItemWaterEntrySplashEffect.Play(targetItem.transform.position, 1f);
        resolvedSinkDuration = ResolveSinkDuration(ratio);
        sinkElapsed = 0f;
        submergedElapsed = 0f;
        if (targetItem.Sprite != null)
        {
            submergedVisual = targetItem.Sprite.transform;
            originalVisualScale = submergedVisual.localScale;
        }
        waterImmersionEffect.SetWaterState(0f, true);
        targetItem.itemData.Stack.CanBePickedUp = true;
        isSinking = true;
        isFloating = false;
        floatSettled = false;
        ItemNetworkStateSerialization.NotifyRuntimeStateChanged(targetItem);
    }

    private void BeginFloat(Item targetItem, float targetDepth)
    {
        EnsureWaterVisual(targetItem);
        floatTargetDepth = Mathf.Clamp01(targetDepth);
        float splashIntensity = ResolveSplashIntensity(floatTargetDepth);
        WorldItemWaterEntrySplashEffect.Play(targetItem.transform.position, splashIntensity);
        floatElapsed = 0f;
        settledTickElapsed = 0f;
        float entryDepth = Mathf.Max(FloatingEntryDepth, floatTargetDepth);
        waterImmersionEffect.SetWaterState(entryDepth, true);
        targetItem.itemData.Stack.CanBePickedUp = true;
        isFloating = true;
        isSinking = false;
        floatSettled = false;
        ItemNetworkStateSerialization.NotifyRuntimeStateChanged(targetItem);
    }

    private void UpdateSink(float deltaTime)
    {
        if (waterItem == null || waterImmersionEffect == null)
        {
            Deactivate();
            return;
        }

        if (WorldItemWaterSystem.TryResolveWaterAt(waterItem.transform.position, out bool isWater) && !isWater)
        {
            Deactivate();
            return;
        }

        if (GameNetwork.HasStateAuthority)
            UpdateWaterDrift(deltaTime);

        sinkElapsed += deltaTime;
        float progress = Mathf.Clamp01(sinkElapsed / Mathf.Max(0.1f, resolvedSinkDuration));
        waterImmersionEffect.SetWaterState(progress, true);

        if (progress < 1f ||
            waterImmersionEffect.CurrentDepth < 0.995f ||
            waterImmersionEffect.CurrentBlend < 0.995f)
        {
            return;
        }

        // 水线完全覆盖之后再缩小，避免把“刚入水”误做成立即消失。
        submergedElapsed += deltaTime;
        float recedeProgress = Mathf.Clamp01(submergedElapsed / SubmergedRecedeDuration);
        if (submergedVisual != null)
            submergedVisual.localScale = originalVisualScale * ResolveSubmergedScale(recedeProgress);
        if (recedeProgress < 1f || !GameNetwork.HasStateAuthority)
            return;

        Item completedItem = waterItem;
        ResetRuntimeState(resetVisual: false);
        if (ItemMgr.Instance != null && completedItem != null && !completedItem.DestructionHandled)
            ItemMgr.Instance.DespawnItem(completedItem, saveData: false);
    }

    private void UpdateFloat(float deltaTime)
    {
        if (waterItem == null || waterImmersionEffect == null || waterRenderEffects == null)
        {
            Deactivate();
            return;
        }

        if (WorldItemWaterSystem.TryResolveWaterAt(waterItem.transform.position, out bool isWater) && !isWater)
        {
            Deactivate();
            return;
        }

        if (GameNetwork.HasStateAuthority)
            UpdateWaterDrift(deltaTime);

        if (floatSettled)
            return;

        floatElapsed = Mathf.Min(FloatingRiseDuration, floatElapsed + deltaTime);
        float t = Mathf.Clamp01(floatElapsed / FloatingRiseDuration);
        float smoothT = t * t * (3f - 2f * t);
        float entryDepth = Mathf.Max(FloatingEntryDepth, floatTargetDepth);
        float currentDepth = Mathf.Lerp(entryDepth, floatTargetDepth, smoothT);
        waterImmersionEffect.SetWaterState(currentDepth, true);

        if (t < 1f ||
            Mathf.Abs(waterImmersionEffect.CurrentDepth - floatTargetDepth) > 0.01f ||
            waterImmersionEffect.CurrentBlend < 0.995f)
        {
            return;
        }

        floatSettled = true;
        settledTickElapsed = 0f;
        waterRenderEffects.enabled = false;
        waterImmersionEffect.enabled = false;
        waterItem.itemData.Stack.CanBePickedUp = true;
        ItemNetworkStateSerialization.NotifyRuntimeStateChanged(waterItem);
    }

    /// <summary>按河流/海洋权威水流推进漂浮物；下一位置不是水时停在岸边。</summary>
    private void UpdateWaterDrift(float deltaTime)
    {
        if (waterItem == null || deltaTime <= 0f)
            return;

        ChunkMgr chunkMgr = ChunkMgr.ExistingInstance;
        if (chunkMgr == null ||
            !chunkMgr.TryGetRuntimeWaterCurrent(waterItem.transform.position, out RuntimeWaterCurrentSample current))
        {
            return;
        }

        float speed = ResolveDriftSpeed(current.Kind, current.Flow);
        if (speed <= 0f || current.Direction.sqrMagnitude <= 0.000001f)
            return;

        Vector2 currentPosition = waterItem.transform.position;
        Vector2 targetPosition = WorldTopologyRuntime.NormalizePosition(
            currentPosition + current.Direction * (speed * deltaTime));
        if (!WorldItemWaterSystem.TryResolveWaterAt(targetPosition, out bool targetIsWater) || !targetIsWater)
            return;

        Vector3 worldPosition = waterItem.transform.position;
        worldPosition.x = targetPosition.x;
        worldPosition.y = targetPosition.y;
        waterItem.transform.position = worldPosition;
        ItemWorldPlacement.TryAttachWorldModelTransientItem(waterItem, targetPosition);
        ItemMgr.Instance?.NotifyRuntimeItemMoved(waterItem);
    }

    private void EnsureWaterVisual(Item targetItem)
    {
        GameRes gameRes = GameRes.ExistingInstance;
        if (gameRes == null)
            throw new System.InvalidOperationException("世界物品进入水体时 GameRes 不存在，无法加载水体表现材质。");

        var materialHandle = gameRes.ResourceAssets.Load<Material>(WaterEffectMaterialAddress);
        materialHandle.WaitForCompletion();
        Material waterEffectMaterial = ResourceAssetScope.Require(
            materialHandle,
            $"世界物品水体表现材质 {WaterEffectMaterialAddress}");

        waterRenderEffects = GetComponent<ActorRenderEffectController>() ??
                             gameObject.AddComponent<ActorRenderEffectController>();
        waterImmersionEffect = GetComponent<WaterImmersionRenderEffect>() ??
                               gameObject.AddComponent<WaterImmersionRenderEffect>();

        waterRenderEffects.enabled = true;
        waterImmersionEffect.enabled = true;
        waterRenderEffects.SetEffectSpriteMaterial(waterEffectMaterial);
        waterRenderEffects.RefreshBindings();
        waterRenderEffects.RegisterExternalRenderers(targetItem.transform);

        SpriteRenderer referenceRenderer = targetItem.Sprite != null && targetItem.Sprite.sprite != null
            ? targetItem.Sprite
            : FindReferenceRenderer(targetItem);
        waterImmersionEffect.SetReferenceRenderer(referenceRenderer);
    }

    private void ResetRuntimeState(bool resetVisual)
    {
        // 回池、拾取和离水都恢复表现尺寸，禁止把缩小结果带入库存或下一次实例。
        if (submergedVisual != null)
            submergedVisual.localScale = originalVisualScale;
        submergedVisual = null;
        submergedElapsed = 0f;
        if (waterRenderEffects != null && waterItem != null)
            waterRenderEffects.UnregisterExternalRenderers(waterItem.transform);

        if (resetVisual && waterImmersionEffect != null)
            waterImmersionEffect.SetWaterState(0f, false);
        if (waterRenderEffects != null)
            waterRenderEffects.enabled = false;
        if (waterImmersionEffect != null)
            waterImmersionEffect.enabled = false;

        waterItem = null;
        isSinking = false;
        isFloating = false;
        floatSettled = false;
        sinkElapsed = 0f;
        resolvedSinkDuration = 0f;
        floatElapsed = 0f;
        floatTargetDepth = 0f;
        settledTickElapsed = 0f;
    }

    private static SpriteRenderer FindReferenceRenderer(Item targetItem)
    {
        SpriteRenderer[] renderers = targetItem.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && renderers[i].sprite != null)
                return renderers[i];
        }

        return null;
    }

    public void OnItemTakenFromPool()
    {
        ResetRuntimeState(resetVisual: true);
        enabled = false;
    }

    public void OnItemReturnedToPool()
    {
        ResetRuntimeState(resetVisual: true);
        enabled = false;
    }
}

/// <summary>
/// 世界散落物共用的一次性入水水花发射器。复用雨天地面水花材质，
/// 只保留一个世界空间粒子系统，避免每个漂浮物各自常驻粒子组件。
/// </summary>
internal static class WorldItemWaterEntrySplashEffect
{
    private const string SplashMaterialResourcePath = "Weather/Materials/RainGroundSplash";
    private const string EmitterName = "WorldItemWaterEntrySplashes";
    private const int MaxParticles = 96;
    private const int SortingOrder = 40;

    private static readonly Color SplashColor = new(0.72f, 0.94f, 1f, 0.72f);
    private static ParticleSystem splashParticles;
    private static ParticleSystemRenderer splashRenderer;
    private static Material splashMaterial;
    private static bool materialLoadFailed;

    /// <summary>按入水强度发射短促环形水花；极轻漂浮物仍至少保留一次很小的反馈。</summary>
    public static void Play(Vector3 worldPosition, float intensity)
    {
        EnsureEmitter();
        if (splashParticles == null)
            return;

        float safeIntensity = Mathf.Clamp01(intensity);
        ParticleSystem.EmitParams emitParams = new()
        {
            position = worldPosition,
            startColor = SplashColor,
            startLifetime = Mathf.Lerp(0.22f, 0.38f, safeIntensity),
            startSize = Mathf.Lerp(0.16f, 0.48f, safeIntensity)
        };

        splashParticles.Emit(emitParams, safeIntensity >= 0.75f ? 2 : 1);
    }

    /// <summary>在当前世界根节点下创建一个共享世界空间粒子系统。</summary>
    private static void EnsureEmitter()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (splashParticles != null && splashParticles.gameObject.scene == scene)
            return;
        if (splashParticles != null) UnityEngine.Object.Destroy(splashParticles.gameObject);
        splashParticles = null;

        ChunkMgr chunkMgr = ChunkMgr.ExistingInstance;
        if (chunkMgr == null)
            return;

        if (splashMaterial == null && !materialLoadFailed)
        {
            splashMaterial = Resources.Load<Material>(SplashMaterialResourcePath);
            if (splashMaterial == null)
            {
                materialLoadFailed = true;
                Debug.LogError($"[WorldItemWaterEntrySplashEffect] 未找到水花材质 Resources/{SplashMaterialResourcePath}。");
                return;
            }
        }

        if (splashMaterial == null)
            return;

        GameObject emitterObject = new GameObject(EmitterName);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(emitterObject, scene);

        splashParticles = emitterObject.GetComponent<ParticleSystem>();
        if (splashParticles == null)
            splashParticles = emitterObject.AddComponent<ParticleSystem>();
        splashRenderer = splashParticles.GetComponent<ParticleSystemRenderer>();

        ConfigureEmitter();
    }

    /// <summary>配置短生命周期、无碰撞、只靠尺寸扩张表达水波的环形粒子。</summary>
    private static void ConfigureEmitter()
    {
        ParticleSystem.MainModule main = splashParticles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        main.maxParticles = MaxParticles;
        main.startSpeed = 0f;
        main.startLifetime = 0.3f;
        main.startSize = 0.28f;
        main.startColor = Color.white;

        ParticleSystem.EmissionModule emission = splashParticles.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = splashParticles.shape;
        shape.enabled = false;

        ParticleSystem.VelocityOverLifetimeModule velocity = splashParticles.velocityOverLifetime;
        velocity.enabled = false;

        ParticleSystem.SizeOverLifetimeModule size = splashParticles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(
            1f,
            new AnimationCurve(
                new Keyframe(0f, 0.35f),
                new Keyframe(0.28f, 0.8f),
                new Keyframe(1f, 1f)));

        ParticleSystem.ColorOverLifetimeModule color = splashParticles.colorOverLifetime;
        color.enabled = true;
        Gradient alphaGradient = new();
        alphaGradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new[]
            {
                new GradientAlphaKey(0.72f, 0f),
                new GradientAlphaKey(0.38f, 0.5f),
                new GradientAlphaKey(0f, 1f)
            });
        color.color = alphaGradient;

        if (splashRenderer != null)
        {
            splashRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            splashRenderer.sharedMaterial = splashMaterial;
            splashRenderer.sortingLayerName = "Default";
            splashRenderer.sortingOrder = SortingOrder;
            splashRenderer.enableGPUInstancing = true;
        }

        splashParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
}
