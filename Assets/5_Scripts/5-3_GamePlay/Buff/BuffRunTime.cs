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
        // 确保 Buff 配置已就绪（只在为空时重新获取）
        if (buff == null)
        {
            buff = GameRes.Instance.GetBuffData(buff_IDName).Clone();
        }

        // 只有在传入非空引用时才更新对应的引用和 Guid，避免把反序列化得到的 Guid 清零
        if (sender != null)
        {
            buff_Sender = sender;
            senderGuid = sender.itemData.Guid;
        }

        if (receiver != null)
        {
            buff_Receiver = receiver;
            receiverGuid = receiver.itemData.Guid;
        }
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