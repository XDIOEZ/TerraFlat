using System.Collections.Generic;
using UnityEngine;

/// <summary>警惕触发、移动目标选择、最后位置确认与真实蜇刺分属具名行为边界。</summary>
public sealed partial class Mod_BeeBehavior
{
    #region 目标采样与警惕
    private const int AllyHelpRadiusCells = 8; // 以受击蜂为中心横纵各八格，即十七乘十七格。
    private const float StingColliderClearance = 0.05f; // 即使中心距离配置偏小，也要在实体碰撞前进入蜇刺范围。

    private sealed class MotionSample
    {
        public Vector2 Position; // 上一次采样的位置。
        public bool Moving; // 本次采样中目标是否主动移动。
        public int Generation; // 清除离开锁定范围的旧记录。
    }

    private readonly List<Item> nearbyCreatures = new(); // ItemMgr 感知查询缓冲。
    private readonly HashSet<Item> nearbyDedupe = new(); // 循环镜像去重。
    private readonly Dictionary<Item, MotionSample> motionSamples = new(); // 只比较目标自身位置，避免蜂移动误判静物。
    private readonly Dictionary<Item, float> overlapSeconds = new(); // 同格连续警惕时长。
    private readonly List<Item> expiredSamples = new(); // 离开范围的采样记录。
    private Item nearestMoving; // 愤怒状态最近的移动候选。
    private Item lockedTarget; // 已追击者停下后仍可继续蜇刺。
    private Item hiveDefenseTarget; // 蜂巢受击后最高优先级的攻击者/持有者。
    private Vector2 lastSeenPosition; // 失去目标时前往确认的位置。
    private bool searchingLastPosition; // 正在确认目标最后出现点。
    private Item lastSeenTarget; // 搜索期间可重新锁定原目标，包括已经停下的生物。
    private float lostTargetAngerRemaining; // 丢失目标后剩余的愤怒搜索秒数。
    private float targetScanElapsed; // 位移采样累计游戏秒。
    private int scanGeneration; // 目标采样轮次。
    private float colonyAlertAnger; // 蜂巢领地共享警戒换算出的当前愤怒值，不持久化。

    /// <summary>蜂巢把玩家在领地内的停留进度注入本蜂；离开领地时传零清除共享警戒源。</summary>
    public void SetColonyTerritoryAlert(float staySeconds, float thresholdSeconds)
    {
        if (thresholdSeconds <= 0f)
            throw new System.ArgumentOutOfRangeException(nameof(thresholdSeconds));
        colonyAlertAnger = Mathf.Clamp01(staySeconds / thresholdSeconds) * AngerMaximum;
    }

    public bool HasHiveDefenseTarget => hiveDefenseTarget != null;

    /// <summary>只有正常巡逻中的蜜蜂受击才呼叫同巢支援，觅食、返巢和战斗状态不会连锁广播。</summary>
    private void HandleBeeDamageReceived(DamageReceiverDamageInfo damageInfo)
    {
        if (damageInfo == null || damageInfo.DamageValue <= 0f || colony == null || !IsPatrollingForHelpCall())
            return;

        Item attacker = ResolveAggressor(damageInfo.Attacker);
        if (!IsLivingCreature(attacker))
            return;

        ForceHiveDefenseTarget(attacker);
        float queryRadius = (AllyHelpRadiusCells + 1f) * 1.414214f;
        ItemMgr.Instance.QueryItemsInCircleNonAlloc(item.transform.position, queryRadius, ~0, item,
            nearbyCreatures, nearbyDedupe);
        Vector2 originCell = GetWorldCellCenter(item.transform.position);
        foreach (Item candidate in nearbyCreatures)
        {
            Mod_BeeBehavior bee = candidate?.itemMods?.GetMod_ByID<Mod_BeeBehavior>(ModuleId);
            if (bee == null || !ReferenceEquals(bee.colony, colony) || !bee.CanAnswerAllyHelpCall())
                continue;

            Vector2 delta = WorldTopologyRuntime.ShortestDelta(
                originCell,
                GetWorldCellCenter(candidate.transform.position));
            if (Mathf.Abs(delta.x) > AllyHelpRadiusCells || Mathf.Abs(delta.y) > AllyHelpRadiusCells)
                continue;

            bee.ForceHiveDefenseTarget(attacker);
        }
    }

    /// <summary>武器、投射物等沿 Owner 链还原成真正攻击者。</summary>
    private static Item ResolveAggressor(Item source)
    {
        Item current = source;
        for (int depth = 0; depth < 8 && current?.Owner != null && current.Owner != current; depth++)
            current = current.Owner;
        return current;
    }

    /// <summary>呼叫范围按整格计算，循环世界也使用规范化后的最近格中心。</summary>
    private static Vector2 GetWorldCellCenter(Vector2 position)
    {
        Vector2 normalized = WorldTopologyRuntime.NormalizePosition(position);
        return new Vector2(Mathf.Floor(normalized.x) + 0.5f, Mathf.Floor(normalized.y) + 0.5f);
    }

    /// <summary>与调试面板的“巡航”状态保持一致，避免觅食中的蜜蜂受击拉响蜂群警报。</summary>
    private bool IsPatrollingForHelpCall()
    {
        return !state.Orphaned &&
               !nightSleepRequested &&
               !state.Angry &&
               !state.ReturningHome &&
               !HasActiveForageTarget &&
               state.Satiety >= ForageBelow;
    }

    /// <summary>正在落地采食的蜜蜂不响应同伴呼救，给玩家保留逐只潜行处理的窗口。</summary>
    private bool CanAnswerAllyHelpCall()
    {
        if (colony == null || state.Orphaned || nightSleepRequested || bird == null)
            return false;
        if (!HasActiveForageTarget || bird.Phase != BirdFlightPhase.Ground)
            return true;
        return WorldTopologyRuntime.SqrDistance(item.transform.position, foragePosition) >
               ForageLandingDistance * ForageLandingDistance;
    }

    /// <summary>蜂巢受击或同伴呼救后强制锁定真实攻击者。</summary>
    public void ForceHiveDefenseTarget(Item target)
    {
        if (!IsLivingCreature(target))
            return;
        ClearForageTarget();
        hiveDefenseTarget = target;
        lockedTarget = target;
        lastSeenPosition = target.transform.position;
        searchingLastPosition = false;
        lastSeenTarget = null;
        lostTargetAngerRemaining = 0f;
        state.Anger = AngerMaximum;
        state.Angry = true;
        state.ReturningHome = false;
        overlapSeconds.Clear();
    }

    /// <summary>重新绑定蜂巢或目标死亡后移除护巢强制目标。</summary>
    private void ClearHiveDefenseTarget()
    {
        hiveDefenseTarget = null;
    }

    /// <summary>护巢目标优先于夜间睡眠、领地警戒和普通移动目标。</summary>
    private bool TickHiveDefenseFlight(float flightSeconds)
    {
        if (hiveDefenseTarget == null)
            return false;
        if (!CanKeepLockedTarget(hiveDefenseTarget))
        {
            BeginLastPositionSearch();
            return false;
        }

        lockedTarget = hiveDefenseTarget;
        lastSeenPosition = hiveDefenseTarget.transform.position;
        searchingLastPosition = false;
        ChaseLockedTarget(flightSeconds);
        return true;
    }

    /// <summary>夜间普通归巢清理本地警戒和旧追击；蜂巢受击强制目标单独保留。</summary>
    private void ClearLocalCombatForSleep()
    {
        nearestMoving = null;
        lockedTarget = null;
        searchingLastPosition = false;
        lastSeenTarget = null;
        lostTargetAngerRemaining = 0f;
        overlapSeconds.Clear();
        motionSamples.Clear();
        expiredSamples.Clear();
        targetScanElapsed = 0f;
        colonyAlertAnger = 0f;
        if (hiveDefenseTarget == null)
        {
            state.Anger = 0f;
            state.Angry = false;
        }
    }

    /// <summary>每隔短时间采样目标自身的位移；静止生物不会因蜜蜂接近而被当作移动目标。</summary>
    private void TickTargetAwareness(float gameSeconds)
    {
        targetScanElapsed += gameSeconds;
        if (targetScanElapsed < TargetScanInterval)
            return;
        float sampleSeconds = targetScanElapsed;
        targetScanElapsed = 0f;
        scanGeneration++;
        nearestMoving = null;
        float nearestDistance = float.PositiveInfinity;
        ItemMgr.Instance.QueryItemsInCircleNonAlloc(item.transform.position, AngryLockRadius, ~0, item,
            nearbyCreatures, nearbyDedupe);
        foreach (Item candidate in nearbyCreatures)
        {
            if (!IsLivingCreature(candidate) || !detector.HasLineOfSight(candidate))
                continue;
            float distance = WorldTopologyRuntime.SqrDistance(item.transform.position, candidate.transform.position);
            if (!motionSamples.TryGetValue(candidate, out MotionSample sample))
            {
                sample = new MotionSample { Position = candidate.transform.position };
                motionSamples.Add(candidate, sample);
            }
            else
            {
                sample.Moving = WorldTopologyRuntime.SqrDistance(sample.Position, candidate.transform.position) > 0.0001f;
                sample.Position = candidate.transform.position;
            }
            sample.Generation = scanGeneration;

            if (sample.Moving && distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestMoving = candidate;
            }
            if (!state.Angry && SharesWorldCell(candidate))
            {
                overlapSeconds.TryGetValue(candidate, out float seconds);
                seconds += sampleSeconds;
                overlapSeconds[candidate] = seconds;
            }
        }

        expiredSamples.Clear();
        foreach (KeyValuePair<Item, MotionSample> pair in motionSamples)
            if (pair.Value.Generation != scanGeneration)
                expiredSamples.Add(pair.Key);
        foreach (Item candidate in expiredSamples)
            motionSamples.Remove(candidate);
        expiredSamples.Clear();
        foreach (Item candidate in overlapSeconds.Keys)
            if (!motionSamples.TryGetValue(candidate, out MotionSample sample) ||
                sample.Generation != scanGeneration || !SharesWorldCell(candidate))
                expiredSamples.Add(candidate);
        foreach (Item candidate in expiredSamples)
            overlapSeconds.Remove(candidate);
        if (!state.Angry)
        {
            float highestStay = 0f;
            foreach (float seconds in overlapSeconds.Values)
                highestStay = Mathf.Max(highestStay, seconds);
            float localAlertAnger = Mathf.Min(AngerMaximum, highestStay / AlertSeconds * AngerMaximum);
            state.Anger = Mathf.Max(localAlertAnger, colonyAlertAnger);
            if (state.Anger >= AngerMaximum)
                EnterAnger();
        }
    }

    /// <summary>玩家或正式 Actor 必须存活且与蜜蜂敌对，静态世界物品不是生物目标。</summary>
    private bool IsLivingCreature(Item candidate)
    {
        if (candidate == null || candidate.DestructionHandled || !candidate.gameObject.activeInHierarchy ||
            !FactionRelationService.CanAttack(item, candidate))
            return false;
        Mod_DamageReceiver health = candidate.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
        if (health == null || health.Hp <= 0f)
            return false;
        return candidate is Player ||
            (GameRes.Instance.TryGetItemDefinition(candidate.itemData.IDName, out RuntimeItemDefinition definition) &&
             definition.IsActor);
    }

    /// <summary>同格计时使用规范化世界格，不依赖贴图或碰撞体。</summary>
    private bool SharesWorldCell(Item candidate)
    {
        Vector2 self = WorldTopologyRuntime.NormalizePosition(item.transform.position);
        Vector2 other = WorldTopologyRuntime.NormalizePosition(candidate.transform.position);
        return Mathf.FloorToInt(self.x) == Mathf.FloorToInt(other.x) &&
               Mathf.FloorToInt(self.y) == Mathf.FloorToInt(other.y);
    }

    /// <summary>蜂巢广播或同格警戒统一进入十分愤怒状态。</summary>
    public void EnterAnger()
    {
        if (HasActiveForageTarget)
            CompleteForageVisit();
        state.Anger = AngerMaximum;
        state.Angry = true;
        overlapSeconds.Clear();
        searchingLastPosition = false;
        lastSeenTarget = null;
        lostTargetAngerRemaining = 0f;
    }
    #endregion

    #region 愤怒追击与蜇刺
    /// <summary>最近的移动生物优先；原目标停下但无其他移动者时继续追击。</summary>
    private void TickAngryFlight(float flightSeconds, float gameSeconds)
    {
        if (nearestMoving != null && CanKeepLockedTarget(nearestMoving))
            LockMovingTarget(nearestMoving);
        else if (searchingLastPosition && CanKeepLockedTarget(lastSeenTarget))
            LockMovingTarget(lastSeenTarget);
        if (lockedTarget != null)
        {
            if (CanKeepLockedTarget(lockedTarget))
            {
                lastSeenPosition = lockedTarget.transform.position;
                ChaseLockedTarget(flightSeconds);
                return;
            }
            BeginLastPositionSearch();
        }
        if (searchingLastPosition)
        {
            lostTargetAngerRemaining = Mathf.Max(0f, lostTargetAngerRemaining - gameSeconds);
            if (lostTargetAngerRemaining <= 0f)
            {
                searchingLastPosition = false;
                lastSeenTarget = null;
                state.Anger = 0f;
                state.Angry = false;
                return;
            }
            if (WorldTopologyRuntime.SqrDistance(item.transform.position, lastSeenPosition) >
                StingDistance * StingDistance)
                bird.FlyTo(lastSeenPosition, flightSeconds);
            return; // 到达最后位置仍等待，不能提前结束十秒搜索。
        }
        // 尚未锁定过目标的愤怒巡航沿用缓慢衰减，丢失目标则由十秒搜索负责结束。
        state.Anger = Mathf.Max(0f, state.Anger - AngerDecayPerSecond * gameSeconds);
        if (state.Anger <= 0f)
            state.Angry = false;
        // 愤怒巡航仍限制在蜂巢领地内，但保持正常飞行速度，不表现为悠闲巡逻。
        if (colony != null)
            TickTerritoryPatrol(flightSeconds, state.Angry ? 1f : PatrolFlightSpeedMultiplier);
        else
            bird.WanderAroundHome(flightSeconds);
    }

    /// <summary>普通追击和护巢追击共用丢失目标后的愤怒记忆，不再读取范围外目标的新位置。</summary>
    private void BeginLastPositionSearch()
    {
        lastSeenTarget = lockedTarget;
        lockedTarget = null;
        hiveDefenseTarget = null;
        searchingLastPosition = true;
        lostTargetAngerRemaining = LostTargetAngerSeconds;
    }

    /// <summary>更近的移动目标替换原目标；激怒者没有优先权。</summary>
    private void LockMovingTarget(Item target)
    {
        lockedTarget = target;
        lastSeenPosition = target.transform.position;
        searchingLastPosition = false;
        lastSeenTarget = null;
        lostTargetAngerRemaining = 0f;
    }

    /// <summary>愤怒时三格内且视线可达的已锁定目标，停下后仍保持追击资格。</summary>
    private bool CanKeepLockedTarget(Item target)
    {
        return IsLivingCreature(target) && detector.HasLineOfSight(target) &&
            WorldTopologyRuntime.SqrDistance(item.transform.position, target.transform.position) <=
            AngryLockRadius * AngryLockRadius;
    }

    /// <summary>靠近目标后按独立冷却蜇刺，实际生命变动仍走统一受击链。</summary>
    private void ChaseLockedTarget(float flightSeconds)
    {
        Vector2 position = lockedTarget.transform.position;
        Mod_DamageReceiver receiver = lockedTarget.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
        if (receiver == null)
            return;
        if (!IsInsideStingReach(receiver, position))
        {
            bird.FlyTo(position, flightSeconds);
            return;
        }
        if (stingRemaining > 0f)
            return;
        receiver.Hurt(this);
        stingRemaining = StingInterval;
    }

    /// <summary>优先使用配置距离，并保证实体碰撞体即将接触时一定已经具备攻击资格。</summary>
    private bool IsInsideStingReach(Mod_DamageReceiver receiver, Vector2 targetPosition)
    {
        if (WorldTopologyRuntime.SqrDistance(item.transform.position, targetPosition) <=
            StingDistance * StingDistance)
            return true;

        Collider2D selfCollider = item.GetComponent<Collider2D>();
        Collider2D targetCollider = receiver.GetComponent<Collider2D>();
        if (selfCollider == null || targetCollider == null || !selfCollider.enabled || !targetCollider.enabled)
            return false;

        ColliderDistance2D separation = selfCollider.Distance(targetCollider);
        return separation.isValid &&
               (separation.isOverlapped || separation.distance <= StingColliderClearance);
    }

    /// <summary>卸载和池复用时释放所有旧目标与位移采样。</summary>
    private void ResetBeeCombat()
    {
        nearbyCreatures.Clear();
        nearbyDedupe.Clear();
        motionSamples.Clear();
        overlapSeconds.Clear();
        expiredSamples.Clear();
        nearestMoving = null;
        lockedTarget = null;
        hiveDefenseTarget = null;
        lastSeenPosition = default;
        searchingLastPosition = false;
        lastSeenTarget = null;
        lostTargetAngerRemaining = 0f;
        targetScanElapsed = 0f;
        scanGeneration = 0;
        colonyAlertAnger = 0f;
    }
    #endregion
}
