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
    [Header("�ƶ�Ŀ��")]
    public Vector3 targetPosition;
    public Transform target;

    [Header("�������")]
    public NavMeshAgent agent;
    public Rigidbody2D rb2D;

    [Header("״̬����")]
    public bool isMoving;
    private Tweener moveTween;

    public UltEvent OnStartMoving { get; set; }
    public UltEvent OnStopMoving { get; set; }

    #region �ӿ�����ʵ��

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
                    Debug.Log("�ƶ�״̬�쳣");
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

    [Button("���Ŀ���Ƿ���Ե���")]
    public void CheckTargetCanArrive()
    {
        if (agent.pathStatus == NavMeshPathStatus.PathPartial)
        {
            Debug.Log("�ƶ�״̬�쳣");
        }
        else
        {
            Debug.Log("�ƶ�״̬����");
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
                Debug.LogError("û���ҵ� Rigidbody2D �����");
                return;
            }
        }

        Vector2 direction = (TargetPosition - transform.position).normalized;
        rb2D.velocity = direction * Speed;
    }
}
