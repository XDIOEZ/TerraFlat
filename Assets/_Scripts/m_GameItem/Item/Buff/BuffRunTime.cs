using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BuffRunTime
{
    //buff����ʱ����
    public BuffSaveData buff_SaveData;

    [MemoryPackIgnore]
    //���õ�buff����
    public Buff_Info buff_Info;
    [MemoryPackIgnore]
    //Buff�ķ�����
    public Item buff_Sender;
    [MemoryPackIgnore]
    //Buff�Ľ�����
    public Item buff_Receiver;

    public BuffRunTime(Buff_Info buff_Info, Item buff_Sender,Item buff_Receiver)
    {
      
        this.buff_Sender = buff_Sender;
        this.buff_Receiver = buff_Receiver;
        this.buff_Info = buff_Info;
        this.buff_SaveData = new BuffSaveData(buff_Info.buff_ID, buff_Sender.Item_Data.Guid, buff_Receiver.Item_Data.Guid);
    }
    public void Run()
    {
        buff_SaveData.buff_CurrentDuration += Time.fixedDeltaTime;

        if (buff_SaveData.buff_CurrentDuration >= buff_Info.buff_Duration)
        {
            OnBuff_Stop();
        }
    }
    //Buff��ʼ��
    public void OnBuff_Start()
    {
        buff_Info.buff_Behavior_Start.Apply(this);
    }
    //Buff����
    public  void OnBuff_Update()
    {
        buff_Info.buff_Behavior_Update.Apply(this);

    }
    //ֹͣBuff
    public  void OnBuff_Stop()
    {
        buff_Info.buff_Behavior_Stop.Apply(this);
    }
}

//buff����ʱ����
[MemoryPackable]
[System.Serializable]
public partial class BuffSaveData
{
    //buff��Ψһ��ʶ��
    public string buff_IDName;
    //buff�����ߵ�Guid
    public int buff_Sender_Guid;
    //buff�����ߵ�Guid
    public int buff_Receiver_Guid;

    //buff��ǰ����ʱ��
    public float buff_CurrentDuration = 0;
    //Buff��ǰ�Ĳ���
    public float buff_CurrentStack = 1;

    //����һ���µ�BuffSaveData
    public BuffSaveData(string buff_IDName, int buff_Sender_Guid, int buff_Receiver_Guid)
    {
        this.buff_IDName = buff_IDName;
        this.buff_Sender_Guid = buff_Sender_Guid;
        this.buff_Receiver_Guid = buff_Receiver_Guid;
    }
}

