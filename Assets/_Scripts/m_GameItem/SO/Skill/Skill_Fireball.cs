using DG.Tweening;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[CreateAssetMenu(fileName = "New Skill", menuName = "Skill/Skill_Test", order = 0)]
public class Skill_Fireball : BaseSkillAction
{
    [Tooltip("技能开始执行时的调试信息")]
    public string StartDebugTest = "技能开始执行";

    [Tooltip("火球")]
    public GameObject Fireball;
    
    [Tooltip("技能执行停止时的调试信息")]
    public string StopDebugTest = "技能执行停止";
    
    // 存储火球的初始飞行方向
    private Vector2 fireballDirection = Vector2.zero;

    public override void StartExecuteSkill(RuntimeSkill runtimeSkill)
    {
        // 使用绿色显示开始调试信息
        Debug.Log($"<color=green>{StartDebugTest}</color>");
        runtimeSkill.skillInstanceDict["Fireball"] = 
            Instantiate(Fireball).transform;

        // 实例化位置向外(目标位置)移动1个单位避免对自己造成伤害
        Vector2 spawnPosition = (Vector2)runtimeSkill.skillManager.transform.position + fireballDirection * 2;

        spawnPosition += runtimeSkill.skillManager.castingPointOffset["Fireball"];

       // 计算并存储火球的初始飞行方向
        fireballDirection = (runtimeSkill.targetPoint - spawnPosition).normalized;

        runtimeSkill.skillInstanceDict["Fireball"].transform.position = new Vector3(spawnPosition.x, spawnPosition.y, runtimeSkill.skillSender.transform.position.z);
        
        // 设置火球初始朝向
        if (fireballDirection != Vector2.zero)
        {
            float angle = Mathf.Atan2(fireballDirection.y, fireballDirection.x) * Mathf.Rad2Deg;
            runtimeSkill.skillInstanceDict["Fireball"].rotation = Quaternion.Euler(0, 0, angle);
        }
    }

    public override void StayExecuteSkill(RuntimeSkill Data,float deltaTime)
    {
       // Debug.Log($"<color=yellow>当前进度:{Data.progress}/{Data.duration}</color>");
       // Debug.Log($"<color=yellow>名字:{Data.skillInstanceDict["Fireball"].name}</color>");
        
        // 直接控制2D火球移动，按照初始方向直线飞行
        Vector3 currentPosition = Data.skillInstanceDict["Fireball"].position;
        
        // 根据速度和时间计算移动距离
        float moveDistance = Data.skillData.speed * deltaTime;
        
        // 按初始方向和速度移动火球
        Vector2 newPosition = currentPosition + (Vector3)(fireballDirection * moveDistance);
        Data.skillInstanceDict["Fireball"].position = new Vector3(newPosition.x, newPosition.y, currentPosition.z);
        
        // 可选：根据持续时间来结束技能
        Data.progress += deltaTime;
    }

    public override void StopExecuteSkill(RuntimeSkill runtimeSkill)
    {
        // 使用红色显示停止调试信息
        Debug.Log($"<color=red>{StopDebugTest}</color>");
        Destroy(runtimeSkill.skillInstanceDict["Fireball"].gameObject);
        // 重置方向
        fireballDirection = Vector2.zero;
    }
}