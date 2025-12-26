using MemoryPack;
using UnityEngine;

[MemoryPackable]
[System.Serializable]
public partial class BuffRunTime
{
    public string buff_IDName;
    public float buff_CurrentDuration = 0;
    public float buff_CurrentStack = 1;

    // 持久化存储 Buff 发送者和接收者对应 Item 的唯一 Guid（ItemData.Guid）
    public int senderGuid;
    public int receiverGuid;

    [MemoryPackIgnore]
    public Buff_Data buff;
    [MemoryPackIgnore]
    public Item buff_Sender;
    [MemoryPackIgnore]
    public Item buff_Receiver;

    private float lastUpdateCheckTime = 0f;

    public void SetBuffData(Item sender, Item receiver)
    {
        buff = GameRes.Instance.GetBuffData(buff_IDName).Clone();
        buff_Sender = sender;
        buff_Receiver = receiver;

        // 同步 Guid，便于存档后通过 Guid 重新绑定 Item 引用
        senderGuid = sender != null ? sender.itemData.Guid : 0;
        receiverGuid = receiver != null ? receiver.itemData.Guid : 0;
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