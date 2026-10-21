using System;
using FlatWorld.Spaceflight;
using Mirror;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    public sealed partial class NetworkWorldPlayer
    {
        #region GM 地表传送事务

        private uint nextTeleportRequest, pendingTeleportRequest, lastServerTeleportRequest;
        private Action<bool> teleportCompleted;

        private void BindTeleport() => PlayerTeleportService.NetworkRequest = RequestSurfaceTeleport;

        private void UnbindTeleport()
        {
            if (PlayerTeleportService.NetworkRequest == RequestSurfaceTeleport)
                PlayerTeleportService.NetworkRequest = null;
            CompleteSurfaceTeleport(false);
        }

        private bool RequestSurfaceTeleport(Player player, Vector3 destination, Action<bool> completed)
        {
            if (!isOwned || !NetworkClient.ready || player != corePlayer || IsInSpace || authoritativeWorldTransferPending ||
                pendingTeleportRequest != 0 || !IsValidPosition(destination))
                return false;

            unchecked { if (++nextTeleportRequest == 0) ++nextTeleportRequest; }
            pendingTeleportRequest = nextTeleportRequest;
            teleportCompleted = completed;
            // 等服务器回执期间不再发送旧坐标，可靠通道中的旧移动先于传送处理。
            if (isServer)
                CompleteSurfaceTeleport(ApplySurfaceTeleportOnServer(pendingTeleportRequest, destination));
            else
                CmdRequestGmTeleport(pendingTeleportRequest, destination, authoritativeRealmRevision);
            return true;
        }

        [Command]
        private void CmdRequestGmTeleport(uint request, Vector3 destination, uint realmRevision)
        {
            // 跨维度或重生后的迟到请求不能改变新世界位置，拒绝时仍回执解除客户端等待。
            bool accepted = AcceptWorldSubmission(realmRevision) && ApplySurfaceTeleportOnServer(request, destination);
            TargetCompleteGmTeleport(connectionToClient, request, accepted, authoritativePosition);
        }

        [Server]
        private bool ApplySurfaceTeleportOnServer(uint request, Vector3 destination)
        {
            if (request == 0 || unchecked((int)(request - lastServerTeleportRequest)) <= 0 ||
                IsInSpace || authoritativeWorldTransferPending || corePlayer?.Data == null || !IsValidPosition(destination) ||
                GameManager.ExistingInstance?.IsGameplayReady != true)
                return false;

            using var worldScope = NetworkGameplayWorldScope.Use(this);
            SpaceSession session = SpaceSessionForNetwork;
            string world = corePlayer.Data.CurrentSceneName;
            if (session == null || !session.TryGetNetworkObserver(displayName, out SpaceNetworkObserver observer) ||
                observer.InSpace || !string.Equals(observer.WorldKey, world, StringComparison.Ordinal) ||
                !session.CanTransferToNetworkSurface(world, out _))
                return false;

            lastServerTeleportRequest = request;
            Vector3 logical = WorldTopologyRuntime.NormalizePosition(destination);
            session.RemoveNetworkPassengerInput(displayName);
            session.EndNetworkAttack(displayName);
            PlayerTeleportService.ApplySurfacePosition(corePlayer, logical);
            authoritativePosition = logical;
            authoritativeVelocity = Vector2.zero;
            authoritativeVisualState = NetworkPlayerVisualState.Idle;
            session.TryBeginNetworkSurfaceTransfer(corePlayer, world, logical);
            RefreshSpaceAuthority();
            ResetTeleportMotion(logical);
            ApplyRemoteVisualState(authoritativeVisualState);
            NetworkChunkStreamingCoordinator.RequestImmediateRefresh();
            return true;
        }

        [TargetRpc]
        private void TargetCompleteGmTeleport(NetworkConnectionToClient target, uint request, bool accepted, Vector3 logical)
        {
            if (pendingTeleportRequest != request) return;
            if (accepted)
            {
                PlayerTeleportService.ApplySurfacePosition(corePlayer, logical);
                ResetTeleportMotion(logical);
                NetworkChunkStreamingCoordinator.RequestImmediateRefresh();
            }
            CompleteSurfaceTeleport(accepted);
        }

        private void ResetTeleportMotion(Vector3 logical)
        {
            Vector3 presentation = WorldLocalPresentation.ProjectPosition(logical);
            transform.position = isOwned ? logical : presentation;
            remoteTargetPosition = presentation;
            remoteSnapshotStartPosition = presentation;
            remoteSnapshotElapsed = 0f;
            remoteVelocity = Vector2.zero;
            lastRemoteSnapshotTime = -1f;
            hasOwnedMotionSample = false;
            networkSendTimer = 0f;
            itemStateDirty = true;
        }

        private void CompleteSurfaceTeleport(bool accepted)
        {
            Action<bool> completed = teleportCompleted;
            pendingTeleportRequest = 0;
            teleportCompleted = null;
            completed?.Invoke(accepted);
        }

        #endregion
    }
}
