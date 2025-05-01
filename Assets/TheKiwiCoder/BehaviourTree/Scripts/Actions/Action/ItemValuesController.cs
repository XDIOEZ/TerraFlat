using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

[System.Serializable]
public struct ValueModification
{
    [Tooltip("数值名字")]
    public string name;
    [Tooltip("数值操作名字")]
    public string SpeedName;
    [Tooltip("要修改的数值")]
    public float SpeedValue;
    [Tooltip("如果 > 0，则为临时变化，单位：秒")]
    public float duration;
}



[NodeMenu("ActionNode/行动/修改血量")]
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
