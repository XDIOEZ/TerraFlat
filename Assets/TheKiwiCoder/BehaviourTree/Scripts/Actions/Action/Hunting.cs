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
        foreach (Item item in context.itemDetector.CurrentItemsInArea)
        {
            if (item?.Item_Data?.ItemTags?.Item_TypeTag == null)
                continue;

            // 检查物品类型是否匹配
            if (ItemType.Exists(type => item.Item_Data.ItemTags.Item_TypeTag.Contains(type)))
            {
                return item;
            }
        }

        return null;
    }
}
