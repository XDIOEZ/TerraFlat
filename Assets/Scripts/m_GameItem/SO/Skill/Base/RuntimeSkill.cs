using MemoryPack;
using System.Collections.Generic;
using UnityEngine;

public class RuntimeSkill
{//运行时的参数  数据大部分来自技能本身 但会在运行时发生变化
    [Tooltip("技能数据")]
    public BaseSkill skillData;
    [Tooltip("技能持续时间")]
    public float duration = 1;
    [Tooltip("技能进度")]
    public float progress = 0;
    [Tooltip("技能发送者")]
    public Item skillSender;
    [Tooltip("技能接收者")]
    public Item skillReceiver;
    [Tooltip("技能起始点")]
    public Vector2 startPoint;
    [Tooltip("技能终点")]
    public Vector2 targetPoint;
    [Tooltip("技能管理模块")]
    public Mod_SkillManager skillManager;
    [Tooltip("技能实例字典")]
    public Dictionary<string, Skill> skillInstanceDict = new Dictionary<string, Skill>();

    public void Start()
    {
        foreach (var action in skillData.Actions)
        {
            // 确保castingPoint字典包含对应的键
            if (skillManager.castingPoint.ContainsKey(skillData.name))
            {
                Skill actionInstance = GameObject.Instantiate(action, skillManager.castingPoint[skillData.name].transform.position, Quaternion.identity);
                actionInstance.runtimeSkill = this;
                skillInstanceDict.Add(action.name, actionInstance);
                actionInstance.Load();
            }
            else
            {
                Debug.LogWarning($"Casting point for action '{skillData.name}' not found.");
            }
        }
    }

    public void Stay(float deltaTime)
    {
        progress += deltaTime;
        
            foreach (var skill in skillInstanceDict.Values)
            {
                if (skill != null)
                {
                    skill.SkillUpdate(deltaTime);
                }
            }
    }

    public void Stop()
    {
            foreach (var skill in skillInstanceDict.Values)
            {
                if (skill != null)
                {
                skill.Save();
                    GameObject.Destroy(skill.gameObject);
                }
            }
        // 清理实例字典
        skillInstanceDict.Clear();
    }

    public bool IsFinished()
    {
        return progress >= duration;
    }
}