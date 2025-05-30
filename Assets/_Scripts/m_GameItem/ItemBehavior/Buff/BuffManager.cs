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
        BuffLogic_Dic.Add("减速Buff", new Buff_SlowDown());
    }

    public void AddBuffByData(BuffData newBuff)
    {
        // 如果已有相同名称 Buff，先停止其效果，再覆盖
        if (buffData_Dic.TryGetValue(newBuff.buff_Name, out BuffData oldBuff))
        {
            if (BuffLogic_Dic.TryGetValue(oldBuff.buff_Type, out Buff oldLogic))
            {
                oldLogic.Effect_Stop(item, oldBuff);
            }

            // 覆盖旧 Buff 数据
            buffData_Dic[newBuff.buff_Name] = newBuff;
        }
        else
        {
            // 添加新 Buff 数据
            buffData_Dic.Add(newBuff.buff_Name, newBuff);
        }

        // 激活新 Buff 的效果
        if (BuffLogic_Dic.TryGetValue(newBuff.buff_Type, out Buff newLogic))
        {
            newLogic.Effect_Work(item, newBuff);
        }
        else
        {
            Debug.LogWarning($"未注册的 Buff 类型：{newBuff.buff_Type}");
        }
    }


    public void RemoveBuffByData(BuffData newBuff)
    {
       string buffName = newBuff.buff_Name;
        // 先查找是否存在此 Buff
        if (buffData_Dic.TryGetValue(buffName, out BuffData buff))
        {
            // 停止 Buff 效果
            if (BuffLogic_Dic.TryGetValue(buff.buff_Type, out Buff buffLogic))
            {
                buffLogic.Effect_Stop(item, buff);
            }
            else
            {
                Debug.LogWarning($"未注册的 Buff 类型：{buff.buff_Type}");
            }

            // 从字典中移除
            buffData_Dic.Remove(buffName);
        }
        else
        {
            Debug.LogWarning($"试图移除不存在的 Buff：{buffName}");
        }
    }



    // Update is called once per frame
    public void FixedUpdate()
    {
        //遍历字典中的Buff，调用Run方法
        foreach (var buff in buffData_Dic.Values)
        {
            BuffLogic_Dic[buff.buff_Type].Run(item,buff);
        }
    }
}
