using System;
using System.Collections.Generic;
using UnityEngine;
using FlatWorld.Networking;
using MemoryPack;
using Unity.Mathematics;

/// <summary>通用撕咬技能按身体质量选择挂咬或拖行，伤害、减速和动作限制均由本来源独立释放。</summary>
public sealed class Mod_BiteAttack : Module, IAnimalCombatSkill
{
    #region 配置与依赖
    public const string ModuleId = "AnimalSkill_BiteAttack";
    public const string HoldBuff = "core:bite_hold", CarryBuff = "core:bite_carried", DragBuff = "core:bite_drag";
    public string SkillId = "animal.bite_attack";
    [Min(0f)] public float rangeMultiplier = 1f;
    public Ex_ModData_MemoryPackable ModData = new();
    public override string CanonicalModuleId => ModuleId;
    public override ModuleData _Data { get => ModData; set => ModData = (Ex_ModData_MemoryPackable)value; }
    public override ModuleTickMode TickMode => ModuleTickMode.EveryFrame;
    private AnimalSkillDefinition definition;
    private Mod_Mover mover, targetMover;
    private Mod_DamageReceiver health, targetHealth;
    private Mod_BuffManager sourceBuffs, targetBuffs;
    private Item target;
    private ActorActionConstraints.FollowLease follow;
    private IDisposable attackLock;
    private IDisposable speedLease;
    private Mod_Mover.DrivenMotionLease draggingMotion;
    private Vector2 draggingDirection;
    private readonly List<Item> candidates = new();
    private readonly HashSet<Item> dedupe = new();
    private readonly BiteDamageSender sender = new();
    private float elapsed, nextTick, cooldown;
    private bool carrying;
    private bool active;
    private int scene;
    private string sourceKey;
    private BiteCooldownState saved = new();
    string IAnimalCombatSkill.SkillId => SkillId;
    public bool IsActive => active;
    public bool IsReady => Resolve();
    public bool IsOnCooldown => cooldown > 0f;
    public float TriggerDistance => definition == null ? 0f : Mathf.Max(.05f,
        (definition.TriggerDistance > 0f ? definition.TriggerDistance : ResolveAttackRange()) * rangeMultiplier);
    public override void Awake() { ModData.ID = ModuleId; base.Awake(); }
    protected override void OnLoad()
    {
        Cancel(); saved = new BiteCooldownState(); ModData.ReadData(ref saved);
        cooldown = float.IsFinite(saved.CooldownRemaining) ? Mathf.Max(0f, saved.CooldownRemaining) : 0f;
        Resolve();
    }
    protected override void OnSave() { saved.CooldownRemaining = cooldown; ModData.WriteData(saved); }
    protected override void OnUnload() => Cancel();
    private void OnDisable() => Cancel();
    private void OnDestroy() => Cancel();
    private bool Resolve()
    {
        if (item == null || !AnimalSkillCatalogService.TryGet(SkillId, out definition) ||
            !string.Equals(definition.Type, AnimalSkillDefinition.BiteAttackType, StringComparison.OrdinalIgnoreCase)) return false;
        mover = item.GetComponentInChildren<Mod_Mover>(true);
        health = item.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
        sourceBuffs = item.itemMods?.GetMod_ByID<Mod_BuffManager>(ModText.Mod_BuffManager);
        return mover != null && health != null && sourceBuffs != null;
    }
    private float ResolveAttackRange()
    {
        foreach (Module module in item.itemMods.Mods.Values)
            if (module is IAnimalAttackRangeSource source) return source.AttackRange;
        Mod_Damage damage = item.GetComponentInChildren<Mod_Damage>(true);
        Collider2D shape = damage != null ? damage.GetComponent<Collider2D>() : null;
        return shape != null ? Mathf.Max(shape.bounds.extents.x, shape.bounds.extents.y) : 1.5f;
    }
    #endregion

    #region 目标与发动
    public static bool ShouldCarry(float casterBodyWeight, float targetBodyWeight) => targetBodyWeight < casterBodyWeight * .5f;
    public bool CanStart(Item _)
    {
        if (!GameNetwork.HasStateAuthority || !Enabled || !isActiveAndEnabled || !IsRuntimeLoaded || IsActive || cooldown > 0f || !Resolve() ||
            health.Hp <= 0f || !mover.IsSurfaceMotionAvailable || mover.HasDrivenMotion || ActorActionConstraints.IsForcedFollowing(item)) return false;
        return FindClosestTarget() != null;
    }
    private Item FindClosestTarget()
    {
        ItemMgr manager = ItemMgr.GetInstance();
        if (manager == null) return null;
        float radius = TriggerDistance;
        Vector2 origin = item.transform.position;
        manager.QueryItemsInCircleNonAlloc(origin, radius, ~0, item, candidates, dedupe);
        WorldTopologyDomain topology = WorldTopologyRuntime.GetActiveDomain();
        Item closest = null; float nearest = float.PositiveInfinity;
        foreach (Item candidate in candidates)
        {
            Mod_DamageReceiver receiver = candidate?.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
            Mod_BuffManager buffs = candidate?.itemMods?.GetMod_ByID<Mod_BuffManager>(ModText.Mod_BuffManager);
            Mod_Mover movement = candidate?.GetComponentInChildren<Mod_Mover>(true);
            if (receiver == null || buffs == null || movement == null || receiver.Hp <= 0f || candidate.DestructionHandled ||
                candidate.gameObject.scene != item.gameObject.scene || !FactionRelationService.CanAttack(item, candidate) ||
                buffs?.HasBuff(CarryBuff) == true) continue;
            if (ShouldCarry(item.itemData.Stack.Weight, candidate.itemData.Stack.Weight) &&
                (movement.CarrierSource != null || (movement.HasDrivenMotion && !ActorActionConstraints.IsForcedFollowing(candidate)))) continue;
            Vector2 position = candidate.transform.position;
            float distance = topology.SqrDistance(new float2(origin.x, origin.y), new float2(position.x, position.y));
            if (distance > radius * radius || distance > nearest ||
                (Mathf.Approximately(distance, nearest) && closest != null && candidate.itemData.Guid >= closest.itemData.Guid)) continue;
            nearest = distance; closest = candidate;
        }
        return closest;
    }
    public bool TryStart(Item requested)
    {
        if (!CanStart(requested)) return false;
        Item selected = FindClosestTarget();
        if (selected == null) return false;
        targetMover = selected.GetComponentInChildren<Mod_Mover>(true);
        targetHealth = selected.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
        targetBuffs = selected.itemMods.GetMod_ByID<Mod_BuffManager>(ModText.Mod_BuffManager);
        if (targetMover == null || targetBuffs == null) return false;
        carrying = ShouldCarry(item.itemData.Stack.Weight, selected.itemData.Stack.Weight);
        if (carrying && targetBuffs.HasBuff(CarryBuff)) return false;
        if (carrying && !mover.TryAcquireDrivenMotion(Vector2.zero, out draggingMotion)) return false;
        if (carrying && ActorActionConstraints.IsForcedFollowing(selected))
            foreach (Module module in selected.itemMods.Mods.Values)
                if (module is IAnimalCombatSkill skill && skill.IsActive) skill.Cancel();
        if (!ActorActionConstraints.TryFollow(carrying ? selected : item, carrying ? item : selected, out follow))
        { draggingMotion?.Dispose(); draggingMotion = null; return false; }
        draggingDirection = WorldTopologyRuntime.ShortestDelta(selected.transform.position, item.transform.position).normalized;
        if (draggingDirection.sqrMagnitude < .001f) draggingDirection = Vector2.left;
        target = selected; active = true; scene = item.gameObject.scene.handle; elapsed = 0f; nextTick = definition.TickIntervalSeconds;
        sourceKey = "bite:" + item.itemData.Guid + ":" + RuntimeGeneration;
        try
        {
            attackLock = ActorActionConstraints.BlockAttacks(item);
            speedLease = ActorActionConstraints.ScaleSpeed(carrying ? item : selected,
                1f - (carrying ? definition.CasterSlowFraction : definition.TargetSlowFraction));
            if (carrying)
            { targetBuffs.EnsureSourceBuff(CarryBuff, sourceKey); sourceBuffs.EnsureSourceBuff(DragBuff, sourceKey); }
            else targetBuffs.EnsureSourceBuff(HoldBuff, sourceKey);
            health.DeathStarted += HandleDeath; targetHealth.DeathStarted += HandleDeath;
            mover.MotionReset += Cancel; targetMover.MotionReset += Cancel;
            cooldown = definition.CooldownSeconds;
            return true;
        }
        catch { Cancel(); throw; }
    }
    #endregion

    #region 周期与释放
    public override void ModUpdate(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds <= 0f) return;
        cooldown = Mathf.Max(0f, cooldown - seconds);
        if (!IsActive) return;
        if (!GameNetwork.HasStateAuthority || follow?.IsActive != true || health == null || targetHealth == null ||
            health.Hp <= 0f || targetHealth.Hp <= 0f || !IsRuntimeLoaded ||
            item.gameObject.scene.handle != scene || target.gameObject.scene.handle != scene ||
            WorldTopologyRuntime.Distance(item.transform.position, target.transform.position) > Mathf.Max(8f, TriggerDistance * 4f))
        { Cancel(); return; }
        follow.Tick(seconds);
        if (carrying)
        {
            if (draggingMotion?.IsActive != true) { Cancel(); return; }
            draggingMotion.SetVelocity(draggingDirection * mover.WalkingSpeed);
        }
        elapsed += seconds;
        // 包含第九秒的最后一次命中，多个来源不共享目标的普通攻击受击间隔。
        while (IsActive && nextTick <= definition.DurationSeconds + .0001f && elapsed + .0001f >= nextTick)
        {
            nextTick += definition.TickIntervalSeconds;
            sender.attacker = item;
            sender.DamageValues = new CombatDamage { Physical = carrying ? definition.CarryDamage : definition.HoldDamage,
                ImpactKind = CombatDamageKind.Piercing };
            targetHealth.Hurt(sender);
        }
        if (IsActive && elapsed >= definition.DurationSeconds) Cancel();
    }
    private void HandleDeath(Mod_DamageReceiver _) => Cancel();
    public void Cancel()
    {
        active = false;
        if (health != null) health.DeathStarted -= HandleDeath;
        if (targetHealth != null) targetHealth.DeathStarted -= HandleDeath;
        if (mover != null) mover.MotionReset -= Cancel;
        if (targetMover != null) targetMover.MotionReset -= Cancel;
        follow?.Dispose(); follow = null;
        attackLock?.Dispose(); attackLock = null;
        speedLease?.Dispose(); speedLease = null;
        draggingMotion?.Dispose(); draggingMotion = null;
        if (!string.IsNullOrEmpty(sourceKey))
        { if (targetBuffs != null) targetBuffs.RemoveSourceBuff(sourceKey); if (carrying && sourceBuffs != null) sourceBuffs.RemoveSourceBuff(sourceKey); }
        target = null; targetHealth = null; targetMover = null; targetBuffs = null; sourceKey = null;
    }
    public void ResetRuntime() => Cancel();
    private sealed class BiteDamageSender : IDamageSender, IPeriodicDamageSource
    {
        public CombatDamage DamageValues { get; set; }
        public Item attacker { get; set; }
    }
    #endregion
}

/// <summary>只保存剩余冷却，临时目标与控制租约在装卸时始终解除。</summary>
[Serializable, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class BiteCooldownState
{
    [MemoryPackOrder(0)] public float CooldownRemaining;
}
