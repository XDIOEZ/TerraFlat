// 伤害模块应该管理的内容
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
public class Mod_Damage : Module, IDamageSender, IHitSlowdownSource
{
    #region 伤害相关数据
    [Header("攻击特效")]
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

    // 定时伤害相关
    [SerializeField]
    private float lastDamageTime = 0f;
    private List<DamageReceiver> insideReceivers = new List<DamageReceiver>();
    private readonly List<Collider2D> overlapColliders = new List<Collider2D>();
    private readonly HashSet<DamageReceiver> windowScanHitReceivers = new HashSet<DamageReceiver>();
    private readonly HashSet<DamageReceiver> attackWindowHitReceivers = new HashSet<DamageReceiver>();
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
    public event System.Action<DamageReceiver, float> OnReceiverDamageResolved;

    public CombatWeaponAudioClass WeaponAudioClass => weaponAudioClass;
    public string AttackAudioCueId => attackAudioCueId;
    public TileDamageToolKind TileDamageToolKind => tileDamageToolKind;
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
    public override void Load()
    {
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
        windowOverlapScanEnabled = false;
        lastColliderEnabled = damageCollider != null && damageCollider.enabled;
        tileDamageAppliedThisWindow = false;
        nonDamageableImpactAppliedThisWindow = false;
    }

    public override void Save()
    {
        // 保存逻辑可以后续实现
    }

    public override void ModUpdate(float deltaTime)
    {
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
        }
    }
    #endregion

    #region 伤害处理
    public void OnTriggerEnter2D(Collider2D other)
    {
        // 伤害接触只接受 DamageReciver 层；交互、拾取、玩家身体等不会进入伤害解析链。
        if (damageCollider == null || !damageCollider.enabled ||
            !CombatPhysicsChannels.IsDamageReceiverCollider(other))
        {
            return;
        }

        DamageReceiver receiver = WorldTopologyColliderProxy.ResolveComponent<DamageReceiver>(other);
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
        DamageReceiver receiver = WorldTopologyColliderProxy.ResolveComponent<DamageReceiver>(other);
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
    private void ApplyDamageToReceiver(DamageReceiver receiver, Collider2D hitCollider = null)
    {
        if (receiver == null ||
            IsDamageSourceReceiver(receiver) ||
            !CanDealDamageNow() ||
            !FactionRelationService.CanAttack(item, receiver.item))
        {
            return;
        }

        if (attackWindowHitReceivers.Contains(receiver) ||
            attackWindowHitReceivers.Count >= Mathf.Max(1, MaxAttackTargets))
        {
            return;
        }

        attackWindowHitReceivers.Add(receiver);

        // 造成伤害
        float acDamage = receiver.Hurt(this);

        // DamageReceiver 与受击 Collider 可能位于不同层级，不能假定接收器节点自身带 Collider。
        if (acDamage >= 0f && AttackEffects != null && AttackEffects.Count > 0)
        {
            Vector2 hitPoint = ResolveHitPoint(receiver, hitCollider);
            SpawnEffect(hitPoint, acDamage);
        }

        // 触发伤害完成事件（无论伤害是否大于 0 都会触发）
        OnReceiverDamageResolved?.Invoke(receiver, acDamage);
        OnDamageApplied?.Invoke(acDamage);


        lastDamageTime = Time.time;

    }

    /// <summary>解析稳定的命中特效位置；缺少碰撞体时回退到受击对象中心。</summary>
    private Vector2 ResolveHitPoint(DamageReceiver receiver, Collider2D hitCollider)
    {
        if (hitCollider != null)
            return hitCollider.ClosestPoint(transform.position);

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
            AttackEffects == null ||
            AttackEffects.Count == 0 ||
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
        if (tileDamageAppliedThisWindow ||
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
        if (result.AppliedDamage >= 0f && AttackEffects != null && AttackEffects.Count > 0)
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
        tileDamageAppliedThisWindow = false;
        nonDamageableImpactAppliedThisWindow = false;
        windowScanHitReceivers.Clear();
        attackWindowHitReceivers.Clear();
        ScanCurrentOverlapsAndApplyDamageForWindow();
        // 动画可能在同一帧内开关伤害 Collider，窗口开启时立即补一次格子建筑查询。
        TryApplyDamageToTilemap();
    }

    private void EndDamageWindow()
    {
        insideReceivers.Clear();
        windowScanHitReceivers.Clear();
        attackWindowHitReceivers.Clear();
        windowOverlapScanEnabled = false;
        tileDamageAppliedThisWindow = false;
        nonDamageableImpactAppliedThisWindow = false;
    }

    /// <summary>判断接收器是否属于伤害物品自身或其拥有者；仅过滤自伤，不改变攻击者身份。</summary>
    private bool IsDamageSourceReceiver(DamageReceiver receiver)
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

    private void SpawnEffect(Vector2 hitPoint, float damage)
    {
        // 特效生成逻辑
        VisualEffectManager effectManager = VisualEffectManager.Instance;
        foreach (GameEffect effectPrefab in AttackEffects)
        {
            if (effectPrefab != null)
            {
                GameEffect effect = effectManager != null
                    ? effectManager.GetGameEffectFromPool(effectPrefab)
                    : Instantiate(effectPrefab);
                effect.transform.position = new Vector3(hitPoint.x, hitPoint.y, 0f);
                object effectData = effect is DamageTextEffect
                    ? BuildDamageTextData(damage)
                    : damage;
                effect.Effect(transform, effectData);
            }
        }
    }

    /// <summary>按占比最大的有效伤害类型选择伤害数字样式。</summary>
    private DamageTextEffectData BuildDamageTextData(float damage)
    {
        DamageTextStyle style = DamageTextStyle.Normal;
        CombatDamage values = ResolveDamageValues();
        float highest = values.Cutting;
        if (highest > 0f)
            style = DamageTextStyle.Cutting;
        if (values.Piercing > highest)
        {
            highest = values.Piercing;
            style = DamageTextStyle.Piercing;
        }
        if (values.Chopping > highest)
        {
            highest = values.Chopping;
            style = DamageTextStyle.Cutting;
        }
        if (values.Blunt > highest)
            style = DamageTextStyle.Blunt;

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

        float relativeMultiplier = targetMultiplier / damageRangeMultiplier;
        boxCollider.size *= relativeMultiplier;
        boxCollider.edgeRadius *= relativeMultiplier;
        damageRangeMultiplier = targetMultiplier;
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
            DamageReceiver receiver = WorldTopologyColliderProxy.ResolveComponent<DamageReceiver>(overlap);
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
