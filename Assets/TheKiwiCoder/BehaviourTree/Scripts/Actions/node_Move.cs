using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;
using UnityEngine.AI;

public class Move : ActionNode
{
    public IAI_NavMesh navMesh;
    public float SpeedRate = 1f;
    public float max_StopTime = 1f;
    public float minDistance = 0.01f;
    [Tooltip("向黑板处的目标移动")]
    public bool MoveToBlackboard = true;

    private Vector3 startPos;
    private float startTime;

    protected override void OnStart()
    {
        if (context.mover == null)
        {
            Debug.LogError("未找到Mover组件");
        }

        if (navMesh == null)
        {
            context.gameObject.TryGetComponent<IAI_NavMesh>(out navMesh);
            if (navMesh == null)
                Debug.LogError("未找到IAI_NavMesh组件");
        }

        context.mover.Speed *= SpeedRate;
        startPos = context.mover.Position;
        startTime = Time.time;
    }

    protected override void OnStop()
    {
        context.mover.Speed /= SpeedRate;
    }

    protected override State OnUpdate()
    {
        context.mover.TargetPosition = blackboard.TargetPosition;
        context.mover.Move();

        float timeElapsed = Time.time - startTime;

        if (timeElapsed > max_StopTime)
        {
            NavMeshPathStatus pathStatus = navMesh.Agent_Nav.pathStatus;
            if (pathStatus != NavMeshPathStatus.PathComplete)
            {
                Debug.Log("路径无效或不完整");
                return State.Failure;
            }

            float distance = Vector3.Distance(startPos, context.mover.Position);
            if (distance < minDistance)
            {
                Debug.Log("移动停滞，返回失败");
                return State.Failure;
            }
            startPos = context.mover.Position;
        }

        if (!context.mover.IsMoving)
        {
            return State.Success;
        }

        return State.Running;
    }

    public override void OnDrawGizmos()
    {
        base.OnDrawGizmos();

        if (context?.mover != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(context.mover.TargetPosition, 0.3f); // 可调整半径
        }
    }
}
