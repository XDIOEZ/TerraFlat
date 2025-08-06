using MemoryPack;
using NaughtyAttributes;
using System;
using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

/*<<<<<<< Updated upstream
public class StoneSword :Item,IColdWeapon,IDamager
=======*/
public class StoneSword :Item,IColdWeapon,IAttackState
//>>>>>>> Stashed changes
{
    public Data_ColdWeapon _data;

    #region ÊôÐÔ
    public Damage WeaponDamage
    {
        get
        {
            return _data._damage;
        }
        set
        {
            _data._damage = value;
        }

    }

    public float MinDamageInterval
    {
        get
        {
            return _data._minDamageInterval;
        }
        set
        {
            _data._minDamageInterval = value;
        }

    }

    public override ItemData itemData { get => _data; set => _data = (Data_ColdWeapon)value; }

    IDamageSender _sender;
    [ShowNativeProperty]
    public IDamageSender Sender
    {
        get
        {
            if (_sender == null)
            {
                _sender = GetComponentInChildren<IDamageSender>();
            }
            return _sender;
        }
        set
        {
            _sender = value;
        }
    }

    public Data_ColdWeapon Data { get => _data; set => _data = value; }
    public float MaxAttackDistance { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public float AttackSpeed { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public float ReturnSpeed { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public float SpinSpeed { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public float EnergyCostSpeed { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public float LastDamageTime { get =>_data._lastDamageTime; set => _data._lastDamageTime = value; }
    //ÎäÆ÷ÉËº¦Êä³ö´°¿Ú
    public float MaxDamageCount { get => _data._maxAttackCount; set => _data._maxAttackCount = value; }
    #endregion

    #region ¹¥»÷ÐÐÎª¼àÌý
    public UltEvent OnAttackStart { get; set; } = new UltEvent();
    public UltEvent OnAttackUpdate { get; set; } = new UltEvent();
    public UltEvent OnAttackEnd { get; set; } = new UltEvent();
    #endregion

    public override void Act()
    {
        throw new System.NotImplementedException();
    }
}


