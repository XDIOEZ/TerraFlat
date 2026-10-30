using System;
using System.Collections.Generic;
using FlatWorld.Combat;
using FlatWorld.Networking;
using UnityEngine;

[Serializable]
public sealed class ColonyResidentState
{
    public float Satiety = 1440f; // 当前饱食度。
    public float Anger; // 警惕值和愤怒值共用同一数值。
    public bool Angry; // 数值首次达到上限后才进入持续追击阶段。
    public bool ReturningHome; // 仅采蜜结束且饱食度大于门槛时置位。
    public bool Orphaned; // 蜂巢被摧毁后转为独立蜜蜂。
    public float OrphanHomeX; // 原蜂巢世界位置 X。
    public float OrphanHomeY; // 原蜂巢世界位置 Y。

    /// <summary>蜂巢快照只复制持久玩法状态，不保存临时追击和采蜜目标。</summary>
    public ColonyResidentState Copy() => new()
    {
        Satiety = Satiety,
        Anger = Anger,
        Angry = Angry,
        ReturningHome = ReturningHome,
        Orphaned = Orphaned,
        OrphanHomeX = OrphanHomeX,
        OrphanHomeY = OrphanHomeY
    };
}


/// <summary>
/// 群落成员的资源采集、饱食与防御能力；行为优先级由共享行为树决定。
/// 成员使用飞行能力移动，通过群落接口交换资源和持久状态。
/// </summary>
public sealed partial class Mod_ColonyResident : Module, IItemModuleDependencyBinder, IDamageSender,
    ICombatDamageContextModifier, IDamageDeliverySource, IColonyResident, IActorDebugOverlayOwner
{
    #region 配置与状态
    public const string ModuleId = "Mod_ColonyResident";
    public string cropResourceTag = "BeeForage.Crop";
    public string groundResourceTag = "BeeForage.Flower";

    public Ex_ModData ModData = new(); // 模块数据契约。
    [Min(1f)] public float SatietyMaximum = 1440f; // 一个游戏日的饱食度上限。
    [Min(0f)] public float SatietyDrainPerSecond = 1f; // 非采蜜状态的每秒消耗。
    [Min(0f)] public float ForageBelow = 800f; // 只在低于该值时主动寻找采蜜源。
    [Min(0f)] public float ReturnAbove = 1000f; // 单次采蜜结束后的严格返巢门槛。
    [Min(0f)] public float HoneyContributionCost = 500f; // 返巢贡献一蜜的饱食度成本。
    [Min(0f)] public float HoneyMealGain = 500f; // 消耗巢蜜一份的恢复值。
    [Min(0f)] public float CropGainPerSecond = 100f; // 农作物采蜜恢复速度。
    [Min(0f)] public float FlowerGainPerSecond = 10f; // 花朵采蜜每秒恢复十点饱食度。
    [Min(0.01f)] public float PatrolFlightSpeedMultiplier = 0.5f; // 悠闲巡逻时使用基础飞行速度的一半。
    [Min(0.1f)] public float ForageScanInterval = 1f; // 九宫格资源扫描间隔。
    [Min(0.1f)] public float ForageLandingDistance = 0.3f; // 采蜜停落半径。
    [Min(0.1f)] public float HomeArrivalDistance = 0.45f; // 返巢交易半径。
    [Min(0.1f)] public float AngryLockRadius = 3f; // 愤怒状态的移动目标锁定半径。
    [Min(0.1f)] public float LostTargetAngerSeconds = 10f; // 丢失目标后保持愤怒并搜索最后位置的时长。
    [Min(0.1f)] public float AlertSeconds = 10f; // 与生物同格持续多久激怒蜜蜂。
    [Min(0.1f)] public float AngerMaximum = 10f; // 警惕与愤怒共用的上限。
    [Min(0f)] public float AngerDecayPerSecond = 0.1f; // 无目标时百秒归零。
    [Min(0.1f)] public float TargetScanInterval = 0.2f; // 运动状态采样间隔。
    [Min(0.1f)] public float StingDistance = 0.45f; // 近身蜇刺距离。
    [Min(0f)] public float ChaseStopDistance = 0.2f; // 追击内圈独立于蜇刺和冷却。
    [Min(0.1f)] public float StingInterval = 1f; // 同一蜜蜂的攻击间隔。

    private ColonyResidentState state = new(); // 本蜂权威玩法状态。
    private Mod_AnimalFlightMovement bird; // 复用飞行与导航的通用模块。
    private IColonyHome colony; // 归属蜂巢。
    private Mod_ItemDetector detector; // 共用地形视线判断。
    private Mod_DamageReceiver damageReceiver; // 监听本蜂受击，用于巡逻时呼叫同巢支援。
    private float stingRemaining; // 蜇刺剩余冷却。
    private bool nightSleepRequested; // 日落后蜂巢下达的归巢睡眠请求。
    private Vector2 patrolTarget; // 当前领地内随机巡逻点，不持久化。
    private bool hasPatrolTarget; // 是否已经选中本轮巡逻点。
    public override string CanonicalModuleId => ModuleId;
    public override ModuleData _Data { get => ModData; set => ModData = (Ex_ModData)value; }
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public bool IsAlive => bird != null && bird.IsAlive;
    public IColonyHome Colony => colony;
    public bool CanRespondToColonyHelp => CanAnswerAllyHelpCall();
    public float Satiety => state.Satiety;
    public float Anger => state.Anger;
    public bool HasActiveAngryPursuit => state.Angry && (lockedTarget != null || searchingLastPosition);
    public float SatietyDrainRate => SatietyDrainPerSecond;
    public float HomeArrivalRadius => HomeArrivalDistance;
    public bool IsOrphaned => state.Orphaned;
    public bool HasColonyHome => colony != null;
    #endregion

    #region 装配与持久化
    public void BindModuleDependencies(ItemMods modules)
    {
        bird = modules.RequireSingleModById<Mod_AnimalFlightMovement>(Mod_AnimalFlightMovement.ModuleId);
        detector = modules.RequireSingleModById<Mod_ItemDetector>(ModText.Detector);
        damageReceiver = modules.RequireSingleModById<Mod_DamageReceiver>(ModText.Hp);
    }

    protected override void OnLoad()
    {
        // 单独重新启用模块时也重绑依赖，避免沿用卸载前的引用。
        BindModuleDependencies(item.itemMods);
        if (!bird.permanentFlight || SatietyMaximum <= ReturnAbove || ReturnAbove <= ForageBelow ||
            HoneyContributionCost < 0f || HoneyMealGain < 0f || CropGainPerSecond <= 0f ||
            FlowerGainPerSecond <= 0f || PatrolFlightSpeedMultiplier <= 0f || LostTargetAngerSeconds <= 0f)
            throw new InvalidOperationException("蜜蜂行为配置无效。");
        state = ModData.GetData<ColonyResidentState>() ?? new ColonyResidentState();
        state.Satiety = Mathf.Clamp(state.Satiety, 0f, SatietyMaximum);
        state.Anger = Mathf.Clamp(state.Anger, 0f, AngerMaximum);
        if (state.Anger <= 0f)
            state.Angry = false;
        ResetBeeTargets();
        nightSleepRequested = false;
        stingRemaining = 0f;
        if (state.Orphaned)
            bird.SetFlightHome(new Vector2(state.OrphanHomeX, state.OrphanHomeY));
        damageReceiver.OnAttacked -= HandleBeeAttacked;
        damageReceiver.OnAttacked += HandleBeeAttacked;
        AI_DebugOverlay.Register(this, DrawDebugOverlay);
    }

    /// <summary>蜂巢在成员装配完毕后注入归属与上一次保存的行为状态。</summary>
    public void BindColony(IColonyHome owner, ColonyResidentState saved)
    {
        if (owner == null || bird == null)
            throw new InvalidOperationException("蜜蜂必须在鸟模块装配后绑定蜂巢。");
        colony = owner;
        bird.SetColonyHome(owner.ColonyItem.itemData.Guid, owner.HomePosition);
        if (saved != null)
            state = saved.Copy();
        else
            state.Satiety = UnityEngine.Random.Range(1f, SatietyMaximum); // 新成员随机错开饱食周期，避免整巢同步巡航。
        state.Orphaned = false;
        state.OrphanHomeX = 0f;
        state.OrphanHomeY = 0f;
        nightSleepRequested = false;
        ClearHiveDefenseTarget();
        state.Satiety = Mathf.Clamp(state.Satiety, 0f, SatietyMaximum);
        state.Anger = Mathf.Clamp(state.Anger, 0f, AngerMaximum);
        if (state.Anger <= 0f)
            state.Angry = false;
    }

    /// <summary>蜂巢保存时取得独立副本，避免卸载后对象池复用修改旧快照。</summary>
    public ColonyResidentState CaptureState() => state.Copy();

    protected override void OnSave() => ModData.WriteData(state);

    protected override void OnUnload()
    {
        AI_DebugOverlay.Unregister(this);
        if (damageReceiver != null)
            damageReceiver.OnAttacked -= HandleBeeAttacked;
        ResetBeeTargets();
        colony = null;
        bird = null;
        detector = null;
        damageReceiver = null;
        nightSleepRequested = false;
    }

    private void OnDestroy() => AI_DebugOverlay.Unregister(this);
    #endregion

    #region 群落状态与节点动作
    public void TickColonyEnvironmentAndThreats(float deltaTime)
    {
        if (!IsRuntimeLoaded || !IsAlive || !GameNetwork.HasStateAuthority ||
            (colony == null && !state.Orphaned)) return;
        float step = Mathf.Max(0f, deltaTime);
        stingRemaining = Mathf.Max(0f, stingRemaining - step);
        forageScanRemaining -= step;
        if (hiveDefenseTarget == null && !(nightSleepRequested && colony != null && !HasActiveAngryPursuit))
            TickTargetAwareness(step);
        RefreshColonyThreatMemory();
        bool gathering = !state.Angry && bird.Phase == BirdFlightPhase.Ground && HasActiveForageTarget &&
            WorldTopologyRuntime.SqrDistance(item.transform.position, foragePosition) <=
            ForageLandingDistance * ForageLandingDistance;
        if (!gathering) DrainSatiety(step);
    }

    public bool HasDefenseTarget => hiveDefenseTarget != null && lockedTarget != null;
    public bool ShouldReturnForSleep => nightSleepRequested && colony != null && !HasActiveAngryPursuit;
    public bool IsAngry => state.Angry;
    public bool HasLockedTarget => state.Angry && lockedTarget != null;
    public bool IsSearchingTarget => state.Angry && searchingLastPosition;
    public bool IsReturningHoney => colony != null && state.ReturningHome;
    public bool CanForageNow
    {
        get
        {
            if (colony == null) return false;
            if (HasActiveForageTarget) return true;
            if (state.Satiety >= ForageBelow || forageScanRemaining > 0f) return false;
            forageScanRemaining = ForageScanInterval;
            bool found = TrySelectForageTarget();
            noForageAvailable = !found;
            return found;
        }
    }
    public bool ShouldTakeColonyMeal => colony != null && noForageAvailable;
    public void TickSleepReturn(float deltaTime) => bird.FlyTo(colony.HomePosition, deltaTime);
    public void TickColonyPursuit(float deltaTime) => ChaseLockedTarget(deltaTime);
    public void TickColonySearch(float deltaTime)
    {
        Vector2 destination = AdvanceLastPositionSearch(item.transform.position, deltaTime);
        if (searchingLastPosition && !bird.TryFlyTo(destination, deltaTime, 1f))
        {
            reachedLastSeenPosition = true;
            hasSearchFlightTarget = false;
        }
    }
    public void TickAngryPatrol(float deltaTime)
    {
        state.Anger = Mathf.Max(0f, state.Anger - AngerDecayPerSecond * deltaTime);
        if (state.Anger <= 0f) state.Angry = false;
        if (colony != null) TickTerritoryPatrol(deltaTime, 1f);
        else bird.WanderAroundHome(deltaTime);
    }
    public void TickIndependentWander(float deltaTime) => bird.WanderAroundHome(deltaTime);
    public void TickColonyPatrol(float deltaTime) => TickTerritoryPatrol(deltaTime, PatrolFlightSpeedMultiplier);
    public void StopColonyAction() => bird?.StopFlightAction();
    public void CancelColonyForage() => ClearForageTarget();

    /// <summary>统一推进清醒状态饱食消耗；睡眠期间由蜂巢以一半倍率推进。</summary>
    private void DrainSatiety(float seconds)
    {
        state.Satiety = Mathf.Max(0f, state.Satiety - SatietyDrainPerSecond * Mathf.Max(0f, seconds));
    }

    /// <summary>蜂巢下达或取消夜间归巢请求；已有追击与最后位置搜索结束后才执行归巢。</summary>
    public void SetNightSleepRequested(bool requested)
    {
        if (nightSleepRequested == requested)
            return;
        nightSleepRequested = requested;
        if (!requested)
            return;
        state.ReturningHome = false;
        noForageAvailable = false;
        ClearForageTarget();
        if (!HasActiveAngryPursuit)
            ClearLocalCombatForSleep();
        ResetPatrol();
    }

    /// <summary>蜂巢摧毁后释放为独立实体，并继续追击最后攻击蜂巢的生物。</summary>
    public void DetachFromDestroyedColony(Item revengeTarget, Vector2 destroyedHome)
    {
        Vector2 home = WorldTopologyRuntime.NormalizePosition(destroyedHome);
        state.Orphaned = true;
        state.OrphanHomeX = home.x;
        state.OrphanHomeY = home.y;
        state.ReturningHome = false;
        nightSleepRequested = false;
        noForageAvailable = false;
        ClearForageTarget();
        ResetPatrol();
        colony = null;
        bird.ReleaseColonyHome(home);
        ForceHiveDefenseTarget(revengeTarget);
    }
    #endregion

    #region 领地巡逻
    private const float PatrolArrivalDistance = 0.35f; // 到点后重新随机选取领地内目标。
    private const float PatrolPreferredTravelDistance = 0.75f; // 尽量避免原地附近反复换点。
    private const int PatrolTargetSelectionAttempts = 8; // 小次数随机即可覆盖当前动态领地方形。

    /// <summary>在蜂巢当前领地内随机巡逻；速度倍率由调用状态决定。</summary>
    private void TickTerritoryPatrol(float flightSeconds, float speedMultiplier)
    {
        Vector2 position = item.transform.position;
        float arrivalDistanceSqr = PatrolArrivalDistance * PatrolArrivalDistance;
        if (!hasPatrolTarget || !colony.ContainsTerritoryPosition(patrolTarget) ||
            WorldTopologyRuntime.SqrDistance(position, patrolTarget) <= arrivalDistanceSqr)
            SelectTerritoryPatrolTarget(position);

        if (!hasPatrolTarget)
            return;
        if (!bird.TryFlyTo(patrolTarget, flightSeconds, speedMultiplier))
            hasPatrolTarget = false;
    }

    /// <summary>优先挑选离当前位置稍远的随机点，避免蜜蜂在一个小区域内抖动。</summary>
    private void SelectTerritoryPatrolTarget(Vector2 origin)
    {
        float preferredDistanceSqr = PatrolPreferredTravelDistance * PatrolPreferredTravelDistance;
        float bestDistanceSqr = -1f;
        Vector2 best = default;
        for (int attempt = 0; attempt < PatrolTargetSelectionAttempts; attempt++)
        {
            Vector2 candidate = colony.GetRandomTerritoryPatrolPosition();
            float distanceSqr = WorldTopologyRuntime.SqrDistance(origin, candidate);
            if (distanceSqr > bestDistanceSqr)
            {
                bestDistanceSqr = distanceSqr;
                best = candidate;
            }
            if (distanceSqr < preferredDistanceSqr)
                continue;
            patrolTarget = candidate;
            hasPatrolTarget = true;
            return;
        }

        patrolTarget = best;
        hasPatrolTarget = bestDistanceSqr >= 0f;
    }

    /// <summary>对象池复用或重新绑定时不沿用上一只蜜蜂的巡逻目标。</summary>
    private void ResetPatrol()
    {
        patrolTarget = default;
        hasPatrolTarget = false;
    }
    #endregion

    #region 调试显示
    private static GUIStyle beeDebugStyle; // 蜜蜂共用头顶调试样式，避免每只蜂重复创建。
    private static readonly GUIContent beeLine1Content = new();
    private static readonly GUIContent beeLine2Content = new();
    private static readonly GUIContent beeLabelContent = new();

    /// <summary>蜜蜂头顶标签由统一绘制器调用，不再逐只触发 IMGUI。</summary>
    private void DrawDebugOverlay(Camera camera)
    {
        if (item == null)
            return;

        Transform visualRoot = bird != null && bird.liftRoot != null ? bird.liftRoot : item.transform;
        Vector3 screenPos = camera.WorldToScreenPoint(visualRoot.position + new Vector3(0f, 0.55f, 0f));
        if (screenPos.z <= 0f || screenPos.x < -Screen.width || screenPos.x > Screen.width * 2f ||
            screenPos.y < -Screen.height || screenPos.y > Screen.height * 2f)
            return;

        beeDebugStyle ??= new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 13,
            normal = { textColor = Color.white }
        };

        string line1 =
            $"蜜蜂 | 状态: {GetBeeDebugState()} | 饱食: {state.Satiety:F0}/{SatietyMaximum:F0} | 愤怒: {state.Anger:F1}/{AngerMaximum:F1}";
        string hiveText = colony != null ? $"{colony.Honey}/{colony.HoneyCapacity}" : state.Orphaned ? "已摧毁" : "未绑定";
        int hiveGuid = bird != null ? bird.HomeHiveGuid : 0;
        string line2 =
            $"目标: {GetBeeDebugTarget()} | 蜂巢: {hiveText} | 巢GUID: {hiveGuid}";
        beeLine1Content.text = line1;
        beeLine2Content.text = line2;
        beeLabelContent.text = $"{line1}\n{line2}";
        float width = Mathf.Max(
            beeDebugStyle.CalcSize(beeLine1Content).x,
            beeDebugStyle.CalcSize(beeLine2Content).x) + 14f;
        const float height = 46f;
        Rect rect = new(
            screenPos.x - width * 0.5f,
            Screen.height - screenPos.y - height * 0.5f,
            width,
            height);
        GUI.Box(rect, beeLabelContent, beeDebugStyle);
    }

    /// <summary>调试状态直接读取行为树当前动作。</summary>
    private string GetBeeDebugState()
    {
        string action = item?.itemMods?.GetMod_ByID<Mod_AnimalBehaviorTree>(ModText.AI)?.CurrentAction;
        return action switch
        {
            "temperature" or "temperature.retreat" => "温度避险",
            "colony.pursue" => HasHiveDefenseTarget ? (state.Orphaned ? "巢毁复仇" : "护巢追击") : "愤怒追击",
            "colony.search" => $"警戒搜索({lostTargetAngerRemaining:F1}秒)",
            "colony.angryPatrol" => "愤怒巡航",
            "colony.returnForSleep" => "回巢睡觉",
            "colony.returnHoney" => "返巢交蜜",
            "colony.meal" => "回巢取蜜",
            "colony.forage" => WorldTopologyRuntime.SqrDistance(item.transform.position, foragePosition) <=
                ForageLandingDistance * ForageLandingDistance ? "采蜜" : "前往蜜源",
            "colony.independentWander" => "独立巡航",
            "colony.patrol" => "巡航",
            _ => "待机"
        };
    }

    /// <summary>优先显示战斗目标，其次显示采蜜目标和返巢目标。</summary>
    private string GetBeeDebugTarget()
    {
        if (lockedTarget != null)
            return lockedTarget.itemData?.IDName ?? lockedTarget.name;
        if (searchingLastPosition)
            return hasSearchFlightTarget
                ? $"搜索点({searchFlightTarget.x:F1},{searchFlightTarget.y:F1})"
                : $"最后位置({lastSeenPosition.x:F1},{lastSeenPosition.y:F1})";
        if (forageCrop != null)
            return forageCrop.itemData?.IDName ?? forageCrop.name;
        if (forageFlowerGuid != 0)
            return $"地表花({foragePosition.x:F1},{foragePosition.y:F1})";
        if (state.ReturningHome)
            return "蜂巢";
        if (hasPatrolTarget)
            return $"巡逻({patrolTarget.x:F1},{patrolTarget.y:F1})";
        return "无";
    }
    #endregion

    #region 蜇刺伤害契约
    private static readonly CombatDamage StingDamage = new(0f, 0f, 0f, 1f); // 一点无视护甲伤害。
    CombatDamage IDamageSender.DamageValues => StingDamage;
    Item IDamageSender.attacker { get => item; set { if (value != item) throw new InvalidOperationException("蜜蜂攻击者身份不可替换。"); } }
    public CombatDeliveryCapabilities DeliveryCapabilities => CombatDeliveryCapabilities.AirborneTargets;

    /// <summary>沿用正式受击链，仅跳过护甲防御并允许命中空中生物。</summary>
    public void ModifyDamageContext(ref CombatDamageContext context)
    {
        context.IsTrueDamage = 1;
        context.DeliveryCapabilities = DeliveryCapabilities;
    }
    #endregion
}
