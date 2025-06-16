using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

public class HungryChecker : ActionNode
{
    public IHunger hungryEr;

    [Tooltip("饥饿阈值（百分比）")]
    [Range(0, 1)] // 0~100%的滑动条
    public float hungryThreshold = 0.3f; // 默认30%

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
