using System;
using FlatWorld.Networking;
using FlatWorld.Spaceflight;
using UnityEngine;

/// <summary>地表传送的玩法入口；联机交给服务器确认，落点应用共用同一套规则。</summary>
public static class PlayerTeleportService
{
    #region 地表传送请求与落点

    public static Func<Player, Vector3, Action<bool>, bool> NetworkRequest;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetNetworkRequest() => NetworkRequest = null;

    public static bool TryTeleportToSurface(Player player, Vector3 destination, Action<bool> completed = null)
    {
        if (player?.Data == null || !player.IsLocalProfile || !ChunkMgr.IsSurfaceStreamingScene ||
            !float.IsFinite(destination.x) || !float.IsFinite(destination.y) || !float.IsFinite(destination.z))
            return false;

        Vector3 logical = WorldTopologyRuntime.NormalizePosition(destination);
        if (GameNetwork.IsOnline)
            return NetworkRequest?.Invoke(player, logical, succeeded =>
            {
                if (succeeded) Debug.Log($"[GM] 玩家已传送到逻辑位置: {logical}");
                else Debug.LogWarning("[GM] 联机传送未完成，服务器拒绝了请求或连接已结束。");
                completed?.Invoke(succeeded);
            }) == true;

        ApplySurfacePosition(player, logical);
        Debug.Log($"[GM] 玩家已传送到逻辑位置: {logical}");
        completed?.Invoke(true);
        return true;
    }

    public static void ApplySurfacePosition(Player player, Vector3 logical)
    {
        // 传送同步清理乘船、速度和角色索引，防止旧位置继续驱动角色。
        SpaceSession.Current?.ResetPassengerForRespawn(player);
        Vector3 presentation = WorldLocalPresentation.ProjectPosition(logical);
        player.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover)?.ResetSwimmingAudio();
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        if (body != null)
        {
            body.velocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.position = presentation;
        }
        player.transform.position = presentation;
        player.Data.transform.position = logical;
        ItemMgr.Instance?.NotifyRuntimeItemMoved(player);
        if (player.IsLocalProfile)
            player.itemMods.GetMod_ByID<Mod_ChunkLoader>(ModText.ChunkLoader)?.RefreshChunksAroundPlayer();
    }

    #endregion
}
