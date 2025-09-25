using NaughtyAttributes;
using TMPro;
using UltEvents;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using DG.Tweening;
using Sirenix.OdinInspector;
using Pathfinding;

/// <summary>
/// 使用 NavMesh + Rigidbody2D 控制的 AI 移动类
/// </summary>
public class Mover_AI : Mover
{
    #region 字段

    [Title("AI 相关参数")]


    [Header("移动目标")]
    public Transform target; // 可选目标物体

    [Header("停止距离")]
    public float stopDistance = 0.1f;

    [Header("动画控制")]
    private Tweener moveTween;

    [Header("状态控制")]
    public bool CanMove = true; // 是否可以移动
    public bool HasReachedTarget = false; // 是否到达目标

    [Header("对象引用")]
   public IAstarAI aiPath; // AI 路径组件


    #endregion

    #region 属性

    public float SpeedValue => Speed.Value;

    #endregion

    #region 生命周期

    public override void Load()
    {
        base.Load();
        aiPath = GetComponentInParent<IAstarAI>();
    }

    #endregion

    #region 移动逻辑

    public override void ModUpdate(float deltaTime)
    {
      
        if(CanMove == false)
        {
            return;
        }

        if (target == null)
        {
            // 调用 Move 实现移动
            Move(TargetPosition, deltaTime);
        }
       
        if(target != null)
        {
            Move(target.position, deltaTime);
        }

        if (aiPath.remainingDistance <= stopDistance)
        {
            HasReachedTarget = true;
        }
        else
        {
            HasReachedTarget = false;
        }

    }

    public override void Move(Vector2 targetPosition, float deltaTime = 0.0f)
    {
        aiPath.maxSpeed = SpeedValue;
        aiPath.destination = targetPosition;
    }

    #endregion
}
