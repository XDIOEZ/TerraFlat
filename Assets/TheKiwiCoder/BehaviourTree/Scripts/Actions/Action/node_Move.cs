using System.Collections;
using UnityEngine;
using TheKiwiCoder;
using UnityEngine.AI;

[NodeMenu("ActionNode/行动/移动")]
public class Move : ActionNode
{

    #region 组件和接口引用

    private Mover speeder;



    #endregion


    // 在类中添加这些字段
    public Vector2 lastPosition;
    public float lastMoveTime = 0f;
    public const float STUCK_THRESHOLD = 0.5f; // 2秒不移动视为卡住
    public const float MIN_MOVE_DISTANCE = 0.1f;
    public bool AutoRotate = true;

    #region 属性封装

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

                if (AutoRotate)
                {
                    Vector3 dir = (context.agent.destination - context.transform.position).normalized;

                    // 随机选择顺时针或逆时针旋转 90 度
                    float angle = Random.value > 0.5f ? 120f : -120f;
                    Vector3 rotatedDir = Quaternion.Euler(0, 0, angle) * dir;

                    // 设定新目标点（距离可以用之前的距离或固定距离）
                    float dist = Vector3.Distance(context.transform.position, context.agent.destination);
                    Vector3 newTarget = context.transform.position + rotatedDir * dist;

                    //context.agent.SetDestination(newTarget);
                    speeder.TargetPosition = newTarget;

                    if (base.DebugMODE)
                    {
                        Debug.Log($"[AutoRotate] AI卡住，目标点已偏转{angle}°，新目标: {newTarget}");
                    }

                    // 重置计时器（不直接失败，尝试新方向）
                    lastPosition = context.transform.position;
                    lastMoveTime = Time.time;

                    return State.Running; // 继续尝试移动
                }

                // 重置并返回失败
                lastPosition = Vector3.zero;
                lastMoveTime = 0f;
                context.agent.isStopped = true;
                return State.Failure;
            }
        }

        Mover.Move(speeder.TargetPosition);

        // 检查是否到达目标
        if (Vector2.Distance(Mover.TargetPosition, currentPosition) <= context.agent.stoppingDistance)
        {
            //if (base.DebugMODE)
            //{
            //    Debug.Log("Arrived");
            //}

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
