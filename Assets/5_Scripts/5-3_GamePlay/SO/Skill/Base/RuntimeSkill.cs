using MemoryPack;
using System.Collections.Generic;
using UnityEngine;

public class RuntimeSkill
{//����ʱ�Ĳ���  ���ݴ󲿷����Լ��ܱ��� ����������ʱ�����仯
    [Tooltip("��������")]
    public BaseSkill skillData;
    [Tooltip("���ܳ���ʱ��")]
    public float duration = 1;
    [Tooltip("���ܽ���")]
    public float progress = 0;
    [Tooltip("���ܷ�����")]
    public Item skillSender;
    [Tooltip("���ܽ�����")]
    public Item skillReceiver;
    [Tooltip("������ʼ��")]
    public Vector2 startPoint;
    [Tooltip("�����յ�")]
    public Vector2 targetPoint;
    [Tooltip("���ܹ���ģ��")]
    public Mod_SkillManager skillManager;
    [Tooltip("����ʵ���ֵ�")]
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
        // ����ʵ���ֵ�
        skillInstanceDict.Clear();
    }

    public bool IsFinished()
    {
        return progress >= duration;
    }
}