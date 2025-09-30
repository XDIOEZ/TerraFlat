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

    [Header("控制选项")]
    [Tooltip("是否自动获取动画长度（仅在Start时获取一次）")]
    public bool autoGetAnimationLength = false;
    
    [Tooltip("是否等待动画播放完毕再返回Success")]
    public bool waitForAnimationComplete = true;

    private float startTime;
    private float animationLength = 0f;
    private int animationHash = 0;

    protected override void OnStart()
    {
        startTime = Time.time;
        animationLength = time; // 默认使用设置的时间
        animationHash = Animator.StringToHash(animationName);

        switch (playType)
        {
            case PlayType.动画名:
                context.animator.Play(animationHash, 0);
                if (autoGetAnimationLength)
                {
                    animationLength = GetAnimationLength(animationName);
                }
                break;
            case PlayType.切换:
                context.animator.SetBool(animationName, Setbool);
                // 对于布尔值切换，我们无法自动获取长度，需要手动设置
                break;
            case PlayType.触发器:
                context.animator.SetTrigger(animationName);
                if (autoGetAnimationLength)
                {
                    animationLength = GetAnimationLength(animationName);
                }
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
        // 如果不等待动画完成，立即返回Success
        if (!waitForAnimationComplete)
        {
            return State.Success;
        }

        // 使用基于时间的检测（性能更好）
        if (Time.time - startTime < animationLength)
        {
            return State.Running;
        }
        
        return State.Success;
    }

    // 获取动画剪辑的长度（只在Start时调用一次）
    private float GetAnimationLength(string animName)
    {
        // 如果没有启用自动获取或没有设置animator，返回默认时间
        if (!autoGetAnimationLength || context.animator == null)
            return time;

        // 获取AnimatorController
        RuntimeAnimatorController runtimeController = context.animator.runtimeAnimatorController;
        if (runtimeController == null)
            return time;

        // 遍历所有动画剪辑（只在初始化时执行一次）
        foreach (AnimationClip clip in runtimeController.animationClips)
        {
            if (clip != null && clip.name == animName)
            {
                return clip.length;
            }
        }

        // 如果找不到对应的动画剪辑，返回默认时间
        return time;
    }
}

public enum PlayType
{
    动画名,
    切换,
    触发器
}