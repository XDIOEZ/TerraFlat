using MemoryPack;
using UnityEngine;

[MemoryPackable]
[System.Serializable]
public partial class BuffRunTime
{
    public string buff_IDName;
    public int buff_Sender_Guid;
    public int buff_Receiver_Guid;
    public float buff_CurrentDuration = 0;
    public float buff_CurrentStack = 1;

    [MemoryPackIgnore]
    public Buff_Data buffData;
    [MemoryPackIgnore]
    public Item buff_Sender;
    [MemoryPackIgnore]
    public Item buff_Receiver;

    private float lastUpdateCheckTime = 0f;

    public void SetItemSenderAndReceiver()
    {
        buff_Sender = GameItemManager .Instance.GetItemByGuid(buff_Sender_Guid);
        buff_Receiver = GameItemManager.Instance.GetItemByGuid(buff_Receiver_Guid);
        buffData = GameRes.Instance.GetBuffData(buff_IDName);
    }

    public void Run()
    {
        buff_CurrentDuration += Time.fixedDeltaTime;

        OnBuff_Update();

        if (buff_CurrentDuration >= buffData.buff_Duration)
        {
            OnBuff_Stop();
        }
    }

    public void OnBuff_Start()
    {
        buffData.buff_Behavior_Start.Apply(this);
    }

    public void OnBuff_Update()
    {
        if (buffData.buff_Behavior_Update == null)
            return;

        float interval = buffData.buff_Interval;

        if (interval < 0f)
            return;

        if (buff_CurrentDuration >= lastUpdateCheckTime + interval)
        {
            buffData.buff_Behavior_Update.Apply(this);
            lastUpdateCheckTime = buff_CurrentDuration;
        }
    }

    public void OnBuff_Stop()
    {
        buffData.buff_Behavior_Stop.Apply(this);
    }
}