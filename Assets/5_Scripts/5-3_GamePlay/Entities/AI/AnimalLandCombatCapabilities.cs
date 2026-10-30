using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>通用动物技能与普通攻击窗口，独立于目标选择和移动策略。</summary>
public sealed class AnimalCombatCapability : AnimalBehaviorTreeCapability
{
    #region 攻击时序与技能
    private readonly AnimalBehaviorTreeContext context;
    private readonly AI_AttackController attack = new();
    private readonly AI_AnimalSkillController skills = new();
    private readonly string animation;
    public float AttackRange { get; }
    public bool IsAttackLocked => attack.IsAttackLocked;
    public bool IsCooldownDone => attack.IsCooldownDone;
    public float CooldownRemaining => attack.CooldownRemaining;
    public bool HasActiveSkill => skills.IsAnyActive;
    public bool HasUsableSkill(Item target) => target != null && skills.HasUsableSkill(target);

    public AnimalCombatCapability(AnimalBehaviorTreeContext context, JObject config, JObject saved)
    {
        this.context = context;
        AttackRange = AnimalLandTargetRules.Number(config, "range", 2.1f);
        animation = AnimalLandTargetRules.Text(config, "animation", "Attack");
        attack.Cooldown = AnimalLandTargetRules.Number(config, "cooldown", 2f);
        attack.DamageWindow = AnimalLandTargetRules.Number(config, "damageWindow", 0.33333334f);
        attack.WindupDuration = AnimalLandTargetRules.Number(config, "windup", 0.25f);
        attack.RecoveryDuration = AnimalLandTargetRules.Number(config, "recovery", 0.35f);
        attack.Bind(context.Actor);
        attack.Reset();
        attack.RestoreCooldown(saved?["cooldownRemaining"]?.Value<float>() ?? 0f);
        skills.Bind(context.Actor);
        skills.ResetRuntime();
    }
    public override void Tick(float deltaTime) => attack.Tick(deltaTime);
    public bool TryStartSkill(Item target) => skills.TryStart(target);
    public void CancelSkills() => skills.CancelAll();
    public void StartAttack(Vector3 targetPosition)
    {
        attack.StartWindow(context.Animator, animation, WorldTopologyRuntime.ShortestDelta(context.Position, targetPosition));
    }
    public void EnterAttack() => attack.OnEnterAttackState();
    public void StopAttack(bool interrupted)
    {
        if (interrupted && attack.IsAttackLocked) attack.OnExitAttackState();
        else attack.StopWindow();
    }
    public override JObject CaptureSaveData() => new() { ["cooldownRemaining"] = attack.CooldownRemaining };
    public override void Dispose() { skills.CancelAll(); attack.StopWindow(); }
    #endregion
}

/// <summary>累计指定来源的实际伤害，在阈值达到后定时锁定反击目标。</summary>
public sealed class AnimalDamageRetaliationCapability : AnimalBehaviorTreeCapability
{
    #region 累伤与反击
    private readonly AnimalBehaviorTreeContext context;
    private readonly float thresholdRate;
    private readonly float duration;
    private float accumulatedDamage;
    private float remaining;
    private int targetGuid;
    private Item target;
    public bool IsActive => remaining > 0f;
    public float Remaining => remaining;
    public float AccumulatedDamage => accumulatedDamage;
    public Item Target
    {
        get
        {
            if (target == null && targetGuid != 0) ItemMgr.Instance?.WorldRunTimeItems.TryGetValue(targetGuid, out target);
            return target;
        }
    }

    public AnimalDamageRetaliationCapability(AnimalBehaviorTreeContext context, JObject config, JObject saved)
    {
        this.context = context;
        thresholdRate = AnimalLandTargetRules.Number(config, "damageThresholdRate", .2f);
        duration = AnimalLandTargetRules.Number(config, "duration", 30f);
        accumulatedDamage = saved?["accumulatedDamage"]?.Value<float>() ?? 0f;
        remaining = saved?["remaining"]?.Value<float>() ?? 0f;
        targetGuid = saved?["targetGuid"]?.Value<int>() ?? 0;
        if (context.Health != null) context.Health.OnDamageReceived += HandleDamage;
    }

    private void HandleDamage(DamageReceiverDamageInfo info)
    {
        if (info == null || info.DamageValue <= 0f || info.HpAfter <= 0f || IsActive) return;
        Item attacker = info.Attacker != null ? info.Attacker.Owner ?? info.Attacker : null;
        if (!AnimalLandTargetRules.IsPlayer(attacker) || info.Receiver == null || info.Receiver.MaxHp <= 0f) return;
        accumulatedDamage += info.DamageValue;
        if (accumulatedDamage < info.Receiver.MaxHp * thresholdRate) return;
        target = attacker;
        targetGuid = target.itemData?.Guid ?? 0;
        remaining = duration;
        accumulatedDamage = 0f;
    }

    public override void Tick(float deltaTime)
    {
        if (remaining <= 0f) return;
        remaining = Mathf.Max(0f, remaining - Mathf.Max(0f, deltaTime));
        if (remaining > 0f) return;
        accumulatedDamage = 0f;
        targetGuid = 0;
        target = null;
    }
    public override JObject CaptureSaveData() => new()
    {
        ["accumulatedDamage"] = accumulatedDamage, ["remaining"] = remaining,
        ["targetGuid"] = target != null ? target.itemData?.Guid ?? 0 : targetGuid
    };
    public override void Dispose()
    {
        if (context.Health != null) context.Health.OnDamageReceived -= HandleDamage;
        target = null;
    }
    #endregion
}

/// <summary>按饥饿、可攻击标签和追逐时长维持捕食目标。</summary>
public sealed class AnimalHuntingCapability : AnimalBehaviorTreeCapability
{
    #region 捕食目标与饥饿
    private readonly AnimalBehaviorTreeContext context;
    private readonly string[] preyIds;
    private readonly float satiatedRange;
    private readonly float hungryRange;
    private readonly float lossRange;
    private readonly float pursuitLimit;
    private readonly float reacquireDelay;
    private readonly string chaseAction;
    private Item target;
    private bool targetIsPrey;
    private float hungerRemaining;
    private float pursueElapsed;
    private float reacquireRemaining;
    private float targetRefreshRemaining;
    public bool IsHungry => hungerRemaining <= 0f;
    public Item Target => target;
    public bool TargetVisible => target != null && AnimalLandTargetRules.IsVisible(context, target, lossRange);

    public AnimalHuntingCapability(AnimalBehaviorTreeContext context, JObject config, JObject saved)
    {
        this.context = context;
        preyIds = AnimalLandTargetRules.Strings(config, "preyIds");
        satiatedRange = AnimalLandTargetRules.Number(config, "satiatedRange", 8f);
        hungryRange = AnimalLandTargetRules.Number(config, "hungryRange", 24f);
        lossRange = AnimalLandTargetRules.Number(config, "lossRange", 30f);
        pursuitLimit = AnimalLandTargetRules.Number(config, "pursuitLimit", 30f);
        reacquireDelay = AnimalLandTargetRules.Number(config, "reacquireDelay", 10f);
        chaseAction = AnimalLandTargetRules.Text(config, "chaseAction", "chase");
        hungerRemaining = saved?["hungerRemaining"]?.Value<float>() ?? GetDayLength();
        if (context.Detector != null) context.Detector.DetectionRadius = Mathf.Max(context.Detector.DetectionRadius, hungryRange, lossRange);
    }

    public override void Tick(float deltaTime)
    {
        hungerRemaining = Mathf.Max(0f, hungerRemaining - Mathf.Max(0f, deltaTime));
        reacquireRemaining = Mathf.Max(0f, reacquireRemaining - Mathf.Max(0f, deltaTime));
        targetRefreshRemaining = Mathf.Max(0f, targetRefreshRemaining - Mathf.Max(0f, deltaTime));
        if (target == null && targetIsPrey)
        {
            hungerRemaining = GetDayLength();
            Clear();
        }
        else if (target != null && !AnimalLandTargetRules.IsLivingOpponent(context, target))
        {
            if (targetIsPrey) hungerRemaining = GetDayLength();
            Clear();
        }
        else if (target != null && WorldTopologyRuntime.Distance(context.Position, target.transform.position) > lossRange) Clear();
        if (target != null && targetIsPrey && context.IsActionActive(chaseAction))
        {
            pursueElapsed += Mathf.Max(0f, deltaTime);
            if (pursueElapsed >= Mathf.Max(.1f, pursuitLimit)) { Clear(); reacquireRemaining = reacquireDelay; }
        }
        if (target == null && reacquireRemaining <= 0f && targetRefreshRemaining <= 0f)
        { targetRefreshRemaining = .15f; AcquireNearestTarget(); }
    }

    private void AcquireNearestTarget()
    {
        List<Item> candidates = context.Detector?.CurrentItemsInArea;
        if (candidates == null) return;
        float range = IsHungry ? hungryRange : satiatedRange;
        float nearest = float.MaxValue;
        foreach (Item candidate in candidates)
        {
            if (!AnimalLandTargetRules.IsLivingOpponent(context, candidate)) continue;
            bool prey = IsPrey(candidate);
            if (!AnimalLandTargetRules.IsPlayer(candidate) && (!prey || !IsHungry)) continue;
            if (!AnimalLandTargetRules.IsVisible(context, candidate, range)) continue;
            float distance = WorldTopologyRuntime.SqrDistance(context.Position, candidate.transform.position);
            if (distance >= nearest) continue;
            nearest = distance;
            target = candidate;
            targetIsPrey = prey;
            pursueElapsed = 0f;
        }
    }
    private bool IsPrey(Item candidate)
    {
        foreach (string id in preyIds)
            if (string.Equals(candidate.itemData?.IDName, id, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
    private void Clear() { target = null; targetIsPrey = false; pursueElapsed = 0f; }
    private float GetDayLength()
    {
        DayTimeSystem time = DayTimeSystem.Instance;
        return time != null && time.WorldTimeDict.TryGetValue(context.Actor.gameObject.scene.name, out TimeData data) && data != null
            ? Mathf.Max(1f, data.DayLength) : 1440f;
    }
    public override JObject CaptureSaveData() => new() { ["hungerRemaining"] = hungerRemaining };
    public override void Dispose() => Clear();
    #endregion
}
