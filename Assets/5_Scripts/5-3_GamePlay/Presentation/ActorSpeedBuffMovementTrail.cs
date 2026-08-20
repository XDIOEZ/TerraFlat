using UnityEngine;

/// <summary>
/// 速度 Buff 的脚底移动粒子表现。
/// 只要角色当前拥有大于 1 倍的移动速度效果，并且 Mover 正在产生实际速度，
/// 就在角色脚下发射少量向移动反方向漂移的粒子；停止移动、Buff 移除或对象禁用时立即清空粒子。
/// 粒子使用世界空间模拟，避免跟随角色整体移动而失去“落在身后”的拖尾感。
/// </summary>
[DisallowMultipleComponent]
public sealed class ActorSpeedBuffMovementTrail : MonoBehaviour
{
    #region 配置

    private const float MovementThreshold = 0.03f;
    private const float ParticlesPerSecond = 16f;
    private const float MaxSpawnAccumulator = 1f;
    private const int MaxParticles = 48;
    private const int MaxEmitsPerFrame = 4;
    private const float FootOffset = 0.015f;
    private const int SortingOrderOffset = -1;
    private const string FootTrailObjectName = "SpeedBuffFootTrail";
    private const string TrailMaterialResourcePath = "Weather/Materials/RainParticle";

    private static readonly Vector2 ParticleLifetimeRange = new(0.28f, 0.48f);
    private static readonly Vector2 ParticleSizeRange = new(0.045f, 0.085f);
    private static readonly Color ParticleColor = new(0.62f, 0.94f, 1f, 0.82f);

    #endregion

    #region 运行时状态

    private ParticleSystem trailParticles;
    private ParticleSystemRenderer trailRenderer;
    private BuffManager buffManager;
    private Mover mover;
    private SpriteRenderer sourceRenderer;
    private float spawnAccumulator;
    private Vector3 lastPosition;
    private bool hasLastPosition;

    #endregion

    #region 生命周期

    /// <summary>缓存角色主体 Sprite，并初始化位置采样基准。</summary>
    private void Awake()
    {
        sourceRenderer = GetComponent<SpriteRenderer>();
        lastPosition = transform.position;
        hasLastPosition = true;
    }

    /// <summary>启用时重新绑定 BuffManager，兼容对象池复用和模块延迟装配。</summary>
    private void OnEnable()
    {
        lastPosition = transform.position;
        hasLastPosition = true;
        BindBuffManager();
    }

    /// <summary>每帧根据当前 Buff 与实际移动速度更新脚底粒子。</summary>
    private void Update()
    {
        BindBuffManager();
        UpdateMovementTrail();
        lastPosition = transform.position;
        hasLastPosition = true;
    }

    /// <summary>禁用时解除事件并清空残留粒子，避免对象池复用时留下旧拖尾。</summary>
    private void OnDisable()
    {
        UnbindBuffManager();
        ClearTrailParticles();
        hasLastPosition = false;
    }

    /// <summary>销毁时再次清理事件和粒子，保证运行时动态创建的子对象不继续显示。</summary>
    private void OnDestroy()
    {
        UnbindBuffManager();
        ClearTrailParticles();
    }

    #endregion

    #region Buff 状态

    /// <summary>解析当前角色的 BuffManager，并在模块替换时切换事件订阅。</summary>
    private void BindBuffManager()
    {
        BuffManager resolvedManager = ResolveBuffManager();
        if (ReferenceEquals(buffManager, resolvedManager))
            return;

        UnbindBuffManager();
        buffManager = resolvedManager;
        if (buffManager == null)
            return;

        buffManager.BuffAdded += OnBuffChanged;
        buffManager.BuffRemoved += OnBuffChanged;
        buffManager.BuffDurationChanged += OnBuffChanged;
    }

    /// <summary>优先从 Item 模块容器查找 BuffManager，兼容角色表现节点的父级装配结构。</summary>
    private BuffManager ResolveBuffManager()
    {
        Item owner = GetComponentInParent<Item>();
        if (owner == null)
            return GetComponentInParent<BuffManager>();

        BuffManager module = owner.itemMods?.GetMod_ByID<BuffManager>(ModText.BuffManager);
        return module != null ? module : owner.GetComponentInChildren<BuffManager>(true);
    }

    /// <summary>解除旧 BuffManager 的生命周期事件订阅。</summary>
    private void UnbindBuffManager()
    {
        if (buffManager == null)
            return;

        buffManager.BuffAdded -= OnBuffChanged;
        buffManager.BuffRemoved -= OnBuffChanged;
        buffManager.BuffDurationChanged -= OnBuffChanged;
        buffManager = null;
    }

    /// <summary>Buff 发生变化时，在下一次表现更新前立即清理已经失效的拖尾。</summary>
    private void OnBuffChanged(BuffInstance runtime)
    {
        if (!HasActiveSpeedBuff())
            ClearTrailParticles();
    }

    /// <summary>判断当前是否存在真正提高移动速度的 Buff，而不是减速或停止阶段的反向效果。</summary>
    private bool HasActiveSpeedBuff()
    {
        if (buffManager?.ActiveBuffs == null)
            return false;

        foreach (BuffInstance runtime in buffManager.ActiveBuffs.Values)
        {
            if (runtime?.Definition?.Effects == null)
                continue;

            for (int i = 0; i < runtime.Definition.Effects.Count; i++)
            {
                BuffEffectDefinition effect = runtime.Definition.Effects[i];
                if (effect != null &&
                    effect.Phase == BuffEffectPhase.Start &&
                    effect.Value > 1f &&
                    effect.TypeId?.Equals(
                        BuffEffectTypeIds.MoveSpeedMultiplier,
                        System.StringComparison.OrdinalIgnoreCase) == true)
                {
                    return true;
                }
            }
        }

        return false;
    }

    #endregion

    #region 移动粒子

    /// <summary>按速度 Buff、Mover 实际速度和发射累积量生成脚底反向粒子。</summary>
    private void UpdateMovementTrail()
    {
        if (!HasActiveSpeedBuff() || !TryGetMovementVelocity(out Vector2 velocity))
        {
            ClearTrailParticles();
            return;
        }

        EnsureTrailParticleSystem();
        if (trailParticles == null)
            return;

        UpdateTrailTransform();
        if (!trailParticles.isPlaying)
            trailParticles.Play(true);

        float speedFactor = Mathf.Clamp(velocity.magnitude / 2.5f, 0.65f, 1.45f);
        spawnAccumulator = Mathf.Min(
            MaxSpawnAccumulator,
            spawnAccumulator + ParticlesPerSecond * speedFactor * Mathf.Max(0f, Time.deltaTime));

        int emitCount = Mathf.Min(Mathf.FloorToInt(spawnAccumulator), MaxEmitsPerFrame);
        if (emitCount <= 0)
            return;

        spawnAccumulator -= emitCount;
        Vector2 backward = -velocity.normalized;
        Vector2 lateral = new(-backward.y, backward.x);
        for (int i = 0; i < emitCount; i++)
            EmitTrailParticle(backward, lateral, velocity.magnitude);
    }

    /// <summary>读取 Mover 或 AI 导航的实际速度；没有刚体速度时再使用表现节点位移兜底。</summary>
    private bool TryGetMovementVelocity(out Vector2 velocity)
    {
        velocity = Vector2.zero;
        mover = ResolveMover();

        if (mover is Mover_AI ai && ai.NavigationAgent != null)
            velocity = ai.NavigationAgent.Velocity;

        if (mover?.rb != null && mover.rb.velocity.sqrMagnitude > velocity.sqrMagnitude)
            velocity = mover.rb.velocity;

        if (velocity.sqrMagnitude <= MovementThreshold * MovementThreshold &&
            hasLastPosition &&
            Time.deltaTime > Mathf.Epsilon)
        {
            Vector2 transformVelocity = (Vector2)(transform.position - lastPosition) / Time.deltaTime;
            if (transformVelocity.sqrMagnitude > velocity.sqrMagnitude)
                velocity = transformVelocity;
        }

        return velocity.sqrMagnitude > MovementThreshold * MovementThreshold;
    }

    /// <summary>解析角色的 Mover 模块，支持玩家 Mover、AI Mover_AI 和运行时延迟装配。</summary>
    private Mover ResolveMover()
    {
        if (mover != null)
            return mover;

        Item owner = GetComponentInParent<Item>();
        Mover resolved = owner?.itemMods?.GetMod_ByID<Mover>(ModText.Mover) as Mover;
        return resolved != null ? resolved : GetComponentInParent<Mover>();
    }

    /// <summary>只创建一次世界空间粒子系统，并排除角色 MPB 对粒子材质的染色。</summary>
    private void EnsureTrailParticleSystem()
    {
        if (trailParticles != null)
            return;

        Transform child = transform.Find(FootTrailObjectName);
        GameObject trailObject = child != null ? child.gameObject : new GameObject(FootTrailObjectName);
        if (child == null)
        {
            trailObject.layer = gameObject.layer;
            trailObject.transform.SetParent(transform, false);
        }

        if (trailObject.GetComponent<ActorRenderEffectExclude>() == null)
            trailObject.AddComponent<ActorRenderEffectExclude>();

        trailParticles = trailObject.GetComponent<ParticleSystem>();
        if (trailParticles == null)
            trailParticles = trailObject.AddComponent<ParticleSystem>();

        trailRenderer = trailParticles.GetComponent<ParticleSystemRenderer>();
        ConfigureTrailParticleSystem();
    }

    /// <summary>配置短生命周期、世界空间、无物理碰撞的轻量脚底粒子。</summary>
    private void ConfigureTrailParticleSystem()
    {
        ParticleSystem.MainModule main = trailParticles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        main.maxParticles = MaxParticles;
        main.startLifetime = new ParticleSystem.MinMaxCurve(
            ParticleLifetimeRange.x,
            ParticleLifetimeRange.y);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(
            ParticleSizeRange.x,
            ParticleSizeRange.y);
        main.startColor = ParticleColor;
        main.gravityModifier = 0f;

        ParticleSystem.EmissionModule emission = trailParticles.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = trailParticles.shape;
        shape.enabled = false;

        ParticleSystem.VelocityOverLifetimeModule velocity = trailParticles.velocityOverLifetime;
        velocity.enabled = false;

        ParticleSystem.SizeOverLifetimeModule size = trailParticles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(
            1f,
            new AnimationCurve(
                new Keyframe(0f, 0.35f),
                new Keyframe(0.22f, 1f),
                new Keyframe(1f, 0.15f)));

        ParticleSystem.ColorOverLifetimeModule color = trailParticles.colorOverLifetime;
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
                new GradientAlphaKey(0.78f, 0f),
                new GradientAlphaKey(0.45f, 0.42f),
                new GradientAlphaKey(0f, 1f)
            });
        color.color = alphaGradient;

        if (trailRenderer != null)
        {
            trailRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            trailRenderer.sharedMaterial = Resources.Load<Material>(TrailMaterialResourcePath);
            trailRenderer.sortingOrder = SortingOrderOffset;
            trailRenderer.enableGPUInstancing = true;
        }

        trailParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    /// <summary>将粒子发射器放在当前 Sprite 脚底，并同步到角色排序层的后方。</summary>
    private void UpdateTrailTransform()
    {
        if (trailParticles == null)
            return;

        sourceRenderer ??= GetComponent<SpriteRenderer>();
        trailParticles.transform.position = GetFootWorldPosition();
        if (trailRenderer == null)
            trailRenderer = trailParticles.GetComponent<ParticleSystemRenderer>();

        if (trailRenderer == null)
            return;

        trailRenderer.sortingLayerID = sourceRenderer != null ? sourceRenderer.sortingLayerID : 0;
        trailRenderer.sortingOrder = sourceRenderer != null
            ? sourceRenderer.sortingOrder + SortingOrderOffset
            : SortingOrderOffset;
    }

    /// <summary>根据当前 Sprite 的可见边界计算世界空间脚底位置，兼容不同体型和动画帧。</summary>
    private Vector3 GetFootWorldPosition()
    {
        sourceRenderer ??= GetComponent<SpriteRenderer>();
        if (sourceRenderer == null || sourceRenderer.sprite == null)
            return transform.position;

        Bounds bounds = sourceRenderer.sprite.bounds;
        Vector3 localPosition = new(bounds.center.x, bounds.min.y + FootOffset, 0f);
        return sourceRenderer.transform.TransformPoint(localPosition);
    }

    /// <summary>在脚底发射一颗向移动反方向漂移、带少量横向随机的粒子。</summary>
    private void EmitTrailParticle(Vector2 backward, Vector2 lateral, float movementSpeed)
    {
        Vector3 footPosition = GetFootWorldPosition();
        footPosition += (Vector3)(lateral * UnityEngine.Random.Range(-0.07f, 0.07f));

        float driftSpeed = Mathf.Clamp(0.14f + movementSpeed * 0.08f, 0.14f, 0.7f);
        Vector2 particleVelocity = backward * driftSpeed +
                                   lateral * UnityEngine.Random.Range(-0.05f, 0.05f);
        ParticleSystem.EmitParams emitParams = new()
        {
            position = footPosition,
            velocity = particleVelocity,
            startColor = Color.Lerp(
                ParticleColor,
                Color.white,
                UnityEngine.Random.Range(0f, 0.35f)),
            startLifetime = UnityEngine.Random.Range(
                ParticleLifetimeRange.x,
                ParticleLifetimeRange.y),
            startSize = UnityEngine.Random.Range(
                ParticleSizeRange.x,
                ParticleSizeRange.y)
        };
        trailParticles.Emit(emitParams, 1);
    }

    /// <summary>停止发射并清除所有已经离开脚底的粒子。</summary>
    private void ClearTrailParticles()
    {
        spawnAccumulator = 0f;
        if (trailParticles != null)
            trailParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    #endregion
}
