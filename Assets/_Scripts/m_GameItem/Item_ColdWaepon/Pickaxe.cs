using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Pickaxe : Item, IColdWeapon
{
    //ÎäÆ÷ÉËº¦Êä³ö´°¿Ú
    public float MaxDamageCount { get => _data._maxAttackCount; set => _data._maxAttackCount = value; }
    public Data_ColdWeapon _data;

    public override ItemData itemData
    {
        get => _data;

        set => _data = (Data_ColdWeapon)value;
    }

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

    public float MaxAttackDistance { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public float AttackSpeed { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public float ReturnSpeed { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public float SpinSpeed { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public float EnergyCostSpeed { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public float LastDamageTime { get => _data._lastDamageTime; set => _data._lastDamageTime = value; }



    public override void Act()
    {
        throw new System.NotImplementedException();
    }
}

