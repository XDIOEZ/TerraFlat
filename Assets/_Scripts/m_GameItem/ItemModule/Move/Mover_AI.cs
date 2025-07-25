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
public class Mover_AI : Mover,IAI_NavMesh
{
    [Header("�ƶ�Ŀ��")]
    public Transform target;       // Ŀ�����壨��ѡ��

    [Header("�������")]
    public NavMeshAgent agent;     // NavMesh ����

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


    public float SpeedValue { get => Speed.Value;}

    public NavMeshAgent Agent_Nav
    {
        get => agent;
        set => agent = value;
    }

    #endregion

    /// <summary>
    /// ��ʼ���������
    /// </summary>
    public override void Load()
    {
        base.Load();
        agent = GetComponentInParent<NavMeshAgent>();
        agent.updateUpAxis = false;      // ���� Y ����£�������2D��
        agent.updateRotation = false;    // �����Զ���ת
    }

    /// <summary>
    /// ���ƶ��߼���ڣ������Ƿ���NavMeshAgentִ�в�ͬ�ƶ���ʽ
    /// </summary>
    /// <param name="targetPosition">Ŀ��λ��</param>
    public override void Move(Vector2 targetPosition)
    {
        agent.speed = SpeedValue;
        if (Vector2.Distance(transform.position, TargetPosition) <= agent.stoppingDistance)
        {
            // �ѵִ�Ŀ��
            IsMoving = false;
            OnStopMoving?.Invoke();
            moveTween?.Kill();
            Rb.velocity = Vector2.zero;
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
        Rb.velocity = (targetPosition - (Vector2)transform.position).normalized * SpeedValue;
    }
}
