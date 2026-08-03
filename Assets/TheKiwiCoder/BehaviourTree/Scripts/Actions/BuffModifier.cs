using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;


[NodeMenu("ActionNode/行动/修改Buff")]
public class BuffModifier : ActionNode
{
    public enum ModifierType
    {
        Add,
        Remove
    }
    public ModifierType Modifier_Type;
    public List<string> Modifier_Buffs = new();

    protected override void OnStart()
    {
        switch (Modifier_Type)
        {
            case ModifierType.Add:
                foreach (string buffId in Modifier_Buffs)
                {
                    context.buffManager.AddBuff(buffId);
                }
                break;
            case ModifierType.Remove:
                foreach (string buffId in Modifier_Buffs)
                {
                    context.buffManager.RemoveBuff(buffId);
                }
                break;
        }
    }

    protected override void OnStop() {
    }

    protected override State OnUpdate() {
        return State.Success;
    }
}
