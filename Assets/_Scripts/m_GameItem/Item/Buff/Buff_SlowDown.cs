/*using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Buff_SlowDown : Buff
{
    public override void OnBuff_Init()
    {
        Debug.Log("���� Buff ��ʼ");
        if (buff_Receiver is ISpeed Speeder)
        {
            Speeder.Speed *= buff_RuntimeData.buff_Data.buff_Value; // ʹ�����������õı���
        }
    }

    public override void OnBuff_Update()
    {
        
    }

    public override void OnBuff_Stop()
    {
        if (buff_Receiver is ISpeed Speeder)
        {
            Speeder.Speed /= buff_RuntimeData.buff_Data.buff_Value; // �ָ�ԭ��
        }
    }


}*/

