using UnityEngine;
using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;

[CreateAssetMenu(fileName = "BaseSkill", menuName = "Skill/BaseSkill")]
public class BaseSkill : ScriptableObject
{
    [Header("������Ϣ")]
    [Tooltip("��������")]
    public string skillName = "�¼���";
    [Tooltip("��������")]
    public string description = "��������";
    [Tooltip("����ͼ��")]
    public Sprite icon;
    [Tooltip("���ܳ���ʱ��")]
    public float duration = 2f;
    [Tooltip("���ܳ�ʼ����ʱ��")]
    public float initialPrograss = 0f;
    [Tooltip("�����ٶ�(���������ϵİ�)")]
    public float speed = 1;


    [Tooltip("������Ϊ")]
    [InlineEditor]
    public List<BaseSkillAction> Actions;

    public void StartAction(RuntimeSkill runtimeSkill)
    {
        foreach (var action in Actions)
        {
            action.StartExecuteSkill(runtimeSkill);
        }
    }
    public void StayAction(RuntimeSkill runtimeSkill, float deltaTime)
    {
        foreach(var action in Actions)
        {
            action.StayExecuteSkill(runtimeSkill,deltaTime);
        }
    }
    public void StopAction(RuntimeSkill runtimeSkill)
    {
        foreach (var action in Actions)
        {
            action.StopExecuteSkill(runtimeSkill);
        }
    }
}