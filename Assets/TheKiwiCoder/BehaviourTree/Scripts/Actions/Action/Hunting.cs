using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

[NodeMenu("ActionNode/�ж�/����")]
public class Hunting : ActionNode
{
    [Header("��Ʒ��������")]
    [Tooltip("Ҫ��������Ʒ�����б�����ƥ�䣩")]
    public List<string> ItemType = new List<string>();

    protected override void OnStart() {
    }

    protected override void OnStop() {
    }

    protected override State OnUpdate() {

        Item targetItem = FindTargetItem();

        if (targetItem == null)
        {
            return State.Failure;
        }
        else
        {
            context.mover.TargetPosition = targetItem.transform.position;
            return State.Success;
        }

          
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
}
