using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

[System.Serializable]
public struct ValueModification
{
    [Tooltip("��ֵ����")]
    public string name;
    [Tooltip("��ֵ��������")]
    public string SpeedName;
    [Tooltip("Ҫ�޸ĵ���ֵ")]
    public float SpeedValue;
    [Tooltip("��� > 0����Ϊ��ʱ�仯����λ����")]
    public float duration;
}



[NodeMenu("ActionNode/�ж�/�޸�Ѫ��")]
public class ItemValuesController : ActionNode
{
    public ValueModification valueModification;
    protected override void OnStart()
    {
    }

    protected override void OnStop()
    {
    }

    protected override State OnUpdate()
    {
        context.itemValues.ItemValues.Add_ChangeSpeed(valueModification.name, valueModification.SpeedName, valueModification.SpeedValue, valueModification.duration);
        return State.Success;
    }

}
