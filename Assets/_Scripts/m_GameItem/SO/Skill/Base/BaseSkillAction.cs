using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class BaseSkillAction : ScriptableObject
{
    [Tooltip("开始执行技能")]
    public abstract void StartExecuteSkill(RuntimeSkill Data);
    [Tooltip("持续更新技能")]
    public abstract void StayExecuteSkill(RuntimeSkill Data,float deltaTime);
    [Tooltip("结束技能")]
    public abstract void StopExecuteSkill(RuntimeSkill Data);

}