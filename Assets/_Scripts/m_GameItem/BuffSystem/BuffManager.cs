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
        Init();
    }

    public void AddBuffRuntime(Buff_Data buffData, Item Sender, Item Receiver)
    {
        Debug.Log($"������� Buff: {buffData?.name}, Sender: {Sender?.name}, Receiver: {Receiver?.name}");

        if (buffData == null || Receiver == null)
        {
            Debug.LogWarning("buffData �� Receiver Ϊ�գ�������� Buff");
            return;
        }

        BuffRunTime newBuff = new BuffRunTime
        {
            buff_IDName = buffData.buff_ID,
            buffData = buffData,
            buff_Sender = Sender,
            buff_Sender_Guid = Sender.itemData.Guid,
            buff_Receiver = Receiver,
            buff_Receiver_Guid = Receiver.itemData.Guid,
        };
        if(newBuff.buffData.buff_ID == "")
        {
            Debug.LogError("Buff ID Ϊ�գ�������� Buff");
            return;
        }

        AddBuffByData(newBuff);
    }

    public bool HasBuff(string buffId)
    {
        return BuffRunTimeData_Dic.ContainsKey(buffId); // ����������� Dictionary ��
    }


    void Init()
    {
        BuffRunTimeData_Dic = item.itemData.BuffRunTimeData_Dic;
        //ͬ��Data�󣬳�ʼ��Buff�� BuffData�ͽ����ߺͷ�����
        foreach (var buff in BuffRunTimeData_Dic.Values)
        {
            buff.SetItemSenderAndReceiver();
        }
    }
 
    public void AddBuffByData(BuffRunTime newBuff)
    {
        string buffID = newBuff.buff_IDName;

       
        if (HasBuff(buffID))
        {
            BuffRunTimeData_Dic.TryGetValue(buffID, out var existingBuff);
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
                    // ʲô��������ֱ���˳�����
                    return;
            }
        }
        else
        {
            // ��һ����Ӹ�Buff
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
