using UnityEngine;
using TheKiwiCoder;
using UnityEngine.AI;

[NodeMenu("ActionNode/�ж�/�ƶ�")]
public class Move : ActionNode
{

    #region �ֶ�

    private Vector2 lastPosition;     // ��һ��λ��

    private float lastMoveTime = 0f;  // ��һ���ƶ�ʱ���
    private const float STUCK_THRESHOLD = 0.5f;   // �ж���ס��ʱ����ֵ
    private const float MIN_MOVE_DISTANCE = 0.1f; // ��Ϊ�ƶ�����С����



    #endregion

    #region ��������

    protected override void OnStart()
    {
        context.mover.IsRunning = true;
        context.mover.HasReachedTarget = false;
    }

    protected override void OnStop()
    {
        // ֹͣʱ������⴦������
        context.mover.IsRunning = false;
    }

    #endregion

    #region ��Ϊ����

    protected override State OnUpdate()
    {
        if(context.mover.HasReachedTarget == true)
        {
            return State.Success;
        }

        return State.Running;
    }

    #endregion

    #region ˽�з���

    /// <summary>�Զ���ת����������ס·��</summary>
    private void HandleAutoRotate(Vector2 currentPosition)
    {
        if (context.mover.IsLock)
        {
            // ԭʼ����
            Vector2 originalDir = (context.mover.TargetPosition - currentPosition).normalized;

            // ��� ��90~180 ��ƫת
            float angleOffset = Random.Range(90f, 180f);
            angleOffset = Random.value < 0.5f ? angleOffset : -angleOffset;

            Vector2 newDir = RotateVector2(originalDir, angleOffset);
            float runDistance = (context.mover.TargetPosition - currentPosition).magnitude;

            // ������Ŀ��λ��
            context.mover.TargetPosition = currentPosition + newDir * runDistance;
        }

        context.mover.IsLock = true;

        // ��¼��ֹ���򣬱����ظ�����
        if (context.mover.MemoryPath_Forbidden.Count < 3)
        {
            context.mover.MemoryPath_Forbidden.Add(lastPosition);
        }

        context.agent.SetDestination(context.mover.TargetPosition);
    }

    /// <summary>��ת2D����</summary>
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

    /// <summary>�����ƶ�״̬</summary>
    private void ResetMoveState()
    {
        lastPosition = Vector3.zero;
        lastMoveTime = 0f;
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
