/*using UnityEngine;
using System;
using NaughtyAttributes;
using MemoryPack;
using UltEvents;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

public class DamageSender : MonoBehaviour, IDamageSender
{
    #region 字段声明
    [Tooltip("武器组件"), Required]
    public IColdWeapon _Weapon;

    [Tooltip("伤害模式开关")]
    public bool isDamageModeEnabled = true;

    [Tooltip("碰撞体组件")]
    public Collider2D _collider;

    [Tooltip("已造成伤害次数")]
    public int damageCount = 0;

    [Tooltip("最小伤害间隔时间")]
    public float CurrentDamageInterval = 1f;

    [Tooltip("上次伤害时间记录")]
    private float lastDamageTime = 0f;

    [Tooltip("伤害事件")]
    public UltEvent<IReceiveDamage> onDamage;
    #endregion

    #region 属性封装
    [Tooltip("伤害模式启用状态")]
    public virtual bool IsDamageModeEnabled
    {
        get => isDamageModeEnabled;
        set
        {
            _collider.enabled = value;
            isDamageModeEnabled = value;
        }
    }

    [Tooltip("碰撞体组件")]
    public virtual Collider2D Collider
    {
        get => _collider;
        set
        {
            if (_collider == null)
                _collider = GetComponent<Collider2D>();
            _collider = value;
        }
    }

    [Tooltip("武器伤害值")]
    public virtual Damage DamageValue
    {
        get => _Weapon?.WeaponDamage ?? new Damage();
        set => _Weapon.WeaponDamage = value;
    }

    [Tooltip("最小伤害间隔时间")]
    public float MinDamageInterval
    {
        get => _Weapon?.MinDamageInterval ?? 1f;
        set => _Weapon.MinDamageInterval = value;
    }
    #endregion

    #region Unity回调
    private void Awake()
    {
        InitializeComponents();
    }
    #endregion

    #region 公共方法
    [Button("初始化组件")]
    public virtual void InitializeComponents()
    {
        _Weapon = GetComponentInParent<IColdWeapon>();
        Collider = GetComponent<Collider2D>();
    }

    public virtual void OnTriggerEnter2D(Collider2D otherCollider)
    {
        // 检测伤害检查点
        Damager_CheckerPoint damagePoint = otherCollider.GetComponentInChildren<Damager_CheckerPoint>();

        // 检测伤害接收者
        IReceiveDamage receiver = otherCollider.GetComponentInParent<IReceiveDamage>();

        // 安全性检查
        if (damagePoint == null)
        {
            Debug.LogWarning($"未找到有效伤害检查点: {otherCollider.name}");
            return;
        }

        if (receiver == null)
        {
            Debug.LogWarning($"未找到伤害接收者: {otherCollider.name}");
            return;
        }

        // 伤害条件判断
        float timeSinceLastDamage = Time.time - lastDamageTime;
        if (timeSinceLastDamage < MinDamageInterval)
        {
            Debug.Log($"[{name}] 伤害冷却中：剩余 {MinDamageInterval - timeSinceLastDamage:F1}s");
            return;
        }

        if (damageCount >= 1)
        {
            Debug.Log($"[{name}] 已达最大伤害次数({damageCount}/{damageCount})");
            return;
        }

        if (!IsDamageModeEnabled)
        {
            Debug.Log($"[{name}] 伤害模式已禁用");
            return;
        }

        // 执行伤害逻辑
        damagePoint.SetDamageValue_Point(DamageValue);
        damageCount++;
        lastDamageTime = Time.time;

        // 触发伤害事件
        onDamage?.Invoke(receiver);
    }

    [Button("启用伤害模式")]
    public void StartTrySendDamage()
    {
        IsDamageModeEnabled = true;
    }

    [Button("持续伤害（未实现）")]
    public virtual void StayTrySendDamage()
    {
        throw new NotImplementedException("请在子类实现持续伤害逻辑");
    }

    [Button("禁用伤害模式")]
    public void EndTrySendDamage()
    {
        IsDamageModeEnabled = false;
        damageCount = 0;
    }
    #endregion

    #region 私有工具方法
    private void ResetDamageCount()
    {
        damageCount = 0;
        lastDamageTime = 0f;
    }
    #endregion
}

*/