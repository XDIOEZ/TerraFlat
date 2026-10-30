using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>为行为节点把不同目标能力转换成同一份目标快照。</summary>
internal readonly struct AnimalLandTargetSnapshot
{
    public readonly Item Item;
    public readonly bool Visible;
    public readonly Vector3 Position;
    public AnimalLandTargetSnapshot(Item item, bool visible, Vector3 position)
    { Item = item; Visible = visible; Position = position; }
}

internal static class AnimalLandActionSupport
{
    #region 能力装配与目标读取
    public static T Capability<T>(AnimalBehaviorTreeContext context, string key,
        Func<AnimalBehaviorTreeContext, JObject, JObject, T> factory) where T : AnimalBehaviorTreeCapability
        => context.GetOrCreateCapability(key, saved => factory(context, context.GetCapabilityConfiguration(key), saved));

    public static AnimalCombatCapability Combat(AnimalBehaviorTreeContext context, JObject p)
        => Capability(context, AnimalLandTargetRules.Text(p, "combatKey", "combat"),
            (c, config, saved) => new AnimalCombatCapability(c, config, saved));
    public static AnimalForagingCapability Forage(AnimalBehaviorTreeContext context, JObject p)
        => Capability(context, AnimalLandTargetRules.Text(p, "forageKey", "forage"),
            (c, config, saved) => new AnimalForagingCapability(c, config, saved));
    public static AnimalSleepCapability Sleep(AnimalBehaviorTreeContext context, JObject p)
        => Capability(context, AnimalLandTargetRules.Text(p, "sleepKey", "sleep"),
            (c, config, saved) => new AnimalSleepCapability(c, config, saved));
    public static AnimalTimedProductionCapability Production(AnimalBehaviorTreeContext context, JObject p)
        => Capability(context, AnimalLandTargetRules.Text(p, "productionKey", "production"),
            (c, config, saved) => new AnimalTimedProductionCapability(c, config, saved));
    public static AnimalAdvanceCapability Advance(AnimalBehaviorTreeContext context, JObject p)
        => Capability(context, AnimalLandTargetRules.Text(p, "advanceKey", "advance"),
            (c, config, saved) => new AnimalAdvanceCapability(c, config, saved));
    public static AnimalPackCapability Pack(AnimalBehaviorTreeContext context, JObject p)
        => Capability(context, AnimalLandTargetRules.Text(p, "packKey", "pack"),
            (c, config, saved) => new AnimalPackCapability(c, config, saved));
    public static AnimalDamageRetaliationCapability Retaliation(AnimalBehaviorTreeContext context, JObject p)
        => Capability(context, AnimalLandTargetRules.Text(p, "retaliationKey", "retaliation"),
            (c, config, saved) => new AnimalDamageRetaliationCapability(c, config, saved));

    public static Func<AnimalLandTargetSnapshot> Target(AnimalBehaviorTreeContext context, JObject p)
    {
        string source = AnimalLandTargetRules.Text(p, "source", "threat");
        string key = AnimalLandTargetRules.Text(p, "sourceKey", source);
        switch (source)
        {
            case "pack":
                AnimalPackCapability pack = Capability(context, key, (c, config, saved) => new AnimalPackCapability(c, config, saved));
                return () => new AnimalLandTargetSnapshot(pack.Memory.Target, pack.Memory.Visible, pack.Memory.LastKnownPosition);
            case "territory":
                AnimalTerritorialHatredCapability territory = Capability(context, key, (c, config, saved) => new AnimalTerritorialHatredCapability(c, config, saved));
                return () => new AnimalLandTargetSnapshot(territory.CurrentTarget, territory.CurrentTarget != null,
                    territory.CurrentTarget != null ? territory.CurrentTarget.transform.position : default);
            case "retaliation":
                AnimalDamageRetaliationCapability retaliation = Capability(context, key, (c, config, saved) => new AnimalDamageRetaliationCapability(c, config, saved));
                return () => new AnimalLandTargetSnapshot(retaliation.IsActive ? retaliation.Target : null, retaliation.IsActive && retaliation.Target != null,
                    retaliation.Target != null ? retaliation.Target.transform.position : default);
            case "hunting":
                AnimalHuntingCapability hunting = Capability(context, key, (c, config, saved) => new AnimalHuntingCapability(c, config, saved));
                return () => new AnimalLandTargetSnapshot(hunting.Target, hunting.TargetVisible,
                    hunting.Target != null ? hunting.Target.transform.position : default);
            case "threat":
                AnimalPerceivedThreatCapability threat = Capability(context, key, (c, config, saved) => new AnimalPerceivedThreatCapability(c, config, saved));
                return () => new AnimalLandTargetSnapshot(threat.Target, threat.Target != null,
                    threat.Target != null ? threat.Target.transform.position : default);
            default: throw new InvalidOperationException($"未知动物目标能力：{source}");
        }
    }
    #endregion
}

/// <summary>追击路径代价拒绝重试和可选左右站位。</summary>
public sealed class AnimalChasePositionCapability : AnimalBehaviorTreeCapability
{
    #region 路径和站位
    private readonly AnimalBehaviorTreeContext context;
    private readonly JObject config;
    private Item rejectedTarget;
    private float retryRemaining;
    private Item sideTarget;
    private int side;

    public AnimalChasePositionCapability(AnimalBehaviorTreeContext context, JObject config, JObject saved)
    { this.context = context; this.config = config ?? new JObject(); }
    public override void Tick(float deltaTime)
    {
        if (retryRemaining <= 0f) return;
        retryRemaining = Mathf.Max(0f, retryRemaining - deltaTime);
        if (retryRemaining > 0f) return;
        rejectedTarget = null;
        if (context.Mod_Mover?.DestinationResult == WorldNavigationDestinationResult.RejectedByPathCost) context.Mod_Mover.ForceRepath();
    }
    public bool IsBlocked(Item target) => target != null && target == rejectedTarget && retryRemaining > 0f;
    public bool HasSidePosition => AnimalLandTargetRules.Flag(config, "sidePosition", false);

    public bool IsInsideRange(Vector3 target, float horizontal)
    {
        float vertical = AnimalLandTargetRules.Number(config, "verticalRange", horizontal);
        Vector2 delta = WorldTopologyRuntime.ShortestDelta(context.Position, target);
        float x = delta.x / Mathf.Max(.01f, horizontal), y = delta.y / Mathf.Max(.01f, vertical);
        return x * x + y * y <= 1f;
    }
    public bool HasReachedSide(Item target, float range)
    {
        if (!HasSidePosition) return true;
        Vector3 position = SidePosition(target, range);
        return IsInsideRange(target.transform.position, range) && WorldTopologyRuntime.Distance(context.Position, position) <=
            Mathf.Max(.05f, AnimalLandTargetRules.Number(config, "sideTolerance", .4f));
    }
    public Vector3 SidePosition(Item target, float range)
    {
        if (target != sideTarget)
        {
            sideTarget = target;
            float horizontal = WorldTopologyRuntime.ShortestDelta(target.transform.position, context.Position).x;
            side = Mathf.Abs(horizontal) > .05f ? horizontal > 0f ? 1 : -1 :
                ((context.Actor.itemData?.Guid ?? context.Actor.GetInstanceID()) & 1) == 0 ? -1 : 1;
        }
        float distance = Mathf.Clamp(AnimalLandTargetRules.Number(config, "sideDistance", 1f), .1f, Mathf.Max(.1f, range - .05f));
        Vector3 position = target.transform.position;
        position.x += side * distance;
        position.z = context.Position.z;
        return WorldTopologyRuntime.NormalizePosition(position);
    }
    public void MoveToward(Item target, Vector3 targetPosition, float range, AnimalPackCapability pack = null)
    {
        if (target == null || IsBlocked(target)) { context.Mod_Mover.StopMovement(); return; }
        Vector3 destination = targetPosition;
        float approach = range;
        if (HasSidePosition)
        {
            destination = SidePosition(target, range);
            float horizontalMargin = range - Mathf.Abs(WorldTopologyRuntime.ShortestDelta(targetPosition, destination).x);
            approach = Mathf.Max(.01f, Mathf.Min(AnimalLandTargetRules.Number(config, "sideTolerance", .4f),
                Mathf.Min(horizontalMargin, AnimalLandTargetRules.Number(config, "verticalRange", range))));
        }
        else if (pack?.FormationTarget is Vector3 formation)
        {
            destination = formation;
            approach = Mathf.Max(.01f, range - WorldTopologyRuntime.Distance(formation, targetPosition));
            if (pack.ShouldHoldFormationPosition(approach, range))
            { context.Mod_Mover.StopMovement(); context.FaceTarget(targetPosition, false); pack.CallNearbyAllies(); return; }
        }
        WorldNavigationDestinationResult result = context.Mod_Mover.SetInteractionDestination(destination, approach,
            AnimalLandTargetRules.Integer(config, "pathCostLimit", 440));
        if (result == WorldNavigationDestinationResult.RejectedByPathCost)
        {
            rejectedTarget = target;
            retryRemaining = Mathf.Max(.1f, AnimalLandTargetRules.Number(config, "retryDelay", 3f));
            context.Mod_Mover.StopMovement();
            return;
        }
        context.FaceTarget(destination, false);
        pack?.CallNearbyAllies();
    }
    public void ClearPosition() { sideTarget = null; side = 0; }
    public override void Dispose() { rejectedTarget = null; ClearPosition(); }
    #endregion
}
