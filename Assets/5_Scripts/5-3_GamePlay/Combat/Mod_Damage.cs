// 伤害模块应该管理的内容
using System.Collections.Generic;
using UnityEngine;

public class Mod_Damage : Module, IDamageSender
{
    #region 伤害相关数据
    [Header("攻击特效")]
    public List<GameEffect> AttackEffects = new List<GameEffect>();

    public List<DamageTag> Weakness = new List<DamageTag>();
    public GameValue_float Damage = new GameValue_float(10f);

    [Header("定时伤害设置")]
    [Tooltip("伤害间隔时间（秒）\n-1: 永远不启用\n0: 每帧造成伤害\n>0: 每间隔秒数造成伤害")]
    public float DamageInterval = -1f;
    [Tooltip("是否启用触发器进入时的伤害逻辑（默认为true）")]
    public bool EnableOnTriggerEnterDamage = true;
    [Tooltip("是否仅允许物品在手上时造成伤害")]
    public bool OnlyDealDamageWhenInHand = false;

    [Header("调试信息")]
    [SerializeField] private bool showDebugWarnings = true;
    [SerializeField] private Collider2D damageCollider;

    // 定时伤害相关
    [SerializeField]
    private float lastDamageTime = 0f;
    private List<DamageReceiver> insideReceivers = new List<DamageReceiver>();
    private bool lastColliderEnabled = false;

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
    #endregion

    #region IDamageSender 实现
    Item IDamageSender.attacker { get => item; set => item = value; }
    List<DamageTag> IDamageSender.Weakness { get => Weakness; set => Weakness = value; }
    GameValue_float IDamageSender.Damage { get => Damage; set => Damage = value; }
    #endregion

    #region Unity 生命周期
    public override void Load()
    {
        // 初始化时尝试获取碰撞体组件
        if (damageCollider == null)
        {
            damageCollider = GetComponent<Collider2D>();
        }


        // 初始化定时伤害相关数据
        lastDamageTime = 0f;
        insideReceivers.Clear();
        lastColliderEnabled = damageCollider != null && damageCollider.enabled;
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
                if (!colliderEnabled)
                {
                    insideReceivers.Clear();
                }
            }
        }

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
        if (!other.TryGetComponent(out DamageReceiver receiver)) return;

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
                ApplyDamageToReceiver(receiver);
            }
            else
            {
                // DamageInterval == 0：视为“每帧都可伤害”，进入时也允许立刻打一击
                // DamageInterval  > 0：需要满足冷却时间
                if (DamageInterval == 0f || Time.time - lastDamageTime >= DamageInterval)
                {
                    // 实际更新时间由 ApplyDamageToReceiver 在真正造成伤害时负责
                    ApplyDamageToReceiver(receiver);
                }
            }
        }
    }

    public void OnTriggerExit2D(Collider2D other)
    {
        // 从内部接收器列表中移除
        if (other.TryGetComponent(out DamageReceiver receiver))
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

    private void ApplyDamageToReceiver(DamageReceiver receiver)
    {
        if (!CanDealDamageNow()) return;

        // 造成伤害
        float acDamage = receiver.Hurt(this);

        // 生成攻击特效
        if (AttackEffects != null && AttackEffects.Count > 0)
        {
            Vector2 hitPoint = receiver.GetComponent<Collider2D>().ClosestPoint(transform.position);
            SpawnEffect(hitPoint, acDamage);
        }

        // 触发伤害完成事件（无论伤害是否大于 0 都会触发）
        OnDamageApplied?.Invoke(acDamage);


        lastDamageTime = Time.time;

    }

    private bool CanDealDamageNow()
    {
        if (!OnlyDealDamageWhenInHand) return true;
        return item.InHand;
    }

    private void SpawnEffect(Vector2 hitPoint, float damage)
    {
        // 特效生成逻辑
        foreach (GameEffect effectPrefab in AttackEffects)
        {
            if (effectPrefab != null)
            {
                var effect = Instantiate(effectPrefab);
                effect.transform.position = new Vector3(hitPoint.x, hitPoint.y, 0f);
                effect.Effect(transform, damage);
            }
        }
    }
    #endregion

    #region 新增方法：控制伤害启用/禁用
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

        if (damageCollider != null && damageCollider.enabled != enabled && showDebugWarnings)
        {
            Debug.LogWarning($"[{name}] 伤害状态由 Collider.enabled 控制，请在外部开关 Collider", this);
        }

        if (!enabled)
        {
            insideReceivers.Clear();
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
        lastDamageTime = Time.time; // 重置伤害计时
    }
    public void StopAttack()
    {
        SetDamageEnabled(false);
    }

    #endregion
}