/*using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Buff_SlowDown : Buff
{
    public override void OnBuff_Init()
    {
        Debug.Log("减速 Buff 开始");
        if (buff_Receiver is ISpeed Speeder)
        {
            Speeder.Speed *= buff_RuntimeData.buff_Data.buff_Value; // 使用数据中配置的倍率
        }
    }

    public override void OnBuff_Update()
    {
        
    }

    public override void OnBuff_Stop()
    {
        if (buff_Receiver is ISpeed Speeder)
        {
            Speeder.Speed /= buff_RuntimeData.buff_Data.buff_Value; // 恢复原速
        }
    }


}*/

