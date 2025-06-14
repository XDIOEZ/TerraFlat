using MemoryPack;
using UnityEngine;

public class BuffRunTime
{
    public string buff_IDName;
    public int buff_Sender_Guid;
    public int buff_Receiver_Guid;
    public float buff_CurrentDuration = 0;
    public float buff_CurrentStack = 1;

    [MemoryPackIgnore]
    public Buff_Data buff_Data;
    [MemoryPackIgnore]
    public Item buff_Sender;
    [MemoryPackIgnore]
    public Item buff_Receiver;

    private float lastUpdateCheckTime = 0f;

    public BuffRunTime(Buff_Data buff_Info, Item buff_Sender, Item buff_Receiver)
    {
        this.buff_Sender = buff_Sender;
        this.buff_Receiver = buff_Receiver;
        this.buff_Data = buff_Info;
        this.buff_IDName = buff_Info.buff_ID;
        this.buff_Sender_Guid = buff_Sender.Item_Data.Guid;
        this.buff_Receiver_Guid = buff_Receiver.Item_Data.Guid;
    }

    public void Run()
    {
        buff_CurrentDuration += Time.fixedDeltaTime;

        OnBuff_Update();

        if (buff_CurrentDuration >= buff_Data.buff_Duration)
        {
            OnBuff_Stop();
        }
    }

    public void OnBuff_Start()
    {
        buff_Data.buff_Behavior_Start.Apply(this);
    }

    public void OnBuff_Update()
    {
        if (buff_Data.buff_Behavior_Update == null)
            return;

        float interval = buff_Data.buff_Interval;

        if (interval < 0f)
            return;

        if (buff_CurrentDuration >= lastUpdateCheckTime + interval)
        {
            buff_Data.buff_Behavior_Update.Apply(this);
            lastUpdateCheckTime = buff_CurrentDuration;
        }
    }

    public void OnBuff_Stop()
    {
        buff_Data.buff_Behavior_Stop.Apply(this);
    }
}