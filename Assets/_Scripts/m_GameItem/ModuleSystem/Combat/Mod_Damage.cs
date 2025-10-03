// 伤害模块应该管理的内容
using AYellowpaper.SerializedCollections;
using System.Collections.Generic;
using UnityEngine;

public class Mod_Damage : Module, IDamageSender
{
    #region 伤害相关数据
    [Header("攻击特效")]
    public List<GameEffect> AttackEffects = new List<GameEffect>();
    
    public SerializedDictionary<DamageTag, float> Weakness = new SerializedDictionary<DamageTag, float>();
    public GameValue_float Damage = new GameValue_float(10f);

    public override ModuleData _Data { get => MemoryPackableData; set => MemoryPackableData = (Ex_ModData_MemoryPackable)value; }
    public Ex_ModData_MemoryPackable MemoryPackableData;
    
    // 添加对碰撞体的引用
    [SerializeField] private Collider2D damageCollider;
    #endregion

    #region IDamageSender 实现
    Item IDamageSender.attacker { get => item; set => item = value; }
    SerializedDictionary<DamageTag, float> IDamageSender.Weakness { get => Weakness; set => Weakness = value; }
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
        
        // 默认禁用碰撞体
        if (damageCollider != null)
        {
            damageCollider.enabled = false;
        }
    }

    public override void Save()
    {
        // 保存逻辑可以后续实现
    }
    #endregion

    #region 伤害处理
    public void OnTriggerEnter2D(Collider2D other)
    {
        // 碰撞检测和伤害处理逻辑
        if (damageCollider == null || !damageCollider.enabled) return;
        if (!other.TryGetComponent(out DamageReceiver receiver)) return;
        
        // 友军检测
        var beAttackTeam = other.GetComponentInParent<ITeam>();
        var belongItem = item?.Owner;
        var belongTeam = belongItem?.GetComponent<ITeam>();
        
        if (beAttackTeam != null && belongTeam != null && 
            belongTeam.CheckRelation(beAttackTeam.TeamID) == RelationType.Ally)
            return;
        
        // 造成伤害
        float acDamage = receiver.Hurt(this);
        
        // 生成攻击特效
        if (AttackEffects != null && AttackEffects.Count > 0)
        {
            Vector2 hitPoint = other.ClosestPoint(transform.position);
            SpawnEffect(hitPoint, acDamage);
        }
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
    /// 设置伤害检测的启用状态
    /// </summary>
    /// <param name="enabled">是否启用伤害检测</param>
    public void SetDamageEnabled(bool enabled)
    {
        if (damageCollider != null)
        {
            damageCollider.enabled = enabled;
        }
        else
        {
            // 如果还没有获取到碰撞体，尝试获取
            damageCollider = GetComponent<Collider2D>();
            if (damageCollider != null)
            {
                damageCollider.enabled = enabled;
            }
            else
            {
                Debug.LogWarning("[Mod_Damage] 未找到Collider2D组件，无法设置伤害检测状态");
            }
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
    }
    public void StopAttack()
    {
        SetDamageEnabled(false);
    }

    #endregion
}