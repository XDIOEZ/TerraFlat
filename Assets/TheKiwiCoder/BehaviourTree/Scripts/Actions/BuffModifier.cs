using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;


[NodeMenu("ActionNode/ÐÐ¶¯/ÐÞ¸ÄBuff")]
public class BuffModifier : ActionNode
{
    public enum ModifierType
    {
        Add,
        Remove
    }
    public ModifierType Modifier_Type;
    public List<Buff_Data> Modifier_Buffs;

    protected override void OnStart()
    {
        switch (Modifier_Type)
        {
            case ModifierType.Add:
                foreach (Buff_Data buff in Modifier_Buffs)
                {
                    context.buffManager.AddBuff(buff);
                }
                break;
            case ModifierType.Remove:
                foreach (Buff_Data buff in Modifier_Buffs)
                {
                    context.buffManager.RemoveBuff(buff.buff_ID);
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