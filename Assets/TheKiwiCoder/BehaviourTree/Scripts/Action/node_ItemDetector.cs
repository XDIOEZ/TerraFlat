using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;
[NodeMenu("ActionNode/�Ѳ�/���½�ɫ�Ѳ���")]
public class node_ItemDetector : ActionNode
{
    public IDetector itemDetector;
    protected override void OnStart()
    {
        if (itemDetector == null)
            itemDetector = context.gameObject.GetComponent<IDetector>();
    }

    protected override void OnStop()
    {
        // ��ѡ���������
    }

    protected override State OnUpdate()
    {
        // ֱ��ִ�м�⣬����������
        itemDetector.Update_Detector();
        return State.Success; // ��������󷵻� Running/Success
    }
}