using System.Collections;
using UnityEngine;
using TheKiwiCoder;
using UnityEngine.AI;
using UnityEditor.Rendering.LookDev;
using TMPro;

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
                // 解锁
                context.mover.IsLock = false;
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
                        if (context.mover.IsLock)
                        {
                            Vector2 originalDir = (context.mover.TargetPosition - currentPosition).normalized;

                            // ±90~180度偏转
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
                        return State.Running; // 继续尝试移动
                    }

                    // 重置并返回失败
                    lastPosition = Vector3.zero;
                    lastMoveTime = 0f;
                    context.agent.isStopped = true;
                    return State.Failure;
                }
            }
        }
      

        Mover.Move(speeder.TargetPosition,0);

        // 检查是否到达目标
        if (Vector2.Distance(Mover.TargetPosition, currentPosition) <= context.agent.stoppingDistance)
        {
            // 重置状态
            lastPosition = Vector3.zero;
            lastMoveTime = 0f;
            if (context.agent.isOnNavMesh)
                context.agent.isStopped = true;
            return State.Success;
        }

        return State.Running;
    }
    /// <summary>
    /// 旋转2D向量
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
