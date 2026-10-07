using System;
using FlatWorld.Combat;
using FlatWorld.Networking;
using MemoryPack;
using UnityEngine;

/// <summary>允许空中捕食者暂时接管水生猎物的位置；生命与死亡仍由猎物自身 DamageReceiver 负责。</summary>
public interface IAquaticPredatorCarryTarget
{
    Item ActorItem { get; }
    bool IsAlive { get; }
    float HealthRatio { get; }
    bool CanBeHuntedBy(Item predator);
    bool TryCaptureByPredator(Item predator);
    bool IsCapturedBy(Item predator);
    void MoveWithPredator(Item predator, Vector2 groundPosition, float visualHeight);
    void ReleaseFromPredator(Item predator);
}

/// <summary>声明该 AI 具备主动捕食水生猎物的能力，供猎物感知复用而不硬编码物种。</summary>
public interface IAquaticPredatorThreat
{
    bool ThreatensAquaticPrey(Item prey);
}

/// <summary>逐条鱼使用现有状态机、营养、Buff、受伤与 Item 生命周期；不建立鱼群模拟器。</summary>
public sealed partial class Mod_AI_Fish : Module, IAIActor, IItemModuleDependencyBinder, ITemperatureSafetyMovement,
    IAquaticPredatorCarryTarget
{
    #region 配置与持久化
    public const string ModuleId = "Mod_AI_Fish";
    private enum Behaviour { Swim, Forage, Flee, Stranded, Hooked }
    private static readonly float[] FleeAngles = { 0f, 30f, -30f, 60f, -60f, 90f, -90f, 135f, -135f, 180f };
    [Min(0.01f)] public float swimSpeed = 1.1f;
    [Min(0.1f)] public float wanderRadius = 3f;
    [Min(0.1f)] public float forageRadius = 8f;
    [Min(0.05f)] public float eatRange = 0.45f;
    [Min(0.1f)] public float eatSeconds = 0.8f;
    [Min(0.1f)] public float scanInterval = 0.5f;
    [Min(0.1f)] public float fleeTriggerDistance = 6f;
    [Min(0.1f)] public float fleeSafeDistance = 10f;
    [Min(0.1f)] public float fleeRunDistance = 5f;
    [Min(1f)] public float fleeSpeedMultiplier = 1.8f;
    [Min(0.05f)] public float vigilanceInterval = 0.3f;
    [Min(0.1f)] public float hurtEscapeSeconds = 4f;
    [Range(0f, 1f)] public float eatAvailableFoodThreshold = 1f;
    [Range(0f, 1f)] public float activeForageThreshold = 0.7f;
    public string[] edibleTags = { "Food", "47", "Meat", "Worm" };
    [Min(1)] public int minimumWetStacks = 3;
    [Min(0.1f)] public float dryDamageInterval = 10f;
    [Min(0f)] public float dryDamage = 10f;
    [Min(0.5f)] public float returnWaterSearchRadius = 8f;
    [Min(0.1f)] public float returnWaterScanInterval = 1f;
    [Min(0.05f)] public float strandedHopDistance = 0.65f;
    [Min(0.1f)] public float strandedHopSeconds = 0.32f;
    [Min(0.05f)] public float strandedHopPause = 0.28f;
    [Min(0f)] public float strandedHopHeight = 0.18f;
    [Range(0f, 90f)] public float strandedHopTilt = 28f;
    public SpriteRenderer fishRenderer;
    public Ex_ModData_MemoryPackable Data = new();
    public override ModuleData _Data { get => Data; set => Data = (Ex_ModData_MemoryPackable)value; }
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.EveryFrame;

    [MemoryPackable]
    public partial class LifeState
    {
        public float DryElapsed;
    }

    public Item ActorItem => item;
    public bool IsAlive => loaded && item != null && !item.DestructionHandled && health != null && health.Hp > 0f;
    public bool IsHooked => fishingRod != null;
    public float HealthRatio => health == null || health.MaxHp <= 0f ? 0f : Mathf.Clamp01(health.Hp / health.MaxHp);
    private LifeState state = new();
    private Mod_Food food;
    private Mod_BuffManager buffs;
    private Mod_DamageReceiver health;
    private Mod_ItemDetector threatDetector;
    private Rigidbody2D body;
    private RigidbodyType2D originalBodyType;
    private Collider2D[] colliders;
    private bool[] originalColliderEnabled;
    private readonly AIStateMachine<Behaviour> machine = new();
    private DroppedItemHandle target;
    private string targetTag;
    private Vector2 destination;
    private bool hasDestination, loaded, nodesRegistered;
    private float scanRemaining, eatElapsed, idleRemaining;
    private float vigilanceRemaining, damageFleeRemaining;
    private long lastVigilanceVersion;
    private Item sensedPredator, damageThreat;
    private Vector2 damageThreatOrigin, fleeDestination;
    private bool hasFleeDestination;
    private Mod_FishingRod fishingRod;
    private Item predatorCarrier;
    private float carriedVisualHeight;
    private AquaticActorPresentation presentation;
    private bool temperatureSafetyRetreat;
    private bool temperatureSafetyReached;
    private Vector2 temperatureSafetyDestination;
    private Vector2 returnWaterDestination, hopOrigin, hopDelta;
    private bool hasReturnWaterDestination, hopping;
    private float returnWaterScanRemaining, hopElapsed, hopDuration, hopPauseRemaining;
    private float hopVisualHeight, hopVisualTilt, hopTiltDirection;
    public int TemperatureSafetyMovementPriority => 100;
    public bool ShouldAdvanceTemperatureSafetyDestination => temperatureSafetyRetreat && temperatureSafetyReached;
    #endregion

    #region 装配与生命周期
    public override void Awake()
    {
        Data.ID = ModuleId;
        base.Awake();
    }

    public void BindModuleDependencies(ItemMods modules)
    {
        food = modules.RequireSingleModById<Mod_Food>(ModText.Food);
        buffs = modules.RequireSingleModById<Mod_BuffManager>(ModText.Mod_BuffManager);
        health = modules.RequireSingleModById<Mod_DamageReceiver>(ModText.Hp);
        threatDetector = modules.RequireSingleModById<Mod_ItemDetector>(ModText.Detector);
    }

    protected override void OnLoad()
    {
        if (fishRenderer == null) fishRenderer = item.Sprite;
        if (fishRenderer == null) throw new InvalidOperationException("小鱼外壳缺少独立的水下 SpriteRenderer。");
        state = Data.GetData<LifeState>() ?? new LifeState();
        if (!float.IsFinite(state.DryElapsed) || state.DryElapsed < 0f) state.DryElapsed = 0f;
        body = item.GetComponent<Rigidbody2D>();
        if (body == null) throw new InvalidOperationException("小鱼外壳缺少位置同步刚体。");
        originalBodyType = body.bodyType;
        body.velocity = Vector2.zero;
        body.bodyType = RigidbodyType2D.Kinematic;
        colliders = item.GetComponentsInChildren<Collider2D>(true);
        originalColliderEnabled = new bool[colliders.Length];
        for (int index = 0; index < colliders.Length; index++)
        {
            originalColliderEnabled[index] = colliders[index].enabled;
            // 只保留受击等 Trigger，不和玩家、船只发生实体碰撞。
            if (!colliders[index].isTrigger) colliders[index].enabled = false;
        }
        presentation = new AquaticActorPresentation(item, fishRenderer);
        target = default;
        targetTag = null;
        fishingRod = null;
        predatorCarrier = null;
        sensedPredator = null;
        damageThreat = null;
        damageThreatOrigin = default;
        carriedVisualHeight = 0f;
        scanRemaining = eatElapsed = idleRemaining = vigilanceRemaining = damageFleeRemaining = 0f;
        lastVigilanceVersion = threatDetector.AppliedVersion;
        hasDestination = false;
        hasFleeDestination = false;
        temperatureSafetyRetreat = false;
        temperatureSafetyReached = false;
        ResetStrandedMotion();
        threatDetector.DetectionRadius = Mathf.Max(fleeTriggerDistance, fleeSafeDistance);
        if (!nodesRegistered)
        {
            machine.Register(new AIStateNode<Behaviour>(Behaviour.Swim, TickSwim));
            machine.Register(new AIStateNode<Behaviour>(Behaviour.Forage, TickForage));
            machine.Register(new AIStateNode<Behaviour>(Behaviour.Flee, TickFlee));
            machine.Register(new AIStateNode<Behaviour>(Behaviour.Stranded, TickStranded,
                ResetStrandedMotion, ResetStrandedMotion));
            machine.Register(new AIStateNode<Behaviour>(Behaviour.Hooked, _ => { }));
            nodesRegistered = true;
        }
        machine.Initialize(Behaviour.Swim);
        loaded = true;
        health.OnDamageReceived -= HandleFishDamage;
        health.OnDamageReceived += HandleFishDamage;
        UpdatePresentation(0f);
    }

    protected override void OnSave() => Data.WriteData(state);

    protected override void OnUnload()
    {
        loaded = false;
        if (health != null) health.OnDamageReceived -= HandleFishDamage;
        fishingRod = null;
        predatorCarrier = null;
        sensedPredator = null;
        damageThreat = null;
        damageFleeRemaining = 0f;
        carriedVisualHeight = 0f;
        target = default;
        targetTag = null;
        hasDestination = false;
        hasFleeDestination = false;
        temperatureSafetyRetreat = false;
        temperatureSafetyReached = false;
        machine.Reset();
        ResetStrandedMotion();
        buffs?.SetWaterStackExposure(false);
        if (body != null) { body.velocity = Vector2.zero; body.bodyType = originalBodyType; }
        if (colliders != null)
            for (int index = 0; index < colliders.Length; index++)
                if (colliders[index] != null) colliders[index].enabled = originalColliderEnabled[index];
        colliders = null;
        originalColliderEnabled = null;
        presentation?.Dispose();
        presentation = null;
    }
    #endregion

    #region 游动、觅食与干燥
    public override void ModUpdate(float deltaTime)
    {
        if (!IsAlive || deltaTime <= 0f || !float.IsFinite(deltaTime)) return;
        if (GameNetwork.HasStateAuthority)
        {
            if (predatorCarrier != null)
            {
                buffs.SetWaterStackExposure(false);
                target = default;
                targetTag = null;
                hasDestination = false;
                UpdatePresentation(deltaTime);
                return;
            }
            // 未加载的地形不是干地，休眠/换区块期间不伪造离水伤害。
            if (!AquaticHabitat.TryGetDepth(item.transform.position, out float depth)) return;
            buffs.SetWaterStackExposure(depth > 0f);
            buffs.AdvanceWaterWetness(depth, deltaTime);
            // 鱼达到最低潮湿层数后视为水分充足。
            if (buffs.GetBuffStacks(WetBuffIds.Wet) >= minimumWetStacks) state.DryElapsed = 0f;
            else
            {
                state.DryElapsed += deltaTime;
                float interval = Mathf.Max(0.1f, dryDamageInterval);
                while (state.DryElapsed >= interval && IsAlive)
                {
                    state.DryElapsed -= interval;
                    health.ForceHurt(dryDamage);
                }
            }
            if (!IsAlive) return;
            // 离水先靠蹦跳回水，不能被温度避险的游动分支卡在岸上。
            if (!IsHooked && depth < AquaticHabitat.MinimumDepth)
            {
                machine.TransitionTo(Behaviour.Stranded, null);
                machine.Tick(deltaTime);
                UpdatePresentation(deltaTime);
                return;
            }
            if (machine.CurrentState == Behaviour.Stranded)
                machine.TransitionTo(IsHooked ? Behaviour.Hooked : Behaviour.Swim, null);
            if (temperatureSafetyRetreat)
            {
                TickTemperatureSafetyRetreat(deltaTime);
                UpdatePresentation(deltaTime);
                return;
            }
            damageFleeRemaining = Mathf.Max(0f, damageFleeRemaining - deltaTime);
            TickVigilance(deltaTime);
            scanRemaining -= deltaTime;
            if (HasActiveThreat)
            {
                target = default;
                targetTag = null;
                eatElapsed = 0f;
            }
            else if (!IsHooked && depth >= AquaticHabitat.MinimumDepth && WillEatAvailableFood && scanRemaining <= 0f)
            {
                scanRemaining = Mathf.Max(0.1f, scanInterval);
                FindFood(ShouldActivelyForage ? forageRadius : eatRange);
            }
            if (!WillEatAvailableFood) { target = default; targetTag = null; }
            Behaviour next = IsHooked ? Behaviour.Hooked : depth < AquaticHabitat.MinimumDepth
                ? Behaviour.Stranded : HasActiveThreat ? Behaviour.Flee : target.IsValid ? Behaviour.Forage : Behaviour.Swim;
            machine.TransitionTo(next, null);
            machine.Tick(deltaTime);
        }
        UpdatePresentation(deltaTime);
    }

    private bool WillEatAvailableFood => food.Data?.nutrition != null &&
                                          food.Data.nutrition.GetFoodRate() < Mathf.Clamp01(eatAvailableFoodThreshold);

    private bool ShouldActivelyForage => food.Data?.nutrition != null &&
                                         food.Data.nutrition.GetFoodRate() <
                                         Mathf.Min(Mathf.Clamp01(eatAvailableFoodThreshold),
                                             Mathf.Clamp01(activeForageThreshold));

    private bool IsReachableFood(Vector2 position) => AquaticHabitat.CanTraverse(item.transform.position, position);

    private void FindFood(float searchRadius)
    {
        DroppedItemHandle previous = target;
        target = default;
        targetTag = null;
        float radius = Mathf.Max(eatRange, searchRadius);
        float bestDistance = radius * radius;
        foreach (string tag in edibleTags)
        {
            if (!DroppedItemService.TryFindNearestTagged(item.transform.position, radius, tag,
                    out DroppedItemHandle candidate, IsReachableFood) ||
                !DroppedItemService.TryGetPickablePosition(candidate, out Vector2 position)) continue;
            float distance = WorldTopologyRuntime.SqrDistance(item.transform.position, position);
            if (distance > bestDistance) continue;
            bestDistance = distance;
            target = candidate;
            targetTag = tag;
        }
        if (previous.Id != target.Id || previous.Epoch != target.Epoch) eatElapsed = 0f;
    }

    private void TickForage(float deltaTime)
    {
        float allowedRadius = ShouldActivelyForage ? forageRadius : eatRange;
        if (!DroppedItemService.TryGetPickablePosition(target, out Vector2 position) ||
            !AquaticHabitat.CanSwimAt(position) ||
            WorldTopologyRuntime.SqrDistance(item.transform.position, position) > allowedRadius * allowedRadius)
        { target = default; eatElapsed = 0f; return; }
        if (WorldTopologyRuntime.SqrDistance(item.transform.position, position) > eatRange * eatRange)
        {
            eatElapsed = 0f;
            // 70% 以上只吃嘴边已有食物，不会主动追过去。
            if (!ShouldActivelyForage) { target = default; return; }
            if (!SwimTowards(position, deltaTime)) target = default;
            return;
        }
        eatElapsed += deltaTime;
        if (eatElapsed < eatSeconds) return;
        eatElapsed = 0f;
        DroppedItemHandle eaten = target;
        int legacyGuid = eaten.Legacy != null ? eaten.Legacy.itemData.Guid : 0;
        if (WillEatAvailableFood && food.TryEatDroppedFood(eaten, targetTag, eatRange))
            Mod_FishingRod.NotifyBaitEaten(eaten, this, legacyGuid);
        target = default;
    }

    private void TickSwim(float deltaTime)
    {
        idleRemaining -= deltaTime;
        if (idleRemaining > 0f) return;
        if (!hasDestination)
        {
            Vector2 origin = item.transform.position;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                Vector2 candidate = WorldTopologyRuntime.NormalizePosition(origin + UnityEngine.Random.insideUnitCircle * wanderRadius);
                if (WorldTopologyRuntime.SqrDistance(origin, candidate) < 0.1f || !AquaticHabitat.CanTraverse(origin, candidate)) continue;
                destination = candidate;
                hasDestination = true;
                break;
            }
            if (!hasDestination) { idleRemaining = 0.5f; return; }
        }
        if (WorldTopologyRuntime.SqrDistance(item.transform.position, destination) <= 0.04f || !SwimTowards(destination, deltaTime))
        {
            hasDestination = false;
            idleRemaining = UnityEngine.Random.Range(0.25f, 1.1f);
        }
    }

    private bool SwimTowards(Vector2 position, float deltaTime, float speedMultiplier = 1f)
    {
        Vector2 origin = item.transform.position;
        Vector2 delta = WorldTopologyRuntime.ShortestDelta(origin, position);
        Vector2 step = Vector2.ClampMagnitude(delta, swimSpeed * Mathf.Max(0f, speedMultiplier) * deltaTime);
        Vector2 next = WorldTopologyRuntime.NormalizePosition(origin + step);
        if (!AquaticHabitat.CanTraverse(origin, next)) return false;
        MovePosition(next);
        return true;
    }

    private void MovePosition(Vector2 position)
    {
        body.velocity = Vector2.zero;
        body.position = position;
        item.transform.position = new Vector3(position.x, position.y, item.transform.position.z);
        ItemMgr.Instance?.NotifyRuntimeItemMoved(item);
    }

    #region 离水蹦跳与回水
    private void ResetStrandedMotion()
    {
        hasReturnWaterDestination = hopping = false;
        returnWaterScanRemaining = hopElapsed = hopPauseRemaining = 0f;
        hopVisualHeight = hopVisualTilt = 0f;
        target = default;
        targetTag = null;
        hasDestination = hasFleeDestination = false;
        eatElapsed = 0f;
    }

    private void TickStranded(float deltaTime)
    {
        returnWaterScanRemaining -= deltaTime;
        if (!hopping)
        {
            hopPauseRemaining -= deltaTime;
            if (hopPauseRemaining > 0f) return;
            Vector2 origin = item.transform.position;
            if (returnWaterScanRemaining <= 0f ||
                (hasReturnWaterDestination && !AquaticHabitat.CanSwimAt(returnWaterDestination)))
            {
                returnWaterScanRemaining = Mathf.Max(0.1f, returnWaterScanInterval);
                hasReturnWaterDestination = TryFindReturnWater(origin, out returnWaterDestination);
            }
            hopOrigin = origin;
            hopDelta = PlanStrandedHop(origin);
            hopElapsed = 0f;
            hopDuration = Mathf.Max(0.1f, strandedHopSeconds) * UnityEngine.Random.Range(0.9f, 1.1f);
            hopTiltDirection = UnityEngine.Random.value < 0.5f ? -1f : 1f;
            hopping = true;
        }

        hopElapsed = Mathf.Min(hopDuration, hopElapsed + deltaTime);
        float progress = hopElapsed / hopDuration;
        Vector2 next = WorldTopologyRuntime.NormalizePosition(hopOrigin + hopDelta * progress);
        if (hopDelta.sqrMagnitude > 0.000001f && !CanHopAcross(item.transform.position, next))
        {
            FinishStrandedHop();
            returnWaterScanRemaining = 0f;
            return;
        }
        if (hopDelta.sqrMagnitude > 0.000001f) MovePosition(next);
        // 地面位置只在起跳期间前进，抛物线高度和甩身只作用于鱼的视觉节点。
        hopVisualHeight = 4f * progress * (1f - progress) * Mathf.Max(0f, strandedHopHeight);
        hopVisualTilt = Mathf.Sin(progress * Mathf.PI * 2f) * strandedHopTilt * hopTiltDirection;
        if (progress >= 1f || AquaticHabitat.CanSwimAt(next)) FinishStrandedHop();
    }

    private void FinishStrandedHop()
    {
        hopping = false;
        hopVisualHeight = hopVisualTilt = 0f;
        hopPauseRemaining = Mathf.Max(0.05f, strandedHopPause) * UnityEngine.Random.Range(0.7f, 1.3f);
    }

    private bool TryFindReturnWater(Vector2 origin, out Vector2 water)
    {
        float radius = Mathf.Clamp(returnWaterSearchRadius, 0.5f, 32f);
        float bestDistance = radius * radius;
        int range = Mathf.CeilToInt(radius);
        Vector2Int cell = WorldNavigationGrid.WorldToCell(origin);
        water = default;
        bool found = false;
        for (int y = -range; y <= range; y++)
        for (int x = -range; x <= range; x++)
        {
            Vector2 candidate = WorldTopologyRuntime.NormalizePosition(
                new Vector2(cell.x + x + 0.5f, cell.y + y + 0.5f));
            float distance = WorldTopologyRuntime.SqrDistance(origin, candidate);
            if (distance > bestDistance || !AquaticHabitat.CanSwimAt(candidate) ||
                !CanHopAcross(origin, candidate)) continue;
            bestDistance = distance;
            water = candidate;
            found = true;
        }
        return found;
    }

    private Vector2 PlanStrandedHop(Vector2 origin)
    {
        Vector2 preferred = hasReturnWaterDestination
            ? WorldTopologyRuntime.ShortestDelta(origin, returnWaterDestination).normalized
            : UnityEngine.Random.insideUnitCircle.normalized;
        if (preferred.sqrMagnitude < 0.0001f) preferred = Vector2.right;
        float distance = Mathf.Max(0.05f, strandedHopDistance) * UnityEngine.Random.Range(0.8f, 1.15f);
        if (hasReturnWaterDestination)
            distance = Mathf.Min(distance, WorldTopologyRuntime.ShortestDelta(origin, returnWaterDestination).magnitude);
        for (int ring = 0; ring < 3; ring++)
        for (int index = 0; index < FleeAngles.Length; index++)
        {
            float radians = FleeAngles[index] * Mathf.Deg2Rad;
            Vector2 direction = new(preferred.x * Mathf.Cos(radians) - preferred.y * Mathf.Sin(radians),
                preferred.x * Mathf.Sin(radians) + preferred.y * Mathf.Cos(radians));
            Vector2 step = direction * (distance * (1f - ring * 0.3f));
            Vector2 candidate = WorldTopologyRuntime.NormalizePosition(origin + step);
            if (hasReturnWaterDestination && WorldTopologyRuntime.SqrDistance(candidate, returnWaterDestination) >=
                WorldTopologyRuntime.SqrDistance(origin, returnWaterDestination)) continue;
            if (CanHopAcross(origin, candidate)) return step;
        }
        return Vector2.zero;
    }

    private static bool CanHopAcross(Vector2 origin, Vector2 target)
    {
        WorldNavigationManager navigation = WorldNavigationManager.ExistingInstance;
        if (navigation == null) return false;
        Vector2 delta = WorldTopologyRuntime.ShortestDelta(origin, target);
        int samples = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / 0.2f));
        Vector2 previous = origin;
        for (int index = 1; index <= samples; index++)
        {
            Vector2 position = WorldTopologyRuntime.NormalizePosition(origin + delta * ((float)index / samples));
            if (!AquaticHabitat.TryGetDepth(position, out _) || !navigation.IsWalkable(position)) return false;
            Vector2Int previousCell = WorldNavigationGrid.WorldToCell(previous);
            Vector2Int cell = WorldNavigationGrid.WorldToCell(position);
            Vector2Int cellDelta = WorldTopologyRuntime.ShortestDelta(previousCell, cell);
            if (cellDelta.x != 0 && cellDelta.y != 0 &&
                (!navigation.IsWalkable(previous + new Vector2(cellDelta.x, 0f)) ||
                 !navigation.IsWalkable(previous + new Vector2(0f, cellDelta.y)))) return false;
            previous = position;
        }
        return true;
    }
    #endregion

    #region 捕食者逃离

    private bool HasActiveThreat => damageFleeRemaining > 0f || sensedPredator != null;

    /// <summary>按固定间隔扫描食肉动物和玩家，逃离状态使用更大的安全距离形成滞回。</summary>
    private void TickVigilance(float deltaTime)
    {
        vigilanceRemaining -= deltaTime;
        if (vigilanceRemaining <= 0f)
        {
            vigilanceRemaining = Mathf.Max(0.05f, vigilanceInterval);
            threatDetector.RequestDetectorUpdate();
        }

        long appliedVersion = threatDetector.AppliedVersion;
        if (appliedVersion <= lastVigilanceVersion) return;
        lastVigilanceVersion = appliedVersion;

        float radius = machine.CurrentState == Behaviour.Flee || damageFleeRemaining > 0f
            ? Mathf.Max(fleeTriggerDistance, fleeSafeDistance)
            : fleeTriggerDistance;
        Item previous = sensedPredator;
        sensedPredator = FindClosestPredator(radius);
        if (previous != sensedPredator) hasFleeDestination = false;
    }

    private Item FindClosestPredator(float radius)
    {
        Item closest = null;
        float closestDistanceSqr = float.PositiveInfinity;

        ItemMgr itemManager = ItemMgr.Instance;
        if (itemManager != null)
        {
            foreach (Player player in itemManager.Player_DIC.Values)
                ConsiderPredator(player, radius, ref closest, ref closestDistanceSqr);
        }

        System.Collections.Generic.List<Item> detected = threatDetector.CurrentItemsInArea;
        for (int index = 0; index < detected.Count; index++)
        {
            Item candidate = detected[index];
            if (candidate == null || candidate == item || !IsFishPredator(candidate)) continue;
            ConsiderPredator(candidate, radius, ref closest, ref closestDistanceSqr);
        }
        return closest;
    }

    private void ConsiderPredator(Item candidate, float radius, ref Item closest, ref float closestDistanceSqr)
    {
        if (candidate == null || candidate == item || !candidate.gameObject.activeInHierarchy ||
            !AIFleeUtility.IsWithinEscapeRange(item.transform.position, candidate, threatDetector, radius)) return;

        float distanceSqr = WorldTopologyRuntime.SqrDistance(item.transform.position, candidate.transform.position);
        if (distanceSqr >= closestDistanceSqr) return;
        closestDistanceSqr = distanceSqr;
        closest = candidate;
    }

    private bool IsFishPredator(Item candidate)
    {
        if (candidate is Player || candidate.itemData?.Tags?.ContainsTag(Tag.Player) == true) return true;
        if (candidate.itemData?.Tags?.ContainsTag(Tag.Carnivore) == true) return true;
        if (candidate.itemMods?.Mods == null) return false;

        foreach (Module module in candidate.itemMods.Mods.Values)
            if (module is IAquaticPredatorThreat aquaticPredator && aquaticPredator.ThreatensAquaticPrey(item))
                return true;
        return false;
    }

    private void HandleFishDamage(DamageReceiverDamageInfo info)
    {
        if (!loaded || !GameNetwork.HasStateAuthority || !IsAlive || info == null || info.DamageValue <= 0f) return;
        Item attacker = info.Attacker != null ? info.Attacker.Owner ?? info.Attacker : null;
        if (attacker == null || attacker == item || !DamageThreatOrigin.TryResolve(info, out Vector2 origin)) return;

        damageThreat = attacker;
        damageThreatOrigin = origin;
        damageFleeRemaining = Mathf.Max(0.1f, hurtEscapeSeconds);
        hasFleeDestination = false;
        target = default;
        targetTag = null;
        eatElapsed = 0f;
    }

    private void TickFlee(float deltaTime)
    {
        if (!TryResolveThreatPosition(out Vector2 threatPosition))
        {
            hasFleeDestination = false;
            return;
        }

        Vector2 current = item.transform.position;
        if (!hasFleeDestination || WorldTopologyRuntime.SqrDistance(current, fleeDestination) <= 0.09f ||
            !AquaticHabitat.CanTraverse(current, fleeDestination))
        {
            if (!TryPlanFleeDestination(threatPosition, out fleeDestination))
            {
                hasFleeDestination = false;
                return;
            }
            hasFleeDestination = true;
        }

        if (!SwimTowards(fleeDestination, deltaTime, Mathf.Max(1f, fleeSpeedMultiplier)))
            hasFleeDestination = false;
    }

    private bool TryResolveThreatPosition(out Vector2 threatPosition)
    {
        if (damageFleeRemaining > 0f)
        {
            if (damageThreat != null && damageThreat.gameObject.activeInHierarchy)
                damageThreatOrigin = damageThreat.transform.position;
            threatPosition = damageThreatOrigin;
            return true;
        }

        if (sensedPredator != null && sensedPredator.gameObject.activeInHierarchy)
        {
            threatPosition = sensedPredator.transform.position;
            return true;
        }

        sensedPredator = null;
        threatPosition = default;
        return false;
    }

    /// <summary>优先沿背离威胁的方向选取仍完全处于连续水域中的逃生点。</summary>
    private bool TryPlanFleeDestination(Vector2 threatPosition, out Vector2 planned)
    {
        Vector2 origin = item.transform.position;
        Vector2 away = WorldTopologyRuntime.ShortestDelta(threatPosition, origin);
        Vector2 preferred = away.sqrMagnitude > 0.0001f ? away.normalized : Vector2.right;
        float baseDistance = Mathf.Max(0.1f, fleeRunDistance);
        float bestScore = float.NegativeInfinity;
        planned = default;
        bool found = false;

        for (int ring = 0; ring < 4; ring++)
        {
            float distance = baseDistance * (1f - ring * 0.2f);
            for (int angleIndex = 0; angleIndex < FleeAngles.Length; angleIndex++)
            {
                float radians = FleeAngles[angleIndex] * Mathf.Deg2Rad;
                float cos = Mathf.Cos(radians);
                float sin = Mathf.Sin(radians);
                Vector2 direction = new(preferred.x * cos - preferred.y * sin,
                    preferred.x * sin + preferred.y * cos);
                Vector2 candidate = WorldTopologyRuntime.NormalizePosition(origin + direction * distance);
                if (!AquaticHabitat.CanTraverse(origin, candidate)) continue;

                float score = WorldTopologyRuntime.SqrDistance(threatPosition, candidate) +
                              Vector2.Dot(direction, preferred) * 0.25f;
                if (score <= bestScore) continue;
                bestScore = score;
                planned = candidate;
                found = true;
            }
            if (found) break;
        }
        return found;
    }

    #endregion

    #region 温度避险

    public void SetTemperatureSafetyDestination(Vector2 position)
    {
        temperatureSafetyDestination = WorldTopologyRuntime.NormalizePosition(position);
        temperatureSafetyRetreat = true;
        temperatureSafetyReached = false;
        target = default;
        targetTag = null;
        hasDestination = false;
        eatElapsed = 0f;
    }

    public void ClearTemperatureSafetyDestination()
    {
        temperatureSafetyRetreat = false;
        temperatureSafetyReached = false;
    }

    private void TickTemperatureSafetyRetreat(float deltaTime)
    {
        if (WorldTopologyRuntime.SqrDistance(item.transform.position, temperatureSafetyDestination) <= 0.04f)
        {
            temperatureSafetyReached = true;
            return;
        }

        if (!AquaticHabitat.CanTraverse(item.transform.position, temperatureSafetyDestination))
        {
            temperatureSafetyReached = true;
            return;
        }

        SwimTowards(temperatureSafetyDestination, deltaTime);
    }

    #endregion

    private void UpdatePresentation(float deltaTime)
    {
        bool carried = predatorCarrier != null;
        presentation?.SetCarried(carried, carriedVisualHeight);
        bool underwater = !carried && AquaticHabitat.CanSwimAt(item.transform.position);
        bool stranded = !underwater && !carried && !IsHooked;
        presentation?.SetStrandedHop(stranded ? hopVisualHeight : 0f, stranded ? hopVisualTilt : 0f);
        presentation?.Tick(underwater, deltaTime);
    }
    #endregion

    #region 捕食者携带
    public bool CanBeHuntedBy(Item predator) => predator != null && IsAlive && !IsHooked &&
                                                 (predatorCarrier == null || predatorCarrier == predator);

    public bool TryCaptureByPredator(Item predator)
    {
        if (!GameNetwork.HasStateAuthority || !CanBeHuntedBy(predator) || predatorCarrier != null)
            return false;
        predatorCarrier = predator;
        ResetStrandedMotion();
        target = default;
        targetTag = null;
        hasDestination = false;
        temperatureSafetyRetreat = false;
        temperatureSafetyReached = false;
        body.velocity = Vector2.zero;
        return true;
    }

    public bool IsCapturedBy(Item predator) => predator != null && predatorCarrier == predator && IsAlive;

    public void MoveWithPredator(Item predator, Vector2 groundPosition, float visualHeight)
    {
        if (!GameNetwork.HasStateAuthority || !IsCapturedBy(predator)) return;
        carriedVisualHeight = Mathf.Max(0f, visualHeight);
        MovePosition(WorldTopologyRuntime.NormalizePosition(groundPosition));
        UpdatePresentation(0f);
    }

    public void ReleaseFromPredator(Item predator)
    {
        if (predatorCarrier != predator) return;
        predatorCarrier = null;
        carriedVisualHeight = 0f;
        hasDestination = false;
        scanRemaining = 0f;
        presentation?.SetCarried(false, 0f);
    }
    #endregion

    #region 钓线约束
    public bool TryHook(Mod_FishingRod rod)
    {
        if (!GameNetwork.HasStateAuthority || !IsAlive || rod == null || IsHooked || predatorCarrier != null) return false;
        fishingRod = rod;
        ResetStrandedMotion();
        target = default;
        return true;
    }

    public bool IsHookedBy(Mod_FishingRod rod) => IsAlive && fishingRod == rod;

    public void PullByLine(Mod_FishingRod rod, Vector2 position)
    {
        if (GameNetwork.HasStateAuthority && IsHookedBy(rod)) MovePosition(WorldTopologyRuntime.NormalizePosition(position));
    }

    public void ReleaseLine(Mod_FishingRod rod)
    {
        if (fishingRod != rod) return;
        fishingRod = null;
        hasDestination = false;
        scanRemaining = 0f;
    }
    #endregion
}
