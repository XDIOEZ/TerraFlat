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
        public static bool OwnsItem(NetworkConnectionToClient connection, Item item)
            => item != null && connection?.identity?.GetComponent<NetworkWorldPlayer>()?.TryGetSpaceObserver(out SpaceNetworkObserver observer) == true &&
                !observer.InSpace && item.gameObject.scene.name == observer.WorldKey;
        public static IDisposable Use(NetworkWorldPlayer player)
        {
            if (player?.TryGetSpaceObserver(out SpaceNetworkObserver observer) != true) return null;
            return Use(observer);
        }
        private static IDisposable Use(SpaceNetworkObserver observer)
        {
            if (observer.InSpace) return GameplayWorldRegistry.UseSpaceScene(SpaceSectorCoordinates.SceneKey(observer.Position.Sector));
            return string.IsNullOrWhiteSpace(observer.WorldKey) ? null : GameplayWorldRegistry.UseWorld(observer.WorldKey);
        }
        // 客户端只提交操作；服务器从当前连接已绑定的玩家读取世界。
        public static Action<NetworkConnectionToClient, T> Wrap<T>(Action<NetworkConnectionToClient, T> handler) where T : struct, NetworkMessage
            => (connection, message) =>
            {
                NetworkWorldPlayer player = connection?.identity?.GetComponent<NetworkWorldPlayer>();
                if (player == null || !connection.isAuthenticated || !connection.isReady || player.IsWorldTransferPending) return;
                // 世界绑定丢失时禁止回落到主机当前世界，也要检查观察者的最新代际。
                if (!player.TryGetSpaceObserver(out SpaceNetworkObserver observer) ||
                    string.IsNullOrWhiteSpace(observer.WorldKey)) return;
                if (message is INetworkWorldRequest request && request.WorldRevision != observer.RealmRevision) return;
                using (Use(observer)) handler(connection, message);
            };
        #endregion
    }
}
