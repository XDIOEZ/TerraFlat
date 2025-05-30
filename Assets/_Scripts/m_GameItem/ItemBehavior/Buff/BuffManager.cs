using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BuffManager : MonoBehaviour
{
    public Item item;
    [ShowInInspector]
    public Dictionary<string, BuffData> buffData_Dic = new Dictionary<string, BuffData>();
    [ShowInInspector]
    public Dictionary<string, Buff> BuffLogic_Dic = new Dictionary<string, Buff>();

    public void Start()
    {
        BuffLogic_Dic.Add("����Buff", new Buff_SlowDown());
    }

    public void AddBuffByData(BuffData newBuff)
    {
        // ���������ͬ���� Buff����ֹͣ��Ч�����ٸ���
        if (buffData_Dic.TryGetValue(newBuff.buff_Name, out BuffData oldBuff))
        {
            if (BuffLogic_Dic.TryGetValue(oldBuff.buff_Type, out Buff oldLogic))
            {
                oldLogic.Effect_Stop(item, oldBuff);
            }

            // ���Ǿ� Buff ����
            buffData_Dic[newBuff.buff_Name] = newBuff;
        }
        else
        {
            // ����� Buff ����
            buffData_Dic.Add(newBuff.buff_Name, newBuff);
        }

        // ������ Buff ��Ч��
        if (BuffLogic_Dic.TryGetValue(newBuff.buff_Type, out Buff newLogic))
        {
            newLogic.Effect_Work(item, newBuff);
        }
        else
        {
            Debug.LogWarning($"δע��� Buff ���ͣ�{newBuff.buff_Type}");
        }
    }


    public void RemoveBuffByData(BuffData newBuff)
    {
       string buffName = newBuff.buff_Name;
        // �Ȳ����Ƿ���ڴ� Buff
        if (buffData_Dic.TryGetValue(buffName, out BuffData buff))
        {
            // ֹͣ Buff Ч��
            if (BuffLogic_Dic.TryGetValue(buff.buff_Type, out Buff buffLogic))
            {
                buffLogic.Effect_Stop(item, buff);
            }
            else
            {
                Debug.LogWarning($"δע��� Buff ���ͣ�{buff.buff_Type}");
            }

            // ���ֵ����Ƴ�
            buffData_Dic.Remove(buffName);
        }
        else
        {
            Debug.LogWarning($"��ͼ�Ƴ������ڵ� Buff��{buffName}");
        }
    }



    // Update is called once per frame
    public void FixedUpdate()
    {
        //�����ֵ��е�Buff������Run����
        foreach (var buff in buffData_Dic.Values)
        {
            BuffLogic_Dic[buff.buff_Type].Run(item,buff);
        }
    }
}
