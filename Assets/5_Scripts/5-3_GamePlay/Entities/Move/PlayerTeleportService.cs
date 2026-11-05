using System;
using System.Collections;
using FlatWorld.Networking;
using FlatWorld.Spaceflight;
using UnityEngine;

/// <summary>地表传送的玩法入口；联机交给服务器确认，落点应用共用同一套规则。</summary>
public static class PlayerTeleportService
{
    #region 地表传送请求与落点

    public static Func<Item, Vector3, Action<bool>, bool> NetworkRequest;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetNetworkRequest() => NetworkRequest = null;

    public static bool TryTeleportToSurface(Item player, Vector3 destination, Action<bool> completed = null)
    {
        if (player?.itemData== null || !ActorProfileRegistry.IsLocallyControlled(player) || !ChunkMgr.IsSurfaceStreamingScene ||
            !float.IsFinite(destination.x) || !float.IsFinite(destination.y) || !float.IsFinite(destination.z))
            return false;

        Vector3 logical = WorldTopologyRuntime.NormalizePosition(destination);
        if (GameNetwork.IsOnline)
            return NetworkRequest?.Invoke(player, logical, succeeded =>
            {
                if (succeeded) Debug.Log($"[GM] 玩家已传送到逻辑位置: {logical}");
                else if (completed != null)
                    Debug.LogWarning("[GM] 本次传送已被更新的传送目标覆盖，或未通过服务器确认。");
                completed?.Invoke(succeeded);
            }) == true;

        ApplySurfacePosition(player, logical);
        Debug.Log($"[GM] 玩家已传送到逻辑位置: {logical}");
        completed?.Invoke(true);
        return true;
    }

    public static void ApplySurfacePosition(Item player, Vector3 logical)
    {
        ValidateSurfacePosition(player, logical);
        SpaceSession.Current?.ResetPassengerForRespawn(player);
        player.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover)?.DetachCarrier(null);
        ApplySurfacePose(player, logical);
    }

    // 已提交世界归属的重生与迁移只应用落点，不能再次重建乘员而覆盖权威位置。
    public static void ApplySurfacePose(Item player, Vector3 logical)
    {
        ValidateSurfacePosition(player, logical);
        logical = WorldTopologyRuntime.NormalizePosition(logical);
        Vector3 previous = player.transform.position;
        Vector3 presentation = WorldLocalPresentation.ProjectPosition(logical);
        Rigidbody2D body = WorldPhysicsService2D.GetItemRigidbody(player);
        if (body != null)
        {
            body.velocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.position = presentation;
        }
        player.transform.position = presentation;
        player.itemData.transform.position = logical;
        player.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover)?.ResetMotion();
        ItemMgr.GetExistingInstance()?.NotifyRuntimeItemMoved(player);
        if (ActorProfileRegistry.IsLocallyControlled(player))
        {
            player.itemMods.GetMod_ByID<Mod_Cam>(ModText.Camera)?.ShiftSpacePresentation(presentation - previous);
            RequestSurfaceStreaming(player);
        }
    }

    private static void ValidateSurfacePosition(Item player, Vector3 logical)
    {
        if (player == null || player.itemData == null) throw new ArgumentException("地表落点缺少有效玩家。", nameof(player));
        if (!float.IsFinite(logical.x) || !float.IsFinite(logical.y) || !float.IsFinite(logical.z))
            throw new ArgumentOutOfRangeException(nameof(logical), "地表落点必须是有限坐标。");
    }

    #endregion

    #region 地表到达的流送与就绪

    // 联机始终刷新观察者并集，不用单个玩家的兜底窗口覆盖其他玩家。
    public static void RequestSurfaceStreaming(Item player)
    {
        if (player == null || !ActorProfileRegistry.IsLocallyControlled(player)) return;
        if (GameNetwork.IsOnline)
        {
            SpaceNetworkBridge.RequestGroundStreamingRefresh();
            return;
        }
        Mod_ChunkLoader loader = player.itemMods.GetMod_ByID<Mod_ChunkLoader>(ModText.ChunkLoader);
        if (loader != null) loader.RefreshConfiguredChunkWindow();
        else ChunkMgr.ExistingInstance?.RefreshRuntimeWindow(player.transform.position, 2, 3,
            includeLocalPresentation: true, prefetchDistance: 3);
    }

    public static bool IsSurfaceArrivalReady(Item player, Vector3 logical)
    {
        ChunkMgr chunks = ChunkMgr.ExistingInstance;
        return player != null && player.itemData != null && ActorProfileRegistry.IsLocallyControlled(player) && chunks != null &&
            !string.IsNullOrWhiteSpace(player.itemData.GetActorProfileMetadata().WorldKey) &&
            float.IsFinite(logical.x) && float.IsFinite(logical.y) && float.IsFinite(logical.z) &&
            GameplayWorldRegistry.TryGet(player.itemData?.GetActorProfileMetadata().WorldKey, out GameplayWorldRuntime world) &&
            ReferenceEquals(world.Chunks, chunks) && player.gameObject.scene == world.Scene &&
            chunks.IsRuntimeArrivalPresentationReady(logical, ResolveArrivalDistance(player));
    }

    private static Vector2Int ResolveArrivalDistance(Item player)
    {
        Mod_ChunkLoader loader = player.itemMods.GetMod_ByID<Mod_ChunkLoader>(ModText.ChunkLoader);
        if (loader == null) return new Vector2Int(2, 2);
        return GameNetwork.IsOnline
            ? new Vector2Int(loader.CurrentLoadChunkDistance, loader.CurrentLoadChunkDistance)
            : loader.CurrentLoadChunkDistanceXY;
    }

    // 只有到达事务等待期间检查窗口；缺失或被临时准备窗口覆盖时限频补发，不增加日常轮询。
    public static IEnumerator WaitForSurfaceArrival(Item player, Vector3 logical,
        Action<float> reportProgress = null, Func<bool> stillValid = null)
    {
        ValidateSurfacePosition(player, logical);
        string world = player.itemData.GetActorProfileMetadata().WorldKey;
        if (string.IsNullOrWhiteSpace(world)) throw new InvalidOperationException("地表到达缺少目标世界。");
        RequestSurfaceStreaming(player);
        yield return null;
        float retryAt = Time.realtimeSinceStartup + 1f;
        float warningAt = Time.realtimeSinceStartup + 12f;
        float progress = .25f;
        while (true)
        {
            if (player == null || !ActorProfileRegistry.IsLocallyControlled(player) || player != ItemMgr.GetExistingInstance()?.LocalControlledActor ||
                player.itemData?.GetActorProfileMetadata().WorldKey != world ||
                stillValid?.Invoke() == false)
                throw new InvalidOperationException("等待地表落点时玩家、世界或会话已改变。");
            if (IsSurfaceArrivalReady(player, logical)) break;
            ChunkMgr chunks = ChunkMgr.ExistingInstance;
            if (Time.realtimeSinceStartup >= retryAt &&
                (chunks == null || !chunks.HasRuntimeArrivalWindow(logical, ResolveArrivalDistance(player))))
            {
                RequestSurfaceStreaming(player);
                retryAt = Time.realtimeSinceStartup + 1f;
            }
            if (Time.realtimeSinceStartup >= warningAt)
            {
                Debug.LogWarning($"[玩家地表到达] 等待目标窗口：world={world}, position={logical}, " +
                    (chunks != null ? chunks.DescribeRuntimeEntityPresentationWait(logical) : "区块管理器尚未就绪"), player);
                warningAt = Time.realtimeSinceStartup + 12f;
            }
            progress = Mathf.MoveTowards(progress, .98f, Time.unscaledDeltaTime * .08f);
            reportProgress?.Invoke(progress);
            yield return null;
        }
        reportProgress?.Invoke(1f);
    }

    #endregion
}
