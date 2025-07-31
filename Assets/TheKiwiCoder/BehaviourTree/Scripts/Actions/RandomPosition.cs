using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

public class RandomPosition : ActionNode
{

    public Vector2 min = Vector2.one * -10;
    public Vector2 max = Vector2.one * 10;
    public bool TrueRandom;

    Mover speed;

    private Vector3 lastValidPosition = Vector3.zero;
    private const int maxTries = 5;

    protected override void OnStart()
    {
        speed = context.item.Mods[ModText.Mover] as Mover;
        lastValidPosition = context.transform.position;
    }

    protected override void OnStop()
    {
    }

    protected override State OnUpdate()
    {
        if (!TrueRandom&& context.map!= null)
        {
            Vector3 chosenPosition = Vector3.zero;
            bool found = false;

            for (int i = 0; i < maxTries; i++)
            {
                Vector2 randomOffset = new Vector2(Random.Range(min.x, max.x), Random.Range(min.y, max.y));

                // ���֮ǰ�м�¼������뷴��ƫ�ƣ���ǰλ�� - ��һ�γɹ�λ�ã�
                Vector2 direction = (Vector2)(context.transform.position - lastValidPosition).normalized;
                if (direction != Vector2.zero)
                {
                    float influence = 0.5f; // Ӱ��ǿ�ȣ�����Ե�С�����
                    randomOffset += direction * influence;
                }

                Vector3 testPosition = context.transform.position + (Vector3)randomOffset;
                Vector2Int testPositionInt = Vector2Int.FloorToInt(testPosition); // ת���� Vector3Int

                int area = context.map.GetTileArea(testPositionInt);

                if (area <= 2)
                {
                    chosenPosition = testPosition;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                // ȫ�����Զ�ʧ�ܣ����һ��Ҳ��ΪĿ��
                chosenPosition = context.transform.position + (Vector3)new Vector2(Random.Range(min.x, max.x), Random.Range(min.y, max.y));
            }

            speed.TargetPosition = chosenPosition;
            blackboard.TargetPosition = chosenPosition - context.transform.position;

            lastValidPosition = context.transform.position;

            return State.Success;
        }
        else
        {
            Vector2 randomOffset = new Vector2(Random.Range(min.x, max.x), Random.Range(min.y, max.y));
            speed.TargetPosition = context.transform.position + (Vector3)randomOffset;
            blackboard.TargetPosition = randomOffset;

            lastValidPosition = context.transform.position;

            return State.Success;
        }
    }
}
