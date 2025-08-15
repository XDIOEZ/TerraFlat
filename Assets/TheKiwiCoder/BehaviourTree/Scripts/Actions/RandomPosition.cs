using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

public class RandomPosition : ActionNode
{
    public Vector2 min = Vector2.one * -10;
    public Vector2 max = Vector2.one * 10;
    public bool TrueRandom;

    // ͬ������������ز���
    public float sameTypeAttraction = 3f;   // ͬ��������ǿ��

    private Vector3 lastValidPosition = Vector3.zero;
    private const int maxTries = 5;
    // �����б����GC����
    private List<Vector2> sameTypePositions = new List<Vector2>(8); // Ԥ������

    protected override void OnStart()
    {
        lastValidPosition = context.transform.position;
    }

    protected override void OnStop()
    {
    }

    protected override State OnUpdate()
    {
        // ��ȡָ��ͬ��ķ������������ڲ����������Ż���
        Vector2 sameTypeDirection = GetSameTypeAverageDirection();

        if (!TrueRandom && context.map != null)
        {
            Vector3 chosenPosition = Vector3.zero;
            bool found = false;

            for (int i = 0; i < maxTries; i++)
            {
                // �ϲ����ƫ�Ƽ��㣬���پֲ�����
                Vector2 randomOffset = new Vector2(
                    Random.Range(min.x, max.x),
                    Random.Range(min.y, max.y)
                );

                // ��ӷ���ƫ�ƣ������߼���
                if (lastValidPosition != context.transform.position) // �������������
                {
                    Vector2 direction = (Vector2)(context.transform.position - lastValidPosition).normalized;
                    randomOffset += direction * 0.5f;
                }

                // ���ͬ������ƫ�ƣ�������ͬ��ʱ���㣩
                if (sameTypeDirection.sqrMagnitude > 0.001f) // ��ֱ�ӱȽ�Vector2.zero����Ч
                {
                    randomOffset += sameTypeDirection * sameTypeAttraction;
                }

                // �������λ�ò���֤
                Vector3 testPosition = context.transform.position + (Vector3)randomOffset;
                Vector2Int testPositionInt = Vector2Int.FloorToInt(testPosition);

                if (context.map.GetTileArea(testPositionInt) <= 2)
                {
                    chosenPosition = testPosition;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                Vector2 randomOffset = new Vector2(
                    Random.Range(min.x, max.x),
                    Random.Range(min.y, max.y)
                );

                if (sameTypeDirection.sqrMagnitude > 0.001f)
                {
                    randomOffset += sameTypeDirection * sameTypeAttraction;
                }
                chosenPosition = context.transform.position + (Vector3)randomOffset;
            }

            context.mover.TargetPosition = chosenPosition;
            blackboard.TargetPosition = chosenPosition - context.transform.position;
            lastValidPosition = context.transform.position;

            return State.Success;
        }
        else
        {
            Vector2 randomOffset = new Vector2(
                Random.Range(min.x, max.x),
                Random.Range(min.y, max.y)
            );

            if (sameTypeDirection.sqrMagnitude > 0.001f)
            {
                randomOffset += sameTypeDirection * sameTypeAttraction;
            }

            context.mover.TargetPosition = context.transform.position + (Vector3)randomOffset;
            blackboard.TargetPosition = randomOffset;
            lastValidPosition = context.transform.position;

            return State.Success;
        }
    }

    // �Ż����ܵ�ͬ�෽�����
    private Vector2 GetSameTypeAverageDirection()
    {
        // ��������б�����ÿ�δ������б�
        sameTypePositions.Clear();

        // ���ٿ����ü��
        if (context.itemDetector == null || context.item == null ||
            context.item.itemData == null || context.itemDetector.CurrentItemsInArea == null)
        {
            return Vector2.zero;
        }

        // ��������ID�����ظ�����
        string selfId = context.item.itemData.IDName;

        // �����ѹ��˵���Ʒ�б�
        foreach (var collider in context.itemDetector.CurrentItemsInArea)
        {
            // �����ų������ú�����
            if (collider == null || collider.transform == context.transform ||
                collider.itemData == null)
            {
                continue;
            }

            // ƥ��ͬ��ID
            if (collider.itemData.IDName == selfId)
            {
                sameTypePositions.Add(collider.transform.position);
            }
        }

        // ��ͬ��ֱ�ӷ���
        int count = sameTypePositions.Count;
        if (count == 0)
        {
            return Vector2.zero;
        }

        // ����ƽ��λ�ã�ʹ�õ�ѭ�����ٵ���������
        Vector2 averagePosition = Vector2.zero;
        for (int i = 0; i < count; i++)
        {
            averagePosition += sameTypePositions[i];
        }
        averagePosition /= count;

        // ���㷽��������ʹ��sqrMagnitude�ж��Ƿ���Ҫ��һ����
        Vector2 direction = averagePosition - (Vector2)context.transform.position;
        return direction.sqrMagnitude > 0.001f ? direction.normalized : Vector2.zero;
    }

    // �༭��Gizmo
    private void OnDrawGizmosSelected()
    {
        if (Application.isPlaying && context != null)
        {
            Gizmos.color = Color.cyan;
            // ������Ի���ʵ�ʼ�ⷶΧ�������Ҫ��
            Gizmos.DrawWireSphere(context.transform.position, 5f); // ���滻Ϊʵ�ʷ�Χֵ
        }
    }
}
