using System.Collections;
using UnityEngine;
using TheKiwiCoder;
using UnityEngine.AI;
using UnityEditor.Rendering.LookDev;
using TMPro;

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
        speeder ??= context.mover;
    }

    protected override void OnStop()
    {
      
    }

    protected override State OnUpdate()
    {
        Vector2 currentPosition = context.agent.transform.position;
        if (context.agent.isOnNavMesh)
        {
            context.agent.isStopped = false;
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
                // ����
                context.mover.IsLock = false;
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
                        if (context.mover.IsLock)
                        {
                            Vector2 originalDir = (context.mover.TargetPosition - currentPosition).normalized;

                            // ��90~180��ƫת
                            float angleOffset = Random.Range(90f, 180f);
                            angleOffset = Random.value < 0.5f ? angleOffset : -angleOffset;

                            Vector2 newDir = RotateVector2(originalDir, angleOffset);
                            float runDistance = (context.mover.TargetPosition - currentPosition).magnitude;

                            context.mover.TargetPosition = currentPosition + newDir * runDistance;


                        }
                        context.mover.IsLock = true;

                        if (context.mover.MemoryPath_Forbidden.Count < 3)
                            context.mover.MemoryPath_Forbidden.Add(lastPosition);
                        context.agent.SetDestination(context.mover.TargetPosition);
                        return State.Running; // ���������ƶ�
                    }

                    // ���ò�����ʧ��
                    lastPosition = Vector3.zero;
                    lastMoveTime = 0f;
                    context.agent.isStopped = true;
                    return State.Failure;
                }
            }
        }
      

        Mover.Move(speeder.TargetPosition,0);

        // ����Ƿ񵽴�Ŀ��
        if (Vector2.Distance(Mover.TargetPosition, currentPosition) <= context.agent.stoppingDistance)
        {
            // ����״̬
            lastPosition = Vector3.zero;
            lastMoveTime = 0f;
            if (context.agent.isOnNavMesh)
                context.agent.isStopped = true;
            return State.Success;
        }

        return State.Running;
    }
    /// <summary>
    /// ��ת2D����
    /// </summary>
    private Vector2 RotateVector2(Vector2 vector, float angleDegrees)
    {
        float angleRadians = angleDegrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(angleRadians);
        float sin = Mathf.Sin(angleRadians);

        return new Vector2(
            vector.x * cos - vector.y * sin,
            vector.x * sin + vector.y * cos
        );
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
