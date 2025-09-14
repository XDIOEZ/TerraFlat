using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine;

public class BuffManager : Module
{
    [ShowInInspector]
    public Dictionary<string, BuffRunTime> BuffRunTimeData_Dic = new Dictionary<string, BuffRunTime>();

    public Ex_ModData_MemoryPackable ModData;
    public override ModuleData _Data { get { return ModData; } set { ModData = (Ex_ModData_MemoryPackable)value; } }

    public override void Load()
    {
        ModData.ReadData(ref BuffRunTimeData_Dic);
    }


    public void Start()
    {
        Init();
    }
    public override void Save()
    {
        ModData.WriteData(BuffRunTimeData_Dic);
    }
    
    public void AddBuffRuntime(Buff_Data buffData, Item Receiver)
    {
       // Debug.Log($"������� Buff: {buffData?.name}, Sender: {Sender?.name}, Receiver: {Receiver?.name}");

        if (buffData == null || Receiver == null)
        {
            Debug.LogWarning("buffData �� Receiver Ϊ�գ�������� Buff");
            return;
        }

        BuffRunTime newBuff = new BuffRunTime
        {
            buff_IDName = buffData.buff_ID,
            buff = buffData,
            buff_Receiver = Receiver,
        };
        if(newBuff.buff.buff_ID == "")
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
        //ͬ��Data�󣬳�ʼ��Buff�� BuffData�ͽ����ߺͷ�����
        foreach (var buff in BuffRunTimeData_Dic.Values)
        {
            buff.SetBuffData(sender: null, receiver: item);
        }
    }
 
    public void AddBuffByData(BuffRunTime newBuff)
    {
        string buffID = newBuff.buff_IDName;

       
        if (HasBuff(buffID))
        {
            BuffRunTimeData_Dic.TryGetValue(buffID, out var existingBuff);
            switch (newBuff.buff.buff_StackType)
            {
                case BuffStackType.DurationAdd:
                    float remainingTime = newBuff.buff.buff_Duration - existingBuff.buff_CurrentDuration;
                    existingBuff.buff_CurrentDuration += remainingTime;
                    break;

                case BuffStackType.RefreshDuration:
                    existingBuff.buff_CurrentDuration = 0;
                    break;

                case BuffStackType.StackCount:
                    if (existingBuff.buff_CurrentStack < existingBuff.buff.buff_MaxStack)
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
