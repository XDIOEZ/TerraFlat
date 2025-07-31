using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

public class HungryChecker : ActionNode
{
    public Mod_Food mod_Food;

    [Tooltip("������ֵ���ٷֱȣ�")]
    [Range(0, 1)] // 0~100%�Ļ�����
    public float hungryThreshold = 0.6f; // Ĭ��60%

    protected override void OnStart() 
    {
        if(mod_Food == null)
        {
            mod_Food = context.item.Mods[ModText.Food] as Mod_Food;
        }
    }

    protected override void OnStop() {
    }

    protected override State OnUpdate() 
    {
        if(mod_Food.Data.nutrition.GetHungerRate() <= hungryThreshold)
        {
            return State.Success;
        }
        
        return State.Failure;
    }
}
