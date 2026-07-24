using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

[NodeMenu("ActionNode/行动/狩猎")]
public class Hunting : ActionNode
{
    [Header("物品搜索设置")]
    [Tooltip("要搜索的物品类型列表（部分匹配）")]
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
    /// 查找符合条件的目标物品
    /// </summary>
    private Item FindTargetItem()
    {
        for (int itemIndex = 0; itemIndex < context.itemDetector.CurrentItemsInArea.Count; itemIndex++)
        {
            Item item = context.itemDetector.CurrentItemsInArea[itemIndex];
            if (item?.itemData.Tags == null)
                continue;

            for (int typeIndex = 0; typeIndex < ItemType.Count; typeIndex++)
            {
                if (item.itemData.Tags.ContainsTag(ItemType[typeIndex]))
                    return item;
            }
        }

        return null;
    }
}
