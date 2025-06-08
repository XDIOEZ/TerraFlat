using System.Collections;
using UnityEngine;
using TheKiwiCoder;
using UnityEngine.AI;

[NodeMenu("ActionNode/�ж�/�ƶ�")]
public class Move : ActionNode
{

    #region ����ͽӿ�����

    private ISpeed speeder;



    #endregion


    // �����������Щ�ֶ�
    public Vector3 lastPosition;
    public float lastMoveTime = 0f;
    public const float STUCK_THRESHOLD = 0.2f; // 2�벻�ƶ���Ϊ��ס
    public const float MIN_MOVE_DISTANCE = 0.1f;

    #region ���Է�װ

    public IMover Mover
    {
        get => context.mover;
        set => context.mover = value;
    }

    #endregion

    protected override void OnStart()
    {
        speeder ??= context.gameObject.GetComponent<ISpeed>();
    }

    protected override void OnStop()
    {
      
    }

    protected override State OnUpdate()
    {
        context.agent.isStopped = false;
        Vector3 currentPosition = context.agent.transform.position;

        // ��ʼ��ʱ���
        if (lastMoveTime == 0f)
        {
            lastMoveTime = Time.time;
            lastPosition = currentPosition;
        }

        // ����Ƿ��ƶ�
        if (Vector2.Distance(currentPosition, lastPosition) >= MIN_MOVE_DISTANCE)
        {
            // ���ƶ�������ʱ�����λ��
            lastMoveTime = Time.time;
            lastPosition = currentPosition;
        }
        else
        {
            // û���ƶ�������Ƿ�סʱ�����
            if (Time.time - lastMoveTime >= STUCK_THRESHOLD)
            {
                if (base.DebugMODE)
                {
                    Debug.LogWarning("AI��ס��Ŀ��λ���޷�����");
                }

                // ���ò�����ʧ��
                lastPosition = Vector3.zero;
                lastMoveTime = 0f;
                context.agent.isStopped = true;
                return State.Failure;
            }
        }

        Mover.Move(speeder.MoveTargetPosition);

        // ����Ƿ񵽴�Ŀ��
        if (Vector2.Distance(Mover.TargetPosition, currentPosition) <= context.agent.stoppingDistance)
        {
            if (base.DebugMODE)
            {
                Debug.Log("Arrived");
            }

            // ����״̬
            lastPosition = Vector3.zero;
            lastMoveTime = 0f;
            context.agent.isStopped = true;
            return State.Success;
        }

        return State.Running;
    }

    public override void OnDrawGizmos()
    {
        base.OnDrawGizmos();
        if (Mover != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(Mover.TargetPosition, 0.2f);
        }
    }
}
