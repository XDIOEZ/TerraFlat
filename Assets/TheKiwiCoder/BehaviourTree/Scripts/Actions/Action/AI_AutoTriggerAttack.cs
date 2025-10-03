using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

[NodeMenu("ActionNode/行动/自动触发攻击")]
public class AI_AutoTriggerAttack : ActionNode
{
    public float TriggerDistance = 0.5f;         // 距离敌人多近会触发攻击
    public float AttackInterval = 0.5f;          // 攻击间隔
    public float AttackDuration = 0.2f;          // 攻击持续时间
    public float AttackWaitForAniation = 0.1f;   // 确认能攻击后等待的时间（播放攻击动画前）

    private float lastAttackTime = -Mathf.Infinity;
    private bool isAttacking = false;
    private bool isPreparingAttack = false;
    private float attackPrepareStartTime;

    protected override void OnStart()
    {
        isAttacking = false;
        isPreparingAttack = false;
    }

    protected override void OnStop()
    {
        if (isAttacking)
        {
            context.Damage.StopAttack();
            isAttacking = false;
        }
        isPreparingAttack = false;
    }

    protected override State OnUpdate()
    {
        float dist = Vector2.Distance(context.mover.TargetPosition, context.Damage.transform.position);

        // 距离足够近
        if (dist <= TriggerDistance)
        {

            // 攻击中：判断是否攻击完成
            if (isAttacking)
            {
                if (Time.time >= lastAttackTime + AttackDuration)
                {
                    context.Damage.StopAttack();
                    isAttacking = false;
                    return State.Success;
                }
                return State.Running;
            }

            // 准备攻击中：等待动画准备完成
            if (isPreparingAttack)
            {
                if (Time.time >= attackPrepareStartTime + AttackWaitForAniation)
                {
                    context.Damage.StartAttack();
                    lastAttackTime = Time.time;
                    isAttacking = true;
                    isPreparingAttack = false;
                    return State.Running;
                }
                return State.Running;
            }

            // 攻击冷却时间已到：进入准备攻击状态
            if (Time.time >= lastAttackTime + AttackInterval)
            {
                attackPrepareStartTime = Time.time;
                isPreparingAttack = true;
                return State.Running;
            }

            return State.Running; // 冷却中
        }

        // 敌人远离：取消所有攻击状态
        if (isAttacking)
        {
            context.Damage.StopAttack();
            isAttacking = false;
        }

        isPreparingAttack = false;

        return State.Failure;
    }
}
