using System;
using System.Collections.Generic;
using FlatWorld.Combat;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEngine;

#region 陆地目标通用规则

internal static class AnimalLandTargetRules
{
    public static bool IsPlayer(Item target) => target is Player || target?.itemData?.Tags?.ContainsTag(Tag.Player) == true;

    public static bool IsUsableOpponent(AnimalBehaviorTreeContext context, Item target)
    {
        if (target == null || target == context.Actor || target.itemData == null || target.DestructionHandled ||
            target.IsUnloadingModules ||
            target.gameObject.scene != context.Actor.gameObject.scene ||
            !FactionRelationService.CanAttack(context.Actor, target)) return false;
        Mod_DamageReceiver health = target.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
        return health == null || health.Hp > 0f;
    }

    public static bool IsLivingOpponent(AnimalBehaviorTreeContext context, Item target) =>
        IsUsableOpponent(context, target) && target.gameObject.activeInHierarchy &&
        target.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp) is { Hp: > 0f };

    public static bool IsVisible(AnimalBehaviorTreeContext context, Item target, float distance)
    {
        if (target == null || context.Detector == null || context.Actor == null ||
            GameplayWorldRegistry.GetItemWorldIdentity(context.Actor) == 0 ||
            GameplayWorldRegistry.GetItemWorldIdentity(context.Actor) != GameplayWorldRegistry.GetItemWorldIdentity(target)) return false;
        float effectiveDistance = Mod_ItemDetector.CalculateEffectiveDetectionRadius(distance, target);
        ItemMgr manager = ItemMgr.GetInstance();
        if (manager != null && manager.TryIsWithinDataPerceptionRange(context.Position, target, effectiveDistance, out bool within))
        {
            if (!within) return false;
        }
        else if (WorldTopologyRuntime.Distance(context.Position, target.transform.position) > effectiveDistance) return false;
        return context.Detector.HasLineOfSight(target);
    }

    public static float Number(JObject config, string name, float fallback) => config?[name]?.Value<float>() ?? fallback;
    public static int Integer(JObject config, string name, int fallback) => config?[name]?.Value<int>() ?? fallback;
    public static bool Flag(JObject config, string name, bool fallback) => config?[name]?.Value<bool>() ?? fallback;
    public static string Text(JObject config, string name, string fallback = null) => config?[name]?.Value<string>() ?? fallback;
    public static string[] Strings(JObject config, string name) => config?[name]?.ToObject<string[]>() ?? Array.Empty<string>();
}

/// <summary>目标身份与最后可见位置；丢失视线后只允许使用冻结坐标。</summary>
public sealed class AnimalTargetMemoryCapability : AnimalBehaviorTreeCapability
{
    #region 记忆与搜索
    private readonly AnimalBehaviorTreeContext context;
    private readonly float travelTimeout;
    private readonly float searchDuration;
    private readonly float arrivalDistance;
    private int rememberedGuid;
    private uint rememberedGeneration;
    private float travelRemaining;
    private float waitRemaining;
    private bool requestedArrivalDetection;

    public Item Target { get; private set; }
    public bool Visible { get; private set; }
    public bool HasMemory { get; private set; }
    public bool CanSearch { get; private set; }
    public bool WaitingAtLastPosition { get; private set; }
    public Vector3 LastKnownPosition { get; private set; }
    public bool IsSearching => HasMemory && !Visible && CanSearch && Matches(Target);
    public float ArrivalDistance => arrivalDistance;

    public AnimalTargetMemoryCapability(AnimalBehaviorTreeContext context, JObject config, JObject saved)
    {
        this.context = context;
        travelTimeout = AnimalLandTargetRules.Number(config, "travelTimeout", 15f);
        searchDuration = AnimalLandTargetRules.Number(config, "searchDuration", 3f);
        arrivalDistance = AnimalLandTargetRules.Number(config, "arrivalDistance", 0.6f);
    }

    public bool Matches(Item target) => HasMemory && target != null && target == Target &&
        target.itemData?.Guid == rememberedGuid && target.RuntimeGeneration == rememberedGeneration;

    public void Remember(Item target, Vector3 position, bool visible, bool pursue = false)
    {
        if (!AnimalLandTargetRules.IsUsableOpponent(context, target)) return;
        if (!Matches(target)) Forget();
        Target = target;
        rememberedGuid = target.itemData.Guid;
        rememberedGeneration = target.RuntimeGeneration;
        LastKnownPosition = position;
        HasMemory = true;
        Visible = visible;
        CanSearch |= pursue;
        WaitingAtLastPosition = requestedArrivalDetection = false;
        travelRemaining = Mathf.Max(0.1f, travelTimeout);
        waitRemaining = Mathf.Max(0.1f, searchDuration);
    }

    public void MarkPursuing() => CanSearch = HasMemory;
    public void StopPursuing() => CanSearch = false;

    public void RefreshVisibility(float retentionDistance)
    {
        if (!Matches(Target) || !AnimalLandTargetRules.IsUsableOpponent(context, Target)) { Forget(); return; }
        if (AnimalLandTargetRules.IsVisible(context, Target, retentionDistance))
            Remember(Target, Target.transform.position, true, CanSearch);
        else
        {
            Visible = false;
            if (!CanSearch) Forget();
        }
    }

    public void Forget()
    {
        Target = null;
        Visible = HasMemory = CanSearch = WaitingAtLastPosition = requestedArrivalDetection = false;
        rememberedGuid = 0;
        rememberedGeneration = 0;
        LastKnownPosition = default;
        travelRemaining = waitRemaining = 0f;
    }

    public void TickSearch(float deltaTime)
    {
        if (!IsSearching) return;
        float arrival = Mathf.Max(0.05f, arrivalDistance);
        if (!WaitingAtLastPosition && WorldTopologyRuntime.SqrDistance(context.Position, LastKnownPosition) <= arrival * arrival)
            WaitingAtLastPosition = true;
        if (WaitingAtLastPosition)
        {
            context.Mod_Mover?.StopMovement();
            if (!requestedArrivalDetection)
            {
                requestedArrivalDetection = true;
                context.Detector?.RequestDetectorUpdate();
            }
            waitRemaining = Mathf.Max(0f, waitRemaining - deltaTime);
        }
        else travelRemaining = Mathf.Max(0f, travelRemaining - deltaTime);
        if ((WaitingAtLastPosition ? waitRemaining : travelRemaining) <= 0f) Forget();
    }

    public override void Dispose() => Forget();
    #endregion
}

/// <summary>按完整战斗身份累计驻留和命中仇恨，并选择当前最高可见目标。</summary>
public sealed class AnimalTerritorialHatredCapability : AnimalBehaviorTreeCapability
{
    #region 仇恨计时与受击归属
    private readonly AnimalBehaviorTreeContext context;
    private readonly float presenceRadius;
    private readonly float chaseDistance;
    private readonly string combatKey;
    private readonly AIProximityHatredLedger<CombatIdentity> ledger = new();
    private readonly Dictionary<CombatIdentity, Item> targets = new();
    private readonly List<CombatIdentity> invalidTargets = new();
    private readonly Predicate<CombatIdentity> eligiblePredicate;

    public Item CurrentTarget { get; private set; }
    public AIProximityHatredLedger<CombatIdentity> Ledger => ledger;
    public bool LockCurrentTarget { get; set; }
    public Func<Item, bool> AdditionalEligibility { get; set; }

    public AnimalTerritorialHatredCapability(AnimalBehaviorTreeContext context, JObject config, JObject saved)
    {
        this.context = context;
        presenceRadius = AnimalLandTargetRules.Number(config, "presenceRadius", 8f);
        chaseDistance = AnimalLandTargetRules.Number(config, "chaseDistance", 30f);
        combatKey = AnimalLandTargetRules.Text(config, "combatKey", "combat");
        ledger.Configure(AnimalLandTargetRules.Number(config, "presenceThresholdSeconds", 60f),
            AnimalLandTargetRules.Number(config, "initialHatred", 120f),
            AnimalLandTargetRules.Number(config, "decayPerSecond", 1f),
            AnimalLandTargetRules.Number(config, "hatredPerHit", 30f));
        eligiblePredicate = IsEligibleVisibleTarget;
        if (context.Detector != null) context.Detector.DetectionRadius = Mathf.Max(context.Detector.DetectionRadius, presenceRadius, chaseDistance);
        if (context.Health != null) context.Health.OnHitReceived += HandleConfirmedHit;
    }

    public override void Tick(float deltaTime)
    {
        ItemMgr manager = ItemMgr.GetInstance();
        Tick(deltaTime, manager?.Player_DIC.Values);
    }

    public void Tick(float deltaTime, IEnumerable<Item> nearbyPlayers)
    {
        ledger.BeginUpdate(Mathf.Max(0f, deltaTime));
        invalidTargets.Clear();
        foreach (KeyValuePair<CombatIdentity, Item> pair in targets)
            if (!AnimalLandTargetRules.IsLivingOpponent(context, pair.Value) || GameplayCombatBridge.Identity(pair.Value) != pair.Key)
                invalidTargets.Add(pair.Key);
        foreach (CombatIdentity identity in invalidTargets) { ledger.Remove(identity); targets.Remove(identity); }
        if (nearbyPlayers != null)
            foreach (Item player in nearbyPlayers) ObserveNearbyPlayer(player);
        ledger.EndUpdate();
        invalidTargets.Clear();
        foreach (CombatIdentity identity in targets.Keys)
            if (!ledger.Contains(identity)) invalidTargets.Add(identity);
        foreach (CombatIdentity identity in invalidTargets) targets.Remove(identity);
        SelectHighestEligibleTarget();
    }

    public bool ObserveNearbyPlayer(Item target)
    {
        if (!AnimalLandTargetRules.IsLivingOpponent(context, target) || !AnimalLandTargetRules.IsPlayer(target)) return false;
        float radius = Mathf.Min(Mathf.Max(0f, presenceRadius), context.Actor.MaximumPerceptionRadius);
        if (WorldTopologyRuntime.SqrDistance(context.Position, target.transform.position) > radius * radius) return false;
        CombatIdentity identity = GameplayCombatBridge.Identity(target);
        if (!identity.IsValid) return false;
        targets[identity] = target;
        ledger.ObserveNearbyPlayer(identity);
        return true;
    }

    public void HandleConfirmedHit(DamageReceiverDamageInfo info)
    {
        if (info == null || info.DamageValue < 0f || context.Health == null || context.Health.Hp <= 0f) return;
        Item attacker = ResolveRetaliationCreditActor(info);
        if (!AnimalLandTargetRules.IsLivingOpponent(context, attacker)) return;
        CombatIdentity identity = GameplayCombatBridge.Identity(attacker);
        if (!identity.IsValid) return;
        targets[identity] = attacker;
        ledger.RecordHit(identity);
        SelectHighestEligibleTarget();
    }

    private void SelectHighestEligibleTarget()
    {
        if (LockCurrentTarget && CurrentTarget != null)
        {
            if (IsEligibleVisibleTarget(GameplayCombatBridge.Identity(CurrentTarget))) return;
            if (context.TryGetCapability(combatKey, out AnimalCombatCapability combat)) combat.StopAttack(true);
            LockCurrentTarget = false;
        }
        CurrentTarget = ledger.TryGetHighestHatredTarget(eligiblePredicate, out CombatIdentity selected) ? targets[selected] : null;
    }

    public bool IsEligibleVisibleTarget(CombatIdentity identity) =>
        targets.TryGetValue(identity, out Item target) && AnimalLandTargetRules.IsLivingOpponent(context, target) &&
        GameplayCombatBridge.Identity(target) == identity && ledger.GetHatred(identity) > 0f &&
        (AdditionalEligibility == null || AdditionalEligibility(target)) &&
        AnimalLandTargetRules.IsVisible(context, target, chaseDistance);

    public static Item ResolveRetaliationCreditActor(DamageReceiverDamageInfo info)
    {
        if (info == null || info.Context.Attack.Source.Backend == CombatBackend.Environment) return null;
        Item attacker = info.Attacker;
        if (info.Context.Credit.IsValid)
        {
            for (int depth = 0; attacker != null && depth < 8; depth++)
            {
                if (GameplayCombatBridge.Identity(attacker) == info.Context.Credit) return attacker;
                if (attacker.Owner == attacker) break;
                attacker = attacker.Owner;
            }
            Item credited = ItemMgr.GetInstance()?.GetItemByGuid(unchecked((int)info.Context.Credit.Value));
            return credited != null && GameplayCombatBridge.Identity(credited) == info.Context.Credit ? credited : null;
        }
        for (int depth = 0; attacker?.Owner != null && attacker.Owner != attacker && depth < 8; depth++) attacker = attacker.Owner;
        return attacker;
    }

    public override void Dispose()
    {
        if (context.Health != null) context.Health.OnHitReceived -= HandleConfirmedHit;
        ledger.Clear();
        targets.Clear();
        CurrentTarget = null;
    }
    #endregion
}
#endregion
