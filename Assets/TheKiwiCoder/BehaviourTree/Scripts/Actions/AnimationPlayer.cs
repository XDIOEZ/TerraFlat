using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;
[NodeMenu("ActionNode/Animator/播放动画")]
public class AnimationPlayer : ActionNode
{
    [Header("动画参数名")]
    public string animationName;
    protected override void OnStart() {
    }

    protected override void OnStop() {
    }

    protected override State OnUpdate() {
        context.animator.Play(animationName,0);
        return State.Success;
    }
}
