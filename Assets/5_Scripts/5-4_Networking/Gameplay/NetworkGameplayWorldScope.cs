using System;
using FlatWorld.Spaceflight;
using Mirror;

namespace FlatWorld.Networking.Gameplay
{
    public interface INetworkWorldRequest { uint WorldRevision { get; } }
    internal static class NetworkGameplayWorldScope
    {
        #region 连接绑定的世界上下文
        public static uint LocalRevision => NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>()?.RealmRevision ?? 0;
        // 首次同步只能在本地网络角色与服务器世界身份完全对应后发送。
        internal static bool CanRequestInitialSurfaceState(bool clientReady, bool playerReady,
            bool worldTransferPending, string remoteWorld, string localWorld)
            => clientReady && playerReady && !worldTransferPending &&
               !string.IsNullOrWhiteSpace(remoteWorld) &&
               string.Equals(remoteWorld, localWorld, StringComparison.Ordinal);

        public static bool TryGetReadyLocalSurface(out uint realmRevision)
        {
            realmRevision = 0;
            if (!NetworkClient.active || NetworkServer.active ||
                GameManager.ExistingInstance?.IsInGameWorld != true)
                return false;

            NetworkWorldPlayer player = NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>();
            if (player == null || !player.TryGetSpaceObserver(out SpaceNetworkObserver observer)) return false;
            bool playerReady = player.CorePlayer?.IsInitialized == true && !player.IsInSpace && !observer.InSpace &&
                player.CorePlayer.Data?.CurrentSceneName == observer.WorldKey &&
                player.RealmRevision == observer.RealmRevision;
            if (!CanRequestInitialSurfaceState(NetworkClient.ready, playerReady, player.IsWorldTransferPending,
                    observer.WorldKey, GameplayWorldRegistry.LocalPresentationWorldKey)) return false;

            realmRevision = observer.RealmRevision;
            return true;
        }
        public static bool TryGetReadyLocalItemWorld(out uint realmRevision, out string worldKey)
        {
            worldKey = null;
            realmRevision = 0;
            if (!NetworkClient.active || NetworkServer.active || !NetworkClient.ready ||
                GameManager.ExistingInstance?.IsInGameWorld != true) return false;
            NetworkWorldPlayer player = NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>();
            string ownerWorld = GameplayWorldRegistry.GetItemWorldKey(player?.CorePlayer);
            if (!GameplayWorldRegistry.UsesLocalCoordinates(ownerWorld))
            {
                if (!TryGetReadyLocalSurface(out realmRevision)) return false;
                worldKey = GameplayWorldRegistry.LocalPresentationWorldKey;
                return true;
            }
            SpaceSession session = SpaceSession.Current;
            if (player?.CorePlayer?.IsInitialized != true || player.IsWorldTransferPending ||
                !player.TryGetSpaceObserver(out SpaceNetworkObserver observer) || player.RealmRevision != observer.RealmRevision ||
                session == null || !session.IsPlayerInShipInteriorWorld(player.CorePlayer, out ShipState ship, out ShipInteriorWorldRuntime world) ||
                observer.ShipId != ship.ShipId || player.CorePlayer.gameObject.scene != world.Scene ||
                !GameplayWorldRegistry.TryGet(world.WorldKey, out _)) return false;
            realmRevision = observer.RealmRevision;
            worldKey = world.WorldKey;
            return true;
        }
        public static bool OwnsItem(NetworkConnectionToClient connection, Item item)
        {
            NetworkWorldPlayer player = connection?.identity?.GetComponent<NetworkWorldPlayer>();
            if (item == null || player == null || !player.TryGetSpaceObserver(out SpaceNetworkObserver observer)) return false;
            string playerWorld = GameplayWorldRegistry.GetItemWorldKey(player.CorePlayer);
            string itemWorld = GameplayWorldRegistry.GetItemWorldKey(item);
            if (!string.IsNullOrEmpty(playerWorld) || !string.IsNullOrEmpty(itemWorld))
                return !string.IsNullOrEmpty(playerWorld) && string.Equals(playerWorld, itemWorld, StringComparison.Ordinal);
            return !observer.InSpace && item.gameObject.scene.name == observer.WorldKey;
        }
        public static IDisposable Use(NetworkWorldPlayer player)
        {
            if (player?.TryGetSpaceObserver(out SpaceNetworkObserver observer) != true) return null;
            string playerWorld = GameplayWorldRegistry.GetItemWorldKey(player.CorePlayer);
            if (GameplayWorldRegistry.UsesLocalCoordinates(playerWorld) && GameplayWorldRegistry.TryGet(playerWorld, out _))
                return GameplayWorldRegistry.UseWorld(playerWorld);
            return Use(observer);
        }
        private static IDisposable Use(SpaceNetworkObserver observer)
        {
            if (observer.InSpace) return GameplayWorldRegistry.UseSpaceScene(SpaceSectorCoordinates.SceneKey(observer.Position.Sector));
            return string.IsNullOrWhiteSpace(observer.WorldKey) ? null : GameplayWorldRegistry.UseWorld(observer.WorldKey);
        }
        // 客户端只提交操作；服务器从当前连接已绑定的玩家读取世界。
        internal static string GetRequestRejection(bool ready, bool transferring, bool hasObserver,
            string worldKey, uint? requestRevision, uint realmRevision)
        {
            if (!ready) return "玩家尚未完成世界加载";
            if (transferring) return "正在切换世界，请稍后重试";
            if (!hasObserver || string.IsNullOrWhiteSpace(worldKey)) return "玩家所在世界尚未就绪";
            if (requestRevision.HasValue && requestRevision.Value != realmRevision) return "世界状态已更新，请重试操作";
            return null;
        }

        public static Action<NetworkConnectionToClient, T> Wrap<T>(Action<NetworkConnectionToClient, T> handler,
            Action<NetworkConnectionToClient, T, string> rejected = null, bool observerWorld = false) where T : struct, NetworkMessage
            => (connection, message) =>
            {
                NetworkWorldPlayer player = connection?.identity?.GetComponent<NetworkWorldPlayer>();
                if (player == null || !connection.isAuthenticated) return;
                // 世界绑定丢失时禁止回落到主机当前世界，也要检查观察者的最新代际。
                bool hasObserver = player.TryGetSpaceObserver(out SpaceNetworkObserver observer);
                string reason = GetRequestRejection(connection.isReady, player.IsWorldTransferPending,
                    hasObserver, observer.WorldKey, message is INetworkWorldRequest request ? request.WorldRevision : null,
                    observer.RealmRevision);
                if (reason != null)
                {
                    // 有事务回执的调用方立即收到真实拒绝原因，不把门禁丢包误报成网络超时。
                    rejected?.Invoke(connection, message, reason);
                    return;
                }
                using (observerWorld ? Use(observer) : Use(player)) handler(connection, message);
            };
        #endregion
    }
}
