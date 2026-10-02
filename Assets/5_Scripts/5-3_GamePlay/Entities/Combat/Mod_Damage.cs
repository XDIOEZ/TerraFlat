// 伤害模块应该管理的内容
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
public class Mod_Damage : Module, IDamageSender, IDamageDeliverySource, IHitSlowdownSource, IResourceHarvestTool, IBuildingDamageSource, ICombatDamageContextModifier
{
    #region 资源工具能力
    [SerializeField] private ResourceToolKind harvestKind; // 采集工具类别。
    [SerializeField, Min(0)] private int harvestTier; // 开采等级，与战斗伤害分离。
    [SerializeField, Min(0.01f)] private float harvestEfficiency = 1f; // 资源伤害倍率。
    public ResourceToolKind HarvestKind => harvestKind;
    public int HarvestTier => harvestTier;
    public float HarvestEfficiency => harvestEfficiency;
    private WorldTileTargetOutline groundHarvestOutline; // 当前铲子指向的地格提示。
    private GroundHarvestCrackOverlay groundHarvestCracks; // 当前地格的累积裂纹。
    private Mod_GameController groundHarvestController; // 铲子持续右键读取玩家统一输入状态。
    private Mod_Weapon_AnimationAction groundHarvestAttackAction; // 每次真实挥动只结算一次挖掘。
    private bool groundHarvestContinuousUseArmed; // 本次按下右键后才允许持续挖掘。
    #endregion

    #region 伤害相关数据
    [Header("攻击特效")]
    [SerializeField, Tooltip("按本次攻击占比最大的伤害类型播放一个命中特效。")]
    private CombatImpactEffectSet impactEffectSet;
    [Tooltip("每次有效命中都播放的通用特效，例如伤害数字；不重复放入类型命中特效。")]
    public List<GameEffect> AttackEffects = new List<GameEffect>();

    [Header("四类攻击伤害")]
    [Tooltip("切割、穿刺、劈砍、钝击分别独立参与防御结算；总战斗力为四项之和。")]
    public CombatDamage DamageValues = new CombatDamage();

    [Header("定时伤害设置")]
    [Tooltip("伤害间隔时间（秒）\n-1: 永远不启用\n0: 每帧造成伤害\n>0: 每间隔秒数造成伤害")]
    public float DamageInterval = -1f;
    [Tooltip("是否启用触发器进入时的伤害逻辑（默认为true）")]
    public bool EnableOnTriggerEnterDamage = true;
    [Tooltip("是否仅允许物品在手上时造成伤害")]
    public bool OnlyDealDamageWhenInHand = false;

    [Header("攻击目标限制")]
    [Min(1)]
    [Tooltip("每次攻击伤害窗口最多命中的实体数量；默认 3，特殊单体武器可按需调低。")]
    public int MaxAttackTargets = 3;

    [Header("逻辑命中")]
    [SerializeField, Range(0.1f, 2f)]
    [Tooltip("武器逻辑命中修正系数；1 为中性，最终还会叠加攻击者状态与目标体积。")]
    private float hitChanceMultiplier = 1f;
    public float HitChanceMultiplier => Mathf.Clamp(hitChanceMultiplier, 0.1f, 2f);

    [Header("受击减速效果")]
    [Tooltip("是否让被本次攻击命中的目标减速")]
    public bool EnableHitSlowdown = true;
    [Range(0.05f, 1f)]
    [Tooltip("受击后的移动速度倍率，数值越小减速越强")]
    public float HitSlowMultiplier = 0.5f;
    [Min(0f)]
    [Tooltip("受击减速持续时间（秒）")]
    public float HitSlowDuration = 0.35f;

    [Header("格子建筑伤害")]
    [SerializeField, Tooltip("明确标记该攻击模块可使用的拆墙工具类型。None 不会绕过目标自身的工具限制。")]
    private TileDamageToolKind tileDamageToolKind = TileDamageToolKind.None;
    [SerializeField, Min(0f), Tooltip("建筑克制倍率；在目标完成防御与最低有效伤害规则后应用。1 表示无额外克制。")]
    private float buildingDamageMultiplier = 1f;

    [Header("武器攻击音效")]
    [SerializeField]
    private CombatWeaponAudioClass weaponAudioClass = CombatWeaponAudioClass.Auto;
    [SerializeField, Tooltip("武器动作层 AudioCue ID。留空时按武器分类自动选择。")]
    private string attackAudioCueId;
    [SerializeField, Tooltip("可选的“武器×受击材质”命中声音覆盖。")]
    private List<CombatImpactAudioOverride> impactAudioOverrides =
        new List<CombatImpactAudioOverride>();
    [SerializeField, Tooltip("命中没有伤害接收器的碰撞体时，是否仍播放一次零伤害反馈。")]
    private bool playImpactFeedbackOnNonDamageableHit;

    [SerializeField] private Collider2D damageCollider;

    // 动画武器命中盒绑定：以实际武器 SpriteRenderer 为唯一空间权威。
    private SpriteRenderer boundWeaponRenderer;
    private Sprite boundWeaponSprite;
    private bool boundWeaponFlipX;
    private bool boundWeaponFlipY;
    private Vector2 boundWeaponHitboxCenter;
    private Vector2 boundWeaponHitboxSize;
    private float boundWeaponHitboxAngle;
    private readonly List<Vector2> boundWeaponShapePoints = new List<Vector2>(32);
    private readonly List<Vector2> boundWeaponShapeBuffer = new List<Vector2>(32);

    // 定时伤害相关
    [SerializeField]
    private float lastDamageTime = 0f;
    private List<Mod_DamageReceiver> insideReceivers = new List<Mod_DamageReceiver>();
    private readonly List<Collider2D> overlapColliders = new List<Collider2D>();
    private readonly HashSet<Mod_DamageReceiver> windowScanHitReceivers = new HashSet<Mod_DamageReceiver>();
    private readonly HashSet<Mod_DamageReceiver> attackWindowHitReceivers = new HashSet<Mod_DamageReceiver>();
    private readonly HashSet<FlatWorld.Combat.CombatIdentity> externalWindowTargets = new HashSet<FlatWorld.Combat.CombatIdentity>(); // 与旧目标共享窗口预算。
    private uint attackSequence, attackPulse; // 每个真实窗口的新序列与实际 Pulse。
    private double nextDataPulseTime; // 周期查询时钟，不依赖是否命中旧 Collider。
    private readonly List<ICombatDamageContextModifier> contextModifiers = new List<ICombatDamageContextModifier>(); // 装配时缓存的能力。
    private bool windowOverlapScanEnabled;
    private bool lastColliderEnabled = false;
    private bool tileDamageAppliedThisWindow;
    private bool nonDamageableImpactAppliedThisWindow;
    private float damageRangeMultiplier = 1f;

    // 实现ModuleData属性
    public override ModuleData _Data
    {
        get => MemoryPackableData;
        set => MemoryPackableData = (Ex_ModData_MemoryPackable)value;
    }
    public Ex_ModData_MemoryPackable MemoryPackableData;

    /// <summary>
    /// 造成伤害后回调事件，参数为本次造成的伤害值（可能小于等于 0）
    /// </summary>
    public event System.Action<float> OnDamageApplied;

    /// <summary>实体伤害完成后发布目标与结算结果；0 表示有效命中，负数表示本次结算无效。</summary>
    public event System.Action<Mod_DamageReceiver, float> OnReceiverDamageResolved;
    /// <summary>碰撞体已接触但逻辑命中失败时发布，参数为本次最终命中概率。</summary>
    public event System.Action<Mod_DamageReceiver, float> OnReceiverLogicalMissed;
    public event System.Action<FlatWorld.Combat.CombatDamageContext, float> OnExternalDamageResolved;
    private bool explicitProjectileSweep;
    private bool hasImpactOrigin;
    private Vector2 impactOrigin;
    private bool hasExplicitImpactPoint;
    private Vector2 explicitImpactPoint;
    public Vector2 DamageOrigin => hasImpactOrigin ? impactOrigin : damageCollider != null ? (Vector2)damageCollider.bounds.center : (Vector2)transform.position;

    public CombatWeaponAudioClass WeaponAudioClass => weaponAudioClass;
    public string AttackAudioCueId => attackAudioCueId;
    public TileDamageToolKind TileDamageToolKind => tileDamageToolKind;
    /// <summary>独立返回建筑克制倍率，避免把工具门槛与建筑伤害倍率隐式绑定。</summary>
    public float BuildingDamageMultiplier => Mathf.Max(0f, buildingDamageMultiplier);
    public Collider2D DamageCollider => damageCollider;

    /// <summary>两个后端共用的剩余名额；先预约再结算，与旧对象窗口语义一致。</summary>
    public int RemainingAttackTargets => Mathf.Max(0, Mathf.Max(1, MaxAttackTargets) - attackWindowHitReceivers.Count - externalWindowTargets.Count);

    /// <summary>同一窗口内同一外部身份只预约一次，不能绕过旧对象已经消耗的名额。</summary>
    public bool TryReserveExternalTarget(FlatWorld.Combat.CombatIdentity target)
    {
        if (!target.IsValid || RemainingAttackTargets == 0 || externalWindowTargets.Contains(target)) return false;
        externalWindowTargets.Add(target); return true;
    }

    /// <summary>发送端组合器只遍历本武器的少量能力，禁止每帧扫描场景或每个 ECS 实体。</summary>
    public void ModifyDamageContext(ref FlatWorld.Combat.CombatDamageContext context)
    {
        context.DeliveryCapabilities |= DeliveryCapabilities;
        for (int i = 0; i < contextModifiers.Count; i++) contextModifiers[i].ModifyDamageContext(ref context);
    }

    public FlatWorld.Combat.CombatDeliveryCapabilities DeliveryCapabilities { get; private set; }

    /// <summary>投射生命周期稳定入口；回收清空，旧碰撞与纯上下文共享能力。</summary>
    public void SetDeliveryCapabilities(FlatWorld.Combat.CombatDeliveryCapabilities capabilities)
    {
        DeliveryCapabilities = capabilities;
    }

    /// <summary>投射物自行提供连续路径，关闭普通 LateUpdate 的离散查询，避免越过最近目标。</summary>
    public void SetExplicitProjectileSweep(bool enabled) => explicitProjectileSweep = enabled;

    /// <summary>仅对一次实际 Pulse 导出真实 BoxCollider 姿态，并使用当前模拟输入时间。</summary>
    private void EmitDataPulse()
    {
        nextDataPulseTime = Time.timeAsDouble + Mathf.Max(0f, DamageInterval);
        EmitDataShape(Vector2.zero);
    }

    /// <summary>由投射物真实运动入口提交上一姿态到当前姿态的扫掠。</summary>
    public void QueryProjectileSweep(Vector2 displacement, float maximumFraction = 1f)
    {
        float fraction = Mathf.Clamp01(maximumFraction);
        EmitDataShape(displacement * fraction, -displacement * (1f - fraction));
    }

    private void EmitDataShape(Vector2 displacement, Vector2 centerOffset = default)
    {
        if (!(damageCollider is BoxCollider2D box) || !box.enabled || !CanDealDamageNow() || RemainingAttackTargets == 0) return;
        SyncBoundWeaponHitbox();
        Vector3 axisX = box.transform.TransformVector(Vector3.right);
        Vector3 axisY = box.transform.TransformVector(Vector3.up);
        var shape = new FlatWorld.Geometry.AttackShape2D { Center = (Unity.Mathematics.float2)((Vector2)box.transform.TransformPoint(box.offset) + centerOffset),
            HalfExtents = new Unity.Mathematics.float2(box.size.x * axisX.magnitude, box.size.y * axisY.magnitude) * 0.5f,
            Rotation = Mathf.Atan2(axisX.y, axisX.x), SweepDelta = displacement };
        var context = GameplayCombatBridge.Context(this, new FlatWorld.Combat.CombatClock {
            Tick = (ulong)Time.frameCount, Time = Time.timeAsDouble, DeltaTime = Time.deltaTime });
        context.Attack.Sequence = attackSequence; context.Attack.Window = 1; context.Attack.Pulse = ++attackPulse;
        context.Origin = shape.Center - shape.SweepDelta;
        GameplayCombatBridge.QueryWeaponPulse(this, shape, context);
    }

    /// <summary>纯数据后端确认生命提交后复用武器反馈，原 DamageReceiver 专用事件仍只传真实旧接收器。</summary>
    public void PublishExternalDamage(in FlatWorld.Combat.CombatDamageContext context, float damage)
    {
        if (GameplayCombatBridge.Identity(item) != context.Attack.Source || context.Attack.Sequence != attackSequence) return;
        if (damage >= 0f) SpawnEffect(context.HitPoint, damage);
        OnExternalDamageResolved?.Invoke(context, damage);
        OnDamageApplied?.Invoke(damage);
    }
    #endregion

    #region IDamageSender 实现
    /// <summary>伤害发送者保持为伤害物品自身；Owner 只用于排除自伤，不改写旧的攻击者语义。</summary>
    Item IDamageSender.attacker { get => item; set => item = value; }
    CombatDamage IDamageSender.DamageValues => ResolveDamageValues();
    bool IHitSlowdownSource.HitSlowdownEnabled => EnableHitSlowdown;
    float IHitSlowdownSource.HitSlowMultiplier => HitSlowMultiplier;
    float IHitSlowdownSource.HitSlowDuration => HitSlowDuration;
    #endregion

    #region Unity 生命周期
    /// <summary>从已完成注册的模块表缓存发送端能力，并在装配时确认纯数据契约容量。</summary>
    public override void Load()
    {
        DeliveryCapabilities = FlatWorld.Combat.CombatDeliveryCapabilities.None;
        explicitProjectileSweep = false;
        contextModifiers.Clear();
        if (item != null)
            foreach (Module module in item.itemMods.Mods.Values)
                // Mod_Damage 本身是上下文聚合器；同一 Item 可有多个伤害盒，聚合器之间禁止互相递归。
                if (module is not Mod_Damage && module is ICombatDamageContextModifier modifier)
                    contextModifiers.Add(modifier);
        var contract = default(FlatWorld.Combat.CombatDamageContext);
        ModifyDamageContext(ref contract);
        NormalizeDamageValues();

        if (damageCollider == null)
        {
            Debug.LogError($"{name} 未配置伤害碰撞体引用，必须在 Prefab 中显式绑定 BoxCollider2D。", this);
        }
        else
        {
            CombatPhysicsChannels.AssignDamageSender(damageCollider);
        }

        // 初始化定时伤害相关数据
        lastDamageTime = 0f;
        insideReceivers.Clear();
        overlapColliders.Clear();
        windowScanHitReceivers.Clear();
        attackWindowHitReceivers.Clear();
        externalWindowTargets.Clear(); attackSequence = 0; attackPulse = 0; nextDataPulseTime = 0;
        windowOverlapScanEnabled = false;
        lastColliderEnabled = damageCollider != null && damageCollider.enabled;
        tileDamageAppliedThisWindow = false;
        nonDamageableImpactAppliedThisWindow = false;
        groundHarvestController = null;
        groundHarvestAttackAction = item?.itemMods?.GetMod_ByID<Mod_Weapon_AnimationAction>("Module_Weapon_AnimationAction");
        groundHarvestContinuousUseArmed = false;
        SynchronizeGroundHarvestAct();
    }

    /// <summary>资源参数热更新后同步右键采挖订阅，无需重置攻击运行状态。</summary>
    public override void OnResourcesReloaded() => SynchronizeGroundHarvestAct();

    /// <summary>按当前工具类别重建右键采挖订阅，避免配置变化后仍使用旧能力。</summary>
    private void SynchronizeGroundHarvestAct()
    {
        if (item == null) return;
        item.OnAct -= HandleGroundHarvestAct;
        if (harvestKind != ResourceToolKind.None)
            item.OnAct += HandleGroundHarvestAct;
    }

    /// <summary>回池或卸载时解绑右键采挖，避免物品重用后重复工作。</summary>
    public override void Unload()
    {
        if (item != null) item.OnAct -= HandleGroundHarvestAct;
        groundHarvestContinuousUseArmed = false;
        groundHarvestController = null;
        groundHarvestAttackAction = null;
        ReleaseGroundHarvestOutline();
    }

    private void OnDisable()
    {
        groundHarvestContinuousUseArmed = false;
        groundHarvestController = null;
        ReleaseGroundHarvestOutline();
    }
    private void OnDestroy() => Unload();

    /// <summary>右键按下立即尝试第一铲，并武装持续使用；后续由 LateUpdate 按挥动节拍继续。</summary>
    private void HandleGroundHarvestAct()
    {
        if (harvestKind == ResourceToolKind.None || item?.Owner == null)
            return;

        groundHarvestController ??= item.Owner.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller);
        groundHarvestContinuousUseArmed = groundHarvestController?.IsRightClickHeld == true;
        TryPerformGroundHarvestSwing(groundHarvestController, true);
    }

    /// <summary>像锄头一样持续读取右键；只有本次挥动动画真正开始后才结算一份地块进度。</summary>
    private void UpdateGroundHarvestContinuousUse()
    {
        if (harvestKind == ResourceToolKind.None || item == null || !item.InHand ||
            item.Owner is not Player player || !player.IsLocalProfile)
        {
            groundHarvestContinuousUseArmed = false;
            groundHarvestController = null;
            return;
        }

        groundHarvestController ??= player.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller);
        if (groundHarvestController == null)
        {
            groundHarvestContinuousUseArmed = false;
            return;
        }

        if (!groundHarvestController.IsRightClickHeld)
            groundHarvestContinuousUseArmed = false;

        if (groundHarvestContinuousUseArmed)
            TryPerformGroundHarvestSwing(groundHarvestController, false);
    }

    /// <summary>单次挥铲：先确认目标，再等待攻击动画取得本次节拍，最后提交地块工作量。</summary>
    private void TryPerformGroundHarvestSwing(Mod_GameController controller, bool showFailureFeedback)
    {
        if (controller == null || item == null || !item.InHand)
            return;

        if (!GroundTileHarvestSystem.TryResolveTarget(this, out _, out _, out _, out string failureReason))
        {
            if (showFailureFeedback && !string.IsNullOrEmpty(failureReason))
                ItemActionFeedback.Show(item.Owner, failureReason);
            return;
        }

        groundHarvestAttackAction ??=
            item.itemMods.GetMod_ByID<Mod_Weapon_AnimationAction>("Module_Weapon_AnimationAction");
        if (groundHarvestAttackAction == null ||
            !groundHarvestAttackAction.TryRequestAttack(queueIfBusy: false))
            return;

        if (!GroundTileHarvestSystem.TryWork(this, out bool completed, out Vector2Int worldCell,
                out _, out failureReason))
        {
            if (showFailureFeedback && !string.IsNullOrEmpty(failureReason))
                ItemActionFeedback.Show(item.Owner, failureReason);
            return;
        }

        HoeTillingFeedback.PlayDigging(item, worldCell);
        UpdateGroundHarvestOutline();

        // 部分工作明确显示次数，避免玩家把累计采挖误认为右键无效。
        if (!completed && GroundTileHarvestSystem.TryResolveTarget(this, out RuntimeTerrainTileSample sample,
                out _, out GroundTileHarvestRule rule))
        {
            int requiredUses = GroundTileHarvestSystem.ResolveRequiredUses(this, rule);
            int completedUses = Mathf.CeilToInt(GroundTileHarvestSystem.ReadProgress(sample) * requiredUses);
            ItemActionFeedback.Show(item.Owner, $"挖掘进度：{completedUses}/{requiredUses}");
        }
    }

    /// <summary>指向任意地表时显示选格框，仅对可采挖地格显示进度裂纹。</summary>
    private void UpdateGroundHarvestOutline()
    {
        if (!GroundTileHarvestSystem.TryResolvePreview(this, out RuntimeTerrainTileSample sample))
        {
            groundHarvestOutline?.Hide();
            groundHarvestCracks?.Hide();
            return;
        }

        groundHarvestOutline ??= WorldTileTargetOutline.Create("Shovel Ground Target Outline");
        groundHarvestOutline.Show(sample.WorldCell);
        if (!GroundTileHarvestSystem.IsHarvestableGround(sample.Cell.GroundTileId))
        {
            groundHarvestCracks?.Hide();
            return;
        }
        float progress = GroundTileHarvestSystem.ReadProgress(sample);
        if (progress <= 0f)
        {
            groundHarvestCracks?.Hide();
            return;
        }

        groundHarvestCracks ??= GroundHarvestCrackOverlay.Create();
        groundHarvestCracks.Show(sample.WorldCell, progress);
    }

    /// <summary>物品卸载或禁用时清理临时表现对象。</summary>
    private void ReleaseGroundHarvestOutline()
    {
        if (groundHarvestOutline != null) Destroy(groundHarvestOutline.gameObject);
        if (groundHarvestCracks != null) Destroy(groundHarvestCracks.gameObject);
        groundHarvestOutline = null;
        groundHarvestCracks = null;
    }

    public override void Save()
    {
        // 保存逻辑可以后续实现
    }

    /// <summary>Animator 更新后再次同步武器命中盒，保证伤害区域始终跟随实际武器画面。</summary>
    private void LateUpdate()
    {
        SyncBoundWeaponHitbox();
        UpdateGroundHarvestContinuousUse();
        UpdateGroundHarvestOutline();
        // 动画开启的一次窗口会移动：每帧检测新进入 OBB 的 ECS 目标，窗口集合仍保证每目标只受击一次。
        if (!explicitProjectileSweep && EnableOnTriggerEnterDamage && DamageInterval < 0f &&
            damageCollider != null && damageCollider.enabled)
            EmitDataPulse();
    }

    public override void ModUpdate(float deltaTime)
    {
        // 没有动画动作模块的简易近战武器也必须在拿到手时绑定实际 Sprite 区域。
        if (boundWeaponRenderer == null &&
            item != null && item.InHand &&
            item.Sprite != null && item.Sprite.sprite != null)
        {
            BindToWeaponRenderer(item.Sprite);
        }

        if (damageCollider != null)
        {
            bool colliderEnabled = damageCollider.enabled;
            if (colliderEnabled != lastColliderEnabled)
            {
                lastColliderEnabled = colliderEnabled;
                if (colliderEnabled)
                {
                    BeginTileDamageWindow();
                    // 动画片段会直接切换 BoxCollider2D.m_Enabled，
                    // 因此这里也是通用的攻击动作音效入口。
                    CombatAudioRouter.PlayWeaponAttack(this);
                }
                else
                {
                    EndDamageWindow();
                }
            }
        }

        // TilemapCollider2D 的整层只会产生一个 Collider 回调；主动查询当前攻击触发器，
        // 才能在连续墙面内移动时仍准确选中当前格，并保证一次攻击窗只伤一格。
        TryApplyDamageToTilemap();

        // 处理定时伤害逻辑
        if (DamageInterval >= 0 && damageCollider != null && damageCollider.enabled && CanDealDamageNow())
        {
            // 检查是否到了造成伤害的时间
            if (DamageInterval == 0 || Time.time - lastDamageTime >= DamageInterval)
            {
                // 实际更新时间由 ApplyDamageToReceiver 在真正造成伤害时负责
                ApplyDamageToInsideReceivers();
            }
            if (!explicitProjectileSweep && Time.timeAsDouble >= nextDataPulseTime)
                EmitDataPulse();
        }
    }
    #endregion

    #region 伤害处理
    public void OnTriggerEnter2D(Collider2D other)
    {
        // 连续投射物统一按扫掠距离排序，不能让物理回调先击中更远的 GO 而跳过近处 ECS。
        if (!explicitProjectileSweep) ProcessDamageColliderHit(other);
    }

    /// <summary>只读候选资格，供连续扫掠确定最近的实体边界；不会消耗命中次数。</summary>
    public bool CanHitColliderTarget(Collider2D collider)
    {
        if (!CombatPhysicsChannels.IsDamageReceiverCollider(collider)) return false;
        Mod_DamageReceiver receiver = GameplayPhysics2D.ResolveComponent<Mod_DamageReceiver>(collider);
        return receiver != null && !IsDamageSourceReceiver(receiver) && CanDealDamageNow() &&
            AllowsTargetDelivery(receiver) && FactionRelationService.CanAttack(item, receiver.item) &&
            !attackWindowHitReceivers.Contains(receiver);
    }

    /// <summary>供高速投射物的射线/碰撞体扫掠复用同一套命中结算，避免绕过 Mod_Damage。</summary>
    public void ProcessExplicitColliderHit(Collider2D other, Vector2? sourceOrigin = null,
        Vector2? hitPoint = null)
    {
        hasImpactOrigin = sourceOrigin.HasValue;
        impactOrigin = sourceOrigin.GetValueOrDefault();
        hasExplicitImpactPoint = hitPoint.HasValue;
        explicitImpactPoint = hitPoint.GetValueOrDefault();
        try { ProcessDamageColliderHit(other); }
        finally { hasImpactOrigin = false; hasExplicitImpactPoint = false; }
    }

    /// <summary>统一处理 Trigger 与显式扫掠得到的伤害碰撞体。</summary>
    private void ProcessDamageColliderHit(Collider2D other)
    {
        // 伤害接触只接受 DamageReciver 层；交互、拾取、玩家身体等不会进入伤害解析链。
        if (damageCollider == null || !damageCollider.enabled ||
            !CombatPhysicsChannels.IsDamageReceiverCollider(other))
        {
            return;
        }

        Mod_DamageReceiver receiver = GameplayPhysics2D.ResolveComponent<Mod_DamageReceiver>(other);
        if (receiver == null)
        {
            TryPlayNonDamageableImpact(other);
            return;
        }

        // 武器/投射物可能与拥有者的碰撞体重叠，攻击者自身永远不进入伤害候选列表。
        if (IsDamageSourceReceiver(receiver))
            return;

        // 窗口起始扫描过的目标，不再被同一窗口内后续触发事件重复结算。
        if (windowOverlapScanEnabled && windowScanHitReceivers.Contains(receiver))
            return;

        // 添加到内部接收器列表
        if (!insideReceivers.Contains(receiver))
        {
            insideReceivers.Add(receiver);
        }

        // 如果启用了进入时伤害，则在尊重伤害间隔的前提下尝试立即造成一次伤害
        if (EnableOnTriggerEnterDamage && CanDealDamageNow())
        {
            // DamageInterval < 0：仅做一次进入伤害，不参与冷却（保持旧行为）
            if (DamageInterval < 0f)
            {
                ApplyDamageToReceiver(receiver, other);
            }
            else
            {
                // DamageInterval == 0：视为“每帧都可伤害”，进入时也允许立刻打一击
                // DamageInterval  > 0：需要满足冷却时间
                if (DamageInterval == 0f || Time.time - lastDamageTime >= DamageInterval)
                {
                    // 实际更新时间由 ApplyDamageToReceiver 在真正造成伤害时负责
                    ApplyDamageToReceiver(receiver, other);
                }
            }

            if (windowOverlapScanEnabled)
                windowScanHitReceivers.Add(receiver);
        }
    }

    public void OnTriggerExit2D(Collider2D other)
    {
        if (!CombatPhysicsChannels.IsDamageReceiverCollider(other))
            return;

        // 从内部接收器列表中移除
        Mod_DamageReceiver receiver = GameplayPhysics2D.ResolveComponent<Mod_DamageReceiver>(other);
        if (receiver != null)
        {
            insideReceivers.Remove(receiver);
        }
    }

    private void ApplyDamageToInsideReceivers()
    {
        // 对所有在碰撞体内的接收器造成伤害
        for (int i = insideReceivers.Count - 1; i >= 0; i--)
        {
            if (insideReceivers[i] != null)
            {
                ApplyDamageToReceiver(insideReceivers[i]);
            }
            else
            {
                // 移除已销毁的接收器
                insideReceivers.RemoveAt(i);
            }
        }
    }

    /// <summary>结算一次实体伤害，并优先使用本次实际命中的碰撞体定位特效。</summary>
    private void ApplyDamageToReceiver(Mod_DamageReceiver receiver, Collider2D hitCollider = null)
    {
        if (receiver == null ||
            IsDamageSourceReceiver(receiver) ||
            !CanDealDamageNow() ||
            !AllowsTargetDelivery(receiver) ||
            !FactionRelationService.CanAttack(item, receiver.item))
        {
            return;
        }

        if (attackWindowHitReceivers.Contains(receiver) ||
            attackWindowHitReceivers.Count + externalWindowTargets.Count >= Mathf.Max(1, MaxAttackTargets))
        {
            return;
        }

        attackWindowHitReceivers.Add(receiver);

        // 碰撞只代表攻击范围接触；角色之间还要经过武器、状态和目标体积共同决定的逻辑命中。
        if (!TryResolveLogicalHit(receiver, out float logicalHitChance))
        {
            OnReceiverLogicalMissed?.Invoke(receiver, logicalHitChance);
            return;
        }

        // 造成伤害
        float acDamage = receiver.Hurt(this);

        // DamageReceiver 与受击 Collider 可能位于不同层级，不能假定接收器节点自身带 Collider。
        if (acDamage >= 0f)
        {
            Vector2 hitPoint = ResolveHitPoint(receiver, hitCollider);
            SpawnEffect(hitPoint, acDamage);
        }

        // 触发伤害完成事件（无论伤害是否大于 0 都会触发）
        OnReceiverDamageResolved?.Invoke(receiver, acDamage);
        OnDamageApplied?.Invoke(acDamage);


        lastDamageTime = Time.time;

    }

    #region 逻辑命中

    private const float HealthyEqualSizeHitChance = 0.73f;
    private const float LogicalHitChanceFloor = 0.02f;
    private const float LogicalHitChanceCeiling = 0.98f;
    private const float TargetSizeExponent = 0.5f;

    /// <summary>只对角色攻击角色追加逻辑命中判定，环境、资源和普通物件伤害保持原规则。</summary>
    private bool TryResolveLogicalHit(Mod_DamageReceiver receiver, out float chance)
    {
        chance = 1f;
        Item attackActor = ResolveAttackActor();
        Item targetActor = receiver?.item;
        if (!IsLogicalHitActor(attackActor) || !IsLogicalHitActor(targetActor))
            return true;

        float weaponFactor = HitChanceMultiplier;
        float stateFactor = ResolveAttackerStateHitChanceMultiplier(attackActor);
        float sizeFactor = Mathf.Pow(Mathf.Max(0.05f, receiver.HitSizeCoefficient), TargetSizeExponent);
        chance = Mathf.Clamp(
            HealthyEqualSizeHitChance * weaponFactor * stateFactor * sizeFactor,
            LogicalHitChanceFloor,
            LogicalHitChanceCeiling);
        return Random.value < chance;
    }

    /// <summary>武器、箭矢等多级 Owner 最终都归到真实攻击角色。</summary>
    private Item ResolveAttackActor()
    {
        Item current = item;
        int guard = 0;
        while (current?.Owner != null && guard++ < 8)
            current = current.Owner;
        return current;
    }

    private static bool IsLogicalHitActor(Item candidate)
    {
        if (candidate == null)
            return false;
        if (candidate is Player)
            return true;

        List<string> tags = candidate.itemData?.Tags;
        return tags != null &&
               (tags.ContainsTag(Tag.Player) || tags.ContainsTag("Animal") || tags.ContainsTag("Blood"));
    }

    /// <summary>健康与体力充足时返回 1；受伤或疲劳只降低命中，不额外奖励满状态。</summary>
    private static float ResolveAttackerStateHitChanceMultiplier(Item attackActor)
    {
        if (attackActor?.itemMods == null)
            return 1f;

        float healthFactor = 1f;
        Mod_DamageReceiver health = attackActor.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
        if (health != null && health.MaxHp > 0f)
        {
            float hpRatio = Mathf.Clamp01(health.Hp / health.MaxHp);
            if (hpRatio < 0.70f)
                healthFactor = Mathf.Lerp(0.65f, 1f, hpRatio / 0.70f);
        }

        float staminaFactor = 1f;
        Mod_Stamina stamina = attackActor.itemMods.GetMod_ByID<Mod_Stamina>(ModText.Stamina);
        if (stamina != null && stamina.MaxValue > 0f)
        {
            float staminaRatio = Mathf.Clamp01(stamina.CurrentValue / stamina.MaxValue);
            if (staminaRatio < 0.50f)
                staminaFactor = Mathf.Lerp(0.75f, 1f, staminaRatio / 0.50f);
        }

        return Mathf.Clamp(healthFactor * staminaFactor, 0.35f, 1f);
    }

    #endregion

    /// <summary>抛掷物高空段只接受真正飞行的目标；不能在地面目标处提前消费唯一命中名额。</summary>
    private bool AllowsTargetDelivery(Mod_DamageReceiver receiver)
    {
        if ((DeliveryCapabilities & FlatWorld.Combat.CombatDeliveryCapabilities.AirborneOnly) == 0) return true;
        if (receiver.item?.itemMods == null) return false;
        foreach (Module module in receiver.item.itemMods.Mods.Values)
            if (module is FlatWorld.Combat.ICombatAirborneTarget airborne && airborne.IsAirborne) return true;
        return false;
    }

    /// <summary>解析稳定的命中特效位置；缺少碰撞体时回退到受击对象中心。</summary>
    private Vector2 ResolveHitPoint(Mod_DamageReceiver receiver, Collider2D hitCollider)
    {
        if (hitCollider != null)
            return hitCollider.ClosestPoint(hasExplicitImpactPoint ? explicitImpactPoint : (Vector2)transform.position);

        Collider2D receiverCollider = receiver.GetComponent<Collider2D>();
        if (receiverCollider == null)
            receiverCollider = receiver.GetComponentInChildren<Collider2D>(true);
        if (receiverCollider == null)
            receiverCollider = receiver.GetComponentInParent<Collider2D>();

        return receiverCollider != null
            ? receiverCollider.ClosestPoint(transform.position)
            : (Vector2)receiver.transform.position;
    }

    /// <summary>为原木等钝器补充命中不可伤害碰撞体时的一次性视觉反馈。</summary>
    private void TryPlayNonDamageableImpact(Collider2D hitCollider)
    {
        if (!playImpactFeedbackOnNonDamageableHit ||
            nonDamageableImpactAppliedThisWindow ||
            hitCollider == null ||
            !CanDealDamageNow() ||
            (item != null && hitCollider.transform.IsChildOf(item.transform)))
        {
            return;
        }

        Vector2 origin = damageCollider != null
            ? damageCollider.bounds.center
            : transform.position;
        SpawnEffect(hitCollider.ClosestPoint(origin), 0f);
        nonDamageableImpactAppliedThisWindow = true;
    }

    private void TryApplyDamageToTilemap()
    {
        if ((DeliveryCapabilities & FlatWorld.Combat.CombatDeliveryCapabilities.AirborneOnly) != 0 || tileDamageAppliedThisWindow ||
            damageCollider == null ||
            !damageCollider.enabled ||
            !CanDealDamageNow() ||
            !IsDamageIntervalReady())
        {
            return;
        }

        if (!TileBuildingSystem.TryDamageNearest(this, damageCollider, out TileBuildingDamageResult result))
            return;

        tileDamageAppliedThisWindow = true;
        if (result.AppliedDamage >= 0f)
            SpawnEffect(result.HitPoint, result.AppliedDamage);
        OnDamageApplied?.Invoke(result.AppliedDamage);
        lastDamageTime = Time.time;
    }

    private bool IsDamageIntervalReady()
    {
        return DamageInterval < 0f ||
               DamageInterval == 0f ||
               Time.time - lastDamageTime >= DamageInterval;
    }

    /// <summary>重置本次攻击命中集合，并立即补查当前重叠的实体与格子建筑。</summary>
    private void BeginTileDamageWindow()
    {
        // 攻击窗口可能由动画曲线在本帧开启，先同步实际武器姿态再做主动重叠扫描。
        SyncBoundWeaponHitbox();
        tileDamageAppliedThisWindow = false;
        nonDamageableImpactAppliedThisWindow = false;
        windowScanHitReceivers.Clear();
        attackWindowHitReceivers.Clear();
        externalWindowTargets.Clear();
        attackSequence++; if (attackSequence == 0) attackSequence = 1;
        attackPulse = 0;
        bool shouldQueryData = EnableOnTriggerEnterDamage && IsDamageIntervalReady();
        ScanCurrentOverlapsAndApplyDamageForWindow();
        if (shouldQueryData) EmitDataPulse();
        // 动画可能在同一帧内开关伤害 Collider，窗口开启时立即补一次格子建筑查询。
        TryApplyDamageToTilemap();
    }

    private void EndDamageWindow()
    {
        insideReceivers.Clear();
        windowScanHitReceivers.Clear();
        attackWindowHitReceivers.Clear();
        externalWindowTargets.Clear();
        windowOverlapScanEnabled = false;
        tileDamageAppliedThisWindow = false;
        nonDamageableImpactAppliedThisWindow = false;
    }

    /// <summary>判断接收器是否属于伤害物品自身或其拥有者；仅过滤自伤，不改变攻击者身份。</summary>
    private bool IsDamageSourceReceiver(Mod_DamageReceiver receiver)
    {
        Item receiverItem = receiver?.item;
        if (receiverItem == null)
            return false;

        return receiverItem == item || (item != null && receiverItem == item.Owner);
    }

    private bool CanDealDamageNow()
    {
        if (!OnlyDealDamageWhenInHand) return true;
        return item.InHand;
    }

    /// <summary>有效命中播放一个主伤害类型特效，再播放独立的数字等通用反馈。</summary>
    private void SpawnEffect(Vector2 hitPoint, float damage)
    {
        VisualEffectManager effectManager = VisualEffectManager.Instance;
        CombatDamageKind kind = ResolveDamageValues().DominantKind;
        if (impactEffectSet != null)
            PlayHitEffect(impactEffectSet.GetPrefab(kind), effectManager, hitPoint, damage, kind);

        if (AttackEffects == null)
            return;

        foreach (GameEffect effectPrefab in AttackEffects)
            PlayHitEffect(effectPrefab, effectManager, hitPoint, damage, kind);
    }

    /// <summary>从现有对象池播放命中特效，类型动画与数字共用相同的命中坐标。</summary>
    private void PlayHitEffect(GameEffect prefab, VisualEffectManager manager, Vector2 hitPoint,
        float damage, CombatDamageKind kind)
    {
        if (prefab == null)
            return;

        GameEffect effect = manager != null ? manager.GetGameEffectFromPool(prefab) : Instantiate(prefab);
        effect.transform.position = new Vector3(hitPoint.x, hitPoint.y, 0f);
        object effectData = effect is DamageTextEffect ? BuildDamageTextData(damage, kind) : damage;
        effect.Effect(transform, effectData);
    }

    /// <summary>数字与命中动画共用同一个主伤害类型，劈砍继续使用刃器数字样式。</summary>
    private static DamageTextEffectData BuildDamageTextData(float damage, CombatDamageKind kind)
    {
        DamageTextStyle style = kind switch
        {
            CombatDamageKind.Cutting => DamageTextStyle.Cutting,
            CombatDamageKind.Chopping => DamageTextStyle.Cutting,
            CombatDamageKind.Piercing => DamageTextStyle.Piercing,
            CombatDamageKind.Blunt => DamageTextStyle.Blunt,
            _ => DamageTextStyle.Normal
        };
        return new DamageTextEffectData(damage, style);
    }

    /// <summary>获取已校正的四类伤害。</summary>
    public CombatDamage ResolveDamageValues()
    {
        NormalizeDamageValues();
        return DamageValues;
    }

    /// <summary>由数值工具显式写入四类伤害，并阻止零伤害配置回退到旧单值。</summary>
    public void SetDamageValues(CombatDamage values)
    {
        DamageValues = values ?? new CombatDamage();
        DamageValues.ClampNonNegative();
    }

    /// <summary>确保四类伤害对象存在，并限制运行时数据不会出现负数。</summary>
    private void NormalizeDamageValues()
    {
        DamageValues ??= new CombatDamage();
        DamageValues.ClampNonNegative();
    }

    #endregion

    #region 新增方法：控制伤害启用/禁用

    /// <summary>
    /// 设置动物攻击使用的伤害范围倍率；只扩大碰撞盒尺寸，不改变伤害数值与攻击窗口。
    /// </summary>
    public virtual void SetDamageRangeMultiplier(float multiplier)
    {
        if (damageCollider is not BoxCollider2D boxCollider)
            return;

        float targetMultiplier = Mathf.Max(1f, multiplier);
        if (Mathf.Approximately(damageRangeMultiplier, targetMultiplier))
            return;

        if (boundWeaponRenderer != null)
        {
            damageRangeMultiplier = targetMultiplier;
            SyncBoundWeaponHitbox(forceShapeSync: true);
            return;
        }

        float relativeMultiplier = targetMultiplier / damageRangeMultiplier;
        boxCollider.size *= relativeMultiplier;
        boxCollider.edgeRadius *= relativeMultiplier;
        damageRangeMultiplier = targetMultiplier;
    }

    /// <summary>把动画武器的伤害盒绑定到实际 SpriteRenderer；位置、旋转、缩放与 Sprite 边界都由画面本身决定。</summary>
    public void BindToWeaponRenderer(SpriteRenderer weaponRenderer)
    {
        if (weaponRenderer == null || weaponRenderer.sprite == null)
            throw new System.InvalidOperationException($"{name} 无法绑定武器伤害区域：SpriteRenderer 或 Sprite 为空。");
        if (damageCollider is not BoxCollider2D)
            throw new MissingComponentException($"{name} 的动画武器伤害区域必须使用 BoxCollider2D。");

        boundWeaponRenderer = weaponRenderer;
        boundWeaponSprite = null;
        SyncBoundWeaponHitbox(forceShapeSync: true);
    }

    /// <summary>同步动画武器伤害盒的空间姿态，并用贴图紧致轮廓刷新盒体边界。</summary>
    private void SyncBoundWeaponHitbox(bool forceShapeSync = false)
    {
        if (boundWeaponRenderer == null || damageCollider is not BoxCollider2D boxCollider)
            return;

        Transform rendererTransform = boundWeaponRenderer.transform;
        Sprite sprite = boundWeaponRenderer.sprite;
        bool flipX = boundWeaponRenderer.flipX;
        bool flipY = boundWeaponRenderer.flipY;
        if (sprite == null)
            throw new System.InvalidOperationException($"{name} 绑定的武器 Sprite 在运行时变为空。");

        if (forceShapeSync || sprite != boundWeaponSprite || flipX != boundWeaponFlipX || flipY != boundWeaponFlipY)
        {
            CalculateTightWeaponBox(sprite, flipX, flipY, out boundWeaponHitboxCenter, out boundWeaponHitboxSize, out boundWeaponHitboxAngle);
            boundWeaponSprite = sprite;
            boundWeaponFlipX = flipX;
            boundWeaponFlipY = flipY;
        }

        Vector3 localCenter = new Vector3(boundWeaponHitboxCenter.x, boundWeaponHitboxCenter.y, 0f);
        Quaternion localBoxRotation = Quaternion.Euler(0f, 0f, boundWeaponHitboxAngle);
        Transform damageParent = transform.parent;
        if (damageParent == rendererTransform.parent)
        {
            transform.localPosition = rendererTransform.localPosition +
                                      rendererTransform.localRotation * Vector3.Scale(localCenter, rendererTransform.localScale);
            transform.localRotation = rendererTransform.localRotation * localBoxRotation;
            transform.localScale = rendererTransform.localScale;
        }
        else
        {
            transform.SetPositionAndRotation(rendererTransform.TransformPoint(localCenter), rendererTransform.rotation * localBoxRotation);
            Vector3 rendererWorldScale = rendererTransform.lossyScale;
            Vector3 parentWorldScale = damageParent != null ? damageParent.lossyScale : Vector3.one;
            transform.localScale = new Vector3(
                DivideScale(rendererWorldScale.x, parentWorldScale.x),
                DivideScale(rendererWorldScale.y, parentWorldScale.y),
                DivideScale(rendererWorldScale.z, parentWorldScale.z));
        }

        boxCollider.offset = Vector2.zero;
        boxCollider.size = boundWeaponHitboxSize * damageRangeMultiplier;
    }

    /// <summary>优先使用 Sprite 物理轮廓计算最小包围矩形，避免透明画布把细长武器伤害盒撑成大方框。</summary>
    private void CalculateTightWeaponBox(Sprite sprite, bool flipX, bool flipY, out Vector2 center, out Vector2 size, out float angle)
    {
        boundWeaponShapePoints.Clear();
        int physicsShapeCount = sprite.GetPhysicsShapeCount();
        for (int shapeIndex = 0; shapeIndex < physicsShapeCount; shapeIndex++)
        {
            boundWeaponShapeBuffer.Clear();
            sprite.GetPhysicsShape(shapeIndex, boundWeaponShapeBuffer);
            for (int pointIndex = 0; pointIndex < boundWeaponShapeBuffer.Count; pointIndex++)
                boundWeaponShapePoints.Add(ApplySpriteFlip(boundWeaponShapeBuffer[pointIndex], flipX, flipY));
        }

        if (boundWeaponShapePoints.Count < 2)
        {
            Vector2[] vertices = sprite.vertices;
            for (int i = 0; i < vertices.Length; i++)
                boundWeaponShapePoints.Add(ApplySpriteFlip(vertices[i], flipX, flipY));
        }

        if (boundWeaponShapePoints.Count >= 2 && TryCalculateMinimumAreaBox(boundWeaponShapePoints, out center, out size, out angle))
        {
            float minimumPixelSize = sprite.pixelsPerUnit > 0f ? 1f / sprite.pixelsPerUnit : 0.01f;
            size.x = Mathf.Max(size.x, minimumPixelSize);
            size.y = Mathf.Max(size.y, minimumPixelSize);
            return;
        }

        Bounds spriteBounds = sprite.bounds;
        center = ApplySpriteFlip(spriteBounds.center, flipX, flipY);
        size = spriteBounds.size;
        angle = 0f;
    }

    /// <summary>从轮廓点计算二维最小面积包围盒，使 BoxCollider 尽量贴住实际武器轮廓。</summary>
    private static bool TryCalculateMinimumAreaBox(List<Vector2> points, out Vector2 center, out Vector2 size, out float angle)
    {
        center = Vector2.zero;
        size = Vector2.zero;
        angle = 0f;
        float bestArea = float.PositiveInfinity;
        bool found = false;

        for (int i = 0; i < points.Count - 1; i++)
        {
            for (int j = i + 1; j < points.Count; j++)
            {
                Vector2 edge = points[j] - points[i];
                if (edge.sqrMagnitude <= 0.000001f)
                    continue;

                Vector2 axisX = edge.normalized;
                Vector2 axisY = new Vector2(-axisX.y, axisX.x);
                float minX = float.PositiveInfinity;
                float maxX = float.NegativeInfinity;
                float minY = float.PositiveInfinity;
                float maxY = float.NegativeInfinity;

                for (int pointIndex = 0; pointIndex < points.Count; pointIndex++)
                {
                    Vector2 point = points[pointIndex];
                    float projectionX = Vector2.Dot(point, axisX);
                    float projectionY = Vector2.Dot(point, axisY);
                    minX = Mathf.Min(minX, projectionX);
                    maxX = Mathf.Max(maxX, projectionX);
                    minY = Mathf.Min(minY, projectionY);
                    maxY = Mathf.Max(maxY, projectionY);
                }

                float width = maxX - minX;
                float height = maxY - minY;
                float area = width * height;
                if (area >= bestArea)
                    continue;

                bestArea = area;
                center = axisX * ((minX + maxX) * 0.5f) + axisY * ((minY + maxY) * 0.5f);
                size = new Vector2(width, height);
                angle = Mathf.Atan2(axisX.y, axisX.x) * Mathf.Rad2Deg;
                found = true;
            }
        }

        return found;
    }

    /// <summary>把 SpriteRenderer 的翻转直接折算进局部轮廓点。</summary>
    private static Vector2 ApplySpriteFlip(Vector2 point, bool flipX, bool flipY)
    {
        if (flipX) point.x = -point.x;
        if (flipY) point.y = -point.y;
        return point;
    }

    /// <summary>把世界缩放转换成当前父节点下的局部缩放。</summary>
    private static float DivideScale(float worldScale, float parentWorldScale)
    {
        return Mathf.Approximately(parentWorldScale, 0f) ? 0f : worldScale / parentWorldScale;
    }

    /// <summary>
    /// 伤害窗口开启后主动扫描当前重叠目标，弥补碰撞体后开时缺少 Enter 事件的问题。
    /// </summary>
    protected void ScanCurrentOverlapsAndApplyDamageForWindow()
    {
        if (damageCollider == null || !damageCollider.enabled)
            return;

        windowOverlapScanEnabled = true;
        windowScanHitReceivers.Clear();
        Physics2D.SyncTransforms();
        overlapColliders.Clear();

        ContactFilter2D filter = new ContactFilter2D
        {
            useTriggers = true,
            useLayerMask = true,
            layerMask = CombatPhysicsChannels.DamageReceiverMask,
            useDepth = false,
            useNormalAngle = false
        };
        damageCollider.OverlapCollider(filter, overlapColliders);

        for (int i = 0; i < overlapColliders.Count; i++)
        {
            Collider2D overlap = overlapColliders[i];
            Mod_DamageReceiver receiver = GameplayPhysics2D.ResolveComponent<Mod_DamageReceiver>(overlap);
            if (receiver == null || IsDamageSourceReceiver(receiver) || !windowScanHitReceivers.Add(receiver))
                continue;

            if (!insideReceivers.Contains(receiver))
                insideReceivers.Add(receiver);

            if (!EnableOnTriggerEnterDamage || !CanDealDamageNow())
                continue;

            if (DamageInterval < 0f ||
                DamageInterval == 0f ||
                Time.time - lastDamageTime >= DamageInterval)
            {
                ApplyDamageToReceiver(receiver, overlap);
            }
        }
    }

    /// <summary>
    /// 设置伤害逻辑与伤害碰撞体的启用状态
    /// </summary>
    /// <param name="enabled">是否启用伤害检测</param>
    public void SetDamageEnabled(bool enabled)
    {
        if (damageCollider == null)
        {
            Debug.LogError($"{name} 未配置伤害碰撞体引用，无法切换伤害窗口。", this);
            return;
        }

        bool wasEnabled = damageCollider.enabled;
        if (damageCollider.enabled != enabled)
        {
           damageCollider.enabled = enabled; // 先切换状态以确保触发器事件正确调用，从而维护内部接收器列表的准确性
        }

        lastColliderEnabled = damageCollider.enabled;
        if (enabled && !wasEnabled)
        {
            BeginTileDamageWindow();
            CombatAudioRouter.PlayWeaponAttack(this);
        }

        if (!enabled)
        {
            EndDamageWindow();
        }
    }

    /// <summary>
    /// 获取当前伤害检测状态
    /// </summary>
    /// <returns>伤害检测是否启用</returns>
    public bool IsDamageEnabled()
    {
        return damageCollider != null && damageCollider.enabled;
    }

    /// <summary>开始新的攻击窗口；伤害冷却只由真正命中负责更新时间。</summary>
    public void StartAttack()
    {
        // 碰撞体可能持续启用，开始新的动作时仍需重新计算本次可命中的目标数。
        attackWindowHitReceivers.Clear();
        if (damageCollider != null && damageCollider.enabled)
        {
            BeginTileDamageWindow();
        }
        else
        {
            SetDamageEnabled(true);
        }
        // 某些持续伤害模块的碰撞体可能已经启用，路由器会按攻击者去重。
        CombatAudioRouter.PlayWeaponAttack(this);
    }
    public void StopAttack()
    {
        SetDamageEnabled(false);
    }

    public bool TryGetImpactAudioOverride(
        CombatImpactMaterial material,
        out string cueId)
    {
        cueId = null;
        if (impactAudioOverrides == null)
            return false;

        for (int i = 0; i < impactAudioOverrides.Count; i++)
        {
            CombatImpactAudioOverride entry = impactAudioOverrides[i];
            if (entry == null ||
                entry.Material != material ||
                string.IsNullOrWhiteSpace(entry.CueId))
            {
                continue;
            }

            cueId = entry.CueId.Trim();
            return true;
        }

        return false;
    }

    #endregion
}

/// <summary>
/// FlatWorld 战斗物理通道的唯一配置入口。
/// DamageSender 只与 DamageReciver 产生 Physics2D 接触；交互系统不使用 Trigger。
/// </summary>
public static class CombatPhysicsChannels
{
    public const string DamageReceiverLayerName = "DamageReciver";
    public const string DamageSenderLayerName = "DamageSender";

    private static bool collisionMatrixConfigured;

    public static int DamageReceiverLayer => LayerMask.NameToLayer(DamageReceiverLayerName);
    public static int DamageSenderLayer => LayerMask.NameToLayer(DamageSenderLayerName);

    public static LayerMask DamageReceiverMask
    {
        get
        {
            int layer = DamageReceiverLayer;
            return layer >= 0 ? 1 << layer : ~0;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        collisionMatrixConfigured = false;
    }

    /// <summary>确保 DamageSender 不再与交互、玩家身体、拾取器、普通阻挡等层产生额外接触。</summary>
    public static void EnsureConfigured()
    {
        if (collisionMatrixConfigured)
            return;

        int senderLayer = DamageSenderLayer;
        int receiverLayer = DamageReceiverLayer;
        if (senderLayer < 0 || receiverLayer < 0)
        {
            Debug.LogError(
                $"战斗物理层缺失：{DamageSenderLayerName}={senderLayer}, {DamageReceiverLayerName}={receiverLayer}");
            return;
        }

        for (int layer = 0; layer < 32; layer++)
        {
            Physics2D.IgnoreLayerCollision(senderLayer, layer, layer != receiverLayer);
            Physics2D.IgnoreLayerCollision(receiverLayer, layer, layer != senderLayer);
        }

        collisionMatrixConfigured = true;
    }

    public static void AssignDamageSender(Collider2D collider)
    {
        EnsureConfigured();
        int layer = DamageSenderLayer;
        if (collider == null || layer < 0)
            return;

        collider.isTrigger = true;
        collider.gameObject.layer = layer;
    }

    public static void AssignDamageReceiver(Component receiver)
    {
        EnsureConfigured();
        int layer = DamageReceiverLayer;
        if (receiver == null || layer < 0)
            return;

        Collider2D[] colliders = receiver.GetComponents<Collider2D>();
        if (colliders.Length == 0)
        {
            Debug.LogError($"{receiver.name} 缺少 DamageReceiver 专用 Collider2D。", receiver);
            return;
        }

        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].isTrigger = true;
            colliders[i].gameObject.layer = layer;
        }
    }

    public static bool IsDamageReceiverCollider(Collider2D collider)
    {
        return collider != null && collider.gameObject.layer == DamageReceiverLayer;
    }
}
