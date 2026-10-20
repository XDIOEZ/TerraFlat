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
        public static bool OwnsItem(NetworkConnectionToClient connection, Item item)
            => item != null && connection?.identity?.GetComponent<NetworkWorldPlayer>()?.TryGetSpaceObserver(out SpaceNetworkObserver observer) == true &&
                !observer.InSpace && item.gameObject.scene.name == observer.WorldKey;
        public static IDisposable Use(NetworkWorldPlayer player)
        {
            if (player?.TryGetSpaceObserver(out SpaceNetworkObserver observer) != true) return null;
            if (observer.InSpace) return GameplayWorldRegistry.UseSpaceScene(SpaceSectorCoordinates.SceneKey(observer.Position.Sector));
            return string.IsNullOrWhiteSpace(observer.WorldKey) ? null : GameplayWorldRegistry.UseWorld(observer.WorldKey);
        }
        // 客户端只提交操作；服务器从当前连接已绑定的玩家读取世界。
        public static Action<NetworkConnectionToClient, T> Wrap<T>(Action<NetworkConnectionToClient, T> handler) where T : struct, NetworkMessage
            => (connection, message) =>
            {
                NetworkWorldPlayer player = connection?.identity?.GetComponent<NetworkWorldPlayer>();
                if (player == null || !connection.isAuthenticated || !connection.isReady || player.IsWorldTransferPending) return;
                if (message is INetworkWorldRequest request && request.WorldRevision != player.RealmRevision) return;
                using (Use(player)) handler(connection, message);
            };
        #endregion
    }
}
