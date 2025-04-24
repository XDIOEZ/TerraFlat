using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

public class AnimationPlayer : ActionNode
{
    public string animationName;

    [Header("动画Trigger设置")]
    public bool isTrigger;

    [Header("动画Bool设置")]
    public bool isBool;
    public bool boolSwitch;
    protected override void OnStart() {
        
    }

    protected override void OnStop() {
    }

    protected override State OnUpdate() 
    {
        if (isBool)
        {
            context.animator.SetBool(animationName, boolSwitch);
        }

        if (isTrigger)
        {
            context.animator.SetTrigger(animationName);
        }
     
        return State.Success;
    }
}
