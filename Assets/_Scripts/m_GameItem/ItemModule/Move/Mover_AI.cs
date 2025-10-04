using UnityEngine;
using DG.Tweening;
using Sirenix.OdinInspector;
using Pathfinding;

/// <summary>
/// ʹ�� NavMesh + Rigidbody2D ���Ƶ� AI �ƶ���
/// </summary>
public class Mover_AI : Mover
{
    #region �ֶ�

    [Title("AI ��ز���")]


    [Header("�ƶ�Ŀ��")]
    public Transform target; // ��ѡĿ������

    [Header("ֹͣ����")]
    public float stopDistance = 0.5f;

    [Header("��������")]
    private Tweener moveTween;

    [Header("״̬����")]
    public bool CanMove = true; // �Ƿ�����ƶ�
    public bool HasReachedTarget = false; // �Ƿ񵽴�Ŀ��

    [Header("��������")]
   public IAstarAI aiPath; // AI ·�����


    #endregion

    #region ����

    public float SpeedValue => Speed.Value;

    #endregion

    #region ��������

    public override void Load()
    {
        base.Load();
        aiPath = GetComponentInParent<IAstarAI>();
    }

    #endregion

    #region �ƶ��߼�

    public override void ModUpdate(float deltaTime)
    {
      
        if(CanMove == false)
        {
            return;
        }

        // ����Ƿ��Ѿ�����Ŀ�꣬���δ������ִ���ƶ�
        if (!HasReachedTarget)
        {
            if (target == null)
            {
                // ���� Move ʵ���ƶ�
                Move(TargetPosition, deltaTime);
            }
           
            if(target != null)
            {
                Move(target.position, deltaTime);
            }
        }

        // �����Ƿ񵽴�Ŀ���״̬
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