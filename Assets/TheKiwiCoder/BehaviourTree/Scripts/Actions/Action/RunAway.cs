using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

[NodeMenu("ActionNode/�ж�/����")]
public class RunAway : ActionNode
{
    [Tooltip("���ܷ������")]
    public Vector2 RunAwayDirection;

    [Tooltip("���ܵľ���")]
    public float RunAwayDistance = 10f;

    [Tooltip("���ƫ�ƽǶȣ��ȣ���180��ʾ ��90�� �������")]
    public float RandomAngle = 180f;

    protected override void OnStart()
    {
        RunAwayDirection = Vector2.zero;
    }

    protected override void OnStop()
    {
    }

    protected override State OnUpdate()
    {
        Vector2 finalDirection = Vector2.zero;
        var attackerUIDs = context.damageReciver.Data.AttackersUIDs;
        var items = context.itemDetector.CurrentItemsInArea;
        Vector2 selfPos = context.transform.position;

        // �ۼ����й����ߵķ�������
        for (int i = 0; i < attackerUIDs.Count; i++)
        {
            int attackerGuid = attackerUIDs[i];
            float weight = (i + 1f) / attackerUIDs.Count; // Խ����Ȩ��Խ��

            foreach (var item in items)
            {
                if (item.itemData.Guid == attackerGuid)
                {
                    Vector2 attackerPos = item.transform.position;
                    Vector2 awayDir = (selfPos - attackerPos).normalized;
                    finalDirection += awayDir * weight;
                }
            }
        }

        if (finalDirection != Vector2.zero)
        {
            // ��һ���������ܷ���
            RunAwayDirection = finalDirection.normalized;

            // ����Ŀ������
            Vector2 baseTarget = RunAwayDirection * RunAwayDistance*2;

            // �������ƫ�ƽǶȣ���Χ [-RandomAngle/2, +RandomAngle/2]
            float halfAngle = RandomAngle * 0.5f;
            float offset = Random.Range(-halfAngle, halfAngle);

            // �� baseTarget �� Z ����ת offset ��
            Vector3 rotated = Quaternion.Euler(0f, 0f, offset) * baseTarget;

            // ����Ŀ��㣨���������λ�ã�
            Vector2 newTargetPos = (Vector2)context.transform.position + (Vector2)rotated;

            // �·��� mover
            context.mover.TargetPosition = newTargetPos;

            return State.Success;
        }
        else
        {
            return State.Failure;
        }
    }
}
