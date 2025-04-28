using MemoryPack;
using NaughtyAttributes;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class StoneSword :Item,IColdWeapon,IDamager
{
    public ColdWeaponData _data;

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

    public override ItemData Item_Data { get => _data; set => _data = (ColdWeaponData)value; }

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

    public ColdWeaponData Data { get => _data; set => _data = value; }
    public float MaxAttackDistance { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public float AttackSpeed { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public float ReturnSpeed { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public float SpinSpeed { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public float EnergyCostSpeed { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }

    public override void Act()
    {
        throw new System.NotImplementedException();
    }
}


