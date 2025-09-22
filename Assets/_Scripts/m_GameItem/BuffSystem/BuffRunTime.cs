using MemoryPack;
using UnityEngine;

[MemoryPackable]
[System.Serializable]
public partial class BuffRunTime
{
    public string buff_IDName;
    public float buff_CurrentDuration = 0;
    public float buff_CurrentStack = 1;

    [MemoryPackIgnore]
    public Buff_Data buff;
    [MemoryPackIgnore]
    public Item buff_Sender;
    [MemoryPackIgnore]
    public Item buff_Receiver;

    private float lastUpdateCheckTime = 0f;

    public void SetBuffData(Item sender, Item receiver)
    {
        buff = GameRes.Instance.GetBuffData(buff_IDName);
        buff_Sender = sender;
        buff_Receiver = receiver;
    }

    public void Run()
    {
        buff_CurrentDuration += Time.fixedDeltaTime;

        OnBuff_Update();

        if (buff_CurrentDuration >= buff.buff_Duration)
        {
            OnBuff_Stop();
        }
    }

    public void OnBuff_Start()
    {
        if (buff.buff_Behavior_Start == null)
            return;
        buff.buff_Behavior_Start.Apply(this);
    }

    public void OnBuff_Update()
    {
        if (buff.buff_Behavior_Update == null)
            return;

        float interval = buff.buff_Interval;

        if (interval < 0f)
            return;

        if (buff_CurrentDuration >= lastUpdateCheckTime + interval)
        {
            buff.buff_Behavior_Update.Apply(this);
            lastUpdateCheckTime = buff_CurrentDuration;
        }
    }

    public void OnBuff_Stop()
    {
        if (buff.buff_Behavior_Stop == null)
            return;
        buff.buff_Behavior_Stop.Apply(this);
    }
}