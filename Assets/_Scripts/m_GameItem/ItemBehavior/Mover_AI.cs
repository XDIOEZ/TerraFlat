using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UltEvents;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using DG.Tweening;

public class Mover_AI : MonoBehaviour, IMover, IAI_NavMesh
{
    [Header("移动目标")]
    public Vector3 targetPosition;
    public Transform target;

    [Header("组件引用")]
    public NavMeshAgent agent;
    public Rigidbody2D rb2D;

    [Header("状态控制")]
    public bool isMoving;
    private Tweener moveTween;

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
        get => targetPosition;
        set
        {
            targetPosition = value;
            if (agent.isOnNavMesh)
            {
                agent.SetDestination(value);
                if (agent.pathStatus == NavMeshPathStatus.PathPartial)
                {
                    Debug.Log("移动状态异常");
                }
            }
        }
    }

    public bool IsMoving
    {
        get => isMoving;
        set => isMoving = value;
    }

    public float Speed
    {
        get => agent.speed;
        set => agent.speed = value;
    }

    public NavMeshAgent Agent_Nav
    {
        get => agent;
        set => agent = value;
    }

    #endregion

    private void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.updateUpAxis = false;
        agent.updateRotation = false;

        rb2D = GetComponent<Rigidbody2D>();
    }

    [Button("检查目标是否可以到达")]
    public void CheckTargetCanArrive()
    {
        if (agent.pathStatus == NavMeshPathStatus.PathPartial)
        {
            Debug.Log("移动状态异常");
        }
        else
        {
            Debug.Log("移动状态正常");
        }
    }

    public void Move()
    {
        if (Vector2.Distance(transform.position, TargetPosition) <= agent.stoppingDistance)
        {
            IsMoving = false;
            OnStopMoving?.Invoke();
            moveTween?.Kill();
            rb2D.velocity = Vector2.zero;
            return;
        }

        if (target != null)
        {
            TargetPosition = target.position;
        }

        if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
        {
            agent.SetDestination(TargetPosition);
        }
        else
        {
            MoveWithDoTween();
        }

        if (!IsMoving)
        {
            OnStartMoving?.Invoke();
            IsMoving = true;
        }
    }

    public void Stop()
    {
        if (agent != null && agent.isActiveAndEnabled)
        {
            agent.isStopped = true;
        }
    }

    private void MoveWithDoTween()
    {
        if (rb2D == null)
        {
            rb2D = GetComponent<Rigidbody2D>();
            if (rb2D == null)
            {
                Debug.LogError("没有找到 Rigidbody2D 组件！");
                return;
            }
        }

        Vector2 direction = (TargetPosition - transform.position).normalized;
        rb2D.velocity = direction * Speed;
    }
}
