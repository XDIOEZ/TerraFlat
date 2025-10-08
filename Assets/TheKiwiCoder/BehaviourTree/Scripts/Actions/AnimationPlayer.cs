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

    [Header("动画层级")]
    [Tooltip("动画播放的层级（0为基础层，数值越大层级越高）")]
    public int layerIndex = 0;
    
    [Header("播放时权重设置")]
    [Tooltip("在播放动画时将权重设置为1，然后在动画播放完毕后将权重设置为0")]
    public float PlayWeight = 1f;

    [Header("控制选项")]
    [Tooltip("是否自动获取动画长度（仅在Start时获取一次）")]
    public bool autoGetAnimationLength = false;
    
    [Tooltip("是否等待动画播放完毕再返回Success")]
    public bool waitForAnimationComplete = true;

    private float startTime;
    private float animationLength = 0f;
    private int animationHash = 0;
    private bool hasSetWeight = false;

    protected override void OnStart()
    {
        startTime = Time.time;
        animationLength = time; // 默认使用设置的时间
        animationHash = Animator.StringToHash(animationName);

        // 错误检查：检查上下文和动画组件
        if (context == null)
        {
            Debug.LogError($"[{GetType().Name}] 上下文为空！", this);
            return;
        }

        if (context.animator == null)
        {
            Debug.LogError($"[{GetType().Name}] 动画组件为空！请检查游戏对象是否包含动画组件。", this);
            return;
        }

        // 错误检查：检查动画名
        if (string.IsNullOrEmpty(animationName))
        {
            Debug.LogWarning($"[{GetType().Name}] 动画名称为空或未设置！", this);
        }

        switch (playType)
        {
            case PlayType.动画名:
                // 错误检查：检查动画控制器
                if (context.animator.runtimeAnimatorController == null)
                {
                    Debug.LogWarning($"[{GetType().Name}] 动画控制器未分配！", this);
                }
                
                context.animator.Play(animationHash, layerIndex);
                // 在播放动画时将权重设置为指定值
                context.animator.SetLayerWeight(layerIndex, PlayWeight);
                hasSetWeight = true;
                
                if (autoGetAnimationLength)
                {
                    float originalLength = animationLength;
                    animationLength = GetAnimationLength(animationName);
                }
                break;
                
            case PlayType.切换:
                context.animator.SetBool(animationName, Setbool);
                // 对于布尔值切换，我们无法自动获取长度，需要手动设置
                break;
                
            case PlayType.触发器:
                context.animator.SetTrigger(animationName);
                // 在触发器模式下也设置权重
                context.animator.SetLayerWeight(layerIndex, PlayWeight);
                hasSetWeight = true;
                
                if (autoGetAnimationLength)
                {
                    float originalLength = animationLength;
                    animationLength = GetAnimationLength(animationName);
                }
                break;
        }
    }

    protected override void OnStop()
    {
        // 错误检查：确保组件仍然存在
        if (hasSetWeight && context != null && context.animator != null)
        {
            // 动画结束时将权重设置为0
            context.animator.SetLayerWeight(layerIndex, 0f);
        }
    }

    protected override State OnUpdate()
    {
        // 错误检查：检查上下文和动画组件
        if (context == null || context.animator == null)
        {
            Debug.LogError($"[{GetType().Name}] 更新时上下文或动画组件为空！", this);
            return State.Failure;
        }

        // 如果不等待动画完成，立即返回Success
        if (!waitForAnimationComplete)
        {
            return State.Success;
        }

        // 使用基于时间的检测（性能更好）
        float elapsed = Time.time - startTime;
        if (elapsed < animationLength)
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
        {
            return time;
        }

        // 获取AnimatorController
        RuntimeAnimatorController runtimeController = context.animator.runtimeAnimatorController;
        if (runtimeController == null)
        {
            Debug.LogWarning($"[{GetType().Name}] 运行时动画控制器为空，使用默认时间: {time}秒", this);
            return time;
        }

        // 遍历所有动画剪辑（只在初始化时执行一次）
        foreach (AnimationClip clip in runtimeController.animationClips)
        {
            if (clip != null && clip.name == animName)
            {
                return clip.length;
            }
        }

        // 如果找不到对应的动画剪辑，返回默认时间
        Debug.LogWarning($"[{GetType().Name}] 在控制器中未找到动画剪辑 '{animName}'，使用默认时间: {time}秒", this);
        return time;
    }
}

public enum PlayType
{
    动画名,
    切换,
    触发器
}