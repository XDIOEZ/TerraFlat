using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

/// <summary>
/// ��Ϊ�������ڵ㣺���ƽ�ɫ�ƶ���ָ��Ŀ��λ��
/// </summary>
public class MoveToPosition : ActionNode
{
    // �ƶ��ٶȣ���λ����λ/�룩
    public float speed = 5;

    // ֹͣ������ֵ�����ӽ�Ŀ��ʱ��ǰֹͣ
    public float stoppingDistance = 0.1f;

    // �Ƿ����ƶ������и��½�ɫ����
    public bool updateRotation = true;

    // ���ٶȲ����������ƶ�ʱ�ļ���Ч��
    public float acceleration = 40.0f;

    // �������Χ����ʣ�����С�ڸ�ֵʱ��Ϊ����Ŀ��
    public float tolerance = 1.0f;

    /// <summary>
    /// �ڵ㿪ʼִ��ʱ��ʼ����������
    /// </summary>
    protected override void OnStart()
    {
        // ���õ��������ֹͣ����
        context.agent.stoppingDistance = stoppingDistance;
        // �����ƶ��ٶ�
        context.agent.speed = speed;
        // �Ӻڰ��ȡĿ��λ�ò�����Ϊ����Ŀ�ĵ�
        context.agent.destination = blackboard.TargetPosition;
        // �����Ƿ������ת
        context.agent.updateRotation = updateRotation;
        // ���ü��ٶȲ���
        context.agent.acceleration = acceleration;
    }

    /// <summary>
    /// �ڵ�ֹͣʱ�������������ǰ�޲�����
    /// </summary>
    protected override void OnStop()
    {
        // ����ִ���κ��������
    }

    /// <summary>
    /// ÿ֡����ʱ����ƶ�״̬�����ؽڵ�״̬
    /// </summary>
    /// <returns>��Ϊ���ڵ�״̬��Success/Running/Failed��</returns>
    protected override State OnUpdate()
    {
        // ���·�����ڼ����У���������״̬
        if (context.agent.pathPending)
        {
            return State.Running;
        }

        // ���ʣ�����С����Χ����Ϊ����Ŀ��
        if (context.agent.remainingDistance < tolerance)
        {
            return State.Success;
        }

        // ���·����Ч����Ŀ�겻�ɴ������ʧ��
        if (context.agent.pathStatus == UnityEngine.AI.NavMeshPathStatus.PathInvalid)
        {
            return State.Failure;
        }

        // Ĭ�ϼ�������ֱ����������
        return State.Running;
    }
}