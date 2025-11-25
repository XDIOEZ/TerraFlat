using UnityEngine;
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
    public float stopDistance = 0.5f;

    [Header("状态控制")]
    public bool CanMove = true; // 是否可以移动
    public bool HasReachedTarget = false; // 是否到达目标

    [Header("对象引用")]
    [ShowInInspector]
   public IAstarAI aiPath; // AI 路径组件


    #endregion

    #region 属性

    public float SpeedValue => Speed.Value;

    #endregion

    #region 生命周期

    public override void Load()
    {
        base.Load();
        aiPath = item.GetComponent<IAstarAI>();
        
    }
        public override void OnValidate()
    {
         _Data.ID = ModText.Mover_AI;
    }

    public void Start()
    {
        TargetPosition = transform.position;
    }

    #endregion

    #region 移动逻辑

    public override void ModUpdate(float deltaTime)
    {
      //TODO 通过监听HasReachedTarget参数执行Move行动
        if(CanMove == false)
        {
            return;
        }

        // 检查是否已经到达目标，如果未到达则执行移动
        if (!HasReachedTarget)
        {
            aiPath.isStopped = false;
            if (target == null)
            {
                // 调用 Move 实现移动
                Move(TargetPosition, deltaTime);
            }
           
            if(target != null)
            {
                Move(target.position, deltaTime);
            }
        }
        else
        {
            aiPath.isStopped = true;
        }

        // 更新是否到达目标的状态
        if (aiPath != null)
        {
            if (aiPath.remainingDistance <= stopDistance)
            {
                HasReachedTarget = true;
            }
            else
            {
                HasReachedTarget = false;
            }
        }
    }

    public override void Move(Vector2 targetPosition, float deltaTime = 0.0f)
    {
        if (aiPath != null)
        {
            aiPath.maxSpeed = SpeedValue;
            aiPath.destination = targetPosition;
        }
    }

    #endregion
}