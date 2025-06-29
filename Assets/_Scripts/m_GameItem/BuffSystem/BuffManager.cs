using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BuffManager : MonoBehaviour
{
    public Item item;
    //public IBuff Buffer;
    [ShowInInspector]
    public Dictionary<string, BuffRunTime> BuffRunTimeData_Dic = new Dictionary<string, BuffRunTime>();

    public void Start()
    {
        item = GetComponentInParent<Item>();
        //Buffer = item.GetComponent<IBuff>();
        //if (Buffer == null)
        //{
        //    Debug.LogError("Item is not a Buff");
        //    return;
        //}
        Init();
    }

    public void AddBuffRuntime(Buff_Data buffData, Item Sender, Item Receiver)
    {
        BuffRunTime newBuff = new BuffRunTime
        {
            buffData = buffData,
            buff_Sender = Sender,
            buff_Receiver = Receiver,
        };
        AddBuffByData(newBuff);
    }
    public bool HasBuff(string buffId)
    {
        return BuffRunTimeData_Dic.ContainsKey(buffId); // 举例，如果用 Dictionary 存
    }


    void Init()
    {
        BuffRunTimeData_Dic = item.Item_Data.BuffRunTimeData_Dic;
    }

    public void AddBuffByData(BuffRunTime newBuff)
    {
        string buffID = newBuff.buff_IDName;

        if (BuffRunTimeData_Dic.TryGetValue(buffID, out var existingBuff))
        {
            switch (newBuff.buffData.buff_StackType)
            {
                case BuffStackType.DurationAdd:
                    float remainingTime = newBuff.buffData.buff_Duration - existingBuff.buff_CurrentDuration;
                    existingBuff.buff_CurrentDuration += remainingTime;
                    break;

                case BuffStackType.RefreshDuration:
                    existingBuff.buff_CurrentDuration = 0;
                    break;

                case BuffStackType.StackCount:
                    if (existingBuff.buff_CurrentStack < existingBuff.buffData.buff_MaxStack)
                    {
                        existingBuff.buff_CurrentStack += 1;
                    }
                    else
                    {
                        existingBuff.buff_CurrentDuration = 0;
                    }
                    break;

                case BuffStackType.Keep:
                    // 什么都不做，直接退出方法
                    return;
            }
        }
        else
        {
            // 第一次添加该Buff
            BuffRunTimeData_Dic[buffID] = newBuff;
            newBuff.OnBuff_Start();
        }
    }

    public void RemoveBuff(string buff_IDName)
    {
        if (!BuffRunTimeData_Dic.ContainsKey(buff_IDName))
        {
            return;
        }
        BuffRunTimeData_Dic[buff_IDName].OnBuff_Stop();

        BuffRunTimeData_Dic.Remove(buff_IDName);
    }

    // Update is called once per frame
    public void FixedUpdate()
    {
        foreach (var buff in BuffRunTimeData_Dic.Values)
        {
            buff.Run();
        }
    }
}
