using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using UnityEngine;

public sealed partial class Mod_Minecart
{
    #region 实体表面与推动
    public const float DefaultRailCaptureDistance = .25f;

    public static Vector2 ResolvePlacementPosition(Item host, Vector2 position, float captureDistance = DefaultRailCaptureDistance)
        => RailTrackNetwork.TryProject(host, position, out RailPosition rail) &&
            (rail.Position - position).sqrMagnitude <= captureDistance * captureDistance ? rail.Position : position;

    public void ActivateCarrier(Mod_Carrier carrier, Rigidbody2D body)
    {
        WorldPhysicsService2D.ApplyLegacyItemLinearDamping(item, body, 0f);
        body.useFullKinematicContacts = false;
    }

    public Vector2 ResolveCarrierPush(Vector2 velocity, bool sourceInWater)
        => MinecartDrivingRules.ResolvePushVelocity(velocity, Vector2.zero, false, PushSpeedMultiplier);

    public Vector2 ResolvePusherLimit(Vector2 velocity)
    {
        int count = train?.Cars.Count ?? 1, ground = 0;
        float multiplier = GroundSpeedMultiplier;
        if (train != null)
            foreach (Mod_Minecart car in train.Cars)
            {
                if (!car.onRail) ground++;
                multiplier = Mathf.Min(multiplier, car.GroundSpeedMultiplier);
            }
        else if (!onRail) ground = 1;
        if (train != null) ground = Mathf.Max(ground, train.GroundResistanceCount);
        float scale = MinecartDrivingRules.SurfaceMultiplier(count, ground, multiplier) / count;
        return MinecartDrivingRules.ResolvePushVelocity(velocity, railPosition.Tangent, onRail, scale);
    }

    private void SnapToRail()
    {
        if (!IsAvailable || Carrier.Body == null) return;
        Vector2 position = ResolvePlacementPosition(item, Carrier.Body.position, RailCaptureDistance);
        Carrier.Body.position = position;
        onRail = TryRail(out railPosition);
    }

    private bool TryRail(out RailPosition result)
    {
        Vector2 actual = Carrier?.Body != null ? Carrier.Body.position : (Vector2)item.transform.position;
        if (railPosition.IsValid)
        {
            float progress = RailPathGeometry.Project(railPosition.Route, actual - railPosition.Center);
            var projected = new RailPosition(railPosition.Track, progress, railPosition.Sign, railPosition.Center);
            for (int end = -1; end <= 1; end += 2)
            {
                var edge = new RailPosition(projected.Track, end < 0 ? 0f : projected.Length,
                    projected.Sign, projected.Center);
                float sign = end * projected.Sign;
                float beyond = Vector2.Dot(actual - edge.Position, edge.Tangent * sign);
                if (beyond <= .00001f || !RailTrackNetwork.TryAdvance(item, edge, sign * (beyond + .0001f),
                    out RailPosition adjacent, out _) || adjacent.Track == projected.Track) continue;
                float nextProgress = RailPathGeometry.Project(adjacent.Route, actual - adjacent.Center);
                var nextPosition = new RailPosition(adjacent.Track, nextProgress, adjacent.Sign, adjacent.Center);
                if ((nextPosition.Position - actual).sqrMagnitude <= (projected.Position - actual).sqrMagnitude + .00000001f)
                    projected = nextPosition;
            }
            if (CanCapture(projected, actual)) { result = projected; return true; }
        }
        if (RailTrackNetwork.TryProject(item, actual, out result) && CanCapture(result, actual)) return true;
        result = default; return false;
    }

    private bool CanCapture(RailPosition rail, Vector2 actual)
    {
        float direction = rail.Progress <= .0001f ? -rail.Sign :
            rail.Progress >= rail.Length - .0001f ? rail.Sign : 0f;
        bool openEndpoint = direction != 0f && !RailTrackNetwork.TryAdvance(item, rail, direction * .001f, out _, out _);
        float outward = Carrier.Body != null ? Vector2.Dot(Carrier.Body.velocity, rail.Tangent * direction) : 0f;
        return MinecartDrivingRules.ShouldCaptureRail(Vector2.Distance(actual, rail.Position), RailCaptureDistance, outward, openEndpoint);
    }

    private bool TryCouplingDistance(Mod_Minecart other, float maximum, out float distance)
    {
        distance = WorldTopologyRuntime.Distance(item.transform.position, other.item.transform.position);
        if (distance > maximum) return false;
        if (TryRail(out RailPosition here) && other.TryRail(out RailPosition there))
        {
            if (!RailTrackNetwork.TrySignedDistance(item, here, there, maximum, out float along)) return false;
            distance = Mathf.Abs(along);
        }
        return distance >= .8f;
    }

    private float LimitMovement(Vector2 target)
    {
        Vector2 delta = target - Carrier.Body.position;
        float distance = delta.magnitude;
        if (distance <= .000001f) return 1f;
        if (!Carrier.CanTraversePosition(target)) return 0f;
        Vector2 direction = delta / distance;
        var filter = new ContactFilter2D { useTriggers = false, useLayerMask = true,
            layerMask = Physics2D.GetLayerCollisionMask(item.gameObject.layer) };
        int count = Carrier.Body.Cast(direction, filter, contactHits, distance + .002f);
        float allowed = distance;
        for (int i = 0; i < count; i++)
        {
            RaycastHit2D hit = contactHits[i];
            if (hit.collider == null || hit.collider.isTrigger || hit.rigidbody == Carrier.Body ||
                hit.normal.sqrMagnitude > .01f && Vector2.Dot(direction, hit.normal) >= -.001f) continue;
            bool ownTrain = false;
            foreach (Mod_Minecart car in train.Cars)
                if (hit.rigidbody == car.Carrier.Body) { ownTrain = true; break; }
            if (!ownTrain) allowed = Mathf.Min(allowed, Mathf.Max(0f, hit.distance - .002f));
        }
        return count >= contactHits.Length ? 0f : Mathf.Clamp01(allowed / distance);
    }

    public void StepCarrierMotion(Mod_Carrier carrier, Vector2 input, bool locked, float deltaTime)
    {
        requestedInput = input; controlsLocked = locked; lastInputAt = Time.time;
        if (locked || input.sqrMagnitude <= .001f)
        { heldDrivingDirection = Vector2.zero; heldDrivingSign = 0f; }
        train ??= new MinecartTrain(this);
        if (!ReferenceEquals(train.Cars[0], this) || train.LastStep == Time.fixedTime) return;
        train.LastStep = Time.fixedTime; train.Step(deltaTime);
    }

    public void StopCarrierMotion()
    {
        requestedInput = Vector2.zero; controlsLocked = true;
        heldDrivingDirection = Vector2.zero; heldDrivingSign = 0f;
        if (train != null) train.Speed = 0f;
    }
    #endregion

    #region 实体车钩
    private void EnsureCoupler()
    {
        if (previous == null || Carrier?.Body == null || previous.Carrier?.Body == null)
        { ReleaseCoupler(); return; }
        if (coupler != null && coupler.connectedBody == previous.Carrier.Body) return;
        ReleaseCoupler();
        coupler = item.gameObject.AddComponent<DistanceJoint2D>();
        coupler.autoConfigureConnectedAnchor = false; coupler.autoConfigureDistance = false;
        coupler.anchor = coupler.connectedAnchor = Vector2.zero;
        coupler.connectedBody = previous.Carrier.Body;
        coupler.maxDistanceOnly = true;
        float spacing = state.PreviousSpacing;
        if (!float.IsFinite(spacing) || spacing < .8f)
            spacing = WorldTopologyRuntime.Distance(item.transform.position, previous.item.transform.position);
        state.PreviousSpacing = Mathf.Clamp(spacing, .8f, CouplingDistance);
        coupler.distance = state.PreviousSpacing;
        coupler.enableCollision = true;
    }

    private void ReleaseCoupler()
    {
        if (coupler == null) return;
        coupler.enabled = false; UnityEngine.Object.Destroy(coupler); coupler = null;
    }

    private static void Rebuild(Mod_Minecart member)
    {
        if (member == null) return;
        var seen = new HashSet<Mod_Minecart>();
        Mod_Minecart first = member;
        while (first.previous != null && seen.Add(first)) first = first.previous;
        var group = new MinecartTrain(first); seen.Clear();
        for (Mod_Minecart current = first; current != null && seen.Add(current); current = current.next)
        {
            if (current != first) group.Cars.Add(current);
            current.train = group; current.railPosition = default;
            current.onRail = false;
            current.heldDrivingDirection = Vector2.zero; current.heldDrivingSign = 0f;
            current.EnsureCoupler();
        }
    }

    private static void ReverseTrain(MinecartTrain group)
    {
        var oldSpacings = new float[group.Cars.Count];
        for (int i = 0; i < group.Cars.Count; i++) oldSpacings[i] = group.Cars[i].state.PreviousSpacing;
        for (int i = 0; i < group.Cars.Count; i++)
        {
            Mod_Minecart cart = group.Cars[i]; cart.ReleaseCoupler();
            (cart.previous, cart.next) = (cart.next, cart.previous);
            (cart.state.PreviousGuid, cart.state.NextGuid) = (cart.state.NextGuid, cart.state.PreviousGuid);
            cart.state.PreviousSpacing = i + 1 < oldSpacings.Length ? oldSpacings[i + 1] : 0f;
            cart.railPosition = RailTrackNetwork.Reverse(cart.railPosition);
        }
        group.Cars.Reverse();
    }

    private static void IgnoreTrainContacts(MinecartTrain group, bool allowRailOverlap)
    {
        if (group == null) return;
        for (int i = 0; i < group.Cars.Count; i++) for (int j = i + 1; j < group.Cars.Count; j++)
        {
            Mod_Minecart a = group.Cars[i], b = group.Cars[j];
            Collider2D ca = a.item != null ? a.item.GetComponent<BoxCollider2D>() : null;
            Collider2D cb = b.item != null ? b.item.GetComponent<BoxCollider2D>() : null;
            if (ca != null && cb != null) Physics2D.IgnoreCollision(ca, cb, allowRailOverlap && a.onRail && b.onRail);
        }
    }
    #endregion

    #region 整列实体运动
    private sealed class MinecartTrain
    {
        public readonly List<Mod_Minecart> Cars = new();
        public float Speed;
        public float LastStep = float.NegativeInfinity;
        public int GroundResistanceCount { get; private set; }
        private readonly List<RailPosition> positions = new();
        private readonly List<Vector2> velocities = new();
        private readonly HashSet<Mod_Mover> pushers = new();
        private readonly List<bool> exits = new();
        private int groundCount;
        private float groundMultiplier, maximum, required;
        private Vector2 driveWorld, pushWorld;
        private float driveSign, pushSign;
        private bool driverOnRail;
        private Mod_Minecart drivingCart;
        private bool railPush;
        public MinecartTrain(Mod_Minecart first) => Cars.Add(first);

        private bool RefreshPositions()
        {
            foreach (Mod_Minecart cart in Cars)
                if (!cart.IsAvailable || cart.Carrier?.Body == null || cart.inventory?.CargoInventory == null || cart.HasUnresolvedConnection)
                    return false;
            positions.Clear(); groundCount = 0; groundMultiplier = 1f;
            maximum = float.PositiveInfinity; required = 0f;
            bool changed = false;
            for (int i = 0; i < Cars.Count; i++)
            {
                Mod_Minecart cart = Cars[i];
                bool rail = cart.TryRail(out RailPosition position);
                if (rail && !cart.onRail && Cars.Count > 1)
                {
                    Vector2 forward = i + 1 < Cars.Count
                        ? WorldTopologyRuntime.ShortestDelta(cart.Carrier.Body.position, Cars[i + 1].Carrier.Body.position)
                        : WorldTopologyRuntime.ShortestDelta(Cars[i - 1].Carrier.Body.position, cart.Carrier.Body.position);
                    if (Vector2.Dot(position.Tangent, forward) < 0f) position = RailTrackNetwork.Reverse(position);
                }
                changed |= cart.onRail != rail;
                cart.onRail = rail; cart.railPosition = position;
                if (rail) cart.motionHeading = position.Tangent;
                else
                {
                    groundCount++;
                    if (Cars.Count > 1)
                    {
                        Vector2 forward = i + 1 < Cars.Count
                            ? WorldTopologyRuntime.ShortestDelta(cart.Carrier.Body.position, Cars[i + 1].Carrier.Body.position)
                            : WorldTopologyRuntime.ShortestDelta(Cars[i - 1].Carrier.Body.position, cart.Carrier.Body.position);
                        if (forward.sqrMagnitude > .0001f) cart.motionHeading = forward.normalized;
                    }
                }
                positions.Add(position);
                groundMultiplier = Mathf.Min(groundMultiplier, cart.GroundSpeedMultiplier);
                maximum = Mathf.Min(maximum, cart.MaxSpeed); required += cart.PowerPerCart;
                cart.EnsureCoupler();
            }
            if (changed) IgnoreTrainContacts(this, true);
            return true;
        }

        private bool ReadInputs()
        {
            bool driver = false; driveWorld = pushWorld = Vector2.zero; driveSign = pushSign = 0f; driverOnRail = railPush = false;
            drivingCart = null;
            pushers.Clear();
            for (int i = 0; i < Cars.Count; i++)
            {
                Mod_Minecart cart = Cars[i];
                bool eligible = cart.Carrier.Rider != null && !cart.controlsLocked && Time.time - cart.lastInputAt <= .25f;
                Vector2 input = eligible ? cart.requestedInput : Vector2.zero;
                float throttle = cart.onRail ? MinecartDrivingRules.ResolveThrottle(input, positions[i].Tangent,
                    ref cart.heldDrivingDirection, ref cart.heldDrivingSign) : 0f;
                if (!cart.onRail) { cart.heldDrivingDirection = Vector2.zero; cart.heldDrivingSign = 0f; }
                if (!driver && (cart.onRail ? Mathf.Abs(throttle) > .01f : input.sqrMagnitude > .001f))
                {
                    driver = true; driverOnRail = cart.onRail;
                    drivingCart = cart;
                    driveWorld = cart.onRail ? positions[i].Tangent * throttle : Vector2.ClampMagnitude(input, 1f);
                    float projection = Vector2.Dot(input, cart.motionHeading);
                    driveSign = cart.onRail ? throttle : Mathf.Abs(projection) <= .01f
                        ? 0f : Mathf.Sign(projection) * input.magnitude;
                }
                Mod_Mover pusher = cart.Carrier.PushSource;
                Vector2 pushing = cart.Carrier.PushedVelocity;
                if (pusher == null || pushing.sqrMagnitude <= .0001f || !pushers.Add(pusher)) continue;
                railPush |= cart.onRail && pushing.sqrMagnitude > .0001f;
                Vector2 tangent = cart.onRail ? positions[i].Tangent : cart.motionHeading;
                pushWorld += MinecartDrivingRules.ResolvePushVelocity(pushing, tangent, cart.onRail, 1f);
                pushSign += Vector2.Dot(pushing, tangent);
            }
            pushWorld /= Cars.Count; pushSign /= Cars.Count;
            return driver;
        }

        private int Plan(float power, float seconds, int predictedGround)
        {
            GroundResistanceCount = predictedGround;
            float traction = MinecartDrivingRules.SurfaceMultiplier(Cars.Count, predictedGround, groundMultiplier);
            float powered = required > 0f ? maximum * Mathf.Sqrt(Mathf.Clamp01(power / required)) : 0f;
            velocities.Clear(); exits.Clear(); int newGround = groundCount;
            for (int i = 0; i < Cars.Count; i++)
            {
                Mod_Minecart cart = Cars[i];
                Vector2 desired;
                if (cart.onRail) desired = positions[i].Tangent * (driveSign * powered + pushSign) * traction;
                else if (driverOnRail || railPush && driveWorld.sqrMagnitude <= .001f)
                    desired = (cart.motionHeading * (driveSign * powered + pushSign)) * traction;
                else desired = (driveWorld * powered + pushWorld) * traction;
                desired = Vector2.ClampMagnitude(desired, maximum * traction);
                float braking = cart.onRail && predictedGround == groundCount ? cart.Braking : cart.GroundBraking;
                Vector2 velocity = MinecartDrivingRules.NextSurfaceVelocity(cart.Carrier.Body.velocity, desired,
                    cart.Acceleration * traction, braking, seconds);
                velocity = Vector2.ClampMagnitude(velocity, maximum * traction);
                bool leaves = false;
                if (cart.onRail)
                {
                    float distance = Vector2.Dot(velocity, positions[i].Tangent) * seconds;
                    if (Mathf.Abs(distance) > .000001f)
                    {
                        bool complete = RailTrackNetwork.TryAdvance(cart.item, positions[i], distance, out RailPosition target, out float moved);
                        Vector2 destination = target.Position;
                        if (!complete)
                        {
                            float remainder = Mathf.Max(0f, Mathf.Abs(distance) - Mathf.Abs(moved));
                            destination += target.Tangent * (Mathf.Sign(distance) * remainder);
                            leaves = remainder > .000001f;
                        }
                        velocity = Vector2.ClampMagnitude((destination - cart.Carrier.Body.position) / seconds, maximum * traction);
                    }
                    else velocity = Vector2.zero;
                }
                if (leaves) newGround++;
                exits.Add(leaves); velocities.Add(velocity);
            }
            return Mathf.Min(Cars.Count, newGround);
        }

        private float MovementClearance(float seconds)
        {
            float allowed = 1f;
            for (int i = 0; i < Cars.Count; i++)
                allowed = Mathf.Min(allowed, Cars[i].LimitMovement(Cars[i].Carrier.Body.position + velocities[i] * seconds));
            return allowed;
        }

        public void Step(float seconds)
        {
            string worldKey = Cars[0].gameObject.scene.name;
            using IDisposable worldScope = GameplayWorldRegistry.TryGet(worldKey, out _)
                ? GameplayWorldRegistry.UseWorld(worldKey) : null;
            if (!(seconds > 0f) || !RefreshPositions())
            {
                foreach (Mod_Minecart cart in Cars)
                    if (cart.Carrier?.Body != null) cart.Carrier.ApplyDrivenVelocity(Vector2.zero, Vector2.zero);
                Speed = 0f; return;
            }
            bool driver = ReadInputs();
            float available = 0f;
            if (driver)
                foreach (Mod_Minecart cart in Cars) available += EnginePowerService.AvailablePower(cart.EngineInventory.Data, cart.CargoInventory.Data);
            float manualPower = drivingCart?.Carrier.Rider?.stamina != null
                ? MinecartDrivingRules.ResolveManualPower(driverOnRail, driveSign, available, drivingCart.ManualPower) : 0f;
            float plannedPower = available + manualPower;
            int predictedGround = Plan(plannedPower, seconds, groundCount);
            if (predictedGround != groundCount) Plan(plannedPower, seconds, predictedGround);
            float allowed = MovementClearance(seconds);
            if (allowed <= .0001f)
            {
                foreach (Mod_Minecart cart in Cars) cart.Carrier.ApplyDrivenVelocity(Vector2.zero, Vector2.zero);
                Speed = 0f; return;
            }
            float supplied = 0f, remaining = Mathf.Min(required, available);
            if (driver && remaining > .0001f)
            {
                foreach (Mod_Minecart cart in Cars)
                {
                    if (remaining <= .0001f) break;
                    EnginePowerService.Supply(cart.EngineInventory.Data, cart.CargoInventory.Data, seconds, remaining, out float power);
                    supplied += power; remaining -= power;
                }
            }
            // 先确认实际供能与可通行位移，再由当前驾驶者支付一次人力体力。
            manualPower = drivingCart?.Carrier.Rider?.stamina != null
                ? MinecartDrivingRules.ResolveManualPower(driverOnRail, driveSign, supplied, drivingCart.ManualPower) : 0f;
            if (supplied + manualPower != plannedPower)
            {
                Plan(supplied + manualPower, seconds, predictedGround);
                allowed = Mathf.Min(allowed, MovementClearance(seconds));
            }
            if (manualPower > 0f && allowed > .0001f)
            {
                float paidPower = MinecartDrivingRules.TrySupplyManualPower(drivingCart.Carrier.Rider.stamina,
                    driverOnRail, driveSign, supplied, drivingCart.ManualPower, drivingCart.ManualStaminaPerSecond, seconds);
                if (paidPower <= 0f)
                {
                    Plan(supplied, seconds, predictedGround);
                    allowed = Mathf.Min(allowed, MovementClearance(seconds));
                }
            }
            Speed = 0f;
            for (int i = 0; i < Cars.Count; i++)
            {
                Mod_Minecart cart = Cars[i];
                Vector2 velocity = velocities[i] * allowed;
                Vector2 heading = velocity.sqrMagnitude > .0001f ? velocity : Vector2.zero;
                cart.Carrier.ApplyDrivenVelocity(velocity, heading);
                Speed = Mathf.Max(Speed, cart.Carrier.Body.velocity.magnitude);
            }
        }
    }
    #endregion
}
