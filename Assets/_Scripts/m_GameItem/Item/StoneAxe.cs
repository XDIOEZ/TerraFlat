using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class StoneAxe : Item, IColdWeapon, IDamager
{
    #region 属性
    #region 物体数据
    [Tooltip("石斧数据")]
    public ColdWeaponData _data;

    public override ItemData Item_Data
    {
        get => _data;
        set => _data = (ColdWeaponData)value;
    }
    #endregion

    #region 伤害发生器
    [ShowNonSerializedField]
    private IDamageSender _sender;

    public IDamageSender Sender
    {
        get
        {
            if (_sender == null)
            {
                _sender = GetComponentInChildren<IDamageSender>();
                _sender.DamageValue = WeaponDamage;
            }
            return _sender;
        }
        set => _sender = value;
    }
    #endregion

    #region 武器数据
    // 通过 _data 映射接口属性
    public Damage WeaponDamage
    {
        get => _data._damage;
        set => _data._damage = value;
    }

    public float MinDamageInterval
    {
        get => _data._minDamageInterval;
        set => _data._minDamageInterval = value;
    }

    public float MaxAttackDistance
    {
        get => _data._maxAttackDistance;
        set => _data._maxAttackDistance = value;
    }

    public float AttackSpeed
    {
        get => _data._attackSpeed;
        set => _data._attackSpeed = value;
    }

    public float ReturnSpeed
    {
        get => _data._returnSpeed;
        set => _data._returnSpeed = value;
    }

    public float SpinSpeed
    {
        get => _data._spinSpeed;
        set => _data._spinSpeed = value;
    }

    public float EnergyCostSpeed
    {
        get => _data._energyConsumptionSpeed;
        set => _data._energyConsumptionSpeed = value;
    }

    #endregion
    #endregion

    #region 方法
    // 实现 IAttacker 接口
    public void AttackStart()
    {
        Sender.StartTrySendDamage();
    }

    public void AttackUpdate()
    {
        
    }

    public void AttackEnd()
    {
        Sender.EndTrySendDamage();
        // 重置武器状态
        //Debug.Log("攻击结束，武器返回速度：" + ReturnSpeed);
    }

    // 重写 Item 的 Act() 方法
    public override void Act()
    {
        // 示例：触发攻击开始
        AttackStart();
    }
    #endregion
}