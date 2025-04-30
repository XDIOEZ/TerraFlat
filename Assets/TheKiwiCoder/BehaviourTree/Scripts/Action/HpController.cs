using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

// ����һ��ö�����ͣ�����ָ����ֵ�޸ĵĲ���
public enum HpModifyOperation
{
    Null,       // ���޸�
    Add,
    Subtract,
    Multiply,
    Divide
}


[System.Serializable]
public struct HpModification
{
    [Tooltip("Ҫ�޸ĵ���ֵ")]
    public float value;

    [Tooltip("ѡ��Ը���ֵ���޸ķ�ʽ��Null ��ʾ���޸�")]
    public HpModifyOperation operation;
}


[NodeMenu("ActionNode/�ж�/�޸�Ѫ��")]
public class HpController : ActionNode
{

    [Header("Ѫ���޸Ĳ���")]
    public HpModification hp;

    [Header("���Ѫ���޸Ĳ���")]
    public HpModification maxHp;

    [Header("��Ѫ�ٶ��޸Ĳ���")]
    public HpModification hpChangeSpeed;

    protected override void OnStart()
    {
        // �ɸ�����Ҫ��չ��ʼ���߼�
    }

    protected override void OnStop()
    {
    }

    protected override State OnUpdate()
    {
        ApplyModification(hp, ref context.health.Hp.value);
        ApplyModification(maxHp, ref context.health.Hp.maxValue);
        ApplyModification(hpChangeSpeed, ref context.health.Hp.HpChangSpeed);
        return State.Success;
    }
    private void ApplyModification(HpModification mod, ref float target)
    {
        if (mod.operation == HpModifyOperation.Null)
            return;

        switch (mod.operation)
        {
            case HpModifyOperation.Add:
                target += mod.value;
                break;
            case HpModifyOperation.Subtract:
                target -= mod.value;
                break;
            case HpModifyOperation.Multiply:
                target *= mod.value;
                break;
            case HpModifyOperation.Divide:
                if (mod.value != 0)
                {
                    target /= mod.value;
                }
                break;
        }
    }

}
