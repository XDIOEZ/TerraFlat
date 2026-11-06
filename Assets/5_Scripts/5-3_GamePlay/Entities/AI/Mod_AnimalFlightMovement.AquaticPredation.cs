using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class Mod_AnimalFlightMovement : IAquaticPredatorThreat
{
    #region 鱼类捕食
    public string aquaticPreyTag = "Fish";
    public string aquaticPreyFoodTag = "Meat";
    public string[] landPreyIds = Array.Empty<string>();
    [Min(0f)] public float maximumPreyWeight = 5f;
    private const float FishDiveMinimumVisualHeight = 0.12f;

    [Tooltip("饱食度低于该比例才会主动捕鱼。"), Range(0f, 1f)] public float fishHuntHungerRate = 0.7f;
    [Min(0.1f)] public float fishHuntRadius = 12f;
    [Min(0.1f)] public float fishDiveRange = 0.7f;
    [Min(0.1f)] public float fishDiveSeconds = 0.45f;
    [Min(0f)] public float fishDiveDamage = 8f;
    [Range(0.01f, 0.99f)] public float fishCaptureHealthRate = 0.5f;
    [Min(0.1f)] public float fishRecoverSeconds = 0.35f;
    [Min(0.5f)] public float fishCarryLandSearchRadius = 12f;
    [Min(0.1f)] public float fishFinishAttackInterval = 0.45f;
    [Min(0.1f)] public float fishMeatSearchRadius = 2f;
    [Min(0.1f)] public float fishMeatWaitSeconds = 3f;
    [Min(0f)] public float fishCarryVisualGap = 0.28f;

    private readonly List<Item> fishHuntCandidates = new();
    private readonly HashSet<Item> fishHuntDedupe = new();
    private readonly CombatDamage fishPredationDamageValues = new();
    private float fishPredationDamageOverride = -1f;
    private bool fishHunting;
    private bool fishHuntHasLandPoint;
    private bool fishHuntMeatReady;
    private FlightPredationTarget fishHuntTarget;
    private FlightPredationTarget pendingAquaticPrey;
    private Vector2 fishHuntLandPoint;
    private DroppedItemHandle fishHuntMeatTarget;
    private float fishHuntTimer;
    private float fishHuntScanRemaining;
    private float fishHuntVisualHeightOverride = -1f;

    private bool IsFishHunting => fishHunting;
    private bool HasFishHuntVisualHeightOverride => fishHuntVisualHeightOverride >= 0f;
    private float FishHuntVisualHeightOverride => Mathf.Max(0f, fishHuntVisualHeightOverride);

    public bool ThreatensAquaticPrey(Item prey)
    {
        return loaded && IsAlive && !permanentFlight && fishHuntRadius > 0f && IsWithinPredationWeight(prey) &&
            prey?.itemData?.Tags?.ContainsTag(aquaticPreyTag) == true;
    }

    CombatDamage IDamageSender.DamageValues
    {
        get
        {
            fishPredationDamageValues.Physical = fishPredationDamageOverride >= 0f
                ? fishPredationDamageOverride
                : Mathf.Max(0f, fishDiveDamage);
            fishPredationDamageValues.Fire = 0f;
            fishPredationDamageValues.Radiation = 0f;
            fishPredationDamageValues.True = 0f;
            fishPredationDamageValues.ImpactKind = CombatDamageKind.Piercing;
            return fishPredationDamageValues;
        }
    }

    Item IDamageSender.attacker
    {
        get => item;
        set
        {
            if (!ReferenceEquals(value, item))
                throw new InvalidOperationException("鸟类捕食伤害的攻击者身份不可替换。");
        }
    }

    public bool CanStartAquaticHunt
    {
        get
        {
            if (permanentFlight || food?.Data?.nutrition == null || state.MustRecoverStamina ||
                state.Stamina <= Mathf.Max(0.01f, flightStaminaMax) * Mathf.Clamp01(fatigueLandingStaminaRatio))
                return false;
            if (fishHunting) return true;
            if (food.Data.nutrition.GetFoodRate() >= Mathf.Clamp01(fishHuntHungerRate)) return false;
            if (IsPendingAquaticPreyUsable()) return true;
            pendingAquaticPrey = null;
            if (fishHuntScanRemaining > 0f) return false;
            fishHuntScanRemaining = 0.5f;
            pendingAquaticPrey = FindAquaticPrey();
            return pendingAquaticPrey != null;
        }
    }


    private bool IsPendingAquaticPreyUsable()
    {
        Item candidate = pendingAquaticPrey?.ActorItem;
        return candidate != null && pendingAquaticPrey.CanBeHuntedBy(item) && IsVisiblePredationTarget(candidate) &&
            (pendingAquaticPrey.IsLandPrey ? IsLandPredationPosition(candidate.transform.position) :
                AquaticHabitat.CanSwimAt(candidate.transform.position)) &&
            flightNavigation.CanTraverse(body.position, candidate.transform.position);
    }

    public bool TryAcquireAquaticHuntTarget()
    {
        if (fishHunting) return fishHuntTarget != null;
        if (!CanStartAquaticHunt || !IsPendingAquaticPreyUsable()) return false;
        fishHuntTarget = pendingAquaticPrey;
        pendingAquaticPrey = null;
        fishHunting = true;
        fishHuntHasLandPoint = fishHuntMeatReady = false;
        forageTarget = default;
        forageTargetTag = null;
        ResetFatigueLanding();
        return true;
    }

    private FlightPredationTarget FindAquaticPrey()
    {
        ItemMgr manager = ItemMgr.Instance;
        if (manager == null) return null;
        float radius = Mathf.Max(0.1f, fishHuntRadius);
        manager.QueryItemsInCircleNonAlloc(body.position, radius, ~0, item, fishHuntCandidates, fishHuntDedupe);
        Item nearestItem = null;
        IAquaticPredatorCarryTarget nearestCarryTarget = null;
        float nearestDistanceSqr = radius * radius;
        foreach (Item candidate in fishHuntCandidates)
        {
            if (candidate == null || candidate == item || !IsWithinPredationWeight(candidate)) continue;
            bool landPrey = MatchesLandPreyId(candidate);
            if (!landPrey && candidate.itemData?.Tags?.ContainsTag(aquaticPreyTag) != true) continue;
            float distanceSqr = WorldTopologyRuntime.SqrDistance(body.position, candidate.transform.position);
            if (distanceSqr >= nearestDistanceSqr) continue;
            if (landPrey ? !IsLandPredationPosition(candidate.transform.position) :
                !AquaticHabitat.CanSwimAt(candidate.transform.position)) continue;
            if (!IsVisiblePredationTarget(candidate) || !flightNavigation.CanTraverse(body.position, candidate.transform.position)) continue;
            if (landPrey)
            {
                if (candidate.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp) is not { Hp: > 0f }) continue;
                nearestItem = candidate;
                nearestCarryTarget = null;
                nearestDistanceSqr = distanceSqr;
                continue;
            }
            var targets = candidate.itemMods?.GetCapabilities<IAquaticPredatorCarryTarget>();
            if (targets == null) continue;
            foreach (IAquaticPredatorCarryTarget prey in targets)
            {
                if (!prey.CanBeHuntedBy(item)) continue;
                nearestItem = candidate;
                nearestCarryTarget = prey;
                nearestDistanceSqr = distanceSqr;
                break;
            }
        }
        fishHuntCandidates.Clear();
        fishHuntDedupe.Clear();
        return nearestItem != null ? new FlightPredationTarget(nearestItem, nearestCarryTarget) : null;
    }

    private bool HasUsableAquaticHuntTarget
    {
        get
        {
            Item target = fishHuntTarget?.ActorItem;
            if (target == null || !fishHuntTarget.IsAlive || !IsWithinPredationWeight(target))
                return false;
            if (fishHuntTarget.IsCapturedBy(item)) return target.gameObject.scene == item.gameObject.scene &&
                threatDetector.IsTargetInDetectorLogicalWorld(target) && FactionRelationService.CanAttack(item, target);
            return food.Data.nutrition.GetFoodRate() < Mathf.Clamp01(fishHuntHungerRate) &&
                fishHuntTarget.CanBeHuntedBy(item) && IsVisiblePredationTarget(target) &&
                (fishHuntTarget.IsLandPrey ? IsLandPredationPosition(target.transform.position) :
                    AquaticHabitat.CanSwimAt(target.transform.position)) &&
                flightNavigation.CanTraverse(body.position, target.transform.position);
        }
    }

    // 猎物必须仍属于同一代实体且可见、可被当前捕食者攻击。
    private bool IsVisiblePredationTarget(Item candidate) => candidate != null && candidate != item &&
        candidate.gameObject.activeInHierarchy && !candidate.DestructionHandled && !candidate.IsUnloadingModules &&
        IsWithinPredationWeight(candidate) &&
        candidate.gameObject.scene == item.gameObject.scene && FactionRelationService.CanAttack(item, candidate) &&
        WorldTopologyRuntime.SqrDistance(body.position, candidate.transform.position) <= fishHuntRadius * fishHuntRadius &&
        threatDetector.HasLineOfSight(candidate);

    private static bool MatchesPredationIdentity(Item candidate, int guid, uint generation) => candidate != null &&
        candidate.itemData?.Guid == guid && candidate.RuntimeGeneration == generation;

    private bool IsWithinPredationWeight(Item candidate) => candidate?.itemData?.Stack != null &&
        candidate.itemData.Stack.CurrentWeight <= Mathf.Max(0f, maximumPreyWeight);

    private static bool IsLandPredationPosition(Vector2 position) =>
        AquaticHabitat.TryGetDepth(position, out float depth) && depth < AquaticHabitat.MinimumDepth &&
        AquaticHabitat.CanTraverse(position, position, true, true);

    private bool MatchesLandPreyId(Item candidate)
    {
        if (landPreyIds == null) return false;
        foreach (string id in landPreyIds)
            if (!string.IsNullOrEmpty(id) && string.Equals(candidate.itemData?.IDName, id, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    // 独立记录猎物身份，只有水生猎物才附带可选的携带能力。
    private sealed class FlightPredationTarget
    {
        private readonly int guid;
        private readonly uint generation;
        private readonly IAquaticPredatorCarryTarget carryTarget;
        public Item ActorItem { get; }
        public bool IsLandPrey => carryTarget == null;
        public FlightPredationTarget(Item actor, IAquaticPredatorCarryTarget carryTarget)
        {
            ActorItem = actor;
            this.carryTarget = carryTarget;
            guid = actor.itemData?.Guid ?? 0;
            generation = actor.RuntimeGeneration;
        }
        private Mod_DamageReceiver Health => MatchesPredationIdentity(ActorItem, guid, generation)
            ? ActorItem.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp) : null;
        private bool HasOriginalIdentity => MatchesPredationIdentity(ActorItem, guid, generation);
        public bool IsAlive => ActorItem != null && ActorItem.gameObject.activeInHierarchy &&
            !ActorItem.DestructionHandled && !ActorItem.IsUnloadingModules && Health is { Hp: > 0f } &&
            (carryTarget == null || carryTarget.IsAlive);
        public float HealthRatio => carryTarget != null ? carryTarget.HealthRatio :
            Health is Mod_DamageReceiver receiver && receiver.MaxHp > 0f ? Mathf.Clamp01(receiver.Hp / receiver.MaxHp) : 0f;
        public bool CanBeHuntedBy(Item predator) => predator != null && IsAlive &&
            FactionRelationService.CanAttack(predator, ActorItem) && (carryTarget == null || carryTarget.CanBeHuntedBy(predator));
        public bool TryCaptureByPredator(Item predator) => HasOriginalIdentity && carryTarget?.TryCaptureByPredator(predator) == true;
        public bool IsCapturedBy(Item predator) => HasOriginalIdentity && carryTarget?.IsCapturedBy(predator) == true;
        public void MoveWithPredator(Item predator, Vector2 position, float height)
        {
            if (HasOriginalIdentity) carryTarget?.MoveWithPredator(predator, position, height);
        }
        public void ReleaseFromPredator(Item predator)
        {
            if (HasOriginalIdentity) carryTarget?.ReleaseFromPredator(predator);
        }
    }

    public AnimalBehaviorTreeStatus TickAquaticHuntApproach(float deltaTime)
    {
        if (!HasUsableAquaticHuntTarget) return AnimalBehaviorTreeStatus.Failure;
        if (state.Phase is BirdFlightPhase.Ground or BirdFlightPhase.Landing)
        {
            BeginTakeoff();
            return AnimalBehaviorTreeStatus.Running;
        }
        if (state.Phase is BirdFlightPhase.RunUp or BirdFlightPhase.TakingOff)
        {
            TickFlightTransition(deltaTime);
            return AnimalBehaviorTreeStatus.Running;
        }
        Vector2 targetPosition = fishHuntTarget.ActorItem.transform.position;
        if (WorldTopologyRuntime.SqrDistance(body.position, targetPosition) <= fishDiveRange * fishDiveRange)
            return AnimalBehaviorTreeStatus.Success;
        FlyTowards(targetPosition, deltaTime);
        return AnimalBehaviorTreeStatus.Running;
    }

    public void BeginAquaticHuntDive() => fishHuntTimer = 0f;
    public AnimalBehaviorTreeStatus TickAquaticHuntDive(float deltaTime)
    {
        if (!HasUsableAquaticHuntTarget) return AnimalBehaviorTreeStatus.Failure;
        Vector2 position = fishHuntTarget.ActorItem.transform.position;
        FlyTowards(position, deltaTime);
        if (WorldTopologyRuntime.SqrDistance(body.position, position) > fishDiveRange * fishDiveRange)
        {
            fishHuntTimer = 0f;
            fishHuntVisualHeightOverride = -1f;
            return AnimalBehaviorTreeStatus.Running;
        }
        fishHuntTimer += deltaTime;
        float progress = Mathf.Clamp01(fishHuntTimer / Mathf.Max(0.1f, fishDiveSeconds));
        fishHuntVisualHeightOverride = Mathf.Lerp(flightHeight, FishDiveMinimumVisualHeight, progress);
        if (progress < 1f) return AnimalBehaviorTreeStatus.Running;
        bool landPrey = fishHuntTarget.IsLandPrey;
        if (landPrey || fishHuntTarget.HealthRatio >= Mathf.Clamp01(fishCaptureHealthRate))
        {
            Mod_DamageReceiver receiver = fishHuntTarget.ActorItem.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
            if (receiver != null)
            {
                fishPredationDamageOverride = landPrey ? -1f :
                    Mathf.Min(Mathf.Max(0f, fishDiveDamage), Mathf.Max(0f, receiver.Hp - 0.01f));
                try { PlayPredationAnimation(fishHuntTarget.ActorItem); receiver.Hurt((IDamageSender)this); }
                finally { fishPredationDamageOverride = -1f; }
            }
        }
        if (!fishHuntTarget.IsAlive) BeginAquaticHuntMeatSearch(body.position);
        else if (landPrey)
        {
            fishHuntTimer = 0f;
            fishHuntVisualHeightOverride = -1f;
            return AnimalBehaviorTreeStatus.Running;
        }
        else if (fishHuntTarget.HealthRatio < Mathf.Clamp01(fishCaptureHealthRate))
            fishHuntTarget.TryCaptureByPredator(item);
        return AnimalBehaviorTreeStatus.Success;
    }

    public void BeginAquaticHuntRecovery() => fishHuntTimer = 0f;
    public AnimalBehaviorTreeStatus TickAquaticHuntRecovery(float deltaTime)
    {
        if (fishHuntMeatReady) return AnimalBehaviorTreeStatus.Success;
        if (!HasUsableAquaticHuntTarget) return AnimalBehaviorTreeStatus.Failure;
        fishHuntTimer += deltaTime;
        float progress = Mathf.Clamp01(fishHuntTimer / Mathf.Max(0.1f, fishRecoverSeconds));
        fishHuntVisualHeightOverride = Mathf.Lerp(FishDiveMinimumVisualHeight, flightHeight, progress);
        bool captured = fishHuntTarget.IsCapturedBy(item);
        if (captured) fishHuntTarget.MoveWithPredator(item, body.position,
            Mathf.Max(0f, fishHuntVisualHeightOverride - fishCarryVisualGap));
        if (progress < 1f) return AnimalBehaviorTreeStatus.Running;
        fishHuntVisualHeightOverride = -1f;
        return captured ? AnimalBehaviorTreeStatus.Success : AnimalBehaviorTreeStatus.Failure;
    }

    public AnimalBehaviorTreeStatus TickAquaticHuntCarry(float deltaTime)
    {
        if (fishHuntMeatReady) return AnimalBehaviorTreeStatus.Success;
        if (!HasUsableAquaticHuntTarget || !fishHuntTarget.IsCapturedBy(item)) return AnimalBehaviorTreeStatus.Failure;
        if (!fishHuntHasLandPoint)
        {
            fishHuntHasLandPoint = TryFindLandingPoint(body.position, fishCarryLandSearchRadius, out fishHuntLandPoint);
            if (!fishHuntHasLandPoint) { fishHuntScanRemaining = 2f; return AnimalBehaviorTreeStatus.Failure; }
        }
        fishHuntTarget.MoveWithPredator(item, body.position, Mathf.Max(0f, CurrentFlightHeight - fishCarryVisualGap));
        float arrival = Mathf.Max(0.2f, peckRange);
        if (WorldTopologyRuntime.SqrDistance(body.position, fishHuntLandPoint) <= arrival * arrival && CanLand(body.position))
        {
            BeginLanding();
            return AnimalBehaviorTreeStatus.Success;
        }
        FlyTowards(fishHuntLandPoint, deltaTime);
        return AnimalBehaviorTreeStatus.Running;
    }

    public AnimalBehaviorTreeStatus TickAquaticHuntLanding(float deltaTime)
    {
        if (fishHuntMeatReady) return AnimalBehaviorTreeStatus.Success;
        if (!HasUsableAquaticHuntTarget || !fishHuntTarget.IsCapturedBy(item)) return AnimalBehaviorTreeStatus.Failure;
        fishHuntTarget.MoveWithPredator(item, body.position, Mathf.Max(0f, CurrentFlightHeight - fishCarryVisualGap));
        mover.StopMovement();
        if (state.Phase == BirdFlightPhase.Landing && state.Elapsed < transitionDuration)
            return AnimalBehaviorTreeStatus.Running;
        if (state.Phase == BirdFlightPhase.Landing) CompleteLanding();
        fishHuntTarget.MoveWithPredator(item, body.position, 0f);
        fishHuntTimer = 0f;
        return AnimalBehaviorTreeStatus.Success;
    }

    public AnimalBehaviorTreeStatus TickAquaticHuntFinish(float deltaTime)
    {
        if (fishHuntMeatReady) return AnimalBehaviorTreeStatus.Success;
        if (fishHuntTarget == null || fishHuntTarget.ActorItem == null) return AnimalBehaviorTreeStatus.Failure;
        mover.StopMovement();
        if (!fishHuntTarget.IsAlive)
        {
            BeginAquaticHuntMeatSearch(body.position);
            return AnimalBehaviorTreeStatus.Success;
        }
        if (!HasUsableAquaticHuntTarget || !fishHuntTarget.IsCapturedBy(item)) return AnimalBehaviorTreeStatus.Failure;
        fishHuntTarget.MoveWithPredator(item, body.position, 0f);
        fishHuntTimer -= deltaTime;
        if (fishHuntTimer > 0f) return AnimalBehaviorTreeStatus.Running;
        fishHuntTimer = Mathf.Max(0.1f, fishFinishAttackInterval);
        Mod_DamageReceiver receiver = fishHuntTarget.ActorItem.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
        if (receiver != null) PlayPredationAnimation(fishHuntTarget.ActorItem);
        receiver?.Hurt((IDamageSender)this);
        if (!fishHuntTarget.IsAlive)
        {
            BeginAquaticHuntMeatSearch(body.position);
            return AnimalBehaviorTreeStatus.Success;
        }
        return AnimalBehaviorTreeStatus.Running;
    }

    private void BeginAquaticHuntMeatSearch(Vector2 position)
    {
        fishHuntLandPoint = WorldTopologyRuntime.NormalizePosition(position);
        fishHuntTarget = null;
        fishHuntMeatTarget = default;
        fishHuntMeatReady = true;
        fishHuntTimer = Mathf.Max(0.1f, fishMeatWaitSeconds);
        fishHuntScanRemaining = 0f;
        fishHuntVisualHeightOverride = -1f;
    }

    public AnimalBehaviorTreeStatus TickAquaticHuntEat(float deltaTime)
    {
        if (!fishHuntMeatReady) return AnimalBehaviorTreeStatus.Failure;
        fishHuntTimer -= deltaTime;
        if (fishHuntTimer <= 0f) { ResetFishHunting(false); return AnimalBehaviorTreeStatus.Success; }
        if (state.Phase != BirdFlightPhase.Ground)
        {
            if (state.Phase == BirdFlightPhase.Flying && CanLand(body.position)) BeginLanding();
            else if (state.Phase == BirdFlightPhase.Landing && state.Elapsed >= transitionDuration) CompleteLanding();
            return AnimalBehaviorTreeStatus.Running;
        }
        if (!DroppedItemService.TryGetPickablePosition(fishHuntMeatTarget, out Vector2 position))
        {
            if (fishHuntScanRemaining > 0f) return AnimalBehaviorTreeStatus.Running;
            fishHuntScanRemaining = 0.15f;
            if (!DroppedItemService.TryFindNearestTagged(fishHuntLandPoint, fishMeatSearchRadius, aquaticPreyFoodTag, out fishHuntMeatTarget) ||
                !DroppedItemService.TryGetPickablePosition(fishHuntMeatTarget, out position)) return AnimalBehaviorTreeStatus.Running;
        }
        float distanceSqr = WorldTopologyRuntime.SqrDistance(body.position, position);
        mover.Speed.BaseValue = groundSpeed;
        mover.SetInteractionDestination(position, peckRange);
        if (distanceSqr > peckRange * peckRange) return AnimalBehaviorTreeStatus.Running;
        if (food.TryEatDroppedFood(fishHuntMeatTarget, aquaticPreyFoodTag, peckRange))
        {
            mover.StopMovement();
            ResetFishHunting(false);
            return AnimalBehaviorTreeStatus.Success;
        }
        fishHuntMeatTarget = default;
        return AnimalBehaviorTreeStatus.Running;
    }

    public void CancelAquaticHunt() => ResetFishHunting(true);
    private void ResetFishHunting(bool releaseCaptured)
    {
        if (releaseCaptured && fishHuntTarget != null && fishHuntTarget.IsCapturedBy(item))
            fishHuntTarget.ReleaseFromPredator(item);
        fishHunting = fishHuntHasLandPoint = fishHuntMeatReady = false;
        fishHuntTarget = null;
        pendingAquaticPrey = null;
        fishHuntLandPoint = default;
        fishHuntMeatTarget = default;
        fishHuntTimer = 0f;
        fishHuntVisualHeightOverride = -1f;
        fishPredationDamageOverride = -1f;
        fishHuntCandidates.Clear();
        fishHuntDedupe.Clear();
    }
    #endregion
}
