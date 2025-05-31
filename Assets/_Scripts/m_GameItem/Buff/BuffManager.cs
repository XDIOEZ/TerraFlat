using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BuffManager : MonoBehaviour
{
    public Item item;
    [ShowInInspector]
    public Dictionary<string, BuffRunTime> BuffRunTimeData_Dic = new Dictionary<string, BuffRunTime>();
    [ShowInInspector]
    public Dictionary<string, BuffSaveData> BuffSaveData_Dic = new Dictionary<string, BuffSaveData>();

    public void Start()
    {
    }

    public void AddBuffByData(BuffRunTime newBuff)
    {
        string buffID = newBuff.buff_SaveData.buff_IDName;

        if (BuffRunTimeData_Dic.TryGetValue(buffID, out var existingBuff))
        {
            switch (newBuff.buff_Info.buff_StackType)
            {
                case BuffStackType.DurationAdd:
                    float remainingTime = newBuff.buff_Info.buff_Duration - existingBuff.buff_SaveData.buff_CurrentDuration;
                    existingBuff.buff_SaveData.buff_CurrentDuration += remainingTime;
                    break;

                case BuffStackType.RefreshDuration:
                    existingBuff.buff_SaveData.buff_CurrentDuration = 0;
                    break;

                case BuffStackType.StackCount:
                    if (existingBuff.buff_SaveData.buff_CurrentStack < existingBuff.buff_Info.buff_MaxStack)
                    {
                        existingBuff.buff_SaveData.buff_CurrentStack += 1;
                    }
                    else
                    {
                        existingBuff.buff_SaveData.buff_CurrentDuration = 0;
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
            BuffSaveData_Dic[buffID] = newBuff.buff_SaveData;
            newBuff.OnBuff_Start();
        }
    }





    public void StopBuff(string buff_IDName)
    {
        if (!BuffRunTimeData_Dic.ContainsKey(buff_IDName))
        {
            return;
        }
        BuffRunTimeData_Dic[buff_IDName].OnBuff_Stop();

        BuffRunTimeData_Dic.Remove(buff_IDName);
        BuffSaveData_Dic.Remove(buff_IDName);
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
