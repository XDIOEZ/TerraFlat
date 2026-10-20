using System;
using FlatWorld.Networking;
using UnityEngine;

public static class NetworkDimensionBridge
{
    #region 维度请求与网络适配边界
    public static Func<Player, string, int, bool, Action<bool, string>, bool> TransitionSender;
    public static Action<Player, string> PresentationReady;
    public static void NotifyPresentationReady(Player actor, string worldKey) => PresentationReady?.Invoke(actor, worldKey);
    public static Func<Player, WorldAddress, Vector3, Action, Action<bool, string>, bool> RespawnSender;
    public static bool RequestTransition(Player actor, string targetDimensionId, int portalGuid, bool generated, Action<bool, string> completed = null)
    {
        bool sent = GameNetwork.IsOnline ? TransitionSender?.Invoke(actor, targetDimensionId, portalGuid, generated, completed) == true
            : DimensionTransitionService.Request(actor, targetDimensionId, portalGuid, generated, completed);
        if (!sent) completed?.Invoke(false, "联机连接尚未就绪，无法切换维度。");
        return sent;
    }
    public static Func<Player, WorldAddress, Vector3, Action<bool, string>, bool> ExactTransferSender;
    public static bool RequestExactLocal(Player actor, WorldAddress target, Vector3 position, Action<bool, string> completed)
    {
        bool sent = GameNetwork.IsOnline && GameNetwork.HasStateAuthority &&
            ExactTransferSender?.Invoke(actor, target, position, completed) == true;
        if (!sent) completed?.Invoke(false, "权威恢复会话尚未准备好。");
        return sent;
    }
    public static bool RequestRespawn(Player actor, WorldAddress target, Vector3 position, Action preparePlayer, Action<bool, string> completed = null)
    {
        bool sent = GameNetwork.IsOnline ? RespawnSender?.Invoke(actor, target, position, preparePlayer, completed) == true
            : DimensionTransitionService.RequestRespawn(actor, completed);
        if (!sent) completed?.Invoke(false, "联机连接尚未就绪，无法跨维度重生。");
        return sent;
    }
    #endregion
}
