using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

public class node_ItemDetector : ActionNode
{
    protected override void OnStart()
    {
        if (context.itemDetector == null)
            context.itemDetector = context.gameObject.GetComponent<IDetector>();
    }

    protected override void OnStop()
    {
        // ��ѡ���������
    }

    protected override State OnUpdate()
    {
        // ֱ��ִ�м�⣬����������
        context.itemDetector.Update_Detector();
        return State.Success; // ��������󷵻� Running/Success
    }
}