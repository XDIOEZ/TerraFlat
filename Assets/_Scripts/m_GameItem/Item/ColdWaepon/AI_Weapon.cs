using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

public class AI_Weapon : Item, IColdWeapon, IAttackState
{
    #region ����
    #region ��������
    [Tooltip("����")]
    public ColdWeaponData _data;

    public override ItemData Item_Data
    {
        get => _data;
        set => _data = (ColdWeaponData)value;
    }
    #endregion

    #region �˺�������
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

    #region ��������

    // ͨ�� _data ӳ��ӿ�����
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

    public float LastDamageTime { get => _data._lastDamageTime; set => _data._lastDamageTime = value; }
    //�����˺��������
    public float MaxDamageCount { get => _data._maxAttackCount; set => _data._maxAttackCount = value; }
    #endregion

    #region ����״̬
    public UltEvent OnAttackStart { get; set; } = new UltEvent();
    public UltEvent OnAttackUpdate { get; set; } = new UltEvent();
    public UltEvent OnAttackEnd { get; set; } = new UltEvent();

    #endregion
    #endregion

    #region ����

    // ��д Item �� Act() ����
    public override void Act()
    {

    }
    #endregion
}
