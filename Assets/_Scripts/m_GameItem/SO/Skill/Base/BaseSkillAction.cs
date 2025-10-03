using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class BaseSkillAction : ScriptableObject
{
    [Tooltip("��ʼִ�м���")]
    public abstract void StartExecuteSkill(RuntimeSkill Data);
    [Tooltip("�������¼���")]
    public abstract void StayExecuteSkill(RuntimeSkill Data,float deltaTime);
    [Tooltip("��������")]
    public abstract void StopExecuteSkill(RuntimeSkill Data);

}