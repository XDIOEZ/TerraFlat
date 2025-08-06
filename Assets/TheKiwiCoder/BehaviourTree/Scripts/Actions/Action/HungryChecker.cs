using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

public class HungryChecker : ActionNode
{

    [Tooltip("������ֵ���ٷֱȣ�")]
    [Range(0, 1)] // 0~100%�Ļ�����
    public float hungryThreshold = 0.6f; // Ĭ��60%

    protected override void OnStart() 
    {

    }

    protected override void OnStop() {
    }

    protected override State OnUpdate() 
    {
        if(context.Food.Data.nutrition.GetHungerRate() <= hungryThreshold)
        {
            return State.Success;
        }
        
        return State.Failure;
    }
}
