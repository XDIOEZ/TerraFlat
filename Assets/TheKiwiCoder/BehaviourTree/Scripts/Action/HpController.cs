using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

// 定义一个枚举类型，用于指定数值修改的操作
public enum HpModifyOperation
{
    Null,       // 不修改
    Add,
    Subtract,
    Multiply,
    Divide
}


[System.Serializable]
public struct HpModification
{
    [Tooltip("要修改的数值")]
    public float value;

    [Tooltip("选择对该数值的修改方式，Null 表示不修改")]
    public HpModifyOperation operation;
}


[NodeMenu("ActionNode/行动/修改血量")]
public class HpController : ActionNode
{

    [Header("血量修改参数")]
    public HpModification hp;

    [Header("最大血量修改参数")]
    public HpModification maxHp;

    [Header("回血速度修改参数")]
    public HpModification hpChangeSpeed;

    protected override void OnStart()
    {
        // 可根据需要扩展初始化逻辑
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
