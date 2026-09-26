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
    [Min(0f)] public float fleeSafeDistance = 30f;
    [Tooltip("每次逃离时选择的单次移动目标距离。"), Min(0.1f)] public float fleeRunDistance = 30f;
    [Min(0.1f)] public float forageRadius = 8f;
    [Min(0.1f)] public float peckRange = 0.55f;
    [Min(0.1f)] public float peckSeconds = 0.8f;
    [Min(0.1f)] public float hurtEscapeSeconds = 4f;
    private Mod_Food food;
    private Mod_ItemDetector threatDetector;
    private readonly System.Collections.Generic.List<string> threatTags = new() { "Predator", "Wolf" };
    private float vigilanceRemaining;
    private long lastVigilanceVersion; // 上次已处理的感知结果版本。
    private DroppedItemHandle forageTarget;
    private string forageTargetTag;
    private float forageScanRemaining, peckRemaining, escapeRemaining;
    private Vector2 escapeDirection;
    private Vector2 escapeDestination; // 当前受惊周期锁定的单次逃离目标。
    private Item escapeThreat; // 当前逃离周期对应的感知威胁。

    private void ResetForaging()
    {
        forageTarget = default;
        forageTargetTag = null;
        vigilanceRemaining = 0f;
        lastVigilanceVersion = threatDetector?.AppliedVersion ?? 0;
        forageScanRemaining = peckRemaining = escapeRemaining = 0f;
        escapeDirection = Vector2.zero;
        escapeDestination = Vector2.zero;
        escapeThreat = null;
    }

    /// <summary>逃跑优先于食物；未吃饱时，附近种子优先于随机巡航和地面漫游。</summary>
    private bool TickForaging(float deltaTime)
    {
        if (state.Phase == BirdFlightPhase.RunUp) return false;
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
            TickTakeoff(deltaTime);
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

    /// <summary>每 0.4 秒提交感知，结果一应用就评估玩家威胁并打断当前行为。</summary>
    private void TickVigilance(float deltaTime)
    {
        vigilanceRemaining -= deltaTime;
        if (vigilanceRemaining <= 0f)
        {
            vigilanceRemaining = 0.4f;
            threatDetector.RequestDetectorUpdate();
        }
        long appliedVersion = threatDetector.AppliedVersion;
        if (appliedVersion <= lastVigilanceVersion) return;
        lastVigilanceVersion = appliedVersion;
        if (!TryGetNearbyThreat(escapeRemaining > 0f ? fleeSafeDistance : fleeTriggerDistance, out Item threat)) return;
        StartEscape(threat);
    }

    /// <summary>
    /// 从检测器已经应用的快照里寻找真实可见威胁。逐个检查而不是只取“最近标签项”，
    /// 避免最近目标被墙挡住时漏掉稍远但可见的玩家或捕食者。
    /// </summary>
    private bool TryGetNearbyThreat(float baseRadius, out Item threat)
    {
        threat = null;
        float closestDistanceSqr = float.PositiveInfinity;
        System.Collections.Generic.List<Item> detected = threatDetector.CurrentItemsInArea;
        for (int itemIndex = 0; itemIndex < detected.Count; itemIndex++)
        {
            Item candidate = detected[itemIndex];
            if (candidate == null || candidate == item || !IsBirdThreat(candidate))
                continue;

            if (!AIFleeUtility.IsWithinEscapeRange(body.position, candidate, threatDetector, baseRadius))
                continue;

            float distanceSqr = WorldTopologyRuntime.SqrDistance(body.position, candidate.transform.position);
            if (distanceSqr >= closestDistanceSqr)
                continue;

            closestDistanceSqr = distanceSqr;
            threat = candidate;
        }
        return threat != null;
    }

    private bool IsBirdThreat(Item candidate)
    {
        if (candidate is Player || candidate.CompareTag("Player"))
            return true;
        if (candidate.itemData?.Tags == null)
            return false;

        System.Collections.Generic.List<string> tags = candidate.itemData.Tags;
        for (int threatIndex = 0; threatIndex < threatTags.Count; threatIndex++)
        {
            string threatTag = threatTags[threatIndex];
            for (int tagIndex = 0; tagIndex < tags.Count; tagIndex++)
                if (tags[tagIndex] == threatTag)
                    return true;
        }
        return false;
    }

    private void HandleBirdDamage(DamageReceiverDamageInfo info)
    {
        if (!loaded || !GameNetwork.HasStateAuthority || !IsAlive || info == null || info.DamageValue <= 0f) return;
        if (!DamageThreatOrigin.TryResolve(info, out Vector2 origin)) return;
        StartEscape(origin);
    }

    /// <summary>感知威胁与受击共用逃离入口，休息锁定时只能在地面行走。</summary>
    private void StartEscape(Item threat)
    {
        if (threat == null)
            return;

        if (escapeRemaining > 0f && escapeThreat == threat)
        {
            escapeRemaining = hurtEscapeSeconds;
            return;
        }

        StartEscape(threat.transform.position, threat);
    }

    private void StartEscape(Vector2 origin)
    {
        StartEscape(origin, null);
    }

    private void StartEscape(Vector2 origin, Item threat)
    {
        bool wasEscaping = escapeRemaining > 0f;
        ResetFatigueLanding();
        if (state.Phase == BirdFlightPhase.Ground && !wasEscaping)
            mover.StopMovement();

        escapeThreat = threat;
        escapeRemaining = hurtEscapeSeconds;
        Vector3 target = AIFleeUtility.ResolveEscapeDestination(
            body.position,
            origin,
            fleeRunDistance,
            UnityEngine.Random.insideUnitCircle);
        escapeDestination = target;
        Vector2 away = WorldTopologyRuntime.ShortestDelta(body.position, escapeDestination);
        escapeDirection = away.sqrMagnitude > 0.0001f ? away.normalized : Vector2.right;
        forageTarget = default;
        // 已在巡航也重新选择逃离方向，不继续沿受击前的随机目的地飞行。
        if (state.Phase == BirdFlightPhase.Ground || state.Phase == BirdFlightPhase.Landing) BeginTakeoff();
        SetWanderTarget(escapeDestination);
    }

    private bool TickEscape(float deltaTime)
    {
        if (escapeRemaining <= 0f) return false;
        escapeRemaining = Mathf.Max(0f, escapeRemaining - deltaTime);
        if (state.Phase == BirdFlightPhase.Ground)
        {
            if (CanLand(escapeDestination)) mover.SetDestination(escapeDestination);
            return true;
        }
        if (state.Phase == BirdFlightPhase.RunUp)
        {
            TickRunUp(deltaTime);
            return true;
        }
        if (state.Phase == BirdFlightPhase.Landing)
        {
            if (state.Elapsed >= transitionDuration) CompleteLanding();
            return true;
        }
        if (state.Phase == BirdFlightPhase.TakingOff)
        {
            TickTakeoff(deltaTime);
            return true;
        }
        FlyTowards(escapeDestination, deltaTime);
        return true;
    }

    private void FlyTowards(Vector2 target, float deltaTime)
    {
        mover.StopMovement();
        body.velocity = Vector2.zero;
        Vector2 displacement = WorldTopologyRuntime.ShortestDelta(body.position, target);
        MoveFlightStep(displacement, flightSpeed, deltaTime);
    }

    #endregion
}
