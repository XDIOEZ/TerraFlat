using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

//TODO ����Զ������� �����б��浱ǰ�ڵ����Ҽ��˵��еĵ�ַ
[NodeMenu("ActionNode/Animator/���Ŷ���")]
public class AnimatorController : ActionNode
{

    // ͨ��һ��ö��ʵ�ֶ�SetTrigger��SetBool��ѡ��
    public enum ParameterType
    {
        SetTrigger,
        SetBool
    }

    [Header("��������")]
    public ParameterType parameterType = ParameterType.SetBool;

    [Header("����������")]
    public string animationName;

    [Header("SetBool ��ѡ�� (�� Trigger ��Ӱ��)")]
    public bool boolSwitch = true;
    [Header("---------------��������----------------")]
    [Header("���paramName�Ƿ������Animator��")]
    public bool checkParamName = true;

    protected override void OnStart()
    {
        // ��ѡ��ʼ���߼�
    }

    protected override void OnStop()
    {
        // ��ѡ�����߼�
    }

    protected override State OnUpdate()
    {
        if (context.animator == null)
        {
            Debug.LogWarning($"[{nameof(AnimatorController)}] Animator Ϊ null���޷����Ŷ�����");
            return State.Failure;
        }

        if (!HasParameter(context.animator, animationName, parameterType))
        {
            Debug.LogError($"[{nameof(AnimatorController)}] Animator ��δ�ҵ�������{animationName}�����ͣ�{parameterType}��");
            return State.Failure;
        }

        switch (parameterType)
        {
            case ParameterType.SetBool:
                context.animator.SetBool(animationName, boolSwitch);
                break;

            case ParameterType.SetTrigger:
                context.animator.SetTrigger(animationName);
                break;
        }

        return State.Success;
    }

    /// <summary>
    /// ��� Animator �Ƿ����ָ������
    /// </summary>
    private bool HasParameter(Animator animator, string paramName, ParameterType type)
    {
        var parameters = animator.parameters;
        foreach (var param in parameters)
        {
            if (param.name == paramName)
            {
                if ((type == ParameterType.SetTrigger && param.type == AnimatorControllerParameterType.Trigger) ||
                    (type == ParameterType.SetBool && param.type == AnimatorControllerParameterType.Bool))
                {
                    return true;
                }
            }
        }
        return false;
    }
}
