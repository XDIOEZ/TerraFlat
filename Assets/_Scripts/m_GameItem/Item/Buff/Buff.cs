using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class Buff
{
    public void Run(Item item, BuffData data)
    {
        data.buff_CurrentDuration += Time.fixedDeltaTime;
        if (data.buff_CurrentDuration >= data.buff_MaxDuration)
        {
            OnBuffEnd(item, data);
        }
    }

    public abstract void Effect_Work(Item item, BuffData data);

    public abstract void Effect_Stop(Item item, BuffData data);

    protected virtual void OnBuffEnd(Item item, BuffData data)
    {
        Effect_Stop(item, data);
    }
}

