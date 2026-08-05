using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

[NodeMenu("ActionNode/检测/检测是否可以使用技能")]
public class AISkillCooldownTime : ActionNode
{
    [Header("冷却时间（秒）")]
    public float cooldownTime = 1.0f;

    [Header("冷却进度倍率（调节冷却速度）")]
    public float cooldownRateMultiplier = 1.0f;

    [Header("调试信息")]
    [SerializeField] private bool isCoolingDown = false;
    [SerializeField] private float accumulatedCooldownProgress = 0f;

    private float lastAccessTime = 0f; // 上次节点被访问的时间

    protected override void OnStart()
    {
    }

    protected override void OnStop() { }

    protected override State OnUpdate()
    {
        float now = Time.time;
        float delta = now - lastAccessTime;
        lastAccessTime = now;

        if (isCoolingDown)
        {
            accumulatedCooldownProgress += delta * cooldownRateMultiplier;
            if (accumulatedCooldownProgress >= cooldownTime)
            {
                isCoolingDown = false;
                accumulatedCooldownProgress = cooldownTime;
            }
        }

        if (!isCoolingDown)
        {
            // 冷却结束 => 技能可用
            StartCooldown();
            return State.Success;
        }

        return State.Failure;
    }


    // 手动开始冷却
    public void StartCooldown()
    {
        isCoolingDown = true;
        accumulatedCooldownProgress = 0f;
    }

    // 获取剩余冷却时间
    public float GetRemainingCooldownTime()
    {
        if (!isCoolingDown) return 0f;
        return Mathf.Max(0f, cooldownTime - accumulatedCooldownProgress);
    }

    // 检查是否冷却完毕
    public bool IsCooledDown()
    {
        return !isCoolingDown;
    }

    // 强制重置
    public void ResetCooldown()
    {
        isCoolingDown = false;
        accumulatedCooldownProgress = 0f;
        lastAccessTime = Time.time;
    }
}
