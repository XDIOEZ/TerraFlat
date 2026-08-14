// 伤害模块应该管理的内容
using System.Collections.Generic;
using UnityEngine;

public class Mod_Damage : Module, IDamageSender
{
    #region 伤害相关数据
    [Header("攻击特效")]
    public List<GameEffect> AttackEffects = new List<GameEffect>();

    [Header("四类攻击伤害")]
    [Tooltip("切割、穿刺、劈砍、钝击分别独立参与防御结算；总战斗力为四项之和。")]
    public CombatDamage DamageValues = new CombatDamage();

    [HideInInspector]
    [Tooltip("仅用于把旧资源迁移到四类伤害，新的战斗结算不会直接读取该值。")]
    public GameValue_float Damage = new GameValue_float(10f);

    [HideInInspector]
    [Tooltip("仅用于识别旧资源的主要伤害类型，等级不再参与结算。")]
    public List<DamageType> Weakness = new List<DamageType>();

    [SerializeField, HideInInspector]
    private int damageSystemVersion;

    [Header("定时伤害设置")]
    [Tooltip("伤害间隔时间（秒）\n-1: 永远不启用\n0: 每帧造成伤害\n>0: 每间隔秒数造成伤害")]
    public float DamageInterval = -1f;
    [Tooltip("是否启用触发器进入时的伤害逻辑（默认为true）")]
    public bool EnableOnTriggerEnterDamage = true;
    [Tooltip("是否仅允许物品在手上时造成伤害")]
    public bool OnlyDealDamageWhenInHand = false;

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

    [SerializeField] private Collider2D damageCollider;

    // 定时伤害相关
    [SerializeField]
    private float lastDamageTime = 0f;
    private List<DamageReceiver> insideReceivers = new List<DamageReceiver>();
    private readonly List<Collider2D> overlapColliders = new List<Collider2D>();
    private readonly HashSet<DamageReceiver> aiWindowHitReceivers = new HashSet<DamageReceiver>();
    private bool aiWindowOverlapScanEnabled;
    private bool lastColliderEnabled = false;
    private bool tileDamageAppliedThisWindow;

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

    public CombatWeaponAudioClass WeaponAudioClass => weaponAudioClass;
    public string AttackAudioCueId => attackAudioCueId;
    public TileDamageToolKind TileDamageToolKind => tileDamageToolKind;
    #endregion

    #region IDamageSender 实现
    Item IDamageSender.attacker { get => item; set => item = value; }
    CombatDamage IDamageSender.DamageValues => ResolveDamageValues();
    #endregion

    #region Unity 生命周期
    public override void Load()
    {
        NormalizeDamageValues();

        // 初始化时尝试获取碰撞体组件
        if (damageCollider == null)
        {
            damageCollider = GetComponent<Collider2D>();
        }


        // 初始化定时伤害相关数据
        lastDamageTime = 0f;
        insideReceivers.Clear();
        overlapColliders.Clear();
        aiWindowHitReceivers.Clear();
        aiWindowOverlapScanEnabled = false;
        lastColliderEnabled = damageCollider != null && damageCollider.enabled;
        tileDamageAppliedThisWindow = false;
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
        // 碰撞检测和伤害处理逻辑
        if (damageCollider == null || !damageCollider.enabled) return;
        DamageReceiver receiver = WorldTopologyColliderProxy.ResolveComponent<DamageReceiver>(other);
        if (receiver == null) return;

        // AI 伤害窗已主动扫描过的目标，不再被同一窗口内后续触发事件重复结算。
        if (aiWindowOverlapScanEnabled && aiWindowHitReceivers.Contains(receiver))
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

            if (aiWindowOverlapScanEnabled)
                aiWindowHitReceivers.Add(receiver);
        }
    }

    public void OnTriggerExit2D(Collider2D other)
    {
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
        if (!CanDealDamageNow()) return;

        // 造成伤害
        float acDamage = receiver.Hurt(this);

        // DamageReceiver 与受击 Collider 可能位于不同层级，不能假定接收器节点自身带 Collider。
        if (acDamage > 0f && AttackEffects != null && AttackEffects.Count > 0)
        {
            Vector2 hitPoint = ResolveHitPoint(receiver, hitCollider);
            SpawnEffect(hitPoint, acDamage);
        }

        // 触发伤害完成事件（无论伤害是否大于 0 都会触发）
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
        if (result.AppliedDamage > 0f && AttackEffects != null && AttackEffects.Count > 0)
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

    private void BeginTileDamageWindow()
    {
        tileDamageAppliedThisWindow = false;
        aiWindowHitReceivers.Clear();
    }

    private void EndDamageWindow()
    {
        insideReceivers.Clear();
        aiWindowHitReceivers.Clear();
        aiWindowOverlapScanEnabled = false;
        tileDamageAppliedThisWindow = false;
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

    /// <summary>获取已校正的四类伤害，并兼容尚未迁移的旧攻击资源。</summary>
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
        damageSystemVersion = 1;
    }

    /// <summary>旧单值伤害按原标签迁入一个主要类型；旧等级被明确丢弃。</summary>
    private void NormalizeDamageValues()
    {
        DamageValues ??= new CombatDamage();
        DamageValues.ClampNonNegative();
        if (damageSystemVersion >= 1)
            return;

        if (DamageValues.TotalCombatPower > 0f)
        {
            damageSystemVersion = 1;
            return;
        }

        if (Damage == null || Damage.Value <= 0f)
        {
            damageSystemVersion = 1;
            return;
        }

        float legacyValue = Mathf.Max(0f, Damage.Value);
        DamageTag legacyTag = Weakness != null && Weakness.Count > 0
            ? Weakness[0].Tag
            : DamageTag.钝击;

        switch (legacyTag)
        {
            case DamageTag.切割:
                DamageValues.Cutting = legacyValue;
                break;
            case DamageTag.穿刺:
                DamageValues.Piercing = legacyValue;
                break;
            case DamageTag.劈砍:
                DamageValues.Chopping = legacyValue;
                break;
            default:
                DamageValues.Blunt = legacyValue;
                break;
        }

        damageSystemVersion = 1;
    }

    #endregion

    #region 新增方法：控制伤害启用/禁用

    /// <summary>
    /// AI 专用：伤害窗口开启后主动扫描当前重叠目标，弥补碰撞体后开时缺少 Enter 事件的问题。
    /// </summary>
    protected void ScanCurrentOverlapsAndApplyDamageForAiWindow()
    {
        if (damageCollider == null || !damageCollider.enabled)
            return;

        aiWindowOverlapScanEnabled = true;
        aiWindowHitReceivers.Clear();
        Physics2D.SyncTransforms();
        overlapColliders.Clear();

        ContactFilter2D filter = new ContactFilter2D
        {
            useTriggers = true,
            useLayerMask = false,
            useDepth = false,
            useNormalAngle = false
        };
        damageCollider.OverlapCollider(filter, overlapColliders);

        for (int i = 0; i < overlapColliders.Count; i++)
        {
            Collider2D overlap = overlapColliders[i];
            DamageReceiver receiver = WorldTopologyColliderProxy.ResolveComponent<DamageReceiver>(overlap);
            if (receiver == null || receiver.item == item || !aiWindowHitReceivers.Add(receiver))
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
    /// 设置伤害逻辑启用状态（不负责开关Collider）
    /// </summary>
    /// <param name="enabled">是否启用伤害检测</param>
    public void SetDamageEnabled(bool enabled)
    {
        if (damageCollider == null)
        {
            damageCollider = GetComponent<Collider2D>();
        }

        bool wasEnabled = damageCollider != null && damageCollider.enabled;
        if (damageCollider != null && damageCollider.enabled != enabled)
        {
           damageCollider.enabled = enabled; // 先切换状态以确保触发器事件正确调用，从而维护内部接收器列表的准确性
        }

        lastColliderEnabled = damageCollider != null && damageCollider.enabled;
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

    public void StartAttack()
    {
        SetDamageEnabled(true);
        // 某些持续伤害模块的碰撞体可能已经启用，路由器会按攻击者去重。
        CombatAudioRouter.PlayWeaponAttack(this);
        lastDamageTime = Time.time; // 重置伤害计时
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
