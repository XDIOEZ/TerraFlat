using DG.Tweening;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Skill_FireBall : Skill
{
    #region 配置和运行状态

    [Header("组件引用")]
    public List<Module> mods = new List<Module>();
    
    [Header("调试信息")]
    public string StartDebugTest = "技能开始执行";
    public string StopDebugTest = "技能执行停止";
    
    // 存储火球的初始飞行方向
    private Vector2 fireballDirection = Vector2.zero;
    private Vector3 startPoint;
    private Rigidbody2D physicsBody;

    #endregion

    #region 物理投影与技能模块

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
            physicsBody = GetComponent<Rigidbody2D>();
            if (physicsBody == null)
                throw new MissingComponentException("火球缺少 Rigidbody2D。");
            physicsBody.bodyType = RigidbodyType2D.Dynamic;
            physicsBody.gravityScale = 0f;
            physicsBody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            physicsBody.velocity = fireballDirection * runtimeSkill.skillData.speed;
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
        
    }

    #endregion

    #region 保存

    public override void Save()
    {
        // 保存所有模块
        foreach (var mod in mods)
        {
            mod.Save();
        }
    }

    #endregion
}
