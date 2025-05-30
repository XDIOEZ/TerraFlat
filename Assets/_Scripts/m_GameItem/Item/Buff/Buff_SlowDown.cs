using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Buff_SlowDown : Buff
{
    public override void Effect_Work(Item item, BuffData data)
    {
        Debug.Log("���� Buff ��ʼ");
        if (item is ISpeed Speeder)
        {
            Speeder.Speed *= data.buff_Value; // ʹ�����������õı���
        }
    }

    public override void Effect_Stop(Item item, BuffData data)
    {
        if (item is ISpeed Speeder)
        {
            Speeder.Speed /= data.buff_Value; // �ָ�ԭ��
        }
    }
}

