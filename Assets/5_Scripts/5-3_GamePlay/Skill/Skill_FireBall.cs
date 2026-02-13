using DG.Tweening;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Skill_FireBall : Skill
{
    [Header("组件引用")]
    public List<Module> mods = new List<Module>();
    
    [Header("调试信息")]
    public string StartDebugTest = "技能开始执行";
    public string StopDebugTest = "技能执行停止";
    
    // 存储火球的初始飞行方向
    private Vector2 fireballDirection = Vector2.zero;
    private Vector3 startPoint;

    public void Start()
    {
        // 使用绿色显示开始调试信息
        Debug.Log($"<color=green>{StartDebugTest}</color>");
        
        

        if (runtimeSkill != null)
        {
            Transform castingPoint = GetCastingPointTransform();
            if (castingPoint == null)
            {
                Debug.LogWarning("火球技能：施法点为空");
                return;
            }
            // 实例化火球位置
            startPoint = castingPoint.position;
            Vector2 spawnPosition = (Vector2)startPoint;

            // 计算并存储火球的初始飞行方向
            fireballDirection = (runtimeSkill.targetPoint - spawnPosition).normalized;

            // 实例化火球
     
                transform.position = new Vector3(spawnPosition.x, spawnPosition.y, runtimeSkill.skillSender.transform.position.z);
                
                // 设置火球初始朝向
                if (fireballDirection != Vector2.zero)
                {
                    float angle = Mathf.Atan2(fireballDirection.y, fireballDirection.x) * Mathf.Rad2Deg;
                transform.rotation = Quaternion.Euler(0, 0, angle);
                }
            
            
            // 获取所有子对象上的Module组件
            mods = new List<Module>(GetComponentsInChildren<Module>());
            
            // 加载所有模块
            foreach (var mod in mods)
            {
                mod.Load();
            }
        }
    }

    public override void SkillUpdate(float deltaTime)
    {
        // 检查runtimeSkill和火球实例是否存在
        if (runtimeSkill == null || transform == null)
            return;
            
        // 更新所有模块
        foreach (var mod in mods)
        {
            mod.ModUpdate(deltaTime);
        }
        
        // 直接控制2D火球移动，按照初始方向直线飞行
        Vector3 currentPosition = transform.position;
        
        // 根据速度和时间计算移动距离
        float moveDistance = runtimeSkill.skillData.speed * deltaTime;
        
        // 按初始方向和速度移动火球
        Vector2 newPosition = currentPosition + (Vector3)(fireballDirection * moveDistance);
        transform.position = new Vector3(newPosition.x, newPosition.y, currentPosition.z);
    }

    public override void Save()
    {
        // 保存所有模块
        foreach (var mod in mods)
        {
            mod.Save();
        }
    }
}