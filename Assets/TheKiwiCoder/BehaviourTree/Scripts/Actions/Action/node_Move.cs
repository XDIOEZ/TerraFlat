using System.Collections;
using UnityEngine;
using TheKiwiCoder;
using UnityEngine.AI;

[NodeMenu("ActionNode/�ж�/�ƶ�")]
public class Move : ActionNode
{

    #region ����ͽӿ�����

    private Mover speeder;



    #endregion


    // �����������Щ�ֶ�
    public Vector2 lastPosition;
    public float lastMoveTime = 0f;
    public const float STUCK_THRESHOLD = 0.5f; // 2�벻�ƶ���Ϊ��ס
    public const float MIN_MOVE_DISTANCE = 0.1f;
    public bool AutoRotate = true;

    #region ���Է�װ

    public Mover Mover
    {
        get => context.mover;
        set => context.mover = value;
    }

    #endregion

    protected override void OnStart()
    {
        speeder ??= context.item.Mods[ModText.Mover] as Mover;
    }

    protected override void OnStop()
    {
      
    }

    protected override State OnUpdate()
    {
        context.agent.isStopped = false;
        Vector2 currentPosition = context.agent.transform.position;

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

                if (AutoRotate)
                {
                    Vector3 dir = (context.agent.destination - context.transform.position).normalized;

                    // ���ѡ��˳ʱ�����ʱ����ת 90 ��
                    float angle = Random.value > 0.5f ? 120f : -120f;
                    Vector3 rotatedDir = Quaternion.Euler(0, 0, angle) * dir;

                    // �趨��Ŀ��㣨���������֮ǰ�ľ����̶����룩
                    float dist = Vector3.Distance(context.transform.position, context.agent.destination);
                    Vector3 newTarget = context.transform.position + rotatedDir * dist;

                    //context.agent.SetDestination(newTarget);
                    speeder.TargetPosition = newTarget;

                    if (base.DebugMODE)
                    {
                        Debug.Log($"[AutoRotate] AI��ס��Ŀ�����ƫת{angle}�㣬��Ŀ��: {newTarget}");
                    }

                    // ���ü�ʱ������ֱ��ʧ�ܣ������·���
                    lastPosition = context.transform.position;
                    lastMoveTime = Time.time;

                    return State.Running; // ���������ƶ�
                }

                // ���ò�����ʧ��
                lastPosition = Vector3.zero;
                lastMoveTime = 0f;
                context.agent.isStopped = true;
                return State.Failure;
            }
        }

        Mover.Move(speeder.TargetPosition);

        // ����Ƿ񵽴�Ŀ��
        if (Vector2.Distance(Mover.TargetPosition, currentPosition) <= context.agent.stoppingDistance)
        {
            //if (base.DebugMODE)
            //{
            //    Debug.Log("Arrived");
            //}

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
