using System;
using System.Collections.Generic;
using FlatWorld.Combat;
using UnityEngine;

/// <summary>
/// 蜜蜂的独立飞行行为模块：1440 点饱食度支撑一个游戏日，采蜜、返巢和警戒均由本模块决定。
/// 飞行导航仍由 AI_Bird 执行；蜂蜜和成员快照由所属 Mod_HiveColony 持久化。
/// 采蜜物种通过 BeeForage.Crop / BeeForage.Flower 标签注册，不依赖具体物品 ID。
/// </summary>
public sealed partial class Mod_BeeBehavior : Module, IBirdFlightPilot, IDamageSender,
    ICombatDamageContextModifier, IDamageDeliverySource
{
    #region 配置与状态
    public const string ModuleId = "Mod_BeeBehavior";
    public const string CropNectarTag = "BeeForage.Crop";
    public const string FlowerNectarTag = "BeeForage.Flower";

    [Serializable]
    public sealed class BeeState
    {
        public float Satiety = 1440f; // 当前饱食度。
        public float Anger; // 警惕值和愤怒值共用同一数值。
        public bool Angry; // 数值首次达到上限后才进入持续追击阶段。
        public bool ReturningHome; // 仅采蜜结束且饱食度大于门槛时置位。
        public bool Orphaned; // 蜂巢被摧毁后转为独立蜜蜂。
        public float OrphanHomeX; // 原蜂巢世界位置 X。
        public float OrphanHomeY; // 原蜂巢世界位置 Y。

        /// <summary>蜂巢快照只复制持久玩法状态，不保存临时追击和采蜜目标。</summary>
        public BeeState Copy() => new()
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
    [Min(0.1f)] public float StingInterval = 1f; // 同一蜜蜂的攻击间隔。

    private BeeState state = new(); // 本蜂权威玩法状态。
    private Mod_AI_Bird bird; // 复用飞行与导航的通用模块。
    private Mod_HiveColony colony; // 归属蜂巢。
    private Mod_ItemDetector detector; // 共用地形视线判断。
    private Mod_DamageReceiver damageReceiver; // 监听本蜂受击，用于巡逻时呼叫同巢支援。
    private float stingRemaining; // 蜇刺剩余冷却。
    private bool nightSleepRequested; // 日落后蜂巢下达的归巢睡眠请求。
    private Vector2 patrolTarget; // 当前领地内随机巡逻点，不持久化。
    private bool hasPatrolTarget; // 是否已经选中本轮巡逻点。
    public override string CanonicalModuleId => ModuleId;
    public override ModuleData _Data { get => ModData; set => ModData = (Ex_ModData)value; }
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public float Satiety => state.Satiety;
    public float Anger => state.Anger;
    public bool HasActiveAngryPursuit => state.Angry && (lockedTarget != null || searchingLastPosition);
    public float SatietyDrainRate => SatietyDrainPerSecond;
    public float HomeArrivalRadius => HomeArrivalDistance;
    public bool IsOrphaned => state.Orphaned;
    #endregion

    #region 装配与持久化
    protected override void OnLoad()
    {
        bird = item.itemMods.RequireSingleModById<Mod_AI_Bird>("AI_Bird");
        detector = item.itemMods.RequireSingleModById<Mod_ItemDetector>(ModText.Detector);
        damageReceiver = item.itemMods.RequireSingleModById<Mod_DamageReceiver>(ModText.Hp);
        if (!bird.permanentFlight || SatietyMaximum <= ReturnAbove || ReturnAbove <= ForageBelow ||
            HoneyContributionCost < 0f || HoneyMealGain < 0f || CropGainPerSecond <= 0f ||
            FlowerGainPerSecond <= 0f || PatrolFlightSpeedMultiplier <= 0f || LostTargetAngerSeconds <= 0f)
            throw new InvalidOperationException("蜜蜂行为配置无效。");
        state = ModData.GetData<BeeState>() ?? new BeeState();
        state.Satiety = Mathf.Clamp(state.Satiety, 0f, SatietyMaximum);
        state.Anger = Mathf.Clamp(state.Anger, 0f, AngerMaximum);
        if (state.Anger <= 0f)
            state.Angry = false;
        ResetBeeTargets();
        nightSleepRequested = false;
        stingRemaining = 0f;
        if (state.Orphaned)
            bird.SetFlightHome(new Vector2(state.OrphanHomeX, state.OrphanHomeY));
        damageReceiver.OnDamageReceived -= HandleBeeDamageReceived;
        damageReceiver.OnDamageReceived += HandleBeeDamageReceived;
        bird.RegisterFlightPilot(this);
    }

    /// <summary>蜂巢在成员装配完毕后注入归属与上一次保存的行为状态。</summary>
    public void BindColony(Mod_HiveColony owner, BeeState saved)
    {
        if (owner == null || bird == null)
            throw new InvalidOperationException("蜜蜂必须在鸟模块装配后绑定蜂巢。");
        colony = owner;
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
    public BeeState CaptureState() => state.Copy();

    protected override void OnSave() => ModData.WriteData(state);

    protected override void OnUnload()
    {
        if (damageReceiver != null)
            damageReceiver.OnDamageReceived -= HandleBeeDamageReceived;
        bird?.UnregisterFlightPilot(this);
        ResetBeeTargets();
        colony = null;
        bird = null;
        detector = null;
        damageReceiver = null;
        nightSleepRequested = false;
    }
    #endregion

    #region 权威行为调度
    /// <summary>鸟的 ModUpdate 权威 Tick 调用；所有蜂类玩法计时统一使用其缩放后的 deltaTime。</summary>
    public void TickFlight(float deltaTime)
    {
        if (colony == null && !state.Orphaned)
            throw new InvalidOperationException("蜜蜂尚未绑定归属蜂巢。");
        float step = Mathf.Max(0f, deltaTime);

        stingRemaining = Mathf.Max(0f, stingRemaining - step);
        if (TickHiveDefenseFlight(step))
        {
            DrainSatiety(step);
            return;
        }
        if (nightSleepRequested && colony != null && !HasActiveAngryPursuit)
        {
            DrainSatiety(step);
            bird.FlyTo(colony.HomePosition, step);
            return;
        }

        TickTargetAwareness(step);
        bool gathering = !state.Angry && bird.Phase == BirdFlightPhase.Ground && HasActiveForageTarget &&
            WorldTopologyRuntime.SqrDistance(item.transform.position, foragePosition) <=
            ForageLandingDistance * ForageLandingDistance;
        if (!gathering)
            DrainSatiety(step);
        if (state.Angry)
        {
            TickAngryFlight(step, step);
            return;
        }

        if (state.Orphaned)
        {
            bird.WanderAroundHome(step);
            return;
        }

        if (state.ReturningHome)
        {
            TickReturnHome(step);
            return;
        }
        if (HasActiveForageTarget || state.Satiety < ForageBelow)
        {
            TickForaging(step, step);
            return;
        }
        TickTerritoryPatrol(step, PatrolFlightSpeedMultiplier);
    }

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

    /// <summary>接入动物参数总开关，显示蜜蜂当前行为、饱食、愤怒、目标与蜂巢库存。</summary>
    private void OnGUI()
    {
        if (!AI_DebugOverlay.Visible || !Application.isPlaying || item == null)
            return;

        Camera camera = Camera.main;
        if (camera == null)
            return;

        Transform visualRoot = bird != null && bird.liftRoot != null ? bird.liftRoot : item.transform;
        Vector3 screenPos = camera.WorldToScreenPoint(visualRoot.position + new Vector3(0f, 0.55f, 0f));
        if (screenPos.z <= 0f)
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
        float width = Mathf.Max(
            beeDebugStyle.CalcSize(new GUIContent(line1)).x,
            beeDebugStyle.CalcSize(new GUIContent(line2)).x) + 14f;
        const float height = 46f;
        Rect rect = new(
            screenPos.x - width * 0.5f,
            Screen.height - screenPos.y - height * 0.5f,
            width,
            height);
        GUI.Box(rect, $"{line1}\n{line2}", beeDebugStyle);
    }

    /// <summary>将蜜蜂内部行为标记整理成可读的调试状态。</summary>
    private string GetBeeDebugState()
    {
        if (HasHiveDefenseTarget)
            return state.Orphaned ? "巢毁复仇" : "护巢追击";
        if (nightSleepRequested && colony != null)
            return "回巢睡觉";
        if (state.Angry)
        {
            if (lockedTarget != null)
                return "愤怒追击";
            return searchingLastPosition ? $"搜索目标({lostTargetAngerRemaining:F1}秒)" : "愤怒巡航";
        }

        if (state.ReturningHome)
            return "返巢交蜜";
        if (HasActiveForageTarget)
        {
            float landingDistanceSqr = ForageLandingDistance * ForageLandingDistance;
            return WorldTopologyRuntime.SqrDistance(item.transform.position, foragePosition) <= landingDistanceSqr
                ? "采蜜"
                : "前往蜜源";
        }
        return state.Satiety < ForageBelow ? "觅食" : "巡航";
    }

    /// <summary>优先显示战斗目标，其次显示采蜜目标和返巢目标。</summary>
    private string GetBeeDebugTarget()
    {
        if (lockedTarget != null)
            return lockedTarget.itemData?.IDName ?? lockedTarget.name;
        if (searchingLastPosition)
            return $"最后位置({lastSeenPosition.x:F1},{lastSeenPosition.y:F1})";
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
