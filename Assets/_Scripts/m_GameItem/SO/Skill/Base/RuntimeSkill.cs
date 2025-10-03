
using MemoryPack;
using System.Collections.Generic;
using UnityEngine;

public class RuntimeSkill
{//��������ʱ�Ĳ���  ����ı�������������ʱ�Ż�仯��
    [Tooltip("������Ϊ")]
    public BaseSkill skillData;
    [Tooltip("���ܳ���ʱ��")]
    public float duration = 1;
    [Tooltip("���ܽ���")]
    public float progress = 0;

    [Tooltip("���ܷ�����")]
    public Item skillSender;
    [Tooltip("���ܽ�����")]
    public Item skillReceiver;
    [Tooltip("���ܿ�ʼ��λ")]
    public Vector2 startPoint;
    [Tooltip("���ܽ��ܵ�")]
    public Vector2 targetPoint;
    [Tooltip("��������ģ��")]
    public Mod_SkillManager skillManager;

    [Tooltip("����ʵ����������gameObject�ֵ�")]
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