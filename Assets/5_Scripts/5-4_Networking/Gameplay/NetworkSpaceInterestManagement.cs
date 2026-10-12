using System;
using System.Collections.Generic;
using FlatWorld.Spaceflight;
using Mirror;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class NetworkSpaceInterestManagement : InterestManagement
    {
        #region 按规范位置观察玩家
        private float nextRebuild;
        public override bool OnCheckObserver(NetworkIdentity identity, NetworkConnectionToClient observer)
        {
            if (identity == null || observer?.identity == null) return false;
            if (identity.connectionToClient == observer) return true;
            NetworkWorldPlayer target = identity.GetComponent<NetworkWorldPlayer>();
            NetworkWorldPlayer source = observer.identity.GetComponent<NetworkWorldPlayer>();
            if (target == null || source == null) return true;
            if (!target.TryGetSpaceObserver(out SpaceNetworkObserver targetState) ||
                !source.TryGetSpaceObserver(out SpaceNetworkObserver sourceState)) return false;
            if (targetState.InSpace != sourceState.InSpace) return false;
            if (!targetState.InSpace)
                return string.IsNullOrEmpty(targetState.WorldKey) || string.IsNullOrEmpty(sourceState.WorldKey) ||
                    string.Equals(targetState.WorldKey, sourceState.WorldKey, StringComparison.Ordinal);
            // 同一条真实距离规则同时覆盖同区与边界两侧，场景身份不隔断交互。
            return SpaceSectorCoordinates.Difference(targetState.Position, sourceState.Position).Magnitude <= NetworkSpaceStateCoordinator.ObserverRangeMeters;
        }
        public override void OnRebuildObservers(NetworkIdentity identity, HashSet<NetworkConnectionToClient> observers)
        {
            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
                if (connection != null && connection.isAuthenticated && connection.isReady && OnCheckObserver(identity, connection))
                    observers.Add(connection);
        }
        public override void SetHostVisibility(NetworkIdentity identity, bool visible)
        {
            NetworkWorldPlayer player = identity.GetComponent<NetworkWorldPlayer>();
            if (player != null) player.SetNetworkVisibility(visible);
            else base.SetHostVisibility(identity, visible);
        }
        private void Update()
        {
            if (!NetworkServer.active || Time.unscaledTime < nextRebuild) return;
            nextRebuild = Time.unscaledTime + .1f;
            RebuildAll();
        }
        #endregion
    }
}
