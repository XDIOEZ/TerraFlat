using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;
using UnityEngine.AI;
[NodeMenu("ActionNode/�ж�/�ƶ�")]
public class Move : ActionNode
{
    public IAI_NavMesh navMesh;
    public float SpeedRate = 1f;
    public float max_StopTime = 1f;
    public float minDistance = 0.01f;
    [Tooltip("��ڰ崦��Ŀ���ƶ�")]
    public bool MoveToBlackboard = true;
    IMover mover;

    private Vector3 startPos;
    private float startTime;

    public IMover Mover { get => context.mover; set => context.mover = value; }

    protected override void OnStart()
    {
        
        if (Mover == null)
        {
            Mover = context.gameObject.GetComponentInChildren<IMover>();
            Debug.Log("δ�ҵ�Mover���");
        }

        if (navMesh == null)
        {
            context.gameObject.TryGetComponent<IAI_NavMesh>(out navMesh);
            if (navMesh == null)
                Debug.LogError("δ�ҵ�IAI_NavMesh���");
        }

        Mover.Speed *= SpeedRate;
        startPos = Mover.Position;
        startTime = Time.time;
    }

    protected override void OnStop()
    {
        context.mover.Speed /= SpeedRate;
    }

    protected override State OnUpdate()
    {
        Mover.TargetPosition = blackboard.TargetPosition;
        Mover.Move();

        float timeElapsed = Time.time - startTime;

        if (timeElapsed > max_StopTime)
        {
            NavMeshPathStatus pathStatus = navMesh.Agent_Nav.pathStatus;
            if (pathStatus != NavMeshPathStatus.PathComplete)
            {
                Debug.Log("·����Ч������");
                return State.Failure;
            }

            float distance = Vector3.Distance(startPos, Mover.Position);
            if (distance < minDistance)
            {
                Debug.Log("�ƶ�ͣ�ͣ�����ʧ��");
                return State.Failure;
            }
            startPos = Mover.Position;
        }

        if (!Mover.IsMoving)
        {
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
            Gizmos.DrawWireSphere(Mover.TargetPosition, 0.3f); // �ɵ����뾶
        }
    }
}
