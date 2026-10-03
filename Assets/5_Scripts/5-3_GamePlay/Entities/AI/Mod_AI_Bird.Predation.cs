using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class Mod_AI_Bird : IAquaticPredatorThreat
{
    #region 鱼类捕食
    private const string FishPreyTag = "Fish";
    private const string FishMeatTag = "Meat";
    private const float FishDiveMinimumVisualHeight = 0.12f;

    private enum FishHuntStage : byte
    {
        None,
        Approach,
        Dive,
        Recover,
        CarryToLand,
        Landing,
        Finish,
        EatMeat
    }

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
    private FishHuntStage fishHuntStage;
    private IAquaticPredatorCarryTarget fishHuntTarget;
    private Vector2 fishHuntLandPoint;
    private DroppedItemHandle fishHuntMeatTarget;
    private float fishHuntTimer;
    private float fishHuntScanRemaining;
    private float fishHuntVisualHeightOverride = -1f;

    private bool IsFishHunting => fishHuntStage != FishHuntStage.None;
    private bool HasFishHuntVisualHeightOverride => fishHuntVisualHeightOverride >= 0f;
    private float FishHuntVisualHeightOverride => Mathf.Max(0f, fishHuntVisualHeightOverride);

    public bool ThreatensAquaticPrey(Item prey)
    {
        return loaded && IsAlive && prey?.itemData?.Tags?.ContainsTag(FishPreyTag) == true;
    }

    CombatDamage IDamageSender.DamageValues
    {
        get
        {
            fishPredationDamageValues.Cutting = 0f;
            fishPredationDamageValues.Piercing = fishPredationDamageOverride >= 0f
                ? fishPredationDamageOverride
                : Mathf.Max(0f, fishDiveDamage);
            fishPredationDamageValues.Chopping = 0f;
            fishPredationDamageValues.Blunt = 0f;
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

    private bool TickFishHunting(float deltaTime)
    {
        if (permanentFlight || flightPilot != null || food?.Data?.nutrition == null)
        {
            ResetFishHunting(releaseCaptured: true);
            return false;
        }

        if (fishHuntStage == FishHuntStage.None)
        {
            float fatigueThreshold = Mathf.Max(0.01f, flightStaminaMax) * Mathf.Clamp01(fatigueLandingStaminaRatio);
            if (food.Data.nutrition.GetFoodRate() >= Mathf.Clamp01(fishHuntHungerRate) ||
                state.MustRecoverStamina || state.Stamina <= fatigueThreshold)
                return false;

            fishHuntScanRemaining -= deltaTime;
            if (fishHuntScanRemaining > 0f)
                return false;
            fishHuntScanRemaining = 0.5f;
            if (!TryAcquireFishTarget())
                return false;

            fishHuntStage = FishHuntStage.Approach;
            forageTarget = default;
            forageTargetTag = null;
            ResetFatigueLanding();
        }

        if (!ValidateFishHuntTarget())
        {
            if (fishHuntStage == FishHuntStage.EatMeat)
                return TickEatFishMeat(deltaTime);
            ResetFishHunting(releaseCaptured: true);
            return false;
        }

        if ((fishHuntStage == FishHuntStage.Approach || fishHuntStage == FishHuntStage.Dive ||
             fishHuntStage == FishHuntStage.Recover) &&
            !fishHuntTarget.IsCapturedBy(item) &&
            food.Data.nutrition.GetFoodRate() >= Mathf.Clamp01(fishHuntHungerRate))
        {
            ResetFishHunting(releaseCaptured: false);
            return false;
        }

        return fishHuntStage switch
        {
            FishHuntStage.Approach => TickFishApproach(deltaTime),
            FishHuntStage.Dive => TickFishDive(deltaTime),
            FishHuntStage.Recover => TickFishRecover(deltaTime),
            FishHuntStage.CarryToLand => TickCarryFishToLand(deltaTime),
            FishHuntStage.Landing => TickFishLanding(deltaTime),
            FishHuntStage.Finish => TickFinishFish(deltaTime),
            FishHuntStage.EatMeat => TickEatFishMeat(deltaTime),
            _ => false
        };
    }

    private bool TryAcquireFishTarget()
    {
        ItemMgr manager = ItemMgr.Instance;
        if (manager == null) return false;
        float radius = Mathf.Max(0.1f, fishHuntRadius);
        manager.QueryItemsInCircleNonAlloc(body.position, radius, ~0, item, fishHuntCandidates, fishHuntDedupe);
        IAquaticPredatorCarryTarget nearest = null;
        float nearestDistanceSqr = radius * radius;
        foreach (Item candidate in fishHuntCandidates)
        {
            if (candidate?.itemData?.Tags?.ContainsTag(FishPreyTag) != true ||
                !FactionRelationService.CanAttack(item, candidate) ||
                !AquaticHabitat.CanSwimAt(candidate.transform.position))
                continue;
            Mod_AI_Fish fish = candidate.itemMods?.GetMod_ByID<Mod_AI_Fish>(Mod_AI_Fish.ModuleId);
            if (fish is not IAquaticPredatorCarryTarget prey || !prey.CanBeHuntedBy(item))
                continue;
            float distanceSqr = WorldTopologyRuntime.SqrDistance(body.position, candidate.transform.position);
            if (distanceSqr >= nearestDistanceSqr || !flightNavigation.CanTraverse(body.position, candidate.transform.position))
                continue;
            nearest = prey;
            nearestDistanceSqr = distanceSqr;
        }
        fishHuntCandidates.Clear();
        fishHuntDedupe.Clear();
        fishHuntTarget = nearest;
        return nearest != null;
    }

    private bool ValidateFishHuntTarget()
    {
        if (fishHuntStage == FishHuntStage.EatMeat)
            return true;
        return fishHuntTarget != null && fishHuntTarget.ActorItem != null && fishHuntTarget.IsAlive &&
               (fishHuntTarget.IsCapturedBy(item) || fishHuntTarget.CanBeHuntedBy(item));
    }

    private bool TickFishApproach(float deltaTime)
    {
        if (state.Phase == BirdFlightPhase.Ground || state.Phase == BirdFlightPhase.Landing)
        {
            BeginTakeoff();
            return true;
        }
        if (state.Phase == BirdFlightPhase.RunUp)
        {
            TickRunUp(deltaTime);
            return true;
        }
        if (state.Phase == BirdFlightPhase.TakingOff)
        {
            TickTakeoff(deltaTime);
            return true;
        }

        Vector2 targetPosition = fishHuntTarget.ActorItem.transform.position;
        float distanceSqr = WorldTopologyRuntime.SqrDistance(body.position, targetPosition);
        if (distanceSqr > fishDiveRange * fishDiveRange)
        {
            FlyTowards(targetPosition, deltaTime);
            return true;
        }

        fishHuntStage = FishHuntStage.Dive;
        fishHuntTimer = 0f;
        return true;
    }

    private bool TickFishDive(float deltaTime)
    {
        Vector2 targetPosition = fishHuntTarget.ActorItem.transform.position;
        FlyTowards(targetPosition, deltaTime);
        fishHuntTimer += deltaTime;
        float progress = Mathf.Clamp01(fishHuntTimer / Mathf.Max(0.1f, fishDiveSeconds));
        fishHuntVisualHeightOverride = Mathf.Lerp(flightHeight, FishDiveMinimumVisualHeight, progress);
        if (progress < 1f) return true;

        // 已经低于抓取线的受伤鱼直接抓走，避免最后一次啄击把低血量目标误杀在水里。
        if (fishHuntTarget.HealthRatio >= Mathf.Clamp01(fishCaptureHealthRate))
        {
            Mod_DamageReceiver receiver = fishHuntTarget.ActorItem.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
            if (receiver != null)
            {
                // 水中俯冲只负责压低血线，不允许这一击直接杀死猎物，死亡必须发生在带回陆地之后。
                fishPredationDamageOverride = Mathf.Min(Mathf.Max(0f, fishDiveDamage), Mathf.Max(0f, receiver.Hp - 0.01f));
                try
                {
                    receiver.Hurt((IDamageSender)this);
                }
                finally
                {
                    fishPredationDamageOverride = -1f;
                }
            }
        }

        if (!fishHuntTarget.IsAlive)
        {
            BeginFishMeatSearch(body.position);
            return true;
        }

        if (fishHuntTarget.HealthRatio < Mathf.Clamp01(fishCaptureHealthRate) &&
            fishHuntTarget.TryCaptureByPredator(item))
        {
            fishHuntStage = FishHuntStage.Recover;
            fishHuntTimer = 0f;
            return true;
        }

        fishHuntStage = FishHuntStage.Recover;
        fishHuntTimer = 0f;
        return true;
    }

    private bool TickFishRecover(float deltaTime)
    {
        fishHuntTimer += deltaTime;
        float progress = Mathf.Clamp01(fishHuntTimer / Mathf.Max(0.1f, fishRecoverSeconds));
        fishHuntVisualHeightOverride = Mathf.Lerp(FishDiveMinimumVisualHeight, flightHeight, progress);
        bool captured = fishHuntTarget.IsCapturedBy(item);
        if (captured)
            fishHuntTarget.MoveWithPredator(item, body.position,
                Mathf.Max(0f, fishHuntVisualHeightOverride - fishCarryVisualGap));
        if (progress < 1f) return true;

        fishHuntVisualHeightOverride = -1f;
        if (!captured)
        {
            fishHuntStage = FishHuntStage.Approach;
            return true;
        }

        if (!TryFindLandingPoint(body.position, fishCarryLandSearchRadius, out fishHuntLandPoint))
        {
            ResetFishHunting(releaseCaptured: true);
            fishHuntScanRemaining = 2f;
            return false;
        }
        fishHuntStage = FishHuntStage.CarryToLand;
        return true;
    }

    private bool TickCarryFishToLand(float deltaTime)
    {
        if (!fishHuntTarget.IsCapturedBy(item))
        {
            ResetFishHunting(releaseCaptured: false);
            return false;
        }
        fishHuntTarget.MoveWithPredator(item, body.position, Mathf.Max(0f, CurrentFlightHeight - fishCarryVisualGap));
        float arrival = Mathf.Max(0.2f, peckRange);
        if (WorldTopologyRuntime.SqrDistance(body.position, fishHuntLandPoint) <= arrival * arrival && CanLand(body.position))
        {
            BeginLanding();
            fishHuntStage = FishHuntStage.Landing;
            return true;
        }
        FlyTowards(fishHuntLandPoint, deltaTime);
        return true;
    }

    private bool TickFishLanding(float deltaTime)
    {
        fishHuntTarget.MoveWithPredator(item, body.position, Mathf.Max(0f, CurrentFlightHeight - fishCarryVisualGap));
        mover.StopMovement();
        if (state.Phase == BirdFlightPhase.Landing && state.Elapsed < transitionDuration)
            return true;
        if (state.Phase == BirdFlightPhase.Landing)
            CompleteLanding();
        fishHuntTarget.MoveWithPredator(item, body.position, 0f);
        fishHuntStage = FishHuntStage.Finish;
        fishHuntTimer = 0f;
        return true;
    }

    private bool TickFinishFish(float deltaTime)
    {
        mover.StopMovement();
        fishHuntTarget.MoveWithPredator(item, body.position, 0f);
        if (!fishHuntTarget.IsAlive)
        {
            BeginFishMeatSearch(body.position);
            return true;
        }
        fishHuntTimer -= deltaTime;
        if (fishHuntTimer > 0f) return true;
        fishHuntTimer = Mathf.Max(0.1f, fishFinishAttackInterval);
        Mod_DamageReceiver receiver = fishHuntTarget.ActorItem.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
        receiver?.Hurt((IDamageSender)this);
        if (!fishHuntTarget.IsAlive)
            BeginFishMeatSearch(body.position);
        return true;
    }

    private void BeginFishMeatSearch(Vector2 position)
    {
        fishHuntLandPoint = WorldTopologyRuntime.NormalizePosition(position);
        fishHuntTarget = null;
        fishHuntMeatTarget = default;
        fishHuntStage = FishHuntStage.EatMeat;
        fishHuntTimer = Mathf.Max(0.1f, fishMeatWaitSeconds);
        fishHuntScanRemaining = 0f;
        fishHuntVisualHeightOverride = -1f;
    }

    private bool TickEatFishMeat(float deltaTime)
    {
        fishHuntTimer -= deltaTime;
        if (fishHuntTimer <= 0f)
        {
            ResetFishHunting(releaseCaptured: false);
            return false;
        }
        if (state.Phase != BirdFlightPhase.Ground)
        {
            if (state.Phase == BirdFlightPhase.Flying && CanLand(body.position)) BeginLanding();
            else if (state.Phase == BirdFlightPhase.Landing && state.Elapsed >= transitionDuration) CompleteLanding();
            return true;
        }

        if (!DroppedItemService.TryGetPickablePosition(fishHuntMeatTarget, out Vector2 meatPosition))
        {
            fishHuntScanRemaining -= deltaTime;
            if (fishHuntScanRemaining > 0f) return true;
            fishHuntScanRemaining = 0.15f;
            if (!DroppedItemService.TryFindNearestTagged(fishHuntLandPoint, fishMeatSearchRadius, FishMeatTag,
                    out fishHuntMeatTarget) ||
                !DroppedItemService.TryGetPickablePosition(fishHuntMeatTarget, out meatPosition))
                return true;
        }

        float distanceSqr = WorldTopologyRuntime.SqrDistance(body.position, meatPosition);
        if (distanceSqr > peckRange * peckRange)
        {
            mover.Speed.BaseValue = groundSpeed;
            mover.SetDestination(meatPosition);
            return true;
        }
        mover.StopMovement();
        if (food.TryEatDroppedFood(fishHuntMeatTarget, FishMeatTag, peckRange))
        {
            ResetFishHunting(releaseCaptured: false);
            return false;
        }
        fishHuntMeatTarget = default;
        return true;
    }

    private void ResetFishHunting(bool releaseCaptured)
    {
        if (releaseCaptured && fishHuntTarget != null && fishHuntTarget.IsCapturedBy(item))
            fishHuntTarget.ReleaseFromPredator(item);
        fishHuntStage = FishHuntStage.None;
        fishHuntTarget = null;
        fishHuntLandPoint = default;
        fishHuntMeatTarget = default;
        fishHuntTimer = 0f;
        fishHuntScanRemaining = 0f;
        fishHuntVisualHeightOverride = -1f;
        fishPredationDamageOverride = -1f;
        fishHuntCandidates.Clear();
        fishHuntDedupe.Clear();
    }
    #endregion
}
