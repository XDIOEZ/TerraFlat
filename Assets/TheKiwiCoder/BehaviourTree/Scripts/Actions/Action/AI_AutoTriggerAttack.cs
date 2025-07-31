using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

[NodeMenu("ActionNode/�ж�/�Զ���������")]
public class AI_AutoTriggerAttack : ActionNode
{
    public float TriggerDistance = 0.5f;     // ������˶���ᴥ������
    public float AttackInterval = 0.5f;      // �������
    public float AttackDuration = 0.2f;      // ��������ʱ��

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

        // ��������㹻��
        if (dist <= TriggerDistance)
        {

            // ��ǰ���ڹ����У�����Ƿ�ȡ��ʱ��
            if (isAttacking)
            {
                if (Time.time >= lastAttackTime + AttackDuration)
                {
                    context.ColdWeapon.CancelAttack();
                    isAttacking = false;
                }
                return State.Running;
            }

            // ����Ѿ����˹��������ִ�й���
            if (Time.time >= lastAttackTime + AttackInterval)
            {
                context.ColdWeapon.StartAttack();
                lastAttackTime = Time.time;
                isAttacking = true;
                return State.Running;
            }
        }

        // �������Զ�����ڹ���״̬�У�Ҳ��Ҫȡ������
        if (isAttacking && dist > TriggerDistance)
        {
            context.ColdWeapon.CancelAttack();
            isAttacking = false;
        }

        return State.Running;
    }
}
