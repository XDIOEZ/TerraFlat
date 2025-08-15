using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

[NodeMenu("ActionNode/Animator/���Ŷ���")]
public class AnimationPlayer : ActionNode
{

    [Header("�������������")]
    public string animationName;

    [Header("��������ʱ�� (��)")]
    public float time = 0f;  // �����ʱ���ڷ��� Running��������󷵻� Success

    [Header("���ŷ�ʽ")]
    public PlayType playType = PlayType.������;

    [Header("�л�ģʽ�µĲ���ֵ")]
    public bool Setbool = true;

    private float startTime;

    protected override void OnStart()
    {
        startTime = Time.time;

        switch (playType)
        {
            case PlayType.������:
                context.animator.Play(animationName, 0);
                break;
            case PlayType.�л�:
                context.animator.SetBool(animationName, Setbool);
                break;
            case PlayType.������:
                context.animator.SetTrigger(animationName);
                break;
        }
    }

    protected override void OnStop()
    {
        // ��ѡ��������ڶ�������������Bool״̬
        // if (playType == PlayType.�л�) {
        //     context.animator.SetBool(animationName, false);
        // }
    }

    protected override State OnUpdate()
    {
        if (Time.time - startTime < time)
        {
            return State.Running;
        }
        return State.Success;
    }
}

public enum PlayType
{
    ������,
    �л�,
    ������
}
