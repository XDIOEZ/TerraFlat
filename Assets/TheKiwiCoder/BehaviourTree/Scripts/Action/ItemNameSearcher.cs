using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

public class ItemNameSearcher : ActionNode
{
    [Tooltip("����������Ϊ�ƶ�Ŀ��")]
    public bool setMoveTarget = true;

    [Tooltip("��Ҫ���ҵĶ��������б�")]
    public List<string> searchNames = new List<string>();

    protected override void OnStart()
    {
        // ��ѡ����ʼ���߼�
    }

    protected override void OnStop()
    {
        // ��ѡ�������߼�
    }

    protected override State OnUpdate()
    {
        foreach (Item item in context.itemDetector.CurrentItemsInArea)
        {
            // ��ȡ��Ʒ��ʵ�����ƣ�ȷ��ʹ����ȷ�����ԣ�
            string itemName = item.Item_Data.Name;

            // ��������Ƿ���Ŀ���б��У������ִ�Сдƥ�䣩
            if (searchNames.Contains(itemName))
            {
                // �����Ҫ�����ƶ�Ŀ��
                if (setMoveTarget)
                {
                    context.mover.TargetPosition = item.transform.position;
                }
                // �������سɹ�
                return State.Success;
            }
        }

        // ѭ������δ�ҵ�ƥ����
        return State.Failure;
    }
}