using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

[NodeMenu("ActionNode/行动/执行攻击")]
public class AI_ExecuteAttack : ActionNode
{
    [Tooltip("攻击动作的总持续时间（秒）")]
    public float AttackDuration = 1.0f;

    [Tooltip("攻击准备阶段的持续时间（秒）")]
    public float AttackPrepareTime = 0.2f;

    [Tooltip("攻击判定阶段的持续时间（秒）")]
    public float AttackActiveTime = 0.5f;

    private float startTime;
    private float attackStartTime;
    private float attackEndTime;
    private float totalEndTime;

    private enum Phase
    {
        Preparing,
        Attacking,
        Ending,
        Done
    }

    private Phase currentPhase;

    protected override void OnStart()
    {
        startTime = Time.time;

        attackStartTime = startTime + AttackPrepareTime;
        attackEndTime = attackStartTime + AttackActiveTime;
        totalEndTime = startTime + AttackDuration;

        currentPhase = Phase.Preparing;
    }

    protected override void OnStop()
    {
        context.ColdWeapon.CancelAttack();
    }

    protected override State OnUpdate()
    {
        float now = Time.time;

        switch (currentPhase)
        {
            case Phase.Preparing:
                if (now >= attackStartTime)
                {
                    context.ColdWeapon.StartAttack();
                    currentPhase = Phase.Attacking;
                }
                break;

            case Phase.Attacking:
                if (now >= attackEndTime)
                {
                    context.ColdWeapon.StopAttack();
                    currentPhase = Phase.Ending;
                }
                break;

            case Phase.Ending:
                if (now >= totalEndTime)
                {
                    currentPhase = Phase.Done;
                    return State.Success;
                }
                break;

            case Phase.Done:
                return State.Success;
        }

        return State.Running;
    }

#if UNITY_EDITOR
    // 编辑器中限制设置错误
    private void OnValidate()
    {
        AttackPrepareTime = Mathf.Clamp(AttackPrepareTime, 0f, AttackDuration);

        float remainTime = AttackDuration - AttackPrepareTime;
        AttackActiveTime = Mathf.Clamp(AttackActiveTime, 0f, remainTime);
    }
#endif
}
