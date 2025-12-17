using UnityEngine;
using TheKiwiCoder;
using UnityEngine.AI;

[NodeMenu("ActionNode/行动/移动")]
public class Move : ActionNode
{

    #region 字段

    private Vector2 lastPosition;     // 上一次位置
    public bool IsRunState = false;




    #endregion

    #region 生命周期
    protected override void OnInit()
    {
        context.OnTreeStop += () => context.mover.SetRunState(false);
    }

    protected override void OnStart()
    {
        context.mover.IsMoving = true;
        context.mover.HasReachedTarget = false;
        if (IsRunState)
        {
            context.mover.SetRunState(true);
        }
    }

    protected override void OnStop()
    {
        // 停止时无需额外处理，留空
        context.mover.IsMoving = false;
        context.mover.aiPath.isStopped = true;
        
                if (IsRunState)
        {
            context.mover.SetRunState(false);
        }
    }

    #endregion

    #region 行为更新

    protected override State OnUpdate()
    {
        if(context.mover.HasReachedTarget == true)
        {
            return State.Success;
        }

        return State.Running;
    }

    #endregion

    #region 私有方法

    /// <summary>自动旋转尝试修正卡住路径</summary>
    private void HandleAutoRotate(Vector2 currentPosition)
    {
        if (context.mover.IsLock)
        {
            // 原始方向
            Vector2 originalDir = (context.mover.TargetPosition - currentPosition).normalized;

            // 随机 ±90~180 度偏转
            float angleOffset = Random.Range(90f, 180f);
            angleOffset = Random.value < 0.5f ? angleOffset : -angleOffset;

            Vector2 newDir = RotateVector2(originalDir, angleOffset);
            float runDistance = (context.mover.TargetPosition - currentPosition).magnitude;

            // 更新新目标位置
            context.mover.TargetPosition = currentPosition + newDir * runDistance;
        }

        context.mover.IsLock = true;

        // 记录禁止区域，避免重复尝试
        if (context.mover.MemoryPath_Forbidden.Count < 3)
        {
            context.mover.MemoryPath_Forbidden.Add(lastPosition);
        }

        context.agent.SetDestination(context.mover.TargetPosition);
    }

    /// <summary>旋转2D向量</summary>
    private Vector2 RotateVector2(Vector2 vector, float angleDegrees)
    {
        float rad = angleDegrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);

        return new Vector2(
            vector.x * cos - vector.y * sin,
            vector.x * sin + vector.y * cos
        );
    }

    #endregion

    #region Gizmos

    public override void OnDrawGizmos()
    {  
        base.OnDrawGizmos();
        if (context.mover != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(context.mover.TargetPosition, 0.2f);
        }
    }

    #endregion
}
