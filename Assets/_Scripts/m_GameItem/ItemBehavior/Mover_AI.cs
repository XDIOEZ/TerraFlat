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
/// ʹ�� NavMesh �� Rigidbody2D ���Ƶ� AI �ƶ���
/// </summary>
public class Mover_AI : Organ, IMover, IAI_NavMesh
{
    [Header("�ƶ�Ŀ��")]
    public Vector3 targetPosition; // Ŀ��λ��
    public Transform target;       // Ŀ�����壨��ѡ��

    [Header("�������")]
    public NavMeshAgent agent;     // NavMesh ����
    public Rigidbody2D rb2D;       // 2D ����

    [Tooltip("�ٶȿ��ƽӿڣ������������ṩ��")]
    public ISpeed speeder;

    [Header("״̬����")]
    public bool isMoving;          // ��ǰ�Ƿ����ƶ�

    [Header("��������")]
    private Tweener moveTween;     // ���� DOTween �ƶ�

    // �ƶ��¼�
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
    /// ��ʼ���������
    /// </summary>
    public void Start()
    {
        agent = GetComponentInParent<NavMeshAgent>();
        agent.updateUpAxis = false;      // ���� Y ����£�������2D��
        agent.updateRotation = false;    // �����Զ���ת

        rb2D = GetComponentInParent<Rigidbody2D>();
        speeder = GetComponentInParent<ISpeed>();
    }

    /// <summary>
    /// ���ƶ��߼���ڣ������Ƿ���NavMeshAgentִ�в�ͬ�ƶ���ʽ
    /// </summary>
    /// <param name="targetPosition">Ŀ��λ��</param>
    public void Move(Vector2 targetPosition)
    {
        agent.speed = speeder.Speed;
        if (Vector2.Distance(transform.position, TargetPosition) <= agent.stoppingDistance)
        {
            // �ѵִ�Ŀ��
            IsMoving = false;
            OnStopMoving?.Invoke();
            moveTween?.Kill();
            rb2D.velocity = Vector2.zero;
            return;
        }

        // ʹ�� NavMeshAgent �ƶ�
        if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
        {
            agent.SetDestination(targetPosition);
        }
        else
        {
            // ʹ�� DoTween ���������ƶ������÷�����
            MoveWithDoTween(targetPosition);
        }

        // ������ʼ�ƶ��¼�
        if (!IsMoving)
        {
            OnStartMoving?.Invoke();
            IsMoving = true;
        }
    }

    /// <summary>
    /// ֹͣ�ƶ�
    /// </summary>
    public void Stop()
    {
          agent.isStopped = true;
          IsMoving = false;
          OnStopMoving?.Invoke();
          moveTween?.Kill();
    }

    /// <summary>
    /// ʹ���ٶ�ʸ�����м����ƶ������÷�ʽ��
    /// </summary>
    private void MoveWithDoTween(Vector2 targetPosition)
    {
        rb2D.velocity = (targetPosition - (Vector2)transform.position).normalized * Speed;
    }

    #region Organ ������ʵ��

    public override void StartWork()
    {
        throw new System.NotImplementedException(); // ��ʵ���߼�
    }

    public override void UpdateWork()
    {
        throw new System.NotImplementedException(); // ��ʵ���߼�
    }

    public override void StopWork()
    {
        throw new System.NotImplementedException(); // ��ʵ���߼�
    }

    #endregion
}
