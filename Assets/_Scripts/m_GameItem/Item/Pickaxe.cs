using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Pickaxe : Weapon, IColdWeapon
{

    public PickaxeToolData stonePicaxeData;

    public DamageSender_ColdWeapon damageSender;
    public override ItemData Item_Data
    {
        get
        {
            return stonePicaxeData;
        }

        set
        {
            stonePicaxeData = (PickaxeToolData)value;
        }
    }

    public Damage WeaponDamage
    {
        get
        {
            return stonePicaxeData._damage;
        }

        set
        {
            stonePicaxeData._damage = value;
        }
    }

    public float MinDamageInterval
    {
        get
        {
            return stonePicaxeData._minDamageInterval;
        }

        set
        {
            stonePicaxeData._minDamageInterval = value;
        }
    }

    public ColdWeaponData Data { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public float MaxAttackDistance { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public float AttackSpeed { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public float ReturnSpeed { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public float SpinSpeed { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public float EnergyCostSpeed { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }

    public new void Start()
    {
        damageSender = GetComponentInChildren<DamageSender_ColdWeapon>();
     //   damageSender.+= Attack;
        base.Start();
    }
    public new void OnDisable()
    {
        base.OnDisable();
    }

    public  void Attack(IReceiveDamage BeAttacker)
    {
        BeAttacker.ReceiveDamage(0);

    }
    public override void StartAttack()
    {
        damageSender.IsDamageModeEnabled = true;
    //    damageSender.damageCount = 0;
        // Debug.Log("Stone Axe Attack");
    }
    public override void StopAttack()
    {
        damageSender.IsDamageModeEnabled = false;
    //    damageSender.damageCount = 1;
        // Debug.Log("Stone Axe Stop Attack");
    }

    public override void StayAttack()
    {
        // Debug.Log("Stone Axe Stay Attack");
    }


}

