using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

public class Mod_Animator : MonoBehaviour
{
    public bool IsAttacking = false;
    public UltEvent AttackEvent = new UltEvent();
    public UltEvent StopAttackEvent = new UltEvent();


    public void Attack()
    {
        AttackEvent.Invoke();
    }
 public void StopAttack()
    {
        StopAttackEvent.Invoke();
    }

    public void Update()
    {
        if (IsAttacking)
        {
            Attack();
        }

        if (!IsAttacking)
        {
            StopAttack();
        }
    }

}
