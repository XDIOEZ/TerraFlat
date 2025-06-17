using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

/// <summary>
/// Mover 用于处理游戏对象的移动逻辑
/// </summary>
public class Mover : Organ, IMove
{
    #region 字段

    [Header("移动设置")]
    [Tooltip("速度源")]
    protected ISpeed speedSource;

    [Tooltip("减速速率")]
    public float slowDownSpeed = 5f;

    [Tooltip("停止时的最小速度，小于该值则停止移动")]
    public float endSpeed = 0.1f;

    private Vector2 _lastDirection = Vector2.zero;

    [Tooltip("是否正在移动")]
    public bool IsMoving;

    [Tooltip("移动结束事件")]
    public UltEvent OnMoveEnd;

    [Tooltip("移动持续事件")]
    public UltEvent OnMoveStay;

    [Tooltip("移动开始事件")]
    public UltEvent OnMoveStart;

    #endregion

    #region 属性

    protected Rigidbody2D Rb;

    #endregion

    #region Unity 生命周期
    public Vector3 TargetPosition
    {
        get => speedSource.MoveTargetPosition;
        set => speedSource.MoveTargetPosition = value;
    }
    public virtual void Start()
    {
        Rb = GetComponentInParent<Rigidbody2D>();
        speedSource = GetComponentInParent<ISpeed>();
    }

    #endregion

    #region 公共方法
    public virtual void Move(Vector2 TargetPosition)
    {
        bool isZero = Vector2.Distance(Rb.position, TargetPosition) < endSpeed;

        // 移动起止状态切换
        if (IsMoving != !isZero)
        {
            IsMoving = !isZero;
            if (IsMoving)
                OnMoveStart?.Invoke();
            else
                OnMoveEnd?.Invoke();
        }

        if (isZero)
        {
            Rb.velocity = Vector2.zero;
            return;
        }

        /*// 更新方向（避免重复归一化）
        if (direction != _lastDirection)
        {
            _lastDirection = direction;
        }*/

        // 计算最终速度
        Rb.velocity = (TargetPosition - Rb.position).normalized * speedSource.Speed.Value;
    }

    #endregion

    #region 私有方法

    public override void StartWork()
    {
        
    }

    public override void UpdateWork()
    {
        //Move(speedSource.MoveTargetPosition);
    }

    public override void StopWork()
    {
        throw new System.NotImplementedException();
    }

    #endregion
}

/// <summary>
/// 速度变化类型枚举：加法或乘法
/// </summary>
public enum ValueChangeType
{
    Add,
    Multiply,
}
