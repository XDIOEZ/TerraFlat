using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

[NodeMenu("ActionNode/行动/自动触发攻击")]
public class AI_AutoTriggerAttack : ActionNode
{
    public float TriggerDistance = 0.5f;     // 距离敌人多近会触发攻击
    public float AttackInterval = 0.5f;      // 攻击间隔
    public float AttackDuration = 0.2f;      // 攻击持续时间

    private float lastAttackTime = -Mathf.Infinity;
    private bool isAttacking = false;

    protected override void OnStart()
    {
        isAttacking = false;
    }

    protected override void OnStop()
    {
        if (isAttacking)
        {
            context.ColdWeapon.CancelAttack();
            isAttacking = false;
        }
    }

    protected override State OnUpdate()
    {
        float dist = Vector2.Distance(context.mover.TargetPosition, context.ColdWeapon.transform.position);

        // 如果距离足够近
        if (dist <= TriggerDistance)
        {

            // 当前正在攻击中，检查是否到取消时间
            if (isAttacking)
            {
                if (Time.time >= lastAttackTime + AttackDuration)
                {
                    context.ColdWeapon.CancelAttack();
                    isAttacking = false;
                }
                return State.Running;
            }

            // 如果已经过了攻击间隔，执行攻击
            if (Time.time >= lastAttackTime + AttackInterval)
            {
                context.ColdWeapon.StartAttack();
                lastAttackTime = Time.time;
                isAttacking = true;
                return State.Running;
            }
        }

        // 如果敌人远离且在攻击状态中，也需要取消攻击
        if (isAttacking && dist > TriggerDistance)
        {
            context.ColdWeapon.CancelAttack();
            isAttacking = false;
        }

        return State.Running;
    }
}
