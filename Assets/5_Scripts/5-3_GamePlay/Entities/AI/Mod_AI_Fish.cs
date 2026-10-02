using System;
using FlatWorld.Networking;
using MemoryPack;
using UnityEngine;

/// <summary>逐条鱼使用现有状态机、营养、Buff、受伤与 Item 生命周期；不建立鱼群模拟器。</summary>
public sealed partial class Mod_AI_Fish : Module, IAIActor, IItemModuleDependencyBinder, ITemperatureSafetyMovement
{
    #region 配置与持久化
    public const string ModuleId = "Mod_AI_Fish";
    private enum Behaviour { Swim, Forage, Stranded, Hooked }
    [Min(0.01f)] public float swimSpeed = 1.1f;
    [Min(0.1f)] public float wanderRadius = 3f;
    [Min(0.1f)] public float forageRadius = 8f;
    [Min(0.05f)] public float eatRange = 0.45f;
    [Min(0.1f)] public float eatSeconds = 0.8f;
    [Min(0.1f)] public float scanInterval = 0.5f;
    public string[] edibleTags = { "Food", "47", "Meat", "Worm" };
    [Min(1)] public int minimumWetStacks = 5;
    [Min(0.1f)] public float dryDamageInterval = 10f;
    [Min(0f)] public float dryDamage = 10f;
    public SpriteRenderer fishRenderer;
    public float spriteForwardAngle = 135f;
    public Ex_ModData_MemoryPackable Data = new();
    public override ModuleData _Data { get => Data; set => Data = (Ex_ModData_MemoryPackable)value; }
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.EveryFrame;

    [MemoryPackable]
    public partial class LifeState
    {
        public float DryElapsed;
        public float Heading = 135f;
    }

    public Item ActorItem => item;
    public bool IsAlive => loaded && item != null && !item.DestructionHandled && health != null && health.Hp > 0f;
    public bool IsHooked => fishingRod != null;
    private LifeState state = new();
    private Mod_Food food;
    private Mod_BuffManager buffs;
    private Mod_DamageReceiver health;
    private Rigidbody2D body;
    private RigidbodyType2D originalBodyType;
    private Collider2D[] colliders;
    private bool[] originalColliderEnabled;
    private readonly AIStateMachine<Behaviour> machine = new();
    private DroppedItemHandle target;
    private string targetTag;
    private Vector2 destination;
    private bool hasDestination, loaded, nodesRegistered;
    private float scanRemaining, eatElapsed, idleRemaining, visualTime;
    private Mod_FishingRod fishingRod;
    private AquaticActorPresentation presentation;
    private Quaternion originalRotation;
    private Vector2 previousVisualPosition;
    private bool temperatureSafetyRetreat;
    private bool temperatureSafetyReached;
    private Vector2 temperatureSafetyDestination;
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
    }

    public override void Load()
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
        originalRotation = fishRenderer.transform.localRotation;
        previousVisualPosition = item.transform.position;
        presentation = new AquaticActorPresentation(item, fishRenderer);
        target = default;
        targetTag = null;
        fishingRod = null;
        scanRemaining = eatElapsed = idleRemaining = visualTime = 0f;
        hasDestination = false;
        temperatureSafetyRetreat = false;
        temperatureSafetyReached = false;
        if (!nodesRegistered)
        {
            machine.Register(new AIStateNode<Behaviour>(Behaviour.Swim, TickSwim));
            machine.Register(new AIStateNode<Behaviour>(Behaviour.Forage, TickForage));
            machine.Register(new AIStateNode<Behaviour>(Behaviour.Stranded, _ => { }));
            machine.Register(new AIStateNode<Behaviour>(Behaviour.Hooked, _ => { }));
            nodesRegistered = true;
        }
        machine.Initialize(Behaviour.Swim);
        loaded = true;
        UpdatePresentation(0f);
    }

    public override void Save() => Data.WriteData(state);

    public override void Unload()
    {
        loaded = false;
        fishingRod = null;
        target = default;
        targetTag = null;
        hasDestination = false;
        temperatureSafetyRetreat = false;
        temperatureSafetyReached = false;
        machine.Reset();
        buffs?.SetWaterStackExposure(false);
        if (body != null) { body.velocity = Vector2.zero; body.bodyType = originalBodyType; }
        if (colliders != null)
            for (int index = 0; index < colliders.Length; index++)
                if (colliders[index] != null) colliders[index].enabled = originalColliderEnabled[index];
        colliders = null;
        originalColliderEnabled = null;
        presentation?.Dispose();
        presentation = null;
        if (fishRenderer != null) fishRenderer.transform.localRotation = originalRotation;
    }
    #endregion

    #region 游动、觅食与干燥
    public override void ModUpdate(float deltaTime)
    {
        if (!IsAlive || deltaTime <= 0f || !float.IsFinite(deltaTime)) return;
        if (GameNetwork.HasStateAuthority)
        {
            // 未加载的地形不是干地，休眠/换区块期间不伪造离水伤害。
            if (!AquaticHabitat.TryGetDepth(item.transform.position, out float depth)) return;
            buffs.SetWaterStackExposure(depth > 0f);
            buffs.AdvanceWaterWetness(depth, deltaTime);
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
            if (temperatureSafetyRetreat)
            {
                TickTemperatureSafetyRetreat(deltaTime);
                UpdatePresentation(deltaTime);
                return;
            }
            scanRemaining -= deltaTime;
            if (!IsHooked && depth >= AquaticHabitat.MinimumDepth && NeedsFood && scanRemaining <= 0f)
            {
                scanRemaining = Mathf.Max(0.1f, scanInterval);
                FindFood();
            }
            if (!NeedsFood) { target = default; targetTag = null; }
            Behaviour next = IsHooked ? Behaviour.Hooked : depth < AquaticHabitat.MinimumDepth
                ? Behaviour.Stranded : target.IsValid ? Behaviour.Forage : Behaviour.Swim;
            machine.TransitionTo(next, null);
            machine.Tick(deltaTime);
        }
        UpdatePresentation(deltaTime);
    }

    private bool NeedsFood => food.Data?.nutrition != null && food.Data.nutrition.GetFoodRate() < 1f;

    private bool IsReachableFood(Vector2 position) => AquaticHabitat.CanTraverse(item.transform.position, position);

    private void FindFood()
    {
        DroppedItemHandle previous = target;
        target = default;
        targetTag = null;
        float bestDistance = forageRadius * forageRadius;
        foreach (string tag in edibleTags)
        {
            if (!DroppedItemService.TryFindNearestTagged(item.transform.position, forageRadius, tag,
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
        if (!DroppedItemService.TryGetPickablePosition(target, out Vector2 position) ||
            !AquaticHabitat.CanSwimAt(position) ||
            WorldTopologyRuntime.SqrDistance(item.transform.position, position) > forageRadius * forageRadius)
        { target = default; eatElapsed = 0f; return; }
        if (WorldTopologyRuntime.SqrDistance(item.transform.position, position) > eatRange * eatRange)
        {
            eatElapsed = 0f;
            if (!SwimTowards(position, deltaTime)) target = default;
            return;
        }
        eatElapsed += deltaTime;
        if (eatElapsed < eatSeconds) return;
        eatElapsed = 0f;
        DroppedItemHandle eaten = target;
        int legacyGuid = eaten.Legacy != null ? eaten.Legacy.itemData.Guid : 0;
        if (NeedsFood && food.TryEatDroppedFood(eaten, targetTag, eatRange))
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

    private bool SwimTowards(Vector2 position, float deltaTime)
    {
        Vector2 origin = item.transform.position;
        Vector2 delta = WorldTopologyRuntime.ShortestDelta(origin, position);
        Vector2 step = Vector2.ClampMagnitude(delta, swimSpeed * deltaTime);
        Vector2 next = WorldTopologyRuntime.NormalizePosition(origin + step);
        if (!AquaticHabitat.CanTraverse(origin, next)) return false;
        MovePosition(next);
        if (step.sqrMagnitude > 0.00001f) state.Heading = Mathf.Atan2(step.y, step.x) * Mathf.Rad2Deg;
        return true;
    }

    private void MovePosition(Vector2 position)
    {
        body.velocity = Vector2.zero;
        body.position = position;
        item.transform.position = new Vector3(position.x, position.y, item.transform.position.z);
        ItemMgr.Instance?.NotifyRuntimeItemMoved(item);
    }

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
        Vector2 position = item.transform.position;
        Vector2 movement = WorldTopologyRuntime.ShortestDelta(previousVisualPosition, position);
        if (!GameNetwork.HasStateAuthority && movement.sqrMagnitude > 0.00001f)
            state.Heading = Mathf.Atan2(movement.y, movement.x) * Mathf.Rad2Deg;
        previousVisualPosition = position;
        visualTime += deltaTime;
        bool underwater = AquaticHabitat.CanSwimAt(item.transform.position);
        presentation?.SetUnderwater(underwater);
        float wiggle = underwater ? Mathf.Sin(visualTime * 9f) * 5f : 0f;
        fishRenderer.transform.localRotation = Quaternion.Euler(0f, 0f, state.Heading - spriteForwardAngle + wiggle);
    }
    #endregion

    #region 钓线约束
    public bool TryHook(Mod_FishingRod rod)
    {
        if (!GameNetwork.HasStateAuthority || !IsAlive || rod == null || IsHooked) return false;
        fishingRod = rod;
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
