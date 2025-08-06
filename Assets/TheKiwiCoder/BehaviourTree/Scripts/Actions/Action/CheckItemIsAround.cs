using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

public class CheckItemIsAround : ActionNode, IDebug
{
    private IDetector itemDetector;

    public string itemName;

    public bool DebugMode { get; set; }

    protected override void OnStart()
    {
        if(itemDetector == null)
        {
            itemDetector = context.gameObject.GetComponent<IDetector>();
           // Debug.LogWarning("δָ����Ʒ���������ʹ��Ĭ�ϵ���Ʒ�����");
        }
       

    }

    protected override void OnStop()
    {
        if (DebugMode)
        {
            Debug.Log($"<color=orange>CheckItemIsAround �����ֹͣ</color>");
        }
    }

    protected override State OnUpdate()
    {
        if (DebugMode)
        {
            Debug.Log($"<color=green>���ڼ����Χ��Ʒ...</color>");
        }

        foreach (var item in itemDetector.CurrentItemsInArea)
        {
            if ( item.itemData.ItemTags.Item_TypeTag.Contains(itemName))
            {
                if (DebugMode)
                {
                    Debug.Log($"<color=lime>��⵽������������Ʒ��{item.name}��ID��{item.GetInstanceID()}��</color>");
                }
                return State.Success;
            }
        }

        if (DebugMode)
        {
            Debug.Log($"<color=gray>δ��⵽������������Ʒ���������...</color>");
        }

        return State.Failure;
    }
}