using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

public class HungryChecker : ActionNode
{
    public IHunger hungryEr;

    [Tooltip("������ֵ���ٷֱȣ�")]
    [Range(0, 1)] // 0~100%�Ļ�����
    public float hungryThreshold = 0.3f; // Ĭ��30%

    protected override void OnStart() 
    {
        if(hungryEr == null)
        {
            hungryEr = context.gameObject.GetComponent<IHunger>();
        }
    }

    protected override void OnStop() {
    }

    protected override State OnUpdate() 
    {
        if(hungryEr.Nutrition.Food <=  hungryEr.Nutrition.MaxFood * hungryThreshold)
        {
            return State.Success;
        }
        
        return State.Failure;
    }
}
