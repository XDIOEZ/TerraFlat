using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Buff_SlowDown : Buff
{
    public override void Effect_Work(Item item, BuffData data)
    {
        Debug.Log("减速 Buff 开始");
        if (item is ISpeed Speeder)
        {
            Speeder.Speed *= data.buff_Value; // 使用数据中配置的倍率
        }
    }

    public override void Effect_Stop(Item item, BuffData data)
    {
        if (item is ISpeed Speeder)
        {
            Speeder.Speed /= data.buff_Value; // 恢复原速
        }
    }
}

