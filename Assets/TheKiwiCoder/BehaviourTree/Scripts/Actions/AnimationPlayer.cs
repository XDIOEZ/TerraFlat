using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

[NodeMenu("ActionNode/Animator/播放动画")]
public class AnimationPlayer : ActionNode
{

    [Header("动画名或参数名")]
    public string animationName;

    [Header("动画播放时间 (秒)")]
    public float time = 0f;  // 在这段时间内返回 Running，播放完后返回 Success

    [Header("播放方式")]
    public PlayType playType = PlayType.动画名;

    [Header("切换模式下的布尔值")]
    public bool Setbool = true;

    private float startTime;

    protected override void OnStart()
    {
        startTime = Time.time;

        switch (playType)
        {
            case PlayType.动画名:
                context.animator.Play(animationName, 0);
                break;
            case PlayType.切换:
                context.animator.SetBool(animationName, Setbool);
                break;
            case PlayType.触发器:
                context.animator.SetTrigger(animationName);
                break;
        }
    }

    protected override void OnStop()
    {
        // 可选：你可以在动画结束后重置Bool状态
        // if (playType == PlayType.切换) {
        //     context.animator.SetBool(animationName, false);
        // }
    }

    protected override State OnUpdate()
    {
        if (Time.time - startTime < time)
        {
            return State.Running;
        }
        return State.Success;
    }
}

public enum PlayType
{
    动画名,
    切换,
    触发器
}
