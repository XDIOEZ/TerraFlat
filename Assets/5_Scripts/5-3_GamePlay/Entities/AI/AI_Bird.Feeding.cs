using FlatWorld.Combat;
using FlatWorld.Networking;
using UnityEngine;

public sealed partial class AI_Bird
{
    #region 种子觅食与受伤逃离

    [Tooltip("地面食物的统一 Tag；鸟和海鸥继承同一策略。")]
    public string forageTag = "Seed";
    [Min(0.1f)] public float forageRadius = 8f;
    [Min(0.1f)] public float peckRange = 0.55f;
    [Min(0.1f)] public float peckSeconds = 0.8f;
    [Min(0.1f)] public float hurtEscapeSeconds = 4f;
    private Mod_Food food;
    private DroppedItemHandle forageTarget;
    private float forageScanRemaining, peckRemaining, escapeRemaining;
    private Vector2 escapeDirection;

    private void ResetForaging()
    {
        forageTarget = default;
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
            if (!DroppedItemService.TryFindNearestTagged(body.position, forageRadius, forageTag, out forageTarget) ||
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
            if (distance <= peckRange) BeginLanding();
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
        if (!food.TryEatDroppedFood(forageTarget, forageTag, peckRange)) forageTarget = default;
        return true;
    }

    private void HandleBirdDamage(DamageReceiverDamageInfo info)
    {
        if (!loaded || !GameNetwork.HasStateAuthority || !IsAlive || info == null || info.DamageValue <= 0f) return;
        if (!DamageThreatOrigin.TryResolve(info, out Vector2 origin)) return;
        Vector2 away = WorldTopologyRuntime.ShortestDelta(origin, body.position);
        if (away.sqrMagnitude < 0.0001f) away = UnityEngine.Random.insideUnitCircle;
        escapeDirection = away.sqrMagnitude > 0.0001f ? away.normalized : Vector2.right;
        escapeRemaining = hurtEscapeSeconds;
        forageTarget = default;
        // 已在巡航也重新选择逃离方向，不继续沿受击前的随机目的地飞行。
        if (state.Phase == BirdFlightPhase.Flying) EnterPhase(BirdFlightPhase.Flying);
        else BeginTakeoff();
        SetWanderTarget(WorldTopologyRuntime.NormalizePosition(body.position + escapeDirection * flightWanderRadius));
    }

    private bool TickEscape(float deltaTime)
    {
        if (escapeRemaining <= 0f) return false;
        escapeRemaining = Mathf.Max(0f, escapeRemaining - deltaTime);
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
