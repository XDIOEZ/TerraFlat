using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

[NodeMenu("ActionNode/检测/检测是否可以使用技能")]
public class AISkillCooldownTime : ActionNode
{
    [Header("冷却时间（秒）")]
    public float cooldownTime = 1.0f;
    
    [Header("调试信息（仅查看）")]
    [SerializeField] private float cooldownStartTime = 0f;
    [SerializeField] private bool isCoolingDown = false;
    
    // 新增字段
    [Header("时间记录信息")]
    [SerializeField] private float lastRecordTime = 0f;  // 上一次记录时间
    [SerializeField] private float currentTime = 0f;    // 当前时间
    [SerializeField] private float timeDifference = 0f; // 时间差
    [SerializeField] private bool isInCooldown = false; // 是否处于冷却中

    protected override void OnStart() 
    {
        // 更新时间信息
        UpdateTimeInfo();
        
        // 如果还没有开始冷却，则开始冷却计时
        if (!isCoolingDown)
        {
            StartCooldown();
        }
    }

    protected override void OnStop() 
    {
    }

    protected override State OnUpdate() 
    {
        // 更新时间信息
        UpdateTimeInfo();
        
        // 更新冷却状态
        isInCooldown = isCoolingDown;
        
        // 如果没有在冷却中，返回Success
        if (!isCoolingDown)
        {
            return State.Success;
        }
        
        // 检查冷却是否完成
        if (Time.time - cooldownStartTime >= cooldownTime)
        {
            // 冷却完成，重置状态
            isCoolingDown = false;
            isInCooldown = false;
            return State.Success;
        }
        
        // 仍在冷却中，返回Failure
        return State.Failure;
    }
    
    // 开始冷却计时
    public void StartCooldown()
    {
        cooldownStartTime = Time.time;
        isCoolingDown = true;
        isInCooldown = true;
        UpdateTimeInfo();
    }
    
    // 检查是否正在冷却中
    public bool IsCoolingDown()
    {
        if (isCoolingDown && Time.time - cooldownStartTime >= cooldownTime)
        {
            isCoolingDown = false; // 自动重置已完成的冷却
            isInCooldown = false;
        }
        return isCoolingDown;
    }
    
    // 获取剩余冷却时间
    public float GetRemainingCooldownTime()
    {
        if (!isCoolingDown)
            return 0f;
            
        float remaining = cooldownTime - (Time.time - cooldownStartTime);
        return Mathf.Max(0f, remaining);
    }
    
    // 重置冷却
    public void ResetCooldown()
    {
        isCoolingDown = false;
        isInCooldown = false;
    }
    
    // 更新时间信息
    private void UpdateTimeInfo()
    {
        lastRecordTime = currentTime;
        currentTime = Time.time;
        timeDifference = currentTime - lastRecordTime;
    }
}