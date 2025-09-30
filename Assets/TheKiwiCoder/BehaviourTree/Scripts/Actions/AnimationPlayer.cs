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

    [Header("����ѡ��")]
    [Tooltip("�Ƿ��Զ���ȡ�������ȣ�����Startʱ��ȡһ�Σ�")]
    public bool autoGetAnimationLength = false;
    
    [Tooltip("�Ƿ�ȴ�������������ٷ���Success")]
    public bool waitForAnimationComplete = true;

    private float startTime;
    private float animationLength = 0f;
    private int animationHash = 0;

    protected override void OnStart()
    {
        startTime = Time.time;
        animationLength = time; // Ĭ��ʹ�����õ�ʱ��
        animationHash = Animator.StringToHash(animationName);

        switch (playType)
        {
            case PlayType.������:
                context.animator.Play(animationHash, 0);
                if (autoGetAnimationLength)
                {
                    animationLength = GetAnimationLength(animationName);
                }
                break;
            case PlayType.�л�:
                context.animator.SetBool(animationName, Setbool);
                // ���ڲ���ֵ�л��������޷��Զ���ȡ���ȣ���Ҫ�ֶ�����
                break;
            case PlayType.������:
                context.animator.SetTrigger(animationName);
                if (autoGetAnimationLength)
                {
                    animationLength = GetAnimationLength(animationName);
                }
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
        // ������ȴ�������ɣ���������Success
        if (!waitForAnimationComplete)
        {
            return State.Success;
        }

        // ʹ�û���ʱ��ļ�⣨���ܸ��ã�
        if (Time.time - startTime < animationLength)
        {
            return State.Running;
        }
        
        return State.Success;
    }

    // ��ȡ���������ĳ��ȣ�ֻ��Startʱ����һ�Σ�
    private float GetAnimationLength(string animName)
    {
        // ���û�������Զ���ȡ��û������animator������Ĭ��ʱ��
        if (!autoGetAnimationLength || context.animator == null)
            return time;

        // ��ȡAnimatorController
        RuntimeAnimatorController runtimeController = context.animator.runtimeAnimatorController;
        if (runtimeController == null)
            return time;

        // �������ж���������ֻ�ڳ�ʼ��ʱִ��һ�Σ�
        foreach (AnimationClip clip in runtimeController.animationClips)
        {
            if (clip != null && clip.name == animName)
            {
                return clip.length;
            }
        }

        // ����Ҳ�����Ӧ�Ķ�������������Ĭ��ʱ��
        return time;
    }
}

public enum PlayType
{
    ������,
    �л�,
    ������
}