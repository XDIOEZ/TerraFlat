using System;
using MemoryPack;
using UnityEngine;

[MemoryPackable(GenerateType.VersionTolerant)]
[Serializable]
public partial class DarkNavigationSaveData
{
    [MemoryPackIgnore] public Vector2 WanderTarget;
    [MemoryPackInclude, MemoryPackOrder(0)] private float WanderTargetX { get => WanderTarget.x; set => WanderTarget.x = value; }
    [MemoryPackInclude, MemoryPackOrder(1)] private float WanderTargetY { get => WanderTarget.y; set => WanderTarget.y = value; }
    [MemoryPackOrder(2)] public bool HasWanderTarget;
    [MemoryPackOrder(3)] public float WanderPauseRemaining;
}

/// <summary>
/// 幽灵 AI：在感知范围内直接追击本地玩家，追击时不受地形可走性和玩家所在光照限制。
/// 幽灵自身所在位置的亮度严格大于 0.5 时持续添加“光耀” Buff，亮度小于等于 0.5 时移除；视觉子节点以低幅度正弦曲线上下浮动，
/// 浮动只作用于 Sprite，不改变根物体、碰撞体、寻路位置和伤害判定。
/// </summary>
public partial class Mod_LightSensitiveNavigation : Module, ISimulationRangeAware
{
    public const string ModuleId = "Mod_LightSensitiveNavigation";
    [SerializeField] private string lightExposureBuffId = "光耀";
    [SerializeField] private float lightDamageThreshold = 0.5f;
    private const float LightEpsilon = 0.0001f;

    // 幽灵 AI 统一使用 JSON/Prefab 的稳定模块身份，避免资源目录解析到旧别名。
    public override string CanonicalModuleId => ModuleId;

    public Ex_ModData_MemoryPackable ModData = new();
    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = (Ex_ModData_MemoryPackable)value;
    }

    [SerializeField]
    private DarkNavigationSaveData Data = new();

    [Header("感知与移动")]
    [SerializeField, Min(0.1f), Tooltip("主动发现玩家的距离，应覆盖幽灵的最大生成距离。")]
    private float perceptionRadius = 60f;
    [SerializeField, Min(0.1f)]
    private float wanderRadius = 6f;
    [SerializeField, Min(0.1f)]
    private float wanderSpeed = 1.25f;
    [SerializeField, Min(0.1f)]
    private float chaseSpeed = 2.5f;
    [SerializeField, Min(0.1f)]
    private float retreatSpeed = 2f;
    [SerializeField, Min(0.05f)]
    private float decisionInterval = 0.2f;
    [SerializeField, Min(0.1f)]
    private float stuckRepathDelay = 0.75f;
    [SerializeField, Min(1)]
    private int darkPositionSamples = 16;
    [SerializeField, Min(0.5f)]
    private float retreatSearchRadius = 6f;

    [Header("表现")]
    [SerializeField]
    private Transform visualTransform;
    [SerializeField]
    private SpriteRenderer spriteRenderer;
    [SerializeField, Min(0f), Tooltip("幽灵视觉上下浮动的高度，不影响根物体和碰撞体。")]
    private float bobHeight = 0.12f;
    [SerializeField, Min(0f)]
    private float bobFrequency = 1.4f;

    private Mod_DamageReceiver _damageReceiver;
    private Mod_BuffManager _buffManager;
    private WorldNavigationAgent _pathAgent;
    private Rigidbody2D _rigidbody;
    private Vector3 _visualBaseLocalPosition;
    private float _bobPhase;
    private bool _hasVisualBasePosition;
    private Vector2 _moveTarget;
    private bool _hasMoveTarget;
    private bool _usesDirectMotion;
    private bool _isWanderTravel;
    private bool _loggedMissingRadianceBuff;
    private float _darkSearchRetryRemaining;
    private bool _forcedRadiance;

    public override ModuleTickMode TickMode => ModuleTickMode.EveryFrame;
    public Item ActorItem => item;
    public bool IsAlive => item != null && !item.DestructionHandled &&
                           (_damageReceiver == null || _damageReceiver.Hp > 0f);

    public override void Awake()
    {
        if (ModData == null)
            ModData = new Ex_ModData_MemoryPackable();
        ModData.ID = ModuleId;
    }

    protected override void OnLoad()
    {
        Data ??= new DarkNavigationSaveData();
        ModData.ReadData(ref Data);

        item.itemMods.GetMod_ByID(ModText.Hp, out _damageReceiver);
        _buffManager = item.GetComponentInChildren<Mod_BuffManager>(true);
        if (visualTransform == null && item.Sprite != null)
            visualTransform = item.Sprite.transform;
        if (spriteRenderer == null)
            spriteRenderer = item.Sprite != null
                ? item.Sprite
                : item.GetComponentInChildren<SpriteRenderer>(true);
        if (item.Sprite == null)
            item.Sprite = spriteRenderer;

        if (visualTransform != null)
        {
            if (!_hasVisualBasePosition)
            {
                _visualBaseLocalPosition = visualTransform.localPosition;
                _hasVisualBasePosition = true;
            }

            // 为不同幽灵分配稳定初始相位，避免同屏实体完全同步浮动。
            _bobPhase = ((GetInstanceID() & int.MaxValue) % 97) / 97f * Mathf.PI * 2f;
        }

        _rigidbody = item.GetComponent<Rigidbody2D>();
        EnsureNavigationAgent();
        _moveTarget = Data.WanderTarget;
        _hasMoveTarget = Data.HasWanderTarget;
        _usesDirectMotion = false;
        _isWanderTravel = Data.HasWanderTarget;
        _darkSearchRetryRemaining = 0f;
        _forcedRadiance = false;
        BindGhostPresentation();
        if (_damageReceiver != null)
        {
            _damageReceiver.DeathStarted -= HandleGhostDeath;
            _damageReceiver.DeathStarted += HandleGhostDeath;
        }
    }

    private void EnsureNavigationAgent()
    {
        _pathAgent = item.GetComponent<WorldNavigationAgent>();
        if (_pathAgent == null)
            _pathAgent = item.gameObject.AddComponent<WorldNavigationAgent>();

        _pathAgent.Bind(item.GetComponent<Rigidbody2D>(), null, null, item);
        _pathAgent.Configure(0.05f, 0.1f, stuckRepathDelay, 0.01f);
        _pathAgent.Stop(clearDestination: true);
    }

    protected override void OnSave()
    {
        Data ??= new DarkNavigationSaveData();
        Data.WanderTarget = _moveTarget;
        Data.HasWanderTarget = _hasMoveTarget && _isWanderTravel;
        ModData.WriteData(Data);

        if (item?.itemData != null && !string.IsNullOrWhiteSpace(ModData.Name))
            item.itemData.ModuleDataDic[ModData.Name] = ModData;
    }

    #region 光照与节点动作
    public override void ModUpdate(float deltaTime)
    {
        if (!IsAlive || item?.itemData == null || GameManager.Instance == null ||
            !GameManager.Instance.IsInGameWorld)
            return;

        UpdateVisual();
        _darkSearchRetryRemaining = Mathf.Max(0f, _darkSearchRetryRemaining - Mathf.Max(0f, deltaTime));
        if (TryGetCurrentLightLevel(out float light) &&
            (light > lightDamageThreshold || (_forcedRadiance && light > LightEpsilon)))
            EnsureRadianceBuff();
        else
            RemoveRadianceBuff();
    }

    public bool TryGetCurrentLightLevel(out float light)
    {
        light = 0f;
        return LightLayerMgr.Instance != null &&
            LightLayerMgr.Instance.TryGetLightLevel(item.transform.position, out light);
    }

    public bool HasVisiblePlayerTarget()
    {
        Transform player = ResolvePlayerTransform();
        if (player == null) return false;
        Item playerItem = player.GetComponentInParent<Player>();
        float radius = item.ClampPerceptionRadius(
            Mod_ItemDetector.CalculateEffectiveDetectionRadius(perceptionRadius, playerItem));
        return WorldTopologyRuntime.SqrDistance(item.transform.position, player.position) <= radius * radius;
    }

    public bool IsCurrentPositionIlluminated() =>
        TryGetCurrentLightLevel(out float light) && light > LightEpsilon;
    public bool CanSearchForDarkness => _hasMoveTarget || _darkSearchRetryRemaining <= 0f;

    public void BeginDirectPursuit()
    {
        _forcedRadiance = false;
        _isWanderTravel = false;
        _hasMoveTarget = false;
        _usesDirectMotion = true;
        _pathAgent?.Stop(clearDestination: true);
    }

    public bool TickDirectPursuit(float deltaTime)
    {
        Transform player = ResolvePlayerTransform();
        if (player == null || !HasVisiblePlayerTarget()) return false;
        _moveTarget = player.position;
        MoveDirectlyTowards(_moveTarget, chaseSpeed, deltaTime);
        return true;
    }

    public void BeginDarkNavigation(bool wander)
    {
        _forcedRadiance = false;
        _usesDirectMotion = false;
        _animationDrivenVelocity = Vector2.zero;
        if (!wander || !_isWanderTravel) _hasMoveTarget = false;
        _isWanderTravel = wander;
    }

    public bool TickDarkNavigation(float deltaTime, bool wander)
    {
        if (!_hasMoveTarget || !CanMoveTo(_moveTarget))
        {
            if (!_hasMoveTarget && _darkSearchRetryRemaining > 0f)
            {
                StopMoving();
                return wander;
            }
            _hasMoveTarget = TryFindDarkPosition(item.transform.position,
                wander ? wanderRadius : retreatSearchRadius, out _moveTarget);
            if (!_hasMoveTarget)
            {
                _darkSearchRetryRemaining = Mathf.Max(0.05f, decisionInterval);
                StopMoving();
                return wander;
            }
        }

        if (_pathAgent == null) return false;
        _pathAgent.MaxSpeed = wander ? wanderSpeed : retreatSpeed;
        _pathAgent.SetDestination(_moveTarget);
        _pathAgent.Tick(deltaTime);
        ItemMgr.Instance?.NotifyRuntimeItemMoved(item);
        if (_pathAgent.ReachedDestination)
        {
            _hasMoveTarget = false;
            StopMoving();
        }
        return true;
    }

    public void StopNavigation(bool clearDestination = true)
    {
        _usesDirectMotion = false;
        _isWanderTravel = false;
        _forcedRadiance = false;
        if (clearDestination) _hasMoveTarget = false;
        StopMoving();
    }

    public void WaitInLight()
    {
        StopNavigation();
        _forcedRadiance = true;
        EnsureRadianceBuff();
    }
    #endregion

    #region 直接追击

    /// <summary>直接沿世界最短方向移动，追击时跳过导航可走性和障碍物检查。</summary>
    private void MoveDirectlyTowards(Vector2 target, float speed, float deltaTime)
    {
        Vector2 delta = WorldTopologyRuntime.ShortestDelta(item.transform.position, target);
        float distance = delta.magnitude;
        if (distance <= 0.05f)
        {
            _hasMoveTarget = false;
            StopMoving();
            return;
        }

        float stepDistance = Mathf.Max(0f, speed) * Mathf.Max(0f, deltaTime);
        Vector2 currentPosition = item.transform.position;
        Vector2 targetPresentation = currentPosition + delta;
        Vector2 nextPosition = distance <= stepDistance
            ? targetPresentation
            : currentPosition + delta / distance * stepDistance;
        _animationDrivenVelocity = deltaTime > 0f ? (nextPosition - currentPosition) / deltaTime : Vector2.zero;
        // 幽灵直追不经导航刚体速度，仍须叠加与其它生物相同的表层水流位移。
        Vector2 currentVelocity = WorldMotionSystem.SampleWaterVelocity(
            item.transform.position, WaterCurrentPushConfigService.ResolvePushSpeed(item));
        currentVelocity += WorldMotionSystem.SampleWindVelocity(currentPosition, item.itemData.Stack.CurrentWeight);
        Vector2 presentationPosition = nextPosition + currentVelocity * Mathf.Max(0f, deltaTime);
        Vector2 logicalPosition = WorldTopologyRuntime.NormalizePosition(presentationPosition);

        if (_rigidbody != null)
        {
            _rigidbody.position = presentationPosition;
            _rigidbody.velocity = Vector2.zero;
        }

        item.transform.position = new Vector3(
            presentationPosition.x,
            presentationPosition.y,
            item.transform.position.z);
        if (item.itemData?.transform != null)
            item.itemData.transform.position = new Vector3(logicalPosition.x, logicalPosition.y, item.transform.position.z);

        ItemMgr.Instance?.NotifyRuntimeItemMoved(item);
    }

    #endregion

    private void StopMoving()
    {
        _animationDrivenVelocity = Vector2.zero;
        _pathAgent?.Stop();
    }

    #region 死亡行为边界

    private void HandleGhostDeath(Mod_DamageReceiver health)
    {
        // 尸体停留期间撤销幽灵的主动追击，视觉交给死亡动画保持。
        _hasMoveTarget = false;
        _animationDrivenVelocity = Vector2.zero;
        _pathAgent?.Stop(clearDestination: true);
        if (_rigidbody != null) _rigidbody.velocity = Vector2.zero;
    }

    #endregion

    #region 模拟范围休眠

    /// <summary>由 ItemMgr 越界回调撤销旧路径和物理速度。</summary>
    public void OnSimulationRangePaused()
    {
        _animationDrivenVelocity = Vector2.zero;
        _pathAgent?.Stop();
        if (_rigidbody != null) _rigidbody.velocity = Vector2.zero;
    }

    /// <summary>下一次 ModUpdate 会按当前状态重新提交追击或巡游目标。</summary>
    public void OnSimulationRangeResumed()
    {
    }

    #endregion

    #region 玩家目标解析

    /// <summary>
    /// 获取当前本地玩家，优先使用 ItemMgr 的正式入口；当存档玩家名尚未同步时，
    /// 回退到 Player_DIC 中已标记为本地档案且仍处于激活状态的玩家，避免幽灵目标为空。
    /// </summary>
    private Transform ResolvePlayerTransform()
    {
        ItemMgr itemManager = ItemMgr.Instance;
        if (itemManager == null)
            return null;

        Transform playerTransform = itemManager.UserPlayerTransform;
        if (playerTransform != null && playerTransform.gameObject.activeInHierarchy)
            return playerTransform;

        foreach (Player player in itemManager.Player_DIC.Values)
        {
            if (player != null && player.IsLocalProfile && player.gameObject.activeInHierarchy)
                return player.transform;
        }

        return null;
    }

    #endregion

    private bool CanMoveTo(Vector2 next)
    {
        if (!IsWalkable(next))
            return false;

        LightLayerMgr lightManager = LightLayerMgr.Instance;
        if (lightManager == null)
            return true;

        if (!lightManager.TryGetLightLevel(next, out float nextLight))
        {
            return false;
        }

        return nextLight <= LightEpsilon;
    }

    private static bool IsWalkable(Vector2 worldPosition)
    {
        WorldNavigationManager navigation = WorldNavigationManager.Instance;
        if (navigation == null || !navigation.IsNavigationReady)
            return false;

        return navigation.TryGetCell(worldPosition, out _, out bool walkable) && walkable;
    }

    private bool TryFindDarkPosition(Vector2 origin, float radius, out Vector2 position)
    {
        position = default;
        LightLayerMgr lightManager = LightLayerMgr.Instance;
        if (lightManager == null)
            return false;

        int samples = Mathf.Max(1, darkPositionSamples);
        float safeRadius = Mathf.Max(0.5f, radius);
        float phase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);

        for (int ring = 1; ring <= 3; ring++)
        {
            float distance = safeRadius * ring / 3f;
            for (int i = 0; i < samples; i++)
            {
                float angle = phase + Mathf.PI * 2f * i / samples;
                Vector2 candidate = origin + new Vector2(
                    Mathf.Cos(angle),
                    Mathf.Sin(angle)) * distance;

                if (lightManager.IsCompletelyDark(candidate) &&
                    IsWalkable(candidate))
                {
                    position = candidate;
                    return true;
                }
            }
        }

        return false;
    }

    private void EnsureRadianceBuff()
    {
        if (_buffManager == null)
            _buffManager = item.GetComponentInChildren<Mod_BuffManager>(true);

        if (_buffManager != null)
        {
            if (_buffManager.HasBuff(lightExposureBuffId) ||
                _buffManager.AddBuff(lightExposureBuffId))
            {
                return;
            }
        }

        if (!_loggedMissingRadianceBuff)
        {
            _loggedMissingRadianceBuff = true;
            Debug.LogWarning("[AI_Ghost] 找不到光耀 Buff 或 BuffManager。", this);
        }
    }

    private void RemoveRadianceBuff()
    {
        if (_buffManager != null && _buffManager.HasBuff(lightExposureBuffId))
            _buffManager.RemoveBuff(lightExposureBuffId);
    }

    private void UpdateVisual()
    {
        if (visualTransform == null)
            return;
        if (_animationBody != null && _animationBody.IsSingleSpriteMode)
        {
            visualTransform.localPosition = _visualBaseLocalPosition;
            return;
        }

        float angle = Time.time * Mathf.Max(0f, bobFrequency) * Mathf.PI * 2f + _bobPhase;
        float offset = Mathf.Sin(angle) * Mathf.Max(0f, bobHeight);
        visualTransform.localPosition = _visualBaseLocalPosition + Vector3.up * offset;
    }

    private void OnValidate()
    {
        ModData ??= new Ex_ModData_MemoryPackable();
        ModData.ID = ModuleId;
        bobHeight = Mathf.Max(0f, bobHeight);
        bobFrequency = Mathf.Max(0f, bobFrequency);
    }
}
