using System.Collections.Generic;
using UnityEngine;

/// <summary>警惕触发、移动目标选择、最后位置确认与真实蜇刺分属具名行为边界。</summary>
public sealed partial class Mod_BeeBehavior
{
    #region 目标采样与警惕
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

    /// <summary>蜂巢受击后强制锁定真实攻击者；武器和投射物由蜂巢侧提前解析为持有者。</summary>
    public void ForceHiveDefenseTarget(Item target)
    {
        if (!IsLivingCreature(target))
            return;
        hiveDefenseTarget = target;
        lockedTarget = target;
        lastSeenPosition = target.transform.position;
        searchingLastPosition = false;
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
        if (!IsLivingCreature(hiveDefenseTarget))
        {
            hiveDefenseTarget = null;
            lockedTarget = null;
            state.Anger = 0f;
            state.Angry = false;
            searchingLastPosition = false;
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
    }
    #endregion

    #region 愤怒追击与蜇刺
    /// <summary>最近的移动生物优先；原目标停下但无其他移动者时继续追击。</summary>
    private void TickAngryFlight(float flightSeconds, float gameSeconds)
    {
        if (nearestMoving != null && CanKeepLockedTarget(nearestMoving))
            LockMovingTarget(nearestMoving);
        if (lockedTarget != null)
        {
            if (CanKeepLockedTarget(lockedTarget))
            {
                lastSeenPosition = lockedTarget.transform.position;
                ChaseLockedTarget(flightSeconds);
                return;
            }
            lockedTarget = null;
            searchingLastPosition = true;
        }
        if (searchingLastPosition)
        {
            if (WorldTopologyRuntime.SqrDistance(item.transform.position, lastSeenPosition) >
                StingDistance * StingDistance)
            {
                bird.FlyTo(lastSeenPosition, flightSeconds);
                return;
            }
            searchingLastPosition = false;
        }
        // 只有无目标且最后位置确认完成，愤怒值才以每秒零点一下降。
        state.Anger = Mathf.Max(0f, state.Anger - AngerDecayPerSecond * gameSeconds);
        if (state.Anger <= 0f)
            state.Angry = false;
        // 愤怒巡航仍限制在蜂巢领地内，但保持正常飞行速度，不表现为悠闲巡逻。
        TickTerritoryPatrol(flightSeconds, state.Angry ? 1f : PatrolFlightSpeedMultiplier);
    }

    /// <summary>更近的移动目标替换原目标；激怒者没有优先权。</summary>
    private void LockMovingTarget(Item target)
    {
        lockedTarget = target;
        lastSeenPosition = target.transform.position;
        searchingLastPosition = false;
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
        if (WorldTopologyRuntime.SqrDistance(item.transform.position, position) >
            StingDistance * StingDistance)
        {
            bird.FlyTo(position, flightSeconds);
            return;
        }
        bird.FlyTo(position, flightSeconds);
        if (stingRemaining > 0f)
            return;
        Mod_DamageReceiver receiver = lockedTarget.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
        if (receiver != null)
            receiver.Hurt(this);
        stingRemaining = StingInterval;
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
        targetScanElapsed = 0f;
        scanGeneration = 0;
        colonyAlertAnger = 0f;
    }
    #endregion
}
