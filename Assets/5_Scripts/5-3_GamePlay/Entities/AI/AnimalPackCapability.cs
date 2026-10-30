using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;

/// <summary>友方群体发现、可见情报传播与围攻站位，可由任意生物组合使用。</summary>
public sealed class AnimalPackCapability : AnimalBehaviorTreeCapability
{
    #region 群体成员和威胁
    private readonly AnimalBehaviorTreeContext context;
    private readonly AnimalTargetMemoryCapability memory;
    private readonly JObject config;
    private readonly string groupKey;
    private readonly string memoryKey;
    private readonly List<AnimalPackCapability> allies = new(8);
    private readonly List<AnimalPackCapability> formationMembers = new(8);
    private readonly string[] groupTags;
    private readonly string[] threatTags;
    private float refreshTimer;
    private float callTimer;
    private float assistTimer;
    private float alertTimer;
    private bool holdingPosition;
    private bool hasFormation;
    private Vector3 formationPosition;
    private int formationSlot = -1;
    private int formationMemberCount;
    private AnimalPackCapability leader;
    private Vector3 groupCenter;

    public Item Actor => context.Actor;
    public int MemberCount => allies.Count + 1;
    public int ValidatedAllyCount => allies.Count;
    public Item Leader => leader?.Actor;
    public Item CurrentTarget => memory.Target;
    public AnimalTargetMemoryCapability Memory => memory;
    public bool IsLeader => leader == this;
    public Vector3? FormationTarget => hasFormation ? formationPosition : (Vector3?)null;
    public int FormationSlot => formationSlot;
    public int FormationMemberCount => formationMemberCount;
    public bool HasAlert => alertTimer > 0f;

    public AnimalPackCapability(AnimalBehaviorTreeContext context, JObject config, JObject saved)
    {
        this.context = context;
        this.config = config ?? new JObject();
        groupKey = AnimalLandTargetRules.Text(config, "key", "pack");
        memoryKey = AnimalLandTargetRules.Text(config, "memoryKey", "memory");
        groupTags = AnimalLandTargetRules.Strings(config, "groupTags");
        threatTags = AnimalLandTargetRules.Strings(config, "threatTags");
        memory = context.GetOrCreateCapability(memoryKey, data =>
            new AnimalTargetMemoryCapability(context, context.GetCapabilityConfiguration(memoryKey), data));
        float radius = Mathf.Max(AnimalLandTargetRules.Number(config, "chaseDistance", 28f),
            AnimalLandTargetRules.Number(config, "avoidSafeDistance", 30f));
        if (context.Detector != null) context.Detector.DetectionRadius = Mathf.Max(context.Detector.DetectionRadius, radius);
        leader = this;
    }

    public override void Tick(float deltaTime)
    {
        if (!memory.HasMemory) alertTimer = assistTimer = 0f;
        callTimer = Mathf.Max(0f, callTimer - deltaTime);
        assistTimer = Mathf.Max(0f, assistTimer - deltaTime);
        alertTimer = Mathf.Max(0f, alertTimer - deltaTime);
        refreshTimer -= deltaTime;
        if (refreshTimer > 0f) return;
        refreshTimer = Mathf.Max(.01f, AnimalLandTargetRules.Number(config, "decisionInterval", .15f));
        RefreshGroupAndThreat();
    }

    public void RefreshGroupAndThreat()
    {
        RefreshGroup();
        RefreshTarget();
        RefreshFormation();
    }

    public void RefreshGroup()
    {
        allies.Clear();
        leader = this;
        groupCenter = context.Position;
        List<Item> candidates = context.Detector?.CurrentItemsInArea;
        if (candidates == null) return;
        Vector3 sum = Vector3.zero;
        foreach (Item candidate in candidates)
        {
            if (!TryGetAlly(candidate, out AnimalPackCapability ally) || allies.Contains(ally)) continue;
            allies.Add(ally);
            sum += candidate.transform.position;
            if (CompareMembers(ally, leader) < 0) leader = ally;
        }
        if (allies.Count > 0) groupCenter = sum / allies.Count;
    }

    private void RefreshTarget()
    {
        Item previousTarget = memory.Target;
        float loss = AnimalLandTargetRules.Number(config, "lossDistance", 44f);
        float safe = AnimalLandTargetRules.Number(config, "avoidSafeDistance", 30f);
        if (memory.HasMemory) memory.RefreshVisibility(Mathf.Max(loss, safe));
        Item target = FindTarget();
        if (target != null) memory.Remember(target, target.transform.position, true);
        if (previousTarget != memory.Target) ClearFormation();
        if (memory.Visible && AnimalLandTargetRules.IsVisible(context, memory.Target,
                AnimalLandTargetRules.Number(config, "alertDistance", 20f)))
            alertTimer = AnimalLandTargetRules.Number(config, "alertDuration", 2f);
    }

    private Item FindTarget()
    {
        bool aggressiveAdvance = context.TryGetCapability("advance", out AnimalAdvanceCapability advance) && advance.AttackActorsOnRoute;
        List<Item> candidates = context.Detector?.CurrentItemsInArea;
        if (candidates == null) return null;
        Item closest = null;
        float closestDistance = float.MaxValue;
        foreach (Item candidate in candidates)
        {
            if (!AnimalLandTargetRules.IsUsableOpponent(context, candidate)) continue;
            bool player = AnimalLandTargetRules.IsPlayer(candidate);
            if (aggressiveAdvance)
            {
                if (candidate.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp) is not { Hp: > 0f }) continue;
                if (!player && candidate.itemMods?.GetMod_ByID(ModText.AI) is not IAIActor) continue;
            }
            else if (!player && !HasAnyTag(candidate, threatTags)) continue;
            if (!AnimalLandTargetRules.IsVisible(context, candidate, context.Detector.DetectionRadius)) continue;
            float distance = WorldTopologyRuntime.SqrDistance(context.Position, candidate.transform.position);
            if (distance >= closestDistance) continue;
            closest = candidate;
            closestDistance = distance;
        }
        return closest;
    }

    public void TriggerGroupAttack(Item target)
    {
        if (!AnimalLandTargetRules.IsUsableOpponent(context, target) ||
            !AnimalLandTargetRules.IsVisible(context, target, AnimalLandTargetRules.Number(config, "lossDistance", 44f))) return;
        memory.Remember(target, target.transform.position, true, true);
        alertTimer = AnimalLandTargetRules.Number(config, "alertDuration", 2f);
        assistTimer = AnimalLandTargetRules.Number(config, "assistDuration", 5f);
        CallNearbyAllies(true);
    }

    public void ReceiveGroupCall(Item target, AnimalPackCapability caller)
    {
        if (caller == this || !AnimalLandTargetRules.IsUsableOpponent(context, target) ||
            caller != null && FactionRelationService.GetRelation(caller.Actor, Actor) != FactionRelation.Friendly) return;
        float loss = AnimalLandTargetRules.Number(config, "lossDistance", 44f);
        if (AnimalLandTargetRules.IsVisible(context, target, loss))
            memory.Remember(target, target.transform.position, true, true);
        else if (caller != null && caller.memory.Visible && caller.memory.Matches(target))
            memory.Remember(target, caller.memory.LastKnownPosition, false, true);
        else return;
        ClearFormation();
        alertTimer = Mathf.Max(alertTimer, AnimalLandTargetRules.Number(config, "alertDuration", 2f));
        assistTimer = Mathf.Max(assistTimer, AnimalLandTargetRules.Number(config, "assistDuration", 5f));
    }

    public void CallNearbyAllies() => CallNearbyAllies(false);

    private void CallNearbyAllies(bool immediate)
    {
        if (!memory.Visible || !memory.Matches(memory.Target) || !immediate && (MemberCount < 2 || callTimer > 0f)) return;
        float distance = AnimalLandTargetRules.Number(config, "allyCallDistance", 120f);
        List<Item> candidates = context.Detector?.CurrentItemsInArea;
        if (candidates == null) return;
        foreach (Item candidate in candidates)
            if (TryGetAlly(candidate, out AnimalPackCapability ally) && AnimalLandTargetRules.IsVisible(context, candidate, distance))
                ally.ReceiveGroupCall(memory.Target, this);
        callTimer = AnimalLandTargetRules.Number(config, "callCooldown", .8f);
    }

    public void ClearAfterCombat()
    {
        ClearFormation();
        if (assistTimer <= 0f) ForgetGroupThreat();
    }

    public void ForgetGroupThreat()
    {
        memory.Forget();
        alertTimer = assistTimer = 0f;
        ClearFormation();
    }

    private bool TryGetAlly(Item target, out AnimalPackCapability ally)
    {
        ally = null;
        if (target == null || target == Actor || target.itemMods == null ||
            FactionRelationService.GetRelation(Actor, target) != FactionRelation.Friendly) return false;
        Mod_AnimalBehaviorTree host = target.itemMods.GetMod_ByID<Mod_AnimalBehaviorTree>(ModText.AI);
        if (host == null || !host.Enabled || !host.IsAlive || !host.IsRuntimeLoaded || host.Context == null ||
            !host.Context.TryGetCapability(groupKey, out ally)) return false;
        return !string.IsNullOrWhiteSpace(target.itemData?.FactionId) || groupTags.Length == 0 || HasAnyTag(target, groupTags);
    }

    private static bool HasAnyTag(Item target, string[] tags)
    {
        if (target?.itemData?.Tags == null) return false;
        foreach (string tag in tags)
            if (target.itemData.Tags.ContainsTag(tag)) return true;
        return false;
    }

    public Vector2 ApplyWanderCohesion(Vector2 offset, float radius)
    {
        if (allies.Count == 0 || IsLeader) return offset;
        Vector2 towardGroup = WorldTopologyRuntime.ShortestDelta(context.Position, leader?.Actor != null ? leader.Actor.transform.position : groupCenter);
        return towardGroup.sqrMagnitude > .0001f
            ? offset + towardGroup.normalized * (radius * AnimalLandTargetRules.Number(config, "wanderCohesion", .65f)) : offset;
    }
    #endregion

    #region 围攻槽位和移动滞回
    public void RefreshFormation()
    {
        formationMembers.Clear();
        if (!AnimalLandTargetRules.Flag(config, "formationEnabled", true) || !memory.Visible ||
            memory.Target == null || !AnimalLandTargetRules.IsPlayer(memory.Target)) { ClearFormation(); return; }
        formationMembers.Add(this);
        foreach (AnimalPackCapability ally in allies)
            if (ally.memory.Target == memory.Target && ally.memory.Visible &&
                !ally.context.IsActionActive("avoid") && !ally.context.IsActionActive("flee")) formationMembers.Add(ally);
        if (formationMembers.Count < Mathf.Max(2, AnimalLandTargetRules.Integer(config, "formationMinMembers", 2)))
        { ClearFormation(); return; }
        formationMembers.Sort(CompareMembers);
        WorldTopologyDomain topology = WorldTopologyRuntime.GetActiveDomain();
        int left = 0, right = 0, slot = -1;
        for (int i = 0; i < formationMembers.Count; i++)
        {
            AnimalPackCapability member = formationMembers[i];
            bool useRight = ResolveRightChaseLane(memory.Target.transform.position, member.context.Position, i, topology);
            int index = useRight ? right++ * 2 + 1 : left++ * 2;
            if (member == this) { slot = index; break; }
        }
        if (slot < 0) { ClearFormation(); return; }
        float attackRange = AnimalLandTargetRules.Number(config, "attackRange", 2.1f);
        float maxRadius = Mathf.Max(.05f, attackRange - Mathf.Max(.05f, AnimalLandTargetRules.Number(config, "formationAttackMargin", .18f)));
        float radius = Mathf.Clamp(AnimalLandTargetRules.Number(config, "formationRadius", 1.05f), .05f, maxRadius);
        Vector2 offset = CalculateChaseFormationSlotOffset(slot, radius,
            AnimalLandTargetRules.Number(config, "formationVerticalSpacing", .42f),
            AnimalLandTargetRules.Number(config, "formationMaxVerticalRatio", .55f));
        Vector2 separated = Vector2.ClampMagnitude(offset + CalculateSeparation(topology), maxRadius);
        Vector3 target = memory.Target.transform.position;
        Vector3 position = NormalizeChasePosition(target + (Vector3)separated, topology);
        if (!IsWalkable(position))
        {
            position = NormalizeChasePosition(target + (Vector3)offset, topology);
            if (!IsWalkable(position)) { ClearFormation(); return; }
        }
        formationPosition = position;
        formationSlot = slot;
        formationMemberCount = formationMembers.Count;
        hasFormation = true;
    }

    private Vector2 CalculateSeparation(in WorldTopologyDomain topology)
    {
        float distance = Mathf.Max(.05f, AnimalLandTargetRules.Number(config, "separationDistance", .85f));
        float strength = AnimalLandTargetRules.Number(config, "separationStrength", .7f);
        float maximum = AnimalLandTargetRules.Number(config, "separationMaxOffset", .2f);
        Vector2 result = Vector2.zero;
        foreach (AnimalPackCapability ally in formationMembers)
        {
            if (ally == this) continue;
            Vector2 away = GetChaseDelta(ally.context.Position, context.Position, topology);
            float sqr = away.sqrMagnitude;
            if (sqr >= distance * distance) continue;
            if (sqr <= .0001f) { result += CompareMembers(this, ally) <= 0 ? Vector2.down : Vector2.up; continue; }
            float length = Mathf.Sqrt(sqr);
            result += away / length * (1f - length / distance);
        }
        return Vector2.ClampMagnitude(result * (distance * strength), Mathf.Max(0f, maximum));
    }

    public bool ShouldHoldFormationPosition(float approachRange, float attackRange)
    {
        if (!hasFormation || WorldTopologyRuntime.SqrDistance(context.Position, memory.LastKnownPosition) > attackRange * attackRange)
        { holdingPosition = false; return false; }
        float arrival = Mathf.Max(AnimalLandTargetRules.Number(config, "formationArrival", .3f),
            context.Mod_Mover != null ? context.Mod_Mover.GetInteractionStopDistance(approachRange) : 0f);
        float tolerance = holdingPosition ? Mathf.Max(arrival, AnimalLandTargetRules.Number(config, "formationResume", .6f)) : arrival;
        return holdingPosition = WorldTopologyRuntime.SqrDistance(context.Position, formationPosition) <= tolerance * tolerance;
    }

    public void ClearFormation()
    {
        formationMembers.Clear();
        hasFormation = holdingPosition = false;
        formationPosition = context.Position;
        formationSlot = -1;
        formationMemberCount = 0;
    }

    public static Vector2 CalculateChaseFormationSlotOffset(int slot, float radius, float spacing, float maximumVerticalRatio)
    {
        radius = Mathf.Max(.05f, radius);
        int pair = Mathf.Max(0, slot) / 2;
        float vertical = pair == 0 ? 0f : ((pair & 1) == 1 ? 1f : -1f) * ((pair + 1) / 2) * Mathf.Max(0f, spacing);
        vertical = Mathf.Clamp(vertical, -radius * Mathf.Clamp(maximumVerticalRatio, .05f, .85f), radius * Mathf.Clamp(maximumVerticalRatio, .05f, .85f));
        float horizontal = Mathf.Sqrt(Mathf.Max(0f, radius * radius - vertical * vertical));
        return new Vector2(horizontal * ((slot & 1) == 0 ? -1f : 1f), vertical);
    }
    public static Vector2 GetChaseDelta(Vector2 from, Vector2 to, in WorldTopologyDomain topology)
    {
        float2 delta = topology.ShortestDelta(new float2(from.x, from.y), new float2(to.x, to.y));
        return new Vector2(delta.x, delta.y);
    }
    public static Vector3 NormalizeChasePosition(Vector3 position, in WorldTopologyDomain topology)
    {
        float2 normalized = topology.Normalize(new float2(position.x, position.y));
        return new Vector3(normalized.x, normalized.y, position.z);
    }
    public static bool ResolveRightChaseLane(Vector2 target, Vector2 member, int index, in WorldTopologyDomain topology)
    {
        float horizontal = GetChaseDelta(target, member, topology).x;
        return Mathf.Abs(horizontal) > .05f ? horizontal > 0f : (index & 1) != 0;
    }
    private static bool IsWalkable(Vector3 position)
    {
        WorldNavigationManager navigation = WorldNavigationManager.ExistingInstance;
        return navigation == null || !navigation.IsNavigationReady || navigation.IsWalkable(position);
    }
    private static int CompareMembers(AnimalPackCapability left, AnimalPackCapability right)
    {
        int order = (left.Actor.itemData?.Guid ?? 0).CompareTo(right.Actor.itemData?.Guid ?? 0);
        return order != 0 ? order : left.Actor.GetInstanceID().CompareTo(right.Actor.GetInstanceID());
    }
    public override void Dispose() { allies.Clear(); ClearFormation(); }
    #endregion
}
