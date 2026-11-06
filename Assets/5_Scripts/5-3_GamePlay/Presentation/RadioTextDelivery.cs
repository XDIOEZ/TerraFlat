using System;
using FlatWorld.Audio;
using UnityEngine;

/// <summary>无线电只投递实际接收设备的消息，表现与网络订阅各自处理。</summary>
public static class RadioTextDelivery
{
    #region 接收设备、玩家与播放
    public const float StationListeningDistance = 24f;
    public static event Action<Item, string, RadioTextMessage> RemoteDeliveryRequested;
    public static event Action<Item, RadioTextMessage> Presented;
    public static event Action<Item, string> Feedback;

    public static void Deliver(Item receiver, RadioTextMessage message)
    {
        if (receiver == null) return;
        string world = GameplayWorldRegistry.GetItemWorldKey(receiver);
        if (ActorProfileRegistry.IsLocallyControlled(receiver)) Present(receiver, message);
        else RemoteDeliveryRequested?.Invoke(receiver, world, message);
    }

    public static void DeliverAt(string worldKey, RadioTextMessage message)
    {
        RemoteDeliveryRequested?.Invoke(null, worldKey, message);
        Item player = ItemMgr.GetExistingInstance()?.LocalControlledActor;
        if (player != null && GameplayWorldRegistry.GetItemWorldKey(player) == worldKey &&
            WorldTopologyRuntime.ShortestDelta(player.transform.position, message.ReceiverPosition).sqrMagnitude <=
            StationListeningDistance * StationListeningDistance) Present(player, message);
    }

    public static void Present(Item receiver, RadioTextMessage message)
    {
        if (receiver == null || !ActorProfileRegistry.IsLocallyControlled(receiver)) return;
        if (!string.IsNullOrEmpty(message.MessageId) && RadioBroadcastMessages.TryGet(message.MessageId, out string localized))
            message = new RadioTextMessage(message.WorldKey, message.SourceId, message.ReceiverId, message.Epoch,
                message.Sequence, message.Channel, message.SourcePosition, message.Sender, localized, message.MessageId, message.ReceiverPosition);
        AudioService.Instance.PlayAt("radio.receive", WorldLocalPresentation.ProjectPosition(message.ReceiverPosition));
        Presented?.Invoke(receiver, message);
    }
    public static void ReportFeedback(Item receiver, string text) => Feedback?.Invoke(receiver, text);
    #endregion
}

public static class RadioTextInputBridge
{
    #region 正式聊天输入桥接
    public static event Func<Item, Func<string, bool>, bool> OpenRequested;
    public static bool Open(Item player, Func<string, bool> submit)
    {
        if (OpenRequested == null) return false;
        foreach (Func<Item, Func<string, bool>, bool> handler in OpenRequested.GetInvocationList())
            if (handler(player, submit)) return true;
        return false;
    }
    #endregion
}
