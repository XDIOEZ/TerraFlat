using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector; // �����˼������using��

public class IronSword : Item, IColdWeapon,IDamager
{
    #region ����
    #region ��������

    public ColdWeaponData _data;

    public override ItemData Item_Data { get => _data; set => _data = (ColdWeaponData)value; }
    #endregion
    #region �˺�������

    IDamageSender _sender;
    [ShowNativeProperty]
    public IDamageSender Sender
    {
        get
        {
            if (_sender == null)
            {
                _sender = GetComponentInChildren<IDamageSender>();
                _sender.DamageValue = WeaponDamage;
                _sender.OnDamage += OnSendDamage;//��������˺��󼤻���¼�
            }
            return _sender;
        }
        set
        {
            _sender = value;
        }
    }

    #endregion

    #region ��������

    public Damage WeaponDamage { get => _data._damage; set => _data._damage = value; }
    public float MinDamageInterval { get => _data._minDamageInterval; set => _data._minDamageInterval = value; }
    public float MaxAttackDistance { get => _data._maxAttackDistance; set => _data._maxAttackDistance = value; }
    public float AttackSpeed { get => _data._attackSpeed; set => _data._attackSpeed = value; }
    public float ReturnSpeed { get => _data._returnSpeed; set => _data._returnSpeed = value; }
    public float SpinSpeed { get => _data._spinSpeed; set => _data._spinSpeed = value; }
    public float EnergyCostSpeed { get => _data._energyConsumptionSpeed; set => _data._energyConsumptionSpeed = value; }
    #endregion

    #endregion

    #region ����

    public override void Act()
    {
        print("IronSword Act");
    }

    public void AttackStart()
    {
        Sender.StartTrySendDamage();
    }

    public void AttackUpdate()
    {
        //
    }
    public void AttackEnd()
    {
        // Attack logic
        Sender.EndTrySendDamage();
    }

    public void OnSendDamage(float damage)
    {
        _data._lastAttackTime = Time.time;
    }
    #endregion
}
