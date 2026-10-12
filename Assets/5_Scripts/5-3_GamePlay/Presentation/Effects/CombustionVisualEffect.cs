using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// 通用燃烧表现：持续生成小型火焰、火星与烟雾粒子。
/// 烟与火星在世界空间向上飘，主体火苗可配置为跟随燃烧点。
/// </summary>
[DisallowMultipleComponent]
public sealed class CombustionVisualEffect : MonoBehaviour, IWaterEntryTransformEffect
{
    #region 常量与配置

    private const string FlameName = "Combustion Flame Particles";
    private const string EmberName = "Combustion Ember Particles";
    private const string SmokeName = "Combustion Smoke Particles";
    private const string ExtinguishRootName = "Combustion Extinguish Burst";
    private const float ExtinguishEffectLifetime = 1.6f;

    [Header("引用")]
    [SerializeField] private Transform flameAnchor;
    [SerializeField] private Material particleMaterial;

    [Header("发射速率")]
    [SerializeField, Min(0f)] private float flameRate = 8f;
    [SerializeField, Min(0f)] private float emberRate = 1.4f;
    [SerializeField, Min(0f)] private float smokeRate = 1.1f;

    private Light2D sourceLight;
    private SpriteRenderer sourceSpriteRenderer;
    private ParticleSystem flameParticles;
    private ParticleSystem emberParticles;
    private ParticleSystem smokeParticles;
    private bool particlesPlaying;
    private bool combustionActive;
    private float flameScale = 1f;
    private float flameHeightRatio = 1f;
    private float flameEmissionMultiplier = 1f;
    private bool attachFlameToAnchor;
    private bool followSourceRenderer;
    private Vector3 sourceRendererAnchorOffset;
    private Transform defaultFlameAnchor;
    private Transform rendererFlameAnchor;
    private bool hasExplicitSorting;
    private int explicitSortingLayerId;
    private int explicitSortingOrder;

    #endregion

    #region 熄灭表现

    /// <summary>燃烧物入水转换前喷出一小团黑烟与白汽；根节点脱离宿主，源物品回收后仍可播完。</summary>
    public void PlayWaterEntryTransformEffect()
    {
        ResolveReferences();

        Vector3 worldPosition = flameAnchor != null ? flameAnchor.position : transform.position;
        int sortingLayerId = 0;
        int sortingOrder = 3;
        if (sourceSpriteRenderer != null)
        {
            WorldSortingManager.ReadExternalRendererKey(sourceSpriteRenderer,
                out sortingLayerId, out sortingOrder);
            sortingOrder += 3;
        }

        var effectRoot = new GameObject(ExtinguishRootName);
        effectRoot.layer = gameObject.layer;
        effectRoot.transform.position = worldPosition;
        effectRoot.transform.rotation = Quaternion.identity;
        effectRoot.transform.localScale = Vector3.one;
        if (gameObject.scene.IsValid() && gameObject.scene.isLoaded)
            SceneManager.MoveGameObjectToScene(effectRoot, gameObject.scene);

        ParticleSystem blackSmoke = CreateExtinguishBurstSystem(
            effectRoot.transform,
            "Black Smoke",
            new Color(0.05f, 0.05f, 0.05f, 0.9f),
            new Color(0.18f, 0.18f, 0.18f, 0.65f),
            particleCount: 8,
            lifetimeMin: 0.75f,
            lifetimeMax: 1.15f,
            sizeMin: 0.075f,
            sizeMax: 0.13f,
            velocityYMin: 0.14f,
            velocityYMax: 0.27f,
            sortingLayerId,
            sortingOrder);

        ParticleSystem whiteSteam = CreateExtinguishBurstSystem(
            effectRoot.transform,
            "White Steam",
            new Color(0.96f, 0.96f, 0.96f, 0.95f),
            new Color(0.68f, 0.68f, 0.68f, 0.5f),
            particleCount: 10,
            lifetimeMin: 0.55f,
            lifetimeMax: 0.9f,
            sizeMin: 0.065f,
            sizeMax: 0.12f,
            velocityYMin: 0.19f,
            velocityYMax: 0.34f,
            sortingLayerId,
            sortingOrder + 1);

        blackSmoke.Play(true);
        blackSmoke.Emit(8);
        whiteSteam.Play(true);
        whiteSteam.Emit(10);
        Destroy(effectRoot, ExtinguishEffectLifetime);
    }

    /// <summary>构建一次性 2D 粒子爆发；黑烟和白汽共用当前燃烧表现的粒子材质。</summary>
    private ParticleSystem CreateExtinguishBurstSystem(
        Transform parent,
        string objectName,
        Color startColor,
        Color endColor,
        int particleCount,
        float lifetimeMin,
        float lifetimeMax,
        float sizeMin,
        float sizeMax,
        float velocityYMin,
        float velocityYMax,
        int sortingLayerId,
        int sortingOrder)
    {
        var particleObject = new GameObject(objectName, typeof(ParticleSystem));
        particleObject.layer = gameObject.layer;
        particleObject.transform.SetParent(parent, false);
        particleObject.transform.localPosition = Vector3.zero;
        particleObject.transform.localRotation = Quaternion.identity;
        particleObject.transform.localScale = Vector3.one;

        ParticleSystem particles = particleObject.GetComponent<ParticleSystem>();
        ParticleSystem.MainModule main = particles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.prewarm = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetimeMin, lifetimeMax);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startColor = Color.white;
        main.maxParticles = particleCount;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.045f;
        shape.radiusThickness = 1f;

        ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
        velocity.y = new ParticleSystem.MinMaxCurve(velocityYMin, velocityYMax);
        velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(CreateGradient(
            startColor,
            Color.Lerp(startColor, endColor, 0.45f),
            endColor,
            startColor.a,
            0f));

        ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.55f),
            new Keyframe(0.35f, 1f),
            new Keyframe(1f, 1.45f)));

        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
        {
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sortMode = ParticleSystemSortMode.OldestInFront;
            renderer.sortingLayerID = sortingLayerId;
            renderer.sortingOrder = sortingOrder;
            if (particleMaterial != null)
                renderer.sharedMaterial = particleMaterial;
        }

        return particles;
    }

    #endregion

    #region Unity 生命周期

    private void Awake()
    {
        ResolveReferences();
        EnsureParticleSystems();
        RefreshPlayingState(force: true);
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsureParticleSystems();
        RefreshPlayingState(force: true);
    }

    private void LateUpdate()
    {
        RefreshRendererAnchor();
        RefreshSorting();
        RefreshPlayingState(force: false);
    }

    private void OnDisable()
    {
        SetParticleSystemsPlaying(false, clear: true);
        particlesPlaying = false;
    }

    private void OnValidate()
    {
        flameRate = Mathf.Max(0f, flameRate);
        emberRate = Mathf.Max(0f, emberRate);
        smokeRate = Mathf.Max(0f, smokeRate);
    }

    #endregion

    #region 引用与状态

    // 行网格设施没有 SpriteRenderer，直接复用粒子模板并显式传入宿主排序。
    public void BindWorldPresentation(CombustionVisualEffect template, int sortingLayerId, int sortingOrder)
    {
        if (template == null)
            throw new System.ArgumentNullException(nameof(template));

        particleMaterial = template.particleMaterial;
        flameRate = template.flameRate;
        emberRate = template.emberRate;
        smokeRate = template.smokeRate;
        hasExplicitSorting = true;
        explicitSortingLayerId = sortingLayerId;
        explicitSortingOrder = sortingOrder;
        EnsureParticleSystems();
        RefreshSorting();
    }

    // 火苗跟随手持物，烟和火星继续独立飘散。
    public void ConfigureFlamePresentation(float scale, float heightRatio,
        float emissionMultiplier, bool followAnchor, bool followRenderer, Vector3 rendererOffset)
    {
        flameScale = Mathf.Max(0.01f, scale);
        flameHeightRatio = Mathf.Max(0.01f, heightRatio);
        flameEmissionMultiplier = Mathf.Max(0f, emissionMultiplier);
        attachFlameToAnchor = followAnchor;
        followSourceRenderer = followRenderer;
        sourceRendererAnchorOffset = rendererOffset;
        SetParticleSystemPlaying(flameParticles, false, clear: true);
        ResolveReferences();
        EnsureParticleSystems();
        RefreshPlayingState(force: true);
    }

    /// <summary>显式绑定宿主表现，避免通用模块 Prefab 依赖自身父子层级猜测排序来源。</summary>
    public void BindHost(Item host)
    {
        if (host == null)
            return;

        SpriteRenderer[] renderers = host.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null || renderer.transform.IsChildOf(transform))
                continue;

            if (renderer.GetComponent<Animator>() != null)
            {
                sourceSpriteRenderer = renderer;
                break;
            }

            sourceSpriteRenderer ??= renderer;
        }

        RefreshRendererAnchor();
        RefreshSorting();
    }

    /// <summary>由通用燃烧模块显式驱动表现开关，不从具体物品类型推断。</summary>
    public void SetCombustionActive(bool active)
    {
        combustionActive = active;
        RefreshPlayingState(force: true);
    }

    /// <summary>优先使用同层级 Light2D 的位置作为燃烧点。</summary>
    private void ResolveReferences()
    {
        if (defaultFlameAnchor == null)
        {
            Light2D light = GetComponentInChildren<Light2D>(true);
            defaultFlameAnchor = flameAnchor != null ? flameAnchor : light != null ? light.transform : transform;
        }

        // 在 Awake 就准备锚点，让物品池记录到固定的表现层级。
        if (rendererFlameAnchor == null)
        {
            var anchorObject = new GameObject("Combustion Renderer Anchor");
            anchorObject.layer = gameObject.layer;
            rendererFlameAnchor = anchorObject.transform;
            rendererFlameAnchor.SetParent(transform, false);
        }
        flameAnchor = followSourceRenderer ? rendererFlameAnchor : defaultFlameAnchor;
        sourceLight ??= defaultFlameAnchor.GetComponent<Light2D>();
        sourceSpriteRenderer ??= GetComponentInParent<SpriteRenderer>();
        RefreshRendererAnchor();
    }

    // 燃烧点读取主贴图的真实动画变换，避免挥动时火焰与木柄分离。
    private void RefreshRendererAnchor()
    {
        if (!followSourceRenderer || rendererFlameAnchor == null || sourceSpriteRenderer == null)
            return;
        Transform source = sourceSpriteRenderer.transform;
        rendererFlameAnchor.SetPositionAndRotation(source.TransformPoint(sourceRendererAnchorOffset), source.rotation);
        Vector3 parentScale = transform.lossyScale;
        Vector3 sourceScale = source.lossyScale;
        rendererFlameAnchor.localScale = new Vector3(
            parentScale.x != 0f ? sourceScale.x / parentScale.x : 1f,
            parentScale.y != 0f ? sourceScale.y / parentScale.y : 1f,
            parentScale.z != 0f ? sourceScale.z / parentScale.z : 1f);
    }

    /// <summary>光源被关闭时同步熄灭燃烧粒子。</summary>
    private void RefreshPlayingState(bool force)
    {
        bool shouldPlay = combustionActive &&
                          isActiveAndEnabled &&
                          (sourceLight == null ||
                           (sourceLight.enabled && sourceLight.intensity > 0f));
        if (!force && shouldPlay == particlesPlaying)
            return;

        SetParticleSystemsPlaying(shouldPlay, clear: !shouldPlay);
        particlesPlaying = shouldPlay;
    }

    private void SetParticleSystemsPlaying(bool shouldPlay, bool clear)
    {
        SetParticleSystemPlaying(flameParticles, shouldPlay, clear);
        SetParticleSystemPlaying(emberParticles, shouldPlay, clear);
        SetParticleSystemPlaying(smokeParticles, shouldPlay, clear);
    }

    private static void SetParticleSystemPlaying(ParticleSystem particles, bool shouldPlay, bool clear)
    {
        if (particles == null)
            return;

        if (shouldPlay)
        {
            if (!particles.isPlaying)
                particles.Play(true);
            return;
        }

        ParticleSystemStopBehavior behavior = clear
            ? ParticleSystemStopBehavior.StopEmittingAndClear
            : ParticleSystemStopBehavior.StopEmitting;
        particles.Stop(true, behavior);
    }

    #endregion

    #region 粒子装配

    private void EnsureParticleSystems()
    {
        if (flameAnchor == null)
            return;

        flameParticles ??= FindParticleSystem(FlameName);
        emberParticles ??= FindParticleSystem(EmberName);
        smokeParticles ??= FindParticleSystem(SmokeName);

        if (flameParticles == null)
            flameParticles = CreateParticleSystem(FlameName);
        if (emberParticles == null)
            emberParticles = CreateParticleSystem(EmberName);
        if (smokeParticles == null)
            smokeParticles = CreateParticleSystem(SmokeName);

        ReparentParticleSystem(flameParticles);
        ReparentParticleSystem(emberParticles);
        ReparentParticleSystem(smokeParticles);
        ConfigureFlame(flameParticles);
        ConfigureEmbers(emberParticles);
        ConfigureSmoke(smokeParticles);
        RefreshSorting();
    }

    private void ReparentParticleSystem(ParticleSystem particles)
    {
        if (particles.transform.parent != flameAnchor)
            particles.transform.SetParent(flameAnchor, false);
    }

    private ParticleSystem FindParticleSystem(string objectName)
    {
        Transform child = flameAnchor.Find(objectName);
        return child != null ? child.GetComponent<ParticleSystem>() : null;
    }

    private ParticleSystem CreateParticleSystem(string objectName)
    {
        var particleObject = new GameObject(objectName, typeof(ParticleSystem));
        particleObject.layer = gameObject.layer;
        particleObject.transform.SetParent(flameAnchor, false);
        particleObject.transform.localPosition = Vector3.zero;
        particleObject.transform.localRotation = Quaternion.identity;
        particleObject.transform.localScale = Vector3.one;

        ParticleSystem particles = particleObject.GetComponent<ParticleSystem>();
        ParticleSystemRenderer renderer = particleObject.GetComponent<ParticleSystemRenderer>();
        if (renderer != null && particleMaterial != null)
            renderer.sharedMaterial = particleMaterial;
        return particles;
    }

    /// <summary>主体火苗：短寿命、较密集，形成持续跳动的暖色核心。</summary>
    private void ConfigureFlame(ParticleSystem particles)
    {
        ConfigureBase(
            particles,
            maxParticles: Mathf.Clamp(Mathf.CeilToInt(flameRate * flameEmissionMultiplier * 0.32f) + 2, 12, 64),
            emissionRate: flameRate * flameEmissionMultiplier,
            lifetimeMin: 0.18f,
            lifetimeMax: 0.32f,
            sizeMin: 0.055f * flameScale,
            sizeMax: 0.095f * flameScale,
            radius: 0.018f * flameScale,
            velocityXMin: -0.035f * flameScale,
            velocityXMax: 0.035f * flameScale,
            velocityYMin: 0.15f * flameScale,
            velocityYMax: 0.28f * flameScale);

        ParticleSystem.MainModule main = particles.main;
        main.simulationSpace = attachFlameToAnchor
            ? ParticleSystemSimulationSpace.Local
            : ParticleSystemSimulationSpace.World;
        main.prewarm = attachFlameToAnchor;
        main.startSize3D = true;
        main.startSizeX = new ParticleSystem.MinMaxCurve(0.055f * flameScale, 0.095f * flameScale);
        main.startSizeY = new ParticleSystem.MinMaxCurve(
            0.055f * flameScale * flameHeightRatio, 0.095f * flameScale * flameHeightRatio);
        main.startSizeZ = 1f;

        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(CreateGradient(
            new Color(1f, 0.95f, 0.45f, 1f),
            new Color(1f, 0.42f, 0.08f, 1f),
            new Color(0.75f, 0.08f, 0.02f, 1f),
            0.95f,
            0f));

        ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.5f),
            new Keyframe(0.3f, 1f),
            new Keyframe(1f, 0.15f)));
    }

    /// <summary>火星：数量少、速度快，用亮点强调正在燃烧。</summary>
    private void ConfigureEmbers(ParticleSystem particles)
    {
        ConfigureBase(
            particles,
            maxParticles: 6,
            emissionRate: emberRate,
            lifetimeMin: 0.45f,
            lifetimeMax: 0.85f,
            sizeMin: 0.014f,
            sizeMax: 0.026f,
            radius: 0.025f,
            velocityXMin: -0.11f,
            velocityXMax: 0.11f,
            velocityYMin: 0.25f,
            velocityYMax: 0.5f);

        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(CreateGradient(
            new Color(1f, 0.9f, 0.28f, 1f),
            new Color(1f, 0.32f, 0.04f, 1f),
            new Color(0.55f, 0.05f, 0.01f, 1f),
            1f,
            0f));

        ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(1f, 0.2f)));
    }

    /// <summary>烟雾：低频、低透明度，缓慢上浮并逐渐扩散消失。</summary>
    private void ConfigureSmoke(ParticleSystem particles)
    {
        ConfigureBase(
            particles,
            maxParticles: 6,
            emissionRate: smokeRate,
            lifetimeMin: 0.9f,
            lifetimeMax: 1.45f,
            sizeMin: 0.055f,
            sizeMax: 0.09f,
            radius: 0.02f,
            velocityXMin: -0.045f,
            velocityXMax: 0.045f,
            velocityYMin: 0.1f,
            velocityYMax: 0.18f);

        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(CreateGradient(
            new Color(0.28f, 0.25f, 0.22f, 1f),
            new Color(0.2f, 0.2f, 0.2f, 1f),
            new Color(0.13f, 0.13f, 0.13f, 1f),
            0.16f,
            0f));

        ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.45f),
            new Keyframe(0.45f, 0.8f),
            new Keyframe(1f, 1.3f)));
    }

    /// <summary>三个效果共用低开销的世界空间发射骨架。</summary>
    private void ConfigureBase(
        ParticleSystem particles,
        int maxParticles,
        float emissionRate,
        float lifetimeMin,
        float lifetimeMax,
        float sizeMin,
        float sizeMax,
        float radius,
        float velocityXMin,
        float velocityXMax,
        float velocityYMin,
        float velocityYMax)
    {
        ParticleSystem.MainModule main = particles.main;
        main.loop = true;
        main.playOnAwake = false;
        main.prewarm = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetimeMin, lifetimeMax);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startColor = Color.white;
        main.maxParticles = maxParticles;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = true;
        emission.rateOverTime = Mathf.Max(0f, emissionRate);

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = radius;
        shape.radiusThickness = 1f;

        ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        // X/Y/Z 统一使用 TwoConstants，避免 Unity 粒子速度曲线模式不一致报错。
        velocity.x = new ParticleSystem.MinMaxCurve(velocityXMin, velocityXMax);
        velocity.y = new ParticleSystem.MinMaxCurve(velocityYMin, velocityYMax);
        velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        if (renderer == null)
            return;

        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.sortMode = ParticleSystemSortMode.OldestInFront;
        if (particleMaterial != null)
            renderer.sharedMaterial = particleMaterial;
    }

    private void RefreshSorting()
    {
        if (!hasExplicitSorting && sourceSpriteRenderer == null)
            sourceSpriteRenderer = GetComponentInParent<SpriteRenderer>();

        int sortingLayerId = hasExplicitSorting ? explicitSortingLayerId :
            sourceSpriteRenderer != null ? sourceSpriteRenderer.sortingLayerID : 0;
        int sortingOrder = (hasExplicitSorting ? explicitSortingOrder :
            sourceSpriteRenderer != null ? sourceSpriteRenderer.sortingOrder : 0) + 2;
        ApplySorting(flameParticles, sortingLayerId, sortingOrder);
        ApplySorting(emberParticles, sortingLayerId, sortingOrder + 1);
        ApplySorting(smokeParticles, sortingLayerId, sortingOrder - 1);
    }

    private static void ApplySorting(ParticleSystem particles, int sortingLayerId, int sortingOrder)
    {
        if (particles == null)
            return;

        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        if (renderer == null)
            return;

        renderer.sortingLayerID = sortingLayerId;
        renderer.sortingOrder = sortingOrder;
    }

    private static Gradient CreateGradient(
        Color start,
        Color middle,
        Color end,
        float startAlpha,
        float endAlpha)
    {
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(start, 0f),
                new GradientColorKey(middle, 0.45f),
                new GradientColorKey(end, 1f)
            },
            new[]
            {
                new GradientAlphaKey(startAlpha, 0f),
                new GradientAlphaKey(startAlpha * 0.8f, 0.55f),
                new GradientAlphaKey(endAlpha, 1f)
            });
        return gradient;
    }

    #endregion
}
