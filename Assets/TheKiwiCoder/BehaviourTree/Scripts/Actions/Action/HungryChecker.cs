using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

public class HungryChecker : ActionNode
{

    [Tooltip("饥饿阈值（百分比）")]
    [Range(0, 1)] // 0~100%的滑动条
    public float hungryThreshold = 0.6f; // 默认60%

    protected override void OnStart() 
    {

    }

    protected override void OnStop() {
    }

    protected override State OnUpdate() 
    {
        // 添加空检查，如果找不到FOOD组件直接返回Failure
        if (context.Food == null || context.Food.Data == null || context.Food.Data.nutrition == null)
        {
            return State.Failure;
        }
        
        if(context.Food.Data.nutrition.GetHungerRate() <= hungryThreshold)
        {
            return State.Success;
        }
        
        return State.Failure;
    }
}