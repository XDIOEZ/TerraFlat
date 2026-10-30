using System.Collections.Generic;
using FlatWorld.Networking;
using UnityEngine;
using static WorldItemWaterRules;
using static WorldItemWaterSystem;

/// <summary>
/// 单个散落物的水中临时运行态。状态由当前位置、物品重量/体积与液体浮力推导，
/// 不写入库存 ItemData；对象池复用或离水时统一清理。漂浮稳定后仅按低频节奏检查水流与离水状态。
/// </summary>
public sealed class WorldItemWaterRuntime : MonoBehaviour, IItemPoolLifecycle
{
    #region 水态运行与生命周期

    private const string WaterEffectMaterialAddress = "Assets/9_Shaders/Material/Sprite-Lit-Master.mat";

    private Item waterItem;
    private ActorRenderEffectController waterRenderEffects;
    private WaterImmersionRenderEffect waterImmersionEffect;
    private bool isSinking;
    private bool isFloating;
    private bool isOnBottom;
    private string observedSpecialData;
    private readonly List<SpriteRenderer> bottomHiddenRenderers = new();
    private bool floatSettled;
    private float sinkElapsed;
    private float resolvedSinkDuration;
    private float floatElapsed;
    private float floatTargetDepth;
    private float settledTickElapsed;
    private float submergedElapsed;
    private Transform submergedVisual;
    private Vector3 originalVisualScale;
    private Transform driftVisual;
    private Vector3 originalDriftVisualPosition;
    private DriftInterpolation driftInterpolation;
    private ulong activatedTick;

    public bool IsActive => waterItem != null && (isSinking || isFloating || isOnBottom);
    public bool IsOnBottom => IsActive && isOnBottom;
    public bool IsSubmerged => IsOnBottom || IsActive && waterImmersionEffect != null && waterImmersionEffect.CurrentDepth >= 0.995f;

    /// <summary>从当前位置建立浮沉状态；重复调用同一活动实例不会重置进度。</summary>
    public void Activate(Item targetItem)
    {
        if (targetItem == null || targetItem.itemData?.Stack == null)
            return;
        if (IsActive && waterItem == targetItem)
            return;

        ResetRuntimeState(resetVisual: true);
        waterItem = targetItem;
        observedSpecialData = targetItem.itemData.ItemSpecialData;
        activatedTick = GameplayFixedStepSimulation.TickIndex;
        enabled = true;

        if (GetSavedBottomState(targetItem.itemData))
        {
            EnsureWaterVisual(targetItem);
            SettleOnBottom();
            return;
        }

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

        string worldKey = GameplayWorldRegistry.GetItemWorldKey(waterItem);
        if (string.IsNullOrEmpty(worldKey)) return;
        bool hasWorld = GameplayWorldRegistry.TryGet(worldKey, out _);
        if (!hasWorld && worldKey != GameplayWorldRegistry.ExecutionWorldKey) return;
        using var worldScope = hasWorld ? GameplayWorldRegistry.UseWorld(worldKey) : null;
        UpdateWaterState(Mathf.Max(0f, Time.deltaTime));
        ApplyDriftVisualPosition();
    }

    private void UpdateWaterState(float deltaTime)
    {
        if (!isOnBottom && observedSpecialData != waterItem.itemData.ItemSpecialData)
        {
            observedSpecialData = waterItem.itemData.ItemSpecialData;
            if (GetSavedBottomState(waterItem.itemData)) SettleOnBottom();
        }

        if (isOnBottom)
        {
            settledTickElapsed += deltaTime;
            if (settledTickElapsed >= SettledTickInterval)
            {
                settledTickElapsed = 0f;
                if (WorldItemWaterSystem.TryResolveWaterAt(waterItem.transform.position, out bool isWater) && !isWater)
                {
                    Deactivate(clearBottomState: true);
                    return;
                }
                DroppedItemService.TryPickupBottomItem(waterItem);
            }
            return;
        }

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
    public void Deactivate(bool clearBottomState = false)
    {
        if (clearBottomState && waterItem != null && GameNetwork.HasStateAuthority && GetSavedBottomState(waterItem.itemData))
        {
            SetSavedBottomState(waterItem.itemData, false);
            ItemNetworkStateSerialization.NotifyRuntimeStateChanged(waterItem);
        }
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
        if (targetItem.Sprite != null && targetItem.Sprite.transform != targetItem.transform)
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
            Deactivate(clearBottomState: true);
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
        if (recedeProgress < 1f)
            return;

        SettleOnBottom();
    }

    #region 河底静止与隐藏

    private void SettleOnBottom()
    {
        if (isOnBottom || waterItem == null) return;
        isSinking = false;
        isFloating = false;
        isOnBottom = true;
        settledTickElapsed = 0f;
        driftInterpolation = default;
        if (driftVisual != null) driftVisual.localPosition = originalDriftVisualPosition;
        if (submergedVisual != null) submergedVisual.localScale = originalVisualScale;
        waterImmersionEffect.SetWaterState(1f, true);
        // 只关闭渲染，物品根节点、碰撞、世界登记与真实尺寸都保留。
        foreach (SpriteRenderer renderer in waterItem.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (renderer.forceRenderingOff) continue;
            renderer.forceRenderingOff = true;
            bottomHiddenRenderers.Add(renderer);
        }
        if (GameNetwork.HasStateAuthority)
        {
            SetSavedBottomState(waterItem.itemData, true);
            observedSpecialData = waterItem.itemData.ItemSpecialData;
            ItemNetworkStateSerialization.NotifyRuntimeStateChanged(waterItem);
        }
    }

    #endregion

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
        // 漂流时水线仍需跟随插值后的贴图位置，只有水流和地形查询保持低频。
        waterItem.itemData.Stack.CanBePickedUp = true;
        ItemNetworkStateSerialization.NotifyRuntimeStateChanged(waterItem);
    }

    /// <summary>水流与外力共用散物运动内核；岸线只限制水流，独立外力可把物品推上岸。</summary>
    private void UpdateWaterDrift(float deltaTime)
    {
        if (waterItem == null || deltaTime <= 0f || activatedTick == GameplayFixedStepSimulation.TickIndex)
            return;

        ChunkMgr chunkMgr = ChunkMgr.ExistingInstance;
        Vector2 velocity = Vector2.zero;
        if (chunkMgr != null && chunkMgr.TryGetRuntimeWaterCurrent(waterItem.transform.position, out RuntimeWaterCurrentSample current))
            velocity = current.Direction * ResolveDriftSpeed(current.Kind, current.Flow, waterItem.itemData.Stack);
        Vector2 currentPosition = waterItem.transform.position;
        Vector2 targetPosition = LooseItemMotion.AdvanceWater(GameplayWorldRegistry.GetItemWorldKey(waterItem), waterItem.itemData,
            currentPosition, velocity, deltaTime, !IsSubmerged);
        if (!LooseItemMotion.CommitItemPosition(waterItem, targetPosition)) return;
        if (WorldPhysicsService2D.GetConfiguredItemPhysics(waterItem) != null)
        {
            driftInterpolation = default;
            if (driftVisual != null) driftVisual.localPosition = originalDriftVisualPosition;
            return;
        }
        driftInterpolation.RecordMovement(
            WorldTopologyRuntime.ShortestDelta(currentPosition, targetPosition), deltaTime, Time.timeAsDouble);
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
        if (referenceRenderer != null && referenceRenderer.transform != targetItem.transform)
        {
            driftVisual = referenceRenderer.transform;
            originalDriftVisualPosition = driftVisual.localPosition;
        }
    }

    #region 水流显示插值

    private void ApplyDriftVisualPosition()
    {
        if (driftVisual == null || driftVisual.parent == null) return;
        Vector2 offset = driftInterpolation.GetOffset(Time.timeAsDouble);
        driftVisual.localPosition = originalDriftVisualPosition +
            driftVisual.parent.InverseTransformVector(new Vector3(offset.x, offset.y, 0f));
    }

    #endregion

    private void ResetRuntimeState(bool resetVisual)
    {
        foreach (SpriteRenderer renderer in bottomHiddenRenderers)
            if (renderer != null) renderer.forceRenderingOff = false;
        bottomHiddenRenderers.Clear();
        if (driftVisual != null)
            driftVisual.localPosition = originalDriftVisualPosition;
        driftVisual = null;
        driftInterpolation = default;
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
        isOnBottom = false;
        observedSpecialData = null;
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

    #endregion
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
        var scene = GameplayWorldRegistry.CurrentScene;
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
            WorldRenderingManager.Instance.ApplyRendererSorting(splashRenderer, WorldSortingManager.GroundSplashCategory);
            splashRenderer.enableGPUInstancing = true;
        }

        splashParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
}
