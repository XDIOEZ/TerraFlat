using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>地面动物以配置组合的通用行为和条件。</summary>
public static class AnimalLandBehaviorNodes
{
    #region 节点注册
    public static void Register()
    {
        AnimalBehaviorTreeRegistry.RegisterAction("land.wander", (c, p) => new AnimalWanderAction(c, p));
        AnimalBehaviorTreeRegistry.RegisterAction("land.flee", (c, p) => new AnimalFleeAction(c, p));
        AnimalBehaviorTreeRegistry.RegisterAction("land.sleep", (c, p) => new AnimalSleepAction(c, p));
        AnimalBehaviorTreeRegistry.RegisterAction("land.forage", (c, p) => new AnimalForageAction(c, p));
        AnimalBehaviorTreeRegistry.RegisterAction("land.produce", (c, p) => new AnimalProduceAction(c, p));
        AnimalBehaviorTreeRegistry.RegisterAction("land.mate", (c, p) => new AnimalMateAction(c, p));
        AnimalBehaviorTreeRegistry.RegisterAction("land.advance", (c, p) => new AnimalAdvanceAction(c, p));
        AnimalBehaviorTreeRegistry.RegisterAction("land.chase", (c, p) => new AnimalChaseAction(c, p));
        AnimalBehaviorTreeRegistry.RegisterAction("land.attack", (c, p) => new AnimalAttackAction(c, p));
        AnimalBehaviorTreeRegistry.RegisterAction("land.skill", (c, p) => new AnimalSkillAction(c, p));
        AnimalBehaviorTreeRegistry.RegisterAction("land.search", (c, p) => new AnimalSearchAction(c, p));
        AnimalBehaviorTreeRegistry.RegisterAction("land.alert", (c, p) => new AnimalAlertAction(c, p));

        AnimalBehaviorTreeRegistry.RegisterCondition("land.target", CreateTargetCondition);
        AnimalBehaviorTreeRegistry.RegisterCondition("land.fleeThreat", CreateFleeCondition);
        AnimalBehaviorTreeRegistry.RegisterCondition("land.groupSize", (c, p) =>
        {
            AnimalPackCapability pack = AnimalLandActionSupport.Pack(c, p);
            int min = AnimalLandTargetRules.Integer(p, "min", 1), max = AnimalLandTargetRules.Integer(p, "max", int.MaxValue);
            return () => pack.MemberCount >= min && pack.MemberCount <= max;
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("land.healthBelow", (c, p) =>
        {
            float enter = AnimalLandTargetRules.Number(p, "enter", .22f), exit = AnimalLandTargetRules.Number(p, "exit", enter);
            string active = AnimalLandTargetRules.Text(p, "activeAction", "flee");
            return () => c.Health != null && c.Health.MaxHp > 0f && c.Health.Hp / c.Health.MaxHp < (c.IsActionActive(active) ? exit : enter);
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("land.retaliating", (c, p) =>
        {
            AnimalDamageRetaliationCapability retaliation = AnimalLandActionSupport.Retaliation(c, p);
            return () => retaliation.IsActive;
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("land.sleepNeeded", (c, p) =>
        {
            AnimalSleepCapability sleep = AnimalLandActionSupport.Sleep(c, p);
            return () => sleep.ShouldSleep;
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("land.forageNeeded", (c, p) =>
        {
            AnimalForagingCapability forage = AnimalLandActionSupport.Forage(c, p);
            string phase = AnimalLandTargetRules.Text(p, "phase", "any");
            return () => forage.TryChooseMeal(c.IsActionActive("eat"), out bool within) &&
                (phase == "any" || phase == "eat" && within || phase == "approach" && !within);
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("land.productionReady", (c, p) =>
        {
            AnimalTimedProductionCapability production = AnimalLandActionSupport.Production(c, p);
            return () => production.Ready;
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("land.mateRequested", (c, p) =>
        {
            AnimalMateRequestCapability mate = AnimalLandActionSupport.Capability(c, AnimalLandTargetRules.Text(p, "mateKey", "mate"),
                (context, config, saved) => new AnimalMateRequestCapability(context, config, saved));
            return () => mate.Requested;
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("land.damageReaction", (c, p) =>
        {
            AnimalLandActionSupport.Capability(c, AnimalLandTargetRules.Text(p, "reactionKey", "reaction"),
                (context, config, saved) => new AnimalDamageReactionCapability(context, config, saved));
            return () => true;
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("land.advanceReady", (c, p) =>
        {
            AnimalAdvanceCapability advance = AnimalLandActionSupport.Advance(c, p);
            return () => advance.HasCommand;
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("land.aggressiveAdvance", (c, p) =>
        {
            AnimalAdvanceCapability advance = AnimalLandActionSupport.Advance(c, p);
            return () => advance.HasCommand && advance.AttackActorsOnRoute;
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("land.searchReady", (c, p) =>
        {
            AnimalPackCapability pack = AnimalLandActionSupport.Pack(c, p);
            AnimalChasePositionCapability chase = Chase(c, p);
            return () => pack.Memory.IsSearching && !chase.IsBlocked(pack.Memory.Target);
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("land.attackReady", CreateAttackCondition);
        AnimalBehaviorTreeRegistry.RegisterCondition("land.attackLocked", (c, p) =>
        {
            AnimalCombatCapability combat = AnimalLandActionSupport.Combat(c, p);
            return () => combat.IsAttackLocked;
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("land.skillReady", (c, p) =>
        {
            AnimalCombatCapability combat = AnimalLandActionSupport.Combat(c, p);
            Func<AnimalLandTargetSnapshot> target = AnimalLandActionSupport.Target(c, p);
            return () => combat.HasActiveSkill || !combat.IsAttackLocked && target().Visible && combat.HasUsableSkill(target().Item);
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("land.skillActive", (c, p) =>
        {
            AnimalCombatCapability combat = AnimalLandActionSupport.Combat(c, p);
            return () => combat.HasActiveSkill;
        });
        AnimalBehaviorTreeRegistry.RegisterCondition("land.alert", (c, p) =>
        {
            AnimalPackCapability pack = AnimalLandActionSupport.Pack(c, p);
            return () => pack.HasAlert;
        });
    }

    internal static AnimalChasePositionCapability Chase(AnimalBehaviorTreeContext c, JObject p)
        => AnimalLandActionSupport.Capability(c, AnimalLandTargetRules.Text(p, "chaseKey", "chase"),
            (context, config, saved) => new AnimalChasePositionCapability(context, config, saved));

    private static Func<bool> CreateTargetCondition(AnimalBehaviorTreeContext c, JObject p)
    {
        Func<AnimalLandTargetSnapshot> target = AnimalLandActionSupport.Target(c, p);
        AnimalChasePositionCapability chase = Chase(c, p);
        bool requireVisible = AnimalLandTargetRules.Flag(p, "visible", true);
        float enter = AnimalLandTargetRules.Number(p, "distance", float.PositiveInfinity);
        float exit = AnimalLandTargetRules.Number(p, "continueDistance", enter);
        string action = AnimalLandTargetRules.Text(p, "activeAction", "chase");
        return () =>
        {
            AnimalLandTargetSnapshot current = target();
            if (current.Item == null || requireVisible && !current.Visible || chase.IsBlocked(current.Item)) return false;
            float range = c.IsActionActive(action) || c.IsActionActive("attack") || c.IsActionActive("search") ? exit : enter;
            return float.IsPositiveInfinity(range) || AnimalLandTargetRules.IsVisible(c, current.Item, range);
        };
    }

    private static Func<bool> CreateFleeCondition(AnimalBehaviorTreeContext c, JObject p)
    {
        Func<AnimalLandTargetSnapshot> target = AnimalLandActionSupport.Target(c, p);
        float enter = AnimalLandTargetRules.Number(p, "distance", 6f);
        float exit = AnimalLandTargetRules.Number(p, "safeDistance", 36f);
        bool damage = AnimalLandTargetRules.Flag(p, "recentDamage", true);
        string action = AnimalLandTargetRules.Text(p, "activeAction", "flee");
        return () =>
        {
            if (damage && c.HasRecentDamageThreat) return true;
            AnimalLandTargetSnapshot current = target();
            return current.Item != null && AnimalLandTargetRules.IsVisible(c, current.Item, c.IsActionActive(action) ? exit : enter);
        };
    }

    private static Func<bool> CreateAttackCondition(AnimalBehaviorTreeContext c, JObject p)
    {
        Func<AnimalLandTargetSnapshot> target = AnimalLandActionSupport.Target(c, p);
        AnimalCombatCapability combat = AnimalLandActionSupport.Combat(c, p);
        AnimalChasePositionCapability chase = Chase(c, p);
        bool requireCooldown = AnimalLandTargetRules.Flag(p, "requireCooldown", false);
        return () =>
        {
            AnimalLandTargetSnapshot current = target();
            return current.Item != null && current.Visible &&
                (combat.IsAttackLocked || (!requireCooldown || combat.IsCooldownDone) &&
                 chase.IsInsideRange(current.Position, combat.AttackRange) && chase.HasReachedSide(current.Item, combat.AttackRange));
        };
    }
    #endregion
}

internal sealed class AnimalWanderAction : AnimalBehaviorTreeNode
{
    #region 待机与游荡
    private readonly AnimalBehaviorTreeContext context;
    private readonly JObject config;
    private readonly AnimalPackCapability pack;
    private Vector3 target;
    private bool hasTarget;
    private float pause;
    public AnimalWanderAction(AnimalBehaviorTreeContext context, JObject p)
    {
        this.context = context;
        config = context.GetCapabilityConfiguration(AnimalLandTargetRules.Text(p, "wanderKey", "wander")) ?? new JObject();
        if (p["packKey"] != null) pack = AnimalLandActionSupport.Pack(context, p);
    }
    protected override void OnEnter()
    {
        pack?.ClearAfterCombat();
        hasTarget = false;
        pause = UnityEngine.Random.Range(AnimalLandTargetRules.Number(config, "idleMin", .8f), AnimalLandTargetRules.Number(config, "idleMax", 2.2f));
    }
    protected override AnimalBehaviorTreeStatus OnTick(float deltaTime)
    {
        if (!AnimalLandTargetRules.Flag(config, "enabled", true))
        { context.BeginAction("idle", "Idle", AIStateAnimationRole.Stopped); context.Mod_Mover.StopMovement(); return AnimalBehaviorTreeStatus.Running; }
        if (hasTarget && (WorldTopologyRuntime.Distance(context.Position, target) <= AnimalLandTargetRules.Number(config, "stopDistance", .4f) ||
            context.Mod_Mover.HasReachedTarget || context.Mod_Mover.DestinationResult == WorldNavigationDestinationResult.Failed))
        {
            hasTarget = false;
            pause = UnityEngine.Random.Range(AnimalLandTargetRules.Number(config, "pauseMin", .8f), AnimalLandTargetRules.Number(config, "pauseMax", 2.5f));
        }
        if (!hasTarget)
        {
            pause = Mathf.Max(0f, pause - deltaTime);
            if (pause > 0f)
            { context.BeginAction("idle", "Idle", AIStateAnimationRole.Stopped); context.Mod_Mover.StopMovement(); return AnimalBehaviorTreeStatus.Running; }
            float radius = AnimalLandTargetRules.Number(config, "radius", 5f);
            float stop = AnimalLandTargetRules.Number(config, "stopDistance", .4f);
            Vector2 offset = UnityEngine.Random.insideUnitCircle * radius;
            if (pack != null) offset = pack.ApplyWanderCohesion(offset, radius);
            offset = Vector2.ClampMagnitude(offset, radius);
            offset = AI_WanderUtility.PickSaferOffset(context.Position, offset, radius,
                AnimalLandTargetRules.Flag(config, "avoidHighPenalty", true), AnimalLandTargetRules.Integer(config, "sampleCount", 8),
                (uint)AnimalLandTargetRules.Integer(config, "dangerPenalty", 1200), AnimalLandTargetRules.Number(config, "penaltyWeight", 1f),
                Mathf.Min(radius, Mathf.Max(stop * 2f, radius * .35f)));
            target = WorldTopologyRuntime.NormalizePosition(context.Position + (Vector3)offset);
            hasTarget = true;
        }
        context.BeginAction("wander", "Move", AIStateAnimationRole.Moving);
        context.Mod_Mover.SetDestination(target);
        return AnimalBehaviorTreeStatus.Running;
    }
    protected override void OnExit() { hasTarget = false; context.Mod_Mover.StopMovement(); }
    #endregion
}

internal sealed class AnimalFleeAction : AnimalBehaviorTreeNode
{
    #region 逃离
    private readonly AnimalBehaviorTreeContext context;
    private readonly Func<AnimalLandTargetSnapshot> target;
    private readonly AIFleeStateNode flee;
    private readonly string action;
    private readonly bool recentDamage;
    private readonly AnimalPackCapability pack;
    public AnimalFleeAction(AnimalBehaviorTreeContext context, JObject p)
    {
        this.context = context;
        target = AnimalLandActionSupport.Target(context, p);
        action = AnimalLandTargetRules.Text(p, "action", "flee");
        recentDamage = AnimalLandTargetRules.Flag(p, "recentDamage", true);
        if (AnimalLandTargetRules.Text(p, "source") == "pack") pack = AnimalLandActionSupport.Pack(context, p);
        float distance = AnimalLandTargetRules.Number(p, "runDistance", 36f);
        flee = new AIFleeStateNode(action, ResolveOrigin,
            (origin, length) => AIFleeUtility.ResolveNavigableGroundEscapeDestination(context.Position, origin, length,
                WorldTopologyRuntime.ShortestDelta(origin, context.Position)),
            () => distance, destination => context.Mod_Mover.SetDestination(destination),
            context.Mod_Mover.StopMovement, () => context.Position, () => context.Mod_Mover.HasReachedTarget,
            () => context.Mod_Mover.DestinationResult, context.Mod_Mover);
    }
    private Vector3? ResolveOrigin()
    {
        if (recentDamage && context.TryGetRecentDamageOrigin(out Vector3 origin)) return origin;
        AnimalLandTargetSnapshot current = target();
        return current.Item != null ? current.Position : (Vector3?)null;
    }
    private void HandleDamage() => flee.Retarget();
    protected override void OnEnter()
    {
        pack?.Memory.StopPursuing();
        pack?.ClearFormation();
        context.Damaged += HandleDamage;
        flee.Enter();
    }
    protected override AnimalBehaviorTreeStatus OnTick(float deltaTime)
    {
        context.BeginAction(action, "Move", AIStateAnimationRole.Moving);
        flee.Tick(deltaTime);
        return AnimalBehaviorTreeStatus.Running;
    }
    protected override void OnExit() { context.Damaged -= HandleDamage; flee.Exit(); }
    #endregion
}

internal sealed class AnimalSleepAction : AnimalBehaviorTreeNode
{
    private readonly AnimalBehaviorTreeContext context;
    private readonly AnimalSleepCapability sleep;
    public AnimalSleepAction(AnimalBehaviorTreeContext context, JObject p) { this.context = context; sleep = AnimalLandActionSupport.Sleep(context, p); }
    protected override void OnEnter() => sleep.EnterSleep();
    protected override AnimalBehaviorTreeStatus OnTick(float deltaTime)
    {
        context.BeginAction("sleep", "Stand", AIStateAnimationRole.Stopped);
        context.Mod_Mover.StopMovement();
        return AnimalBehaviorTreeStatus.Running;
    }
    protected override void OnExit() { sleep.ExitSleep(); context.Mod_Mover.StopMovement(); }
}

internal sealed class AnimalForageAction : AnimalBehaviorTreeNode
{
    private readonly AnimalBehaviorTreeContext context;
    private readonly AnimalForagingCapability forage;
    private bool eating;
    private float eatElapsed;
    public AnimalForageAction(AnimalBehaviorTreeContext context, JObject p) { this.context = context; forage = AnimalLandActionSupport.Forage(context, p); }
    protected override AnimalBehaviorTreeStatus OnTick(float deltaTime)
    {
        if (!forage.TryChooseMeal(eating, out bool inRange)) return AnimalBehaviorTreeStatus.Failure;
        if (eating != inRange) { eating = inRange; eatElapsed = 0f; }
        if (eating) eatElapsed += deltaTime;
        context.BeginAction(eating ? "eat" : "forage", eating ? "Stand" : "Move",
            eating ? AIStateAnimationRole.Action : AIStateAnimationRole.Moving);
        forage.TickMeal(eating, eatElapsed, deltaTime);
        return AnimalBehaviorTreeStatus.Running;
    }
    protected override void OnExit() { eating = false; eatElapsed = 0f; forage.ClearTargets(); context.Mod_Mover.StopMovement(); }
}

internal sealed class AnimalProduceAction : AnimalBehaviorTreeNode
{
    private readonly AnimalBehaviorTreeContext context;
    private readonly AnimalTimedProductionCapability production;
    private readonly float duration;
    private float elapsed;
    public AnimalProduceAction(AnimalBehaviorTreeContext context, JObject p)
    { this.context = context; production = AnimalLandActionSupport.Production(context, p); duration = AnimalLandTargetRules.Number(p, "duration", 2f); }
    protected override void OnEnter() => elapsed = 0f;
    protected override AnimalBehaviorTreeStatus OnTick(float deltaTime)
    {
        context.BeginAction("production", "Stand", AIStateAnimationRole.Action);
        context.Mod_Mover.StopMovement();
        elapsed += deltaTime;
        if (elapsed < duration) return AnimalBehaviorTreeStatus.Running;
        production.Produce();
        return AnimalBehaviorTreeStatus.Success;
    }
}

internal sealed class AnimalAdvanceAction : AnimalBehaviorTreeNode
{
    private readonly AnimalBehaviorTreeContext context;
    private readonly AnimalAdvanceCapability advance;
    public AnimalAdvanceAction(AnimalBehaviorTreeContext context, JObject p) { this.context = context; advance = AnimalLandActionSupport.Advance(context, p); }
    protected override void OnEnter()
    {
        if (context.TryGetCapability("pack", out AnimalPackCapability pack)) pack.ClearAfterCombat();
    }
    protected override AnimalBehaviorTreeStatus OnTick(float deltaTime)
    {
        if (!advance.HasCommand) return AnimalBehaviorTreeStatus.Failure;
        context.BeginAction("advance", "Move", AIStateAnimationRole.Moving);
        Vector3 destination = advance.TargetPosition;
        if (!AnimalAdvanceCapability.IsFinite(destination))
        { context.Mod_Mover.StopMovement(); return AnimalBehaviorTreeStatus.Running; }
        if (WorldTopologyRuntime.Distance(context.Position, destination) <= advance.ArrivalDistance)
        { advance.CancelAdvance(); return AnimalBehaviorTreeStatus.Success; }
        context.Mod_Mover.SetInteractionDestination(destination, advance.ArrivalDistance);
        return AnimalBehaviorTreeStatus.Running;
    }
    protected override void OnExit() => context.Mod_Mover.StopMovement();
}

internal sealed class AnimalMateAction : AnimalBehaviorTreeNode
{
    private readonly AnimalBehaviorTreeContext context;
    public AnimalMateAction(AnimalBehaviorTreeContext context, JObject p) => this.context = context;
    protected override AnimalBehaviorTreeStatus OnTick(float deltaTime)
    {
        context.BeginAction("mate", "Stand", AIStateAnimationRole.Stopped);
        context.Mod_Mover.StopMovement();
        return AnimalBehaviorTreeStatus.Running;
    }
    protected override void OnExit() => context.Mod_Mover.StopMovement();
}

internal sealed class AnimalChaseAction : AnimalBehaviorTreeNode
{
    private readonly AnimalBehaviorTreeContext context;
    private readonly Func<AnimalLandTargetSnapshot> target;
    private readonly AnimalChasePositionCapability chase;
    private readonly AnimalCombatCapability combat;
    private readonly AnimalPackCapability pack;
    public AnimalChaseAction(AnimalBehaviorTreeContext context, JObject p)
    {
        this.context = context;
        target = AnimalLandActionSupport.Target(context, p);
        chase = AnimalLandBehaviorNodes.Chase(context, p);
        combat = AnimalLandActionSupport.Combat(context, p);
        if (AnimalLandTargetRules.Text(p, "source") == "pack") pack = AnimalLandActionSupport.Pack(context, p);
    }
    protected override AnimalBehaviorTreeStatus OnTick(float deltaTime)
    {
        AnimalLandTargetSnapshot current = target();
        if (current.Item == null || !current.Visible) return AnimalBehaviorTreeStatus.Failure;
        context.BeginAction("chase", "Move", AIStateAnimationRole.Moving);
        pack?.Memory.MarkPursuing();
        chase.MoveToward(current.Item, current.Position, combat.AttackRange, pack);
        return AnimalBehaviorTreeStatus.Running;
    }
    protected override void OnExit() => context.Mod_Mover.StopMovement();
}

internal sealed class AnimalAttackAction : AnimalBehaviorTreeNode
{
    private readonly AnimalBehaviorTreeContext context;
    private readonly Func<AnimalLandTargetSnapshot> target;
    private readonly AnimalChasePositionCapability chase;
    private readonly AnimalCombatCapability combat;
    private readonly AnimalPackCapability pack;
    private readonly AnimalTerritorialHatredCapability territory;
    private readonly bool trySkills;
    public AnimalAttackAction(AnimalBehaviorTreeContext context, JObject p)
    {
        this.context = context;
        target = AnimalLandActionSupport.Target(context, p);
        chase = AnimalLandBehaviorNodes.Chase(context, p);
        combat = AnimalLandActionSupport.Combat(context, p);
        string source = AnimalLandTargetRules.Text(p, "source");
        trySkills = AnimalLandTargetRules.Flag(p, "trySkills", false);
        if (source == "pack") pack = AnimalLandActionSupport.Pack(context, p);
        if (source == "territory")
            territory = AnimalLandActionSupport.Capability(context, AnimalLandTargetRules.Text(p, "sourceKey", source),
                (c, config, saved) => new AnimalTerritorialHatredCapability(c, config, saved));
    }
    protected override void OnEnter() => combat.EnterAttack();
    protected override AnimalBehaviorTreeStatus OnTick(float deltaTime)
    {
        AnimalLandTargetSnapshot current = target();
        if (current.Item == null || !current.Visible)
        { combat.StopAttack(true); context.Mod_Mover.StopMovement(); return AnimalBehaviorTreeStatus.Failure; }
        context.BeginAction("attack", "Attack", AIStateAnimationRole.Action);
        pack?.Memory.MarkPursuing();
        if (territory != null) territory.LockCurrentTarget = combat.IsAttackLocked;
        if (combat.HasActiveSkill) { combat.StopAttack(false); context.Mod_Mover.StopMovement(); return AnimalBehaviorTreeStatus.Running; }
        if (trySkills && !combat.IsAttackLocked && combat.TryStartSkill(current.Item))
        { combat.StopAttack(false); context.Mod_Mover.StopMovement(); return AnimalBehaviorTreeStatus.Running; }
        context.FaceTarget(current.Position, true);
        if (combat.IsAttackLocked) context.Mod_Mover.StopMovement();
        else if (combat.IsCooldownDone && chase.IsInsideRange(current.Position, combat.AttackRange) &&
                 chase.HasReachedSide(current.Item, combat.AttackRange))
        {
            context.Mod_Mover.StopMovement();
            combat.StartAttack(current.Position);
            if (territory != null) territory.LockCurrentTarget = combat.IsAttackLocked;
        }
        else chase.MoveToward(current.Item, current.Position, combat.AttackRange, pack);
        pack?.CallNearbyAllies();
        return AnimalBehaviorTreeStatus.Running;
    }
    protected override void OnExit()
    {
        combat.StopAttack(true);
        if (territory != null) territory.LockCurrentTarget = false;
        context.Mod_Mover.StopMovement();
    }
}

internal sealed class AnimalSkillAction : AnimalBehaviorTreeNode
{
    private readonly AnimalBehaviorTreeContext context;
    private readonly AnimalCombatCapability combat;
    private readonly Func<AnimalLandTargetSnapshot> target;
    public AnimalSkillAction(AnimalBehaviorTreeContext context, JObject p)
    { this.context = context; combat = AnimalLandActionSupport.Combat(context, p); target = AnimalLandActionSupport.Target(context, p); }
    protected override void OnEnter()
    {
        if (!combat.HasActiveSkill) combat.TryStartSkill(target().Item);
    }
    protected override AnimalBehaviorTreeStatus OnTick(float deltaTime)
    {
        context.BeginAction("skill", "Attack", AIStateAnimationRole.Action);
        return combat.HasActiveSkill ? AnimalBehaviorTreeStatus.Running : AnimalBehaviorTreeStatus.Success;
    }
    protected override void OnExit() => combat.CancelSkills();
}

internal sealed class AnimalSearchAction : AnimalBehaviorTreeNode
{
    #region 赶往最后可见位置
    private readonly AnimalBehaviorTreeContext context;
    private readonly AnimalPackCapability pack;
    private readonly AnimalChasePositionCapability chase;
    private readonly int pathCostLimit;
    public AnimalSearchAction(AnimalBehaviorTreeContext context, JObject p)
    {
        this.context = context;
        pack = AnimalLandActionSupport.Pack(context, p);
        chase = AnimalLandBehaviorNodes.Chase(context, p);
        pathCostLimit = AnimalLandTargetRules.Integer(context.GetCapabilityConfiguration(AnimalLandTargetRules.Text(p, "chaseKey", "chase")), "pathCostLimit", 440);
    }
    protected override void OnEnter() => pack.ClearFormation();
    protected override AnimalBehaviorTreeStatus OnTick(float deltaTime)
    {
        AnimalTargetMemoryCapability memory = pack.Memory;
        if (!memory.IsSearching) return AnimalBehaviorTreeStatus.Failure;
        context.BeginAction("search", "Move", AIStateAnimationRole.Moving);
        memory.TickSearch(deltaTime);
        if (!memory.IsSearching) return AnimalBehaviorTreeStatus.Success;
        if (memory.WaitingAtLastPosition) return AnimalBehaviorTreeStatus.Running;
        WorldNavigationDestinationResult result = context.Mod_Mover.SetInteractionDestination(memory.LastKnownPosition,
            memory.ArrivalDistance, pathCostLimit);
        if (result == WorldNavigationDestinationResult.RejectedByPathCost || result == WorldNavigationDestinationResult.Failed)
        { memory.Forget(); context.Mod_Mover.StopMovement(); return AnimalBehaviorTreeStatus.Failure; }
        context.FaceTarget(memory.LastKnownPosition, false);
        return AnimalBehaviorTreeStatus.Running;
    }
    protected override void OnExit() => context.Mod_Mover.StopMovement();
    #endregion
}

internal sealed class AnimalAlertAction : AnimalBehaviorTreeNode
{
    private readonly AnimalBehaviorTreeContext context;
    private readonly Func<AnimalLandTargetSnapshot> target;
    public AnimalAlertAction(AnimalBehaviorTreeContext context, JObject p) { this.context = context; target = AnimalLandActionSupport.Target(context, p); }
    protected override AnimalBehaviorTreeStatus OnTick(float deltaTime)
    {
        context.BeginAction("alert", "Idle", AIStateAnimationRole.Stopped);
        context.Mod_Mover.StopMovement();
        AnimalLandTargetSnapshot current = target();
        if (current.Item != null) context.FaceTarget(current.Position, false);
        return AnimalBehaviorTreeStatus.Running;
    }
}
