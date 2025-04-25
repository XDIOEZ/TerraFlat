using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

public class ItemNameSearcher : ActionNode
{
    [Tooltip("将对象设置为移动目标")]
    public bool setMoveTarget = true;

    [Tooltip("需要查找的对象名称列表")]
    public List<string> searchNames = new List<string>();

    protected override void OnStart()
    {
        // 可选：初始化逻辑
    }

    protected override void OnStop()
    {
        // 可选：清理逻辑
    }

    protected override State OnUpdate()
    {
        foreach (Item item in context.itemDetector.CurrentItemsInArea)
        {
            // 获取物品的实际名称（确保使用正确的属性）
            string itemName = item.Item_Data.Name;

            // 检查名称是否在目标列表中（不区分大小写匹配）
            if (searchNames.Contains(itemName))
            {
                // 如果需要设置移动目标
                if (setMoveTarget)
                {
                    context.mover.TargetPosition = item.transform.position;
                }
                // 立即返回成功
                return State.Success;
            }
        }

        // 循环结束未找到匹配项
        return State.Failure;
    }
}