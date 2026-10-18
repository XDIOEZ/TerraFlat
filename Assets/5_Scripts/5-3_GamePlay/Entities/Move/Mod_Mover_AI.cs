using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// AI 公共移动模块。对上层保持原有目标、停止和到达接口，内部使用无限地图导航。
/// </summary>
public class Mod_Mover_AI : Mod_Mover, ISimulationRangeAware, ITemperatureSafetyMovement
{
    private const float MinimumDestinationChangeDistance = 0.5f;

    [Title("AI 移动")]
    [Header("移动目标")]
    public Transform target;

    [Header("到达判定")]
    [MinValue(0.01f)]
    public float stopDistance = 0.5f;

    [Tooltip("目的地变化超过该距离时视为新请求")]
    [MinValue(0.001f)]
    public float destinationChangeThreshold = MinimumDestinationChangeDistance;

    [Header("卡住恢复")]
    [MinValue(0.1f)]
    public float stalledRepathDelay = 0.8f;

    [MinValue(0.001f)]
    public float progressDistanceThreshold = 0.02f;

    [Tooltip("实际速度低于该值时，动画视为停止")]
    [MinValue(0f)]
    public float animationMoveSpeedThreshold = 0.03f;

    [Header("运行时状态")]
    public bool CanMove = true;
    public bool HasReachedTarget;

    [ShowInInspector, ReadOnly]
    public WorldNavigationAgent NavigationAgent { get; private set; }

    private bool hasDestination;
    private bool hasTemperatureSafetyDestination;
    private Mod_Temperature temperature;
    private Mod_DamageReceiver health;
    private bool CanActivelyMove => health == null || health.Hp > 0f;
    private bool hasTemperatureBiasedDestination;
    private Vector2 temperatureBiasedRequest;
    private Vector2 temperatureBiasedDestination;
    private Vector2 lastSubmittedDestination;
    private float EffectiveDestinationChangeDistance =>
        Mathf.Max(MinimumDestinationChangeDistance, destinationChangeThreshold);

    public float SpeedValue => Speed.Value;
    public bool HasActiveDestination => hasDestination;
    public bool IsPathPending => NavigationAgent != null && NavigationAgent.PathPending;
    public WorldNavigationDestinationResult DestinationResult => NavigationAgent != null
        ? NavigationAgent.DestinationResult
        : hasDestination
            ? WorldNavigationDestinationResult.Pending
            : WorldNavigationDestinationResult.None;
    public bool IsActuallyMoving =>
        CanMove &&
        hasDestination &&
        !HasReachedTarget &&
        NavigationAgent != null &&
        DrivenVelocity.sqrMagnitude >
        animationMoveSpeedThreshold * animationMoveSpeedThreshold;
    public int TemperatureSafetyMovementPriority => 0;
    public bool ShouldAdvanceTemperatureSafetyDestination =>
        hasTemperatureSafetyDestination &&
        (HasReachedTarget || DestinationResult == WorldNavigationDestinationResult.Failed);

    protected override void OnLoad()
    {
        base.OnLoad();

        GameObject agentObject = item != null ? item.gameObject : gameObject;
        NavigationAgent = agentObject.GetComponent<WorldNavigationAgent>();
        if (NavigationAgent == null)
            NavigationAgent = agentObject.AddComponent<WorldNavigationAgent>();

        NavigationAgent.Bind(rb, null, this);
        NavigationAgent.Configure(
            stopDistance,
            EffectiveDestinationChangeDistance,
            stalledRepathDelay,
            progressDistanceThreshold);

        Vector2 currentPosition = rb != null ? rb.position : (Vector2)agentObject.transform.position;
        TargetPosition = currentPosition;
        lastSubmittedDestination = currentPosition;
        hasDestination = false;
        hasTemperatureSafetyDestination = false;
        hasTemperatureBiasedDestination = false;
        temperature = item?.itemMods?.GetMod_ByID<Mod_Temperature>(Mod_Temperature.ModuleId);
        HasReachedTarget = true;
        CanMove = true;
        NavigationAgent.Stop(clearDestination: true);
        if (health != null) health.DeathStarted -= HandleActorDeath;
        health = item?.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
        if (health != null)
        {
            health.DeathStarted += HandleActorDeath;
            if (!CanActivelyMove) HandleActorDeath(health);
        }
    }

    public override void ModUpdate(float deltaTime)
    {
        if (NavigationAgent == null)
            return;

        if (!CanActivelyMove)
        {
            if (CanMove || hasDestination || hasTemperatureSafetyDestination)
                HandleActorDeath(health);
            ApplyNavigationVelocity(Vector2.zero, deltaTime);
            return;
        }

        if (hasTemperatureSafetyDestination)
            CanMove = true;
        else if (target != null)
            SetDestination(target.position);

        NavigationAgent.MaxSpeed = SpeedValue;
        NavigationAgent.CanMove = CanMove && hasDestination;
        NavigationAgent.Tick(deltaTime);

        if (!CanMove || !hasDestination)
        {
            HasReachedTarget = true;
            return;
        }

        HasReachedTarget = NavigationAgent.ReachedDestination;
    }

    /// <summary>提交不限制路径总代价的普通移动目标。</summary>
    public void SetDestination(Vector2 destination, bool forceRepath = false)
    {
        if (!CanActivelyMove || hasTemperatureSafetyDestination)
            return;
        SubmitDestination(ResolveTemperatureBiasedDestination(destination), int.MaxValue, forceRepath);
    }

    /// <summary>提交只有路径总代价严格小于上限时才接受的移动目标。</summary>
    public WorldNavigationDestinationResult SetCostLimitedDestination(
        Vector2 destination,
        int maximumPathCostExclusive,
        bool forceRepath = false)
    {
        if (!CanActivelyMove || hasTemperatureSafetyDestination)
            return DestinationResult;
        SubmitDestination(ResolveTemperatureBiasedDestination(destination), Mathf.Max(1, maximumPathCostExclusive), forceRepath);
        return DestinationResult;
    }

    /// <summary>统一同步移动模块状态并把目标交给导航代理。</summary>
    private void SubmitDestination(
        Vector2 destination,
        int maximumPathCostExclusive,
        bool forceRepath)
    {
        if (!CanActivelyMove)
            return;

        bool isNewRequest =
            !hasDestination ||
            (destination - lastSubmittedDestination).sqrMagnitude >
            EffectiveDestinationChangeDistance * EffectiveDestinationChangeDistance;

        TargetPosition = destination;
        CanMove = true;
        HasReachedTarget = false;
        hasDestination = true;

        if (NavigationAgent == null)
            return;

        NavigationAgent.MaxSpeed = SpeedValue;
        if (maximumPathCostExclusive == int.MaxValue)
            NavigationAgent.SetDestination(destination, forceRepath);
        else
            NavigationAgent.SetCostLimitedDestination(
                destination,
                maximumPathCostExclusive,
                forceRepath);

        if (isNewRequest || forceRepath)
            lastSubmittedDestination = destination;
    }

    public void StopMovement()
    {
        if (hasTemperatureSafetyDestination && CanTemperatureSafetyOverrideStop())
            return;
        StopMovementInternal();
    }

    private void StopMovementInternal()
    {
        CanMove = false;
        HasReachedTarget = true;
        hasDestination = false;
        // 显式停止同时撤销导航目标；模拟范围休眠另行保留原移动意图。
        NavigationAgent?.Stop(clearDestination: true);
    }

    #region 温度避险

    public void SetTemperatureSafetyDestination(Vector2 destination)
    {
        if (!CanActivelyMove)
            return;

        bool changed = !hasTemperatureSafetyDestination ||
            WorldTopologyRuntime.SqrDistance(TargetPosition, destination) >
            EffectiveDestinationChangeDistance * EffectiveDestinationChangeDistance;
        hasTemperatureSafetyDestination = true;
        hasTemperatureBiasedDestination = false;
        if (changed)
            SubmitDestination(destination, int.MaxValue, forceRepath: true);
    }

    public void ClearTemperatureSafetyDestination()
    {
        if (!hasTemperatureSafetyDestination)
            return;

        hasTemperatureSafetyDestination = false;
        hasTemperatureBiasedDestination = false;
        StopMovementInternal();
    }

    private Vector2 ResolveTemperatureBiasedDestination(Vector2 destination)
    {
        temperature ??= item?.itemMods?.GetMod_ByID<Mod_Temperature>(Mod_Temperature.ModuleId);
        if (temperature == null || !temperature.HasTemperatureDangerMemory)
        {
            hasTemperatureBiasedDestination = false;
            return destination;
        }

        // 同一个普通目标按整段保持偏向，不能因逐帧改变原点反复刷新寻路。
        if (!hasTemperatureBiasedDestination || HasReachedTarget ||
            DestinationResult == WorldNavigationDestinationResult.Failed ||
            WorldTopologyRuntime.SqrDistance(temperatureBiasedRequest, destination) >
            EffectiveDestinationChangeDistance * EffectiveDestinationChangeDistance)
        {
            Vector2 current = rb != null ? rb.position : (Vector2)item.transform.position;
            temperatureBiasedRequest = destination;
            temperatureBiasedDestination = temperature.BiasTemperatureMovementDestination(current, destination);
            hasTemperatureBiasedDestination = WorldTopologyRuntime.SqrDistance(
                temperatureBiasedDestination, destination) > 0.0001f;
        }
        return temperatureBiasedDestination;
    }

    #endregion

    #region 模拟范围休眠

    /// <summary>由 ItemMgr 越界回调取消寻路与残余速度，保留原移动意图。</summary>
    public void OnSimulationRangePaused()
    {
        NavigationAgent?.Stop();
        if (rb != null) rb.velocity = Vector2.zero;
    }

    /// <summary>重入范围后按旧目标重新申请路径，不补走暂停期间的位移。</summary>
    public void OnSimulationRangeResumed()
    {
        if (!CanActivelyMove || !CanMove || !hasDestination || NavigationAgent == null)
            return;
        NavigationAgent.SetDestination(lastSubmittedDestination, forceRepath: true);
        HasReachedTarget = false;
    }

    #endregion

    public void ForceRepath()
    {
        NavigationAgent?.ForceRepath();
    }

    public override void Move(Vector2 targetPosition, float deltaTime = 0f)
    {
        SetDestination(targetPosition);
    }

    protected override void OnUnload()
    {
        if (health != null) health.DeathStarted -= HandleActorDeath;
        health = null;
        hasTemperatureSafetyDestination = false;
        hasTemperatureBiasedDestination = false;
        temperature = null;
        StopMovementInternal();
        base.OnUnload();
    }

    #region 死亡移动边界

    private void HandleActorDeath(Mod_DamageReceiver receiver)
    {
        // 死亡撤销普通与温度避险目标，保留原有被动位移规则。
        hasTemperatureSafetyDestination = false;
        hasTemperatureBiasedDestination = false;
        StopMovementInternal();
        ApplyNavigationVelocity(Vector2.zero, 0f);
    }

    #endregion

    private bool CanTemperatureSafetyOverrideStop()
    {
        if (item == null || item.DestructionHandled)
            return false;

        return CanActivelyMove;
    }
}
