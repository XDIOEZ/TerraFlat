using FlatWorld.Combat;
using FlatWorld.Networking;
using UnityEngine;

public sealed partial class AI_Bird
{
    #region 种子觅食与受伤逃离

    [Tooltip("地面食物的统一 Tag；鸟和海鸥继承同一策略。")]
    public string forageTag = "Seed";
    public string[] additionalForageTags = { "Berry", "Fruit" };
    [Min(0f)] public float fleeTriggerDistance = 12f; // 原谨慎型鸟类 6 格的两倍。
    [Min(0f)] public float fleeSafeDistance = 20f; // 原安全距离 10 格的两倍。
    [Min(0.1f)] public float forageRadius = 8f;
    [Min(0.1f)] public float peckRange = 0.55f;
    [Min(0.1f)] public float peckSeconds = 0.8f;
    [Min(0.1f)] public float hurtEscapeSeconds = 4f;
    private Mod_Food food;
    private Mod_ItemDetector threatDetector;
    private readonly System.Collections.Generic.List<string> threatTags = new() { "Predator", "Wolf" };
    private float vigilanceRemaining;
    private DroppedItemHandle forageTarget;
    private string forageTargetTag;
    private float forageScanRemaining, peckRemaining, escapeRemaining;
    private Vector2 escapeDirection;

    private void ResetForaging()
    {
        forageTarget = default;
        forageTargetTag = null;
        vigilanceRemaining = 0f;
        forageScanRemaining = peckRemaining = escapeRemaining = 0f;
        escapeDirection = Vector2.zero;
    }

    /// <summary>逃跑优先于食物；未吃饱时，附近种子优先于随机巡航和地面漫游。</summary>
    private bool TickForaging(float deltaTime)
    {
        if (food?.Data?.nutrition == null || food.Data.nutrition.GetFoodRate() >= 0.9999f)
        {
            forageTarget = default;
            return false;
        }
        forageScanRemaining -= deltaTime;
        if (!DroppedItemService.TryGetPickablePosition(forageTarget, out Vector2 target))
        {
            if (forageScanRemaining > 0f) return false;
            forageScanRemaining = 0.4f;
            if (!TryFindNearestFood() ||
                !DroppedItemService.TryGetPickablePosition(forageTarget, out target)) return false;
            peckRemaining = peckSeconds;
        }
        if (!CanLand(target) || WorldTopologyRuntime.ShortestDelta(body.position, target).sqrMagnitude > forageRadius * forageRadius)
        {
            forageTarget = default;
            return false;
        }
        float distance = WorldTopologyRuntime.ShortestDelta(body.position, target).magnitude;
        if (state.Phase == BirdFlightPhase.Landing)
        {
            mover.StopMovement();
            if (state.Elapsed >= transitionDuration) CompleteLanding();
            return true;
        }
        if (state.Phase == BirdFlightPhase.TakingOff)
        {
            if (state.Elapsed >= transitionDuration) EnterPhase(BirdFlightPhase.Flying);
            return true;
        }
        if (state.Phase == BirdFlightPhase.Flying)
        {
            // 食物所在格可落地不代表鸟当前脚下也可落地，先飞到可落脚的位置再收起翅膀。
            if (distance <= peckRange && CanLand(body.position)) BeginLanding();
            else FlyTowards(target, deltaTime);
            return true;
        }
        state.Elapsed = 0f;
        if (distance > peckRange)
        {
            peckRemaining = peckSeconds;
            mover.Speed.BaseValue = groundSpeed;
            mover.SetDestination(target);
            return true;
        }
        mover.StopMovement();
        peckRemaining -= deltaTime;
        if (peckRemaining > 0f) return true;
        peckRemaining = peckSeconds;
        if (!food.TryEatDroppedFood(forageTarget, forageTargetTag, peckRange)) forageTarget = default;
        return true;
    }

    /// <summary>同一优先级按实际距离选择种子或水果，不让多标签食物获得额外搜寻权重。</summary>
    private bool TryFindNearestFood()
    {
        forageTarget = default;
        forageTargetTag = null;
        float best = float.PositiveInfinity;
        ConsiderFoodTag(forageTag, ref best);
        if (additionalForageTags != null)
            foreach (string tag in additionalForageTags) ConsiderFoodTag(tag, ref best);
        return forageTargetTag != null;
    }

    private void ConsiderFoodTag(string tag, ref float best)
    {
        if (string.IsNullOrWhiteSpace(tag) ||
            !DroppedItemService.TryFindNearestTagged(body.position, forageRadius, tag, out DroppedItemHandle candidate) ||
            !DroppedItemService.TryGetPickablePosition(candidate, out Vector2 target)) return;
        float distance = WorldTopologyRuntime.SqrDistance(body.position, target);
        if (distance >= best || !CanLand(target)) return;
        best = distance;
        forageTarget = candidate;
        forageTargetTag = tag;
    }

    /// <summary>复用空间检测队列与目标感知倍率，每 0.4 秒更新一次，避免全场逐鸟逐帧扫描。</summary>
    private void TickVigilance(float deltaTime)
    {
        vigilanceRemaining -= deltaTime;
        if (vigilanceRemaining > 0f) return;
        vigilanceRemaining = 0.4f;
        threatDetector.RequestDetectorUpdate();
        Item threat = threatDetector.FindClosestItemByTags(threatTags, body.position, includeUnityPlayerTag: true);
        if (threat == null || threat == item || !threatDetector.HasLineOfSight(threat)) return;
        float radius = Mod_ItemDetector.CalculateEffectiveDetectionRadius(
            escapeRemaining > 0f ? fleeSafeDistance : fleeTriggerDistance, threat);
        if (WorldTopologyRuntime.SqrDistance(body.position, threat.transform.position) > radius * radius) return;
        StartEscape(threat.transform.position);
    }

    private void HandleBirdDamage(DamageReceiverDamageInfo info)
    {
        if (!loaded || !GameNetwork.HasStateAuthority || !IsAlive || info == null || info.DamageValue <= 0f) return;
        if (!DamageThreatOrigin.TryResolve(info, out Vector2 origin)) return;
        StartEscape(origin);
    }

    /// <summary>感知威胁与受击共用逃离入口，休息锁定时只能在地面行走。</summary>
    private void StartEscape(Vector2 origin)
    {
        Vector2 away = WorldTopologyRuntime.ShortestDelta(origin, body.position);
        if (away.sqrMagnitude < 0.0001f) away = UnityEngine.Random.insideUnitCircle;
        escapeDirection = away.sqrMagnitude > 0.0001f ? away.normalized : Vector2.right;
        escapeRemaining = hurtEscapeSeconds;
        forageTarget = default;
        // 已在巡航也重新选择逃离方向，不继续沿受击前的随机目的地飞行。
        if (state.Phase == BirdFlightPhase.Ground || state.Phase == BirdFlightPhase.Landing) BeginTakeoff();
        SetWanderTarget(WorldTopologyRuntime.NormalizePosition(body.position + escapeDirection * flightWanderRadius));
    }

    private bool TickEscape(float deltaTime)
    {
        if (escapeRemaining <= 0f) return false;
        escapeRemaining = Mathf.Max(0f, escapeRemaining - deltaTime);
        if (state.Phase == BirdFlightPhase.Ground)
        {
            Vector2 destination = WorldTopologyRuntime.NormalizePosition(body.position + escapeDirection * groundWanderRadius);
            if (CanLand(destination)) mover.SetDestination(destination);
            return true;
        }
        if (state.Phase == BirdFlightPhase.Landing)
        {
            if (state.Elapsed >= transitionDuration) CompleteLanding();
            return true;
        }
        if (state.Phase == BirdFlightPhase.TakingOff && state.Elapsed >= transitionDuration)
            EnterPhase(BirdFlightPhase.Flying);
        FlyTowards(WorldTopologyRuntime.NormalizePosition(body.position + escapeDirection * flightWanderRadius), deltaTime);
        return true;
    }

    private void FlyTowards(Vector2 target, float deltaTime)
    {
        mover.StopMovement();
        body.velocity = Vector2.zero;
        Vector2 displacement = WorldTopologyRuntime.ShortestDelta(body.position, target);
        Vector2 next = WorldTopologyRuntime.NormalizePosition(body.position + Vector2.ClampMagnitude(displacement, flightSpeed * deltaTime));
        if (!flightNavigation.CanTraverse(body.position, next)) return;
        body.position = next;
        ItemMgr.Instance?.NotifyRuntimeItemMoved(item);
    }

    #endregion
}
