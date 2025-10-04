using UnityEngine;
using TheKiwiCoder;

[NodeMenu("ActionNode/����/����Ƿ񵽴�Ŀ���")]
public class CheckTargetReached : ActionNode
{
    [Header("������ֵ")]
    [Tooltip("�жϵ���Ŀ���ľ�����ֵ")]
    public float distanceThreshold = 0.1f;

    protected override void OnStart()
    {
        // ����Ҫ��ʼ������
        context.mover.stopDistance = distanceThreshold;
    }

    protected override void OnStop()
    {
        // ����Ҫ�������
    }

    protected override State OnUpdate()
    {
        // ����Ҫ������Ƿ����
        if (context.mover == null)
        {
            return State.Failure;
        }

        // ��ȡ��ǰλ�ú�Ŀ��λ��
        Vector2 currentPosition = context.mover.transform.position;
        Vector2 targetPosition = context.mover.TargetPosition;

        // �������
        float distanceToTarget = Vector2.Distance(currentPosition, targetPosition);

        // ����Ƿ�����ֵ��Χ��
        if (distanceToTarget <= distanceThreshold)
        {
            // �Ѿ�����Ŀ���
            return State.Success;
        }
        else
        {
            // ��δ����Ŀ���
            return State.Failure;
        }
    }
}