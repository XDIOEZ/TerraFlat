using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BuffRunTime
{
    //buff运行时数据
    public BuffSaveData buff_SaveData;

    [MemoryPackIgnore]
    //引用的buff数据
    public Buff_Info buff_Info;
    [MemoryPackIgnore]
    //Buff的发出者
    public Item buff_Sender;
    [MemoryPackIgnore]
    //Buff的接收者
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
    //Buff初始化
    public void OnBuff_Start()
    {
        buff_Info.buff_Behavior_Start.Apply(this);
    }
    //Buff更新
    public  void OnBuff_Update()
    {
        buff_Info.buff_Behavior_Update.Apply(this);

    }
    //停止Buff
    public  void OnBuff_Stop()
    {
        buff_Info.buff_Behavior_Stop.Apply(this);
    }
}

//buff运行时数据
[MemoryPackable]
[System.Serializable]
public partial class BuffSaveData
{
    //buff的唯一标识符
    public string buff_IDName;
    //buff发出者的Guid
    public int buff_Sender_Guid;
    //buff接收者的Guid
    public int buff_Receiver_Guid;

    //buff当前持续时间
    public float buff_CurrentDuration = 0;
    //Buff当前的层数
    public float buff_CurrentStack = 1;

    //创建一个新的BuffSaveData
    public BuffSaveData(string buff_IDName, int buff_Sender_Guid, int buff_Receiver_Guid)
    {
        this.buff_IDName = buff_IDName;
        this.buff_Sender_Guid = buff_Sender_Guid;
        this.buff_Receiver_Guid = buff_Receiver_Guid;
    }
}

