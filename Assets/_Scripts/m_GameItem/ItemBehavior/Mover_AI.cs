using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UltEvents;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// 使用 NavMesh 和 Rigidbody2D 控制的 AI 移动类
/// </summary>
public class Mover_AI : Organ, IMover, IAI_NavMesh
{
    [Header("移动目标")]
    public Vector3 targetPosition; // 目标位置
    public Transform target;       // 目标物体（可选）

    [Header("组件引用")]
    public NavMeshAgent agent;     // NavMesh 代理
    public Rigidbody2D rb2D;       // 2D 刚体

    [Tooltip("速度控制接口（由宿主对象提供）")]
    public ISpeed speeder;

    [Header("状态控制")]
    public bool isMoving;          // 当前是否在移动

    [Header("动画控制")]
    private Tweener moveTween;     // 用于 DOTween 移动

    // 移动事件
    public UltEvent OnStartMoving { get; set; }
    public UltEvent OnStopMoving { get; set; }

    #region 接口属性实现

    public Vector3 Position
    {
        get => transform.position;
        set => transform.position = value;
    }

    public Vector3 TargetPosition
    {
        get => speeder.MoveTargetPosition;
        set => speeder.MoveTargetPosition = value;
    }

    public bool IsMoving
    {
        get => isMoving;
        set => isMoving = value;
    }

    public float Speed
    {
        get => speeder.Speed;
        set => speeder.Speed = value;
    }

    public NavMeshAgent Agent_Nav
    {
        get => agent;
        set => agent = value;
    }

    #endregion

    /// <summary>
    /// 初始化组件引用
    /// </summary>
    public void Start()
    {
        agent = GetComponentInParent<NavMeshAgent>();
        agent.updateUpAxis = false;      // 禁用 Y 轴更新（适用于2D）
        agent.updateRotation = false;    // 禁用自动旋转

        rb2D = GetComponentInParent<Rigidbody2D>();
        speeder = GetComponentInParent<ISpeed>();
    }

    /// <summary>
    /// 主移动逻辑入口，根据是否有NavMeshAgent执行不同移动方式
    /// </summary>
    /// <param name="targetPosition">目标位置</param>
    public void Move(Vector2 targetPosition)
    {
        agent.speed = speeder.Speed;
        if (Vector2.Distance(transform.position, TargetPosition) <= agent.stoppingDistance)
        {
            // 已抵达目标
            IsMoving = false;
            OnStopMoving?.Invoke();
            moveTween?.Kill();
            rb2D.velocity = Vector2.zero;
            return;
        }

        // 使用 NavMeshAgent 移动
        if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
        {
            agent.SetDestination(targetPosition);
        }
        else
        {
            // 使用 DoTween 或物理力移动（备用方案）
            MoveWithDoTween(targetPosition);
        }

        // 触发开始移动事件
        if (!IsMoving)
        {
            OnStartMoving?.Invoke();
            IsMoving = true;
        }
    }

    /// <summary>
    /// 停止移动
    /// </summary>
    public void Stop()
    {
          agent.isStopped = true;
          IsMoving = false;
          OnStopMoving?.Invoke();
          moveTween?.Kill();
    }

    /// <summary>
    /// 使用速度矢量进行简易移动（备用方式）
    /// </summary>
    private void MoveWithDoTween(Vector2 targetPosition)
    {
        rb2D.velocity = (targetPosition - (Vector2)transform.position).normalized * Speed;
    }

    #region Organ 抽象类实现

    public override void StartWork()
    {
        throw new System.NotImplementedException(); // 待实现逻辑
    }

    public override void UpdateWork()
    {
        throw new System.NotImplementedException(); // 待实现逻辑
    }

    public override void StopWork()
    {
        throw new System.NotImplementedException(); // 待实现逻辑
    }

    #endregion
}
