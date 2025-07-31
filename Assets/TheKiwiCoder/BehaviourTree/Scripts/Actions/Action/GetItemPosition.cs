using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

[NodeMenu("ActionNode/�Ѳ�/����ItemType�趨ΪĿ��")]
public class GetItemPosition : ActionNode
{
    #region ö�ٶ���
    public enum MovementBehaviorType
    {
        Chase,  // ׷��
        Flee    // ����
    }
    #endregion

    #region ���л��ֶ�
    [Header("��Ʒ��������")]
    [Tooltip("Ҫ��������Ʒ�����б�����ƥ�䣩")]
    public List<string> ItemType = new List<string>();

    [Header("��Ϊ����")]
    [Tooltip("ѡ����Ϊ���ͣ�׷��������")]
    public MovementBehaviorType BehaviorType = MovementBehaviorType.Chase;

    [Header("������Ϊ����")]
    [Tooltip("���ܵ���С����")]
    [Range(1f, 10f)]
    public float minRunDistance = 3.0f;

    [Tooltip("���ܵ�������")]
    [Range(1f, 20f)]
    public float maxRunDistance = 7.0f;

    [Tooltip("���ܷ���ĽǶȲ�����Χ���ȣ�")]
    [Range(0f, 180f)]
    public float angleVariance = 30f;

    [Header("�ڰ�����")]
    [Tooltip("�Ƿ����úڰ�Ŀ�����")]
    public bool setBlackboardTarget = true;

    [Tooltip("�ҵ�Ŀ���ִ���κ��ƶ�����")]
    public bool doNothing = false;

    public Mover Speeder =>context.mover;
    #endregion

    #region ��д����
    protected override void OnStart()
    {
    }

    protected override void OnStop()
    {
        // �ڵ�ֹͣʱ�������߼�
    }

    public override void OnDrawGizmos()
    {
        base.OnDrawGizmos();

        // ��Scene��ͼ�л��Ƶ�����Ϣ
        if (Application.isPlaying && context != null)
        {
            DrawDebugInfo();
        }
    }

    protected override State OnUpdate()
    {
        // ��֤��Ҫ���
        if (!ValidateComponents())
        {
            return State.Failure;
        }

        // ����Ŀ����Ʒ
        Item targetItem = FindTargetItem();
        if (targetItem == null)
        {
            HandleNoTargetFound();
            return State.Failure;
        }

        // ���úڰ�Ŀ��
        if (setBlackboardTarget)
        {
            blackboard.target = targetItem.transform;
        }

        // �������Ϊ��ִ���κβ�����ֱ�ӷ��سɹ�
        if (doNothing)
        {
            return State.Success;
        }

        // ������Ϊ���ʹ����ƶ�
        ProcessMovementBehavior(targetItem);
        return State.Success;
    }
    #endregion

    #region ˽�з���
    /// <summary>
    /// ��֤��Ҫ������Ƿ����
    /// </summary>
    private bool ValidateComponents()
    {
        if (context?.itemDetector == null)
        {
            Debug.LogWarning($"[{GetType().Name}] ItemDetector���δ�ҵ�");
            return false;
        }

        if (context.itemDetector.CurrentItemsInArea == null)
        {
            Debug.LogWarning($"[{GetType().Name}] ��ǰ������Ʒ�б�Ϊ��");
            return false;
        }

        if (ItemType == null || ItemType.Count == 0)
        {
            Debug.LogWarning($"[{GetType().Name}] δ����Ҫ��������Ʒ����");
            return false;
        }

        return true;
    }

    /// <summary>
    /// ���ҷ���������Ŀ����Ʒ
    /// </summary>
    private Item FindTargetItem()
    {
        foreach (Item item in context.itemDetector.CurrentItemsInArea)
        {
            if (item?.Item_Data?.ItemTags?.Item_TypeTag == null)
                continue;

            // �����Ʒ�����Ƿ�ƥ��
            if (ItemType.Exists(type => item.Item_Data.ItemTags.Item_TypeTag.Contains(type)))
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>
    /// ����δ�ҵ�Ŀ������
    /// </summary>
    private void HandleNoTargetFound()
    {
        if (setBlackboardTarget)
        {
            blackboard.target = null;
        }
     //   Debug.Log($"[{GetType().Name}] δ�ҵ�ƥ�����Ʒ����");
    }

    /// <summary>
    /// ������Ϊ���ʹ����ƶ��߼�
    /// </summary>
    private void ProcessMovementBehavior(Item targetItem)
    {
        Vector2 targetPosition = targetItem.transform.position;

        switch (BehaviorType)
        {
            case MovementBehaviorType.Chase:
                ProcessChaseMovement(targetPosition);
                break;

            case MovementBehaviorType.Flee:
                ProcessFleeMovement(targetPosition);
                break;

            default:
                Debug.LogError($"[{GetType().Name}] δ֪����Ϊ����: {BehaviorType}");
                break;
        }
    }

    /// <summary>
    /// ����׷���ƶ�
    /// </summary>
    private void ProcessChaseMovement(Vector2 targetPosition)
    {
        blackboard.TargetPosition = targetPosition;
        Speeder.TargetPosition = targetPosition;
      //  Debug.Log($"[{GetType().Name}] ����׷��Ŀ��λ��: {targetPosition}");
    }

    /// <summary>
    /// ���������ƶ�
    /// </summary>
    private void ProcessFleeMovement(Vector2 targetPosition)
    {
        Vector2 currentPosition = context.transform.position;
        Vector2 directionAway = (currentPosition - targetPosition).normalized;

        // �������Ƕ�ƫ��
        float angleOffset = Random.Range(-angleVariance, angleVariance);
        Vector2 rotatedDirection = RotateVector2(directionAway, angleOffset);

        // ���������
        float runDistance = Random.Range(minRunDistance, maxRunDistance);
        Vector2 escapePoint = currentPosition + rotatedDirection * runDistance;

        blackboard.TargetPosition = escapePoint;
        Speeder.TargetPosition = escapePoint;
       // Debug.Log($"[{GetType().Name}] ��������Ŀ��λ��: {escapePoint}");
    }

    /// <summary>
    /// ��ת2D����
    /// </summary>
    private Vector2 RotateVector2(Vector2 vector, float angleDegrees)
    {
        float angleRadians = angleDegrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(angleRadians);
        float sin = Mathf.Sin(angleRadians);

        return new Vector2(
            vector.x * cos - vector.y * sin,
            vector.x * sin + vector.y * cos
        );
    }

    /// <summary>
    /// ���Ƶ�����Ϣ
    /// </summary>
    private void DrawDebugInfo()
    {
        if (context?.itemDetector?.CurrentItemsInArea == null)
            return;

        // ���Ƽ�⵽����Ʒλ��
        Gizmos.color = Color.yellow;
        foreach (Item item in context.itemDetector.CurrentItemsInArea)
        {
            if (item != null)
            {
                Gizmos.DrawWireSphere(item.transform.position, 0.5f);
            }
        }
    }
    #endregion

    #region ��֤����
    private void OnValidate()
    {
        // ȷ����������ĺ�����
        if (minRunDistance > maxRunDistance)
        {
            maxRunDistance = minRunDistance;
        }

        // ȷ���Ƕȷ�Χ����
        angleVariance = Mathf.Clamp(angleVariance, 0f, 180f);
    }
    #endregion
}