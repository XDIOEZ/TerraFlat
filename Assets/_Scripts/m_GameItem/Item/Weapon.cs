using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

public interface ICanBePickUp
{
    ItemData Pickup();

    ItemData PickUp_ItemData { get; set; }
}

public abstract class Weapon : Item,IFunction_Attack
{
    #region 字段 | Fields 
    public WeaponData _Data;
    public AttackTrigger triggerAttack;
    public AttackTrigger TriggerAttack
    {
        get
        {
            //Debug.Log(" Get TriggerAttack");
            if (triggerAttack == null)
            {
               // Debug.Log("  TriggerAttack");
                triggerAttack = XDTool.GetComponentInChildrenAndParent<AttackTrigger>(gameObject);
            }
            return triggerAttack;
        }
    }
    #endregion
    #region 生命周期 | Life Cycle 
    public virtual void OnEnable()
    {
        if (TriggerAttack != null)
        {
           // TriggerAttack.SetWeapon(this);
          //  Debug.Log("Weapon Enable");
        }
    }
    public virtual void Start()
    {
        if (TriggerAttack != null)
        {
          //  TriggerAttack.SetWeapon(this);
           // Debug.Log("Weapon Start");
        }
    }
    public virtual void OnDisable()
    {
        if (TriggerAttack != null)
        {
          //  TriggerAttack.RemoveWeapon(this);
            Debug.Log("Weapon Disable");
        }
    }
    #endregion

    #region 行为接口 | Attack Logi
    public override void Act()
    {
    }

    public abstract void StartAttack();

    public abstract void StayAttack();

    public abstract void StopAttack();
    #endregion

}