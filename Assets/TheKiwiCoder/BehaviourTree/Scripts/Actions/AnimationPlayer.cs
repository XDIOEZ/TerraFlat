using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;
[NodeMenu("ActionNode/Animator/���Ŷ���")]
public class AnimationPlayer : ActionNode
{
    [Header("����������")]
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
