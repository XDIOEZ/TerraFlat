
using MemoryPack;
using System.Collections.Generic;
using UnityEngine;

public class RuntimeSkill
{//都是运行时的参数  这里的变量都是在运行时才会变化的
    [Tooltip("技能行为")]
    public BaseSkill skillData;
    [Tooltip("技能持续时间")]
    public float duration = 1;
    [Tooltip("技能进度")]
    public float progress = 0;

    [Tooltip("技能发起者")]
    public Item skillSender;
    [Tooltip("技能接收者")]
    public Item skillReceiver;
    [Tooltip("技能开始点位")]
    public Vector2 startPoint;
    [Tooltip("技能接受点")]
    public Vector2 targetPoint;
    [Tooltip("技能生成模块")]
    public Mod_SkillManager skillManager;

    [Tooltip("技能实例化出来的gameObject字典")]
    public Dictionary<string, Transform> skillInstanceDict = new();

    public void Start()
    {
        skillData.StartAction(this);
    }

    public void Stay(float deltaTime)
    {
            progress += deltaTime;
            skillData.StayAction(this,deltaTime);
    }

    public void Stop()
    {
        skillData.StopAction(this);
    }

    public bool IsFinished()
    {
        return progress >= duration;
    }
}