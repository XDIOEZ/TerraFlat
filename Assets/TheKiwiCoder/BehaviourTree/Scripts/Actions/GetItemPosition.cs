using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

public class GetItemPosition : ActionNode
{
    public enum MovementBehaviorType
    {
        Chase,
        Flee
    }

    [Tooltip("Ҫ��������Ʒ�����б�����ƥ�䣩")]
    public List<string> ItemType;

    [Tooltip("ѡ����Ϊ���ͣ�׷��������")]
    public MovementBehaviorType BehaviorType;

    [Tooltip("���ܵ���С����")]
    public float minRunDistance = 3.0f;

    [Tooltip("���ܵ�������")]
    public float maxRunDistance = 7.0f;

    [Tooltip("���ܷ���ĽǶȲ�����Χ")]
    public float angleVariance = 30f;

    protected override void OnStart()
    {
    }

    protected override void OnStop()
    {
    }

    public override void OnDrawGizmos()
    {
        base.OnDrawGizmos();
        Debug.Log("GetItemPosition OnDrawGizmos");
    }
    protected override State OnUpdate()
    {
        if (context.itemDetector == null || context.itemDetector.CurrentItemsInArea == null)
            return State.Failure;

        Item targetItem = null;
        Debug.Log("GetItemPosition OnDrawGizmos");
        foreach (Item item in context.itemDetector.CurrentItemsInArea)
        {
            if (item != null && ItemType.Exists(type => item.Item_Data.ItemTags.Item_TypeTag.Contains(type)))
            {
                targetItem = item;
                break;
            }
        }

        if (targetItem == null)
            return State.Failure;

        // ����Ʒλ��ת��ΪVector2����ʹ��X��Y��
        Vector2 targetPosition = targetItem.transform.position;

        switch (BehaviorType)
        {
            case MovementBehaviorType.Chase:
                // ֱ�ӽ���Ʒλ����ΪĿ���
                context.mover.TargetPosition = targetPosition;
                break;

            case MovementBehaviorType.Flee:
                // ��ȡ��ǰ��ɫ��Vector2λ��
                Vector2 currentPosition = context.transform.position;
                // �������뷽�򣨴���Ʒָ���ɫ�ķ�����
                Vector2 directionAway = (currentPosition - targetPosition).normalized;

                // �������Ƕ�ƫ�ƣ���Z����ת��
                float angleOffset = Random.Range(-angleVariance, angleVariance);
                Quaternion rotation = Quaternion.Euler(0, 0, angleOffset);
                Vector2 rotatedDirection = (Vector2)(rotation * (Vector3)directionAway);

                // ���������
                float runDistance = Random.Range(minRunDistance, maxRunDistance);
                Vector2 escapePoint = currentPosition + rotatedDirection * runDistance;

                // �����ƶ�Ŀ��Ϊ�����
                context.mover.TargetPosition = escapePoint;
                break;

        }

        return State.Success;
    }
}