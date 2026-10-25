using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// 通用燃烧适配器：向独立 Shader 火焰转发燃烧状态、锚点和排序。
/// 常驻火焰不模拟粒子，只有入水熄灭时产生一次性烟汽。
/// </summary>
[DisallowMultipleComponent]
public sealed class CombustionVisualEffect : MonoBehaviour, IWaterEntryTransformEffect
{
    #region 常量与配置

    private const string FlameName = "Combustion Shader Flame";
    private const string ExtinguishRootName = "Combustion Extinguish Burst";
    private const float ExtinguishEffectLifetime = 1.6f;

    [Header("引用")]
    [SerializeField] private Transform flameAnchor;
    [SerializeField] private Material particleMaterial;

    [SerializeField] private Material flameMaterial;

    private Light2D sourceLight;
    private SpriteRenderer sourceSpriteRenderer;
    private ShaderFlameEffect flame;
    private bool effectPlaying;
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
    private Material presentationFlameMaterialOverride;
    public Material FlameSourceMaterial => flameMaterial != null ? flameMaterial : Resources.Load<Material>(ShaderFlameEffect.MaterialResourcePath);

    /// <summary>宿主按所在水面层提供共享表现材质，不改变燃烧状态或源模板。</summary>
    public void SetFlamePresentationMaterialOverride(Material material)
    {
        if (presentationFlameMaterialOverride == material) return;
        presentationFlameMaterialOverride = material;
        EnsureFlame();
    }

    #endregion

    #region 熄灭表现

    /// <summary>燃烧物入水转换前喷出一小团黑烟与白汽；根节点脱离宿主，源物品回收后仍可播完。</summary>
    public void PlayWaterEntryTransformEffect()
    {
        ResolveReferences();

        Vector3 worldPosition = flameAnchor != null ? flameAnchor.position : transform.position;
        if (particleMaterial == null) particleMaterial = Resources.Load<Material>("Combustion_Particle");
        int sortingLayerId = hasExplicitSorting ? explicitSortingLayerId : 0;
        int sortingOrder = (hasExplicitSorting ? explicitSortingOrder : 0) + 3;
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
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
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
        EnsureFlame();
        RefreshPlayingState(force: true);
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsureFlame();
        RefreshPlayingState(force: true);
    }

    private void LateUpdate()
    {
        if (!combustionActive) return;
        RefreshRendererAnchor();
        RefreshFlamePose();
        RefreshSorting();
        RefreshPlayingState(force: false);
    }

    private void OnDisable()
    {
        flame?.SetBurning(false);
        effectPlaying = false;
    }

    #endregion

    #region 引用与状态

    // 行网格设施没有 SpriteRenderer，直接复用火焰模板并显式传入宿主排序。
    public void BindWorldPresentation(CombustionVisualEffect template, int sortingLayerId, int sortingOrder)
    {
        if (template == null)
            throw new System.ArgumentNullException(nameof(template));

        particleMaterial = template.particleMaterial;
        flameMaterial = template.flameMaterial;
        hasExplicitSorting = true;
        explicitSortingLayerId = sortingLayerId;
        explicitSortingOrder = sortingOrder;
        EnsureFlame();
        RefreshSorting();
    }

    // 保留内容配置接口，原发射倍率现在只控制火势，不再驱动粒子数量。
    public void ConfigureFlamePresentation(float scale, float heightRatio,
        float emissionMultiplier, bool followAnchor, bool followRenderer, Vector3 rendererOffset)
    {
        flameScale = Mathf.Max(0.01f, scale);
        flameHeightRatio = Mathf.Max(0.01f, heightRatio);
        flameEmissionMultiplier = Mathf.Max(0f, emissionMultiplier);
        attachFlameToAnchor = followAnchor;
        followSourceRenderer = followRenderer;
        sourceRendererAnchorOffset = rendererOffset;
        ResolveReferences();
        EnsureFlame();
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
        bool changed = combustionActive != active || flame == null;
        combustionActive = active;
        if (flame == null)
        {
            ResolveReferences();
            EnsureFlame();
        }
        RefreshPlayingState(force: changed);
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
        Vector3 offset = sourceRendererAnchorOffset;
        if (sourceSpriteRenderer.flipX) offset.x = -offset.x;
        if (sourceSpriteRenderer.flipY) offset.y = -offset.y;
        Vector3 anchorPosition = source.TransformPoint(offset);
        Quaternion anchorRotation = source.rotation;
        // 静止火源不反复写入 Transform；源或父级移动时仍按最终世界姿态同步。
        if (rendererFlameAnchor.position != anchorPosition || rendererFlameAnchor.rotation != anchorRotation)
            rendererFlameAnchor.SetPositionAndRotation(anchorPosition, anchorRotation);
        Vector3 parentScale = transform.lossyScale;
        Vector3 sourceScale = source.lossyScale;
        Vector3 anchorScale = new Vector3(
            parentScale.x != 0f ? sourceScale.x / parentScale.x : 1f,
            parentScale.y != 0f ? sourceScale.y / parentScale.y : 1f,
            parentScale.z != 0f ? sourceScale.z / parentScale.z : 1f);
        if (rendererFlameAnchor.localScale != anchorScale)
            rendererFlameAnchor.localScale = anchorScale;
    }

    /// <summary>光源被关闭时同步隐藏火焰，不改变权威燃烧状态。</summary>
    private void RefreshPlayingState(bool force)
    {
        bool shouldPlay = combustionActive &&
                          isActiveAndEnabled &&
                          (sourceLight == null ||
                           (sourceLight.enabled && sourceLight.intensity > 0f));
        if (!force && shouldPlay == effectPlaying)
            return;

        flame?.SetBurning(shouldPlay);
        effectPlaying = shouldPlay;
    }

    #endregion

    #region 网格装配与宿主同步

    private void EnsureFlame()
    {
        if (flameAnchor == null) return;
        if (flame == null)
        {
            var root = new GameObject(FlameName);
            root.layer = gameObject.layer;
            root.transform.SetParent(flameAnchor, false);
            flame = root.AddComponent<ShaderFlameEffect>();
        }
        if (flame.transform.parent != flameAnchor) flame.transform.SetParent(flameAnchor, false);
        flame.transform.localPosition = Vector3.zero;
        flame.transform.localScale = Vector3.one;
        float strength = flameEmissionMultiplier > 0f ? 0.85f + 0.15f * Mathf.Sqrt(flameEmissionMultiplier) : 0f;
        flame.Configure(new Vector2(0.15f * flameScale, 0.20f * flameScale * flameHeightRatio), strength,
            presentationFlameMaterialOverride != null ? presentationFlameMaterialOverride : flameMaterial);
        RefreshFlamePose();
        RefreshSorting();
    }

    // 手持火焰可随武器倾斜，其他火源保持向世界上方燃烧。
    private void RefreshFlamePose()
    {
        if (flame == null) return;
        Quaternion rotation = attachFlameToAnchor && flameAnchor != null ? flameAnchor.rotation : Quaternion.identity;
        if (flame.transform.rotation != rotation) flame.transform.rotation = rotation;
    }

    private void RefreshSorting()
    {
        if (flame == null) return;
        int layerId = hasExplicitSorting ? explicitSortingLayerId :
            sourceSpriteRenderer != null ? sourceSpriteRenderer.sortingLayerID : 0;
        int order = hasExplicitSorting ? explicitSortingOrder :
            sourceSpriteRenderer != null ? sourceSpriteRenderer.sortingOrder : 0;
        // 留在宿主 SortingGroup 内使用相对顺序，独立掉落火焰由其后端传入最终键。
        flame.SetSorting(layerId, order + 2);
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
