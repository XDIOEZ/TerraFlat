using System.Collections;
using UnityEngine;
using TheKiwiCoder;
using UnityEngine.AI;

[NodeMenu("ActionNode/行动/移动")]
public class Move : ActionNode
{

    #region 组件和接口引用

    private ISpeed speeder;



    #endregion


    // 在类中添加这些字段
    public Vector3 lastPosition;
    public float lastMoveTime = 0f;
    public const float STUCK_THRESHOLD = 0.2f; // 2秒不移动视为卡住
    public const float MIN_MOVE_DISTANCE = 0.1f;

    #region 属性封装

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

        // 初始化时间戳
        if (lastMoveTime == 0f)
        {
            lastMoveTime = Time.time;
            lastPosition = currentPosition;
        }

        // 检测是否移动
        if (Vector2.Distance(currentPosition, lastPosition) >= MIN_MOVE_DISTANCE)
        {
            // 有移动，更新时间戳和位置
            lastMoveTime = Time.time;
            lastPosition = currentPosition;
        }
        else
        {
            // 没有移动，检查是否卡住时间过长
            if (Time.time - lastMoveTime >= STUCK_THRESHOLD)
            {
                if (base.DebugMODE)
                {
                    Debug.LogWarning("AI卡住，目标位置无法到达");
                }

                // 重置并返回失败
                lastPosition = Vector3.zero;
                lastMoveTime = 0f;
                context.agent.isStopped = true;
                return State.Failure;
            }
        }

        Mover.Move(speeder.MoveTargetPosition);

        // 检查是否到达目标
        if (Vector2.Distance(Mover.TargetPosition, currentPosition) <= context.agent.stoppingDistance)
        {
            if (base.DebugMODE)
            {
                Debug.Log("Arrived");
            }

            // 重置状态
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
