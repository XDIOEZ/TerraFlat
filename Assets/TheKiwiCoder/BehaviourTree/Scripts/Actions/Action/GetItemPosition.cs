using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

[NodeMenu("ActionNode/�Ѳ�/����ItemType�趨ΪĿ��")]
public class GetItemPosition : ActionNode
{
    #region ö�ٶ���
    public enum MovementBehaviorType
    {
        ׷��,  // ׷��
        ����    // ����
    }
    #endregion

    #region ���л��ֶ�
    [Header("��Ʒ��������")]
    [Tooltip("Ҫ��������Ʒ�����б�����ƥ�䣩")]
    public List<string> ItemType = new List<string>();

    [Header("��Ϊ����")]
    [Tooltip("ѡ����Ϊ���ͣ�׷��������")]
    public MovementBehaviorType BehaviorType = MovementBehaviorType.׷��;

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

    public Mover mover =>context.mover;
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
        // ����Ŀ����Ʒ
        Item targetItem = FindTargetItem();
        if (targetItem == null)
        {
            return State.Failure;
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
    /// ���ҷ���������Ŀ����Ʒ
    /// </summary>
    private Item FindTargetItem()
    {
        foreach (Item item in context.itemDetector.CurrentItemsInArea)
        {
            if (item?.itemData?.ItemTags?.Item_TypeTag == null)
                continue;

            // �����Ʒ�����Ƿ�ƥ��
            if (ItemType.Exists(type => item.itemData.ItemTags.Item_TypeTag.Contains(type)))
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>
    /// ������Ϊ���ʹ����ƶ��߼�
    /// </summary>
    private void ProcessMovementBehavior(Item targetItem)
    {
        Vector2 targetPosition = targetItem.transform.position;

        switch (BehaviorType)
        {
            case MovementBehaviorType.׷��:
                ProcessChaseMovement(targetPosition);
                break;

            case MovementBehaviorType.����:
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
        mover.TargetPosition = targetPosition;
        context.mover.TargetPosition = targetPosition;
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

        escapePoint = GetUnlockedTargetPosition(escapePoint);

        //TODO MemoryPath_Forbidden��һ���洢�˽�ֹ�ƶ���λ ���б�
        //TODO �ڴ���escapePoint����λ��ʱ ���ݽ�ֹ�ƶ���λ���ƫ�� ���������ֹ��λ
        //TODO ͨ����escapePoint������� ʵ�ַ����ƫ��
        blackboard.TargetPosition = escapePoint;
        mover.TargetPosition = escapePoint;
       // Debug.Log($"[{GetType().Name}] ��������Ŀ��λ��: {escapePoint}");
    }

    public Vector2 GetUnlockedTargetPosition(Vector2 targetPosition)
    {
        Vector2 currentPosition = context.transform.position;

        // ��� 1����ǰ����������ǿ��ƫת
        if (context.mover.IsLock)
        {
            Vector2 originalDir = (targetPosition - currentPosition).normalized;

            // ��90~180��ƫת
            float angleOffset = Random.Range(90f, 180f);
            angleOffset = Random.value < 0.5f ? angleOffset : -angleOffset;

            Vector2 newDir = RotateVector2(originalDir, angleOffset);
            float runDistance = (targetPosition - currentPosition).magnitude;

            return currentPosition + newDir * runDistance;
        }

        // ��� 2���������룬���ܿ�Σ�����򣨳���ȫ�����ӣ�
        Vector2 escapeDir = (targetPosition - currentPosition).normalized;

        Vector2 dangerDir = Vector2.zero;
        foreach (var danger in context.mover.MemoryPath_Forbidden)
        {
            dangerDir += (danger - currentPosition).normalized;
        }

        if (context.mover.MemoryPath_Forbidden.Count > 0)
        {
            dangerDir /= context.mover.MemoryPath_Forbidden.Count;
            context.mover.MemoryPath_Forbidden.RemoveAt(0); // ��̭�����
            // �������뷽�򣺱ܿ�Σ�շ���
            escapeDir = (escapeDir - dangerDir).normalized;
        }

        float finalDistance = (targetPosition - currentPosition).magnitude;

        // ��ѡ�����һ���Ƕ��Ŷ�
        float angleNoise = Random.Range(-angleVariance, angleVariance);
        escapeDir = RotateVector2(escapeDir, angleNoise);

        return currentPosition + escapeDir * finalDistance;
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