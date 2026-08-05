using MemoryPack;
using System.Collections.Generic;
using UnityEngine;

public class RuntimeSkill
{
    // 运行时参数：数据大部分来自技能模板，并在技能运行期间动态变化。
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
    [Tooltip("技能起点")]
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
            if (action == null)
            {
                Debug.LogWarning("RuntimeSkill: 技能行为为空,已跳过");
                continue;
            }

            Transform castingPoint = skillManager.GetCastingPoint(action.GetCastingPointIndex());
            if (castingPoint == null)
            {
                Debug.LogWarning($"RuntimeSkill: 施法点为空,已跳过技能行为 '{action.name}'");
                continue;
            }

            Skill actionInstance = GameObject.Instantiate(action, castingPoint.position, Quaternion.identity);
            actionInstance.runtimeSkill = this;
            skillInstanceDict.Add(action.name, actionInstance);
            actionInstance.Load();
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
        // 清空技能实例字典。
        skillInstanceDict.Clear();
    }

    public bool IsFinished()
    {
        return progress >= duration;
    }
}
