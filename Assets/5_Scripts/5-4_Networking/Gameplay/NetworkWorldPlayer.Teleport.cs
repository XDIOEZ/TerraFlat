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
        private Vector3 pendingTeleportDestination, queuedTeleportDestination;
        private uint pendingTeleportRealmRevision;
        private Action<bool> queuedTeleportCompleted;
        private bool hasQueuedTeleport;

        private void BindTeleport() => PlayerTeleportService.NetworkRequest = RequestSurfaceTeleport;

        private void UnbindTeleport()
        {
            if (PlayerTeleportService.NetworkRequest == RequestSurfaceTeleport)
                PlayerTeleportService.NetworkRequest = null;
            CompleteSurfaceTeleport(false);
            Action<bool> queued = queuedTeleportCompleted;
            queuedTeleportCompleted = null;
            hasQueuedTeleport = false;
            queued?.Invoke(false);
        }

        private bool RequestSurfaceTeleport(Item player, Vector3 destination, Action<bool> completed)
        {
            if (!isOwned || !NetworkClient.ready || player != corePlayer || IsInSpace || authoritativeWorldTransferPending ||
                !IsValidPosition(destination))
                return false;

            if (isServer)
            {
                unchecked { if (++nextTeleportRequest == 0) ++nextTeleportRequest; }
                completed?.Invoke(ApplySurfaceTeleportOnServer(nextTeleportRequest, destination));
            }
            else
            {
                // 客户端先在本地瞬移；服务器仍按可靠顺序确认，每次只保留最新的后续落点。
                PlayerTeleportService.ApplySurfacePosition(corePlayer, destination);
                ResetTeleportMotion(destination);
                NetworkChunkStreamingCoordinator.RequestImmediateRefresh();
                if (pendingTeleportRequest != 0)
                {
                    Action<bool> superseded = teleportCompleted;
                    Action<bool> supersededQueued = queuedTeleportCompleted;
                    teleportCompleted = null;
                    queuedTeleportDestination = destination;
                    queuedTeleportCompleted = completed;
                    hasQueuedTeleport = true;
                    superseded?.Invoke(false);
                    supersededQueued?.Invoke(false);
                }
                else
                    SendSurfaceTeleportRequest(destination, completed, authoritativeRealmRevision);
            }
            return true;
        }

        private void SendSurfaceTeleportRequest(Vector3 destination, Action<bool> completed, uint realmRevision)
        {
            unchecked { if (++nextTeleportRequest == 0) ++nextTeleportRequest; }
            pendingTeleportRequest = nextTeleportRequest;
            pendingTeleportDestination = destination;
            pendingTeleportRealmRevision = realmRevision;
            teleportCompleted = completed;
            // 旧移动已先于本次命令发送；后续移动在回执前不写回旧的服务器坐标。
            CmdRequestGmTeleport(pendingTeleportRequest, destination, realmRevision);
        }

        [Command]
        private void CmdRequestGmTeleport(uint request, Vector3 destination, uint realmRevision)
        {
            // 跨维度或重生后的迟到请求不能改变新世界位置，拒绝时仍回执解除客户端等待。
            bool accepted = AcceptWorldSubmission(realmRevision) && ApplySurfaceTeleportOnServer(request, destination);
            TargetCompleteGmTeleport(connectionToClient, request, accepted, authoritativePosition,
                authoritativeRealmRevision);
        }

        [Server]
        private bool ApplySurfaceTeleportOnServer(uint request, Vector3 destination)
        {
            if (request == 0 || unchecked((int)(request - lastServerTeleportRequest)) <= 0 ||
                IsInSpace || authoritativeWorldTransferPending || corePlayer?.itemData== null || !IsValidPosition(destination) ||
                GameManager.ExistingInstance?.IsGameplayReady != true)
                return false;

            using var worldScope = NetworkGameplayWorldScope.Use(this);
            SpaceSession session = SpaceSessionForNetwork;
            string world = corePlayer.itemData.GetActorProfileMetadata().WorldKey;
            if (session == null || !session.TryGetNetworkObserver(profileId, out SpaceNetworkObserver observer) ||
                observer.InSpace || !string.Equals(observer.WorldKey, world, StringComparison.Ordinal) ||
                !session.CanTransferToNetworkSurface(world, out _))
                return false;

            lastServerTeleportRequest = request;
            Vector3 logical = WorldTopologyRuntime.NormalizePosition(destination);
            session.RemoveNetworkPassengerInput(profileId);
            session.EndNetworkAttack(profileId);
            PlayerTeleportService.ApplySurfacePosition(corePlayer, logical);
            authoritativePosition = logical;
            authoritativeVelocity = Vector2.zero;
            authoritativeVisualState = NetworkPlayerVisualState.Idle;
            session.TryBeginNetworkSurfaceTransfer(corePlayer, world, logical, isDirectTeleport: true);
            RefreshSpaceAuthority();
            ResetTeleportMotion(logical);
            ApplyRemoteVisualState(authoritativeVisualState);
            NetworkChunkStreamingCoordinator.RequestImmediateRefresh();
            return true;
        }

        [TargetRpc]
        private void TargetCompleteGmTeleport(NetworkConnectionToClient target, uint request, bool accepted,
            Vector3 logical, uint realmRevision)
        {
            if (!isOwned || pendingTeleportRequest != request) return;
            Vector3 requested = pendingTeleportDestination;
            uint submittedRealm = pendingTeleportRealmRevision;
            Action<bool> completed = teleportCompleted;
            pendingTeleportRequest = 0;
            teleportCompleted = null;

            if (hasQueuedTeleport && accepted && NetworkClient.ready && !IsInSpace &&
                !authoritativeWorldTransferPending && corePlayer?.itemData?.GetActorProfileMetadata().WorldKey == authoritativeWorldKey)
            {
                // 同世界传送每次都增加 RealmRevision；下一跳必须使用本次回执的版本。
                Vector3 next = queuedTeleportDestination;
                Action<bool> nextCompleted = queuedTeleportCompleted;
                hasQueuedTeleport = false;
                queuedTeleportCompleted = null;
                SendSurfaceTeleportRequest(next, nextCompleted, realmRevision);
                completed?.Invoke(false);
                return;
            }

            if (accepted)
            {
                // 预测位置已经生效，只在服务端校正落点时重定位玩家。
                if (corePlayer != null &&
                    (WorldTopologyRuntime.ShortestDelta(requested, logical).sqrMagnitude > .0001f ||
                     hasQueuedTeleport && !IsInSpace && !authoritativeWorldTransferPending &&
                     corePlayer.itemData?.GetActorProfileMetadata().WorldKey == authoritativeWorldKey))
                    PlayerTeleportService.ApplySurfacePosition(corePlayer, logical);
                ResetTeleportMotion(logical);
                NetworkChunkStreamingCoordinator.RequestImmediateRefresh();
            }
            else if (submittedRealm == realmRevision && !IsInSpace && !authoritativeWorldTransferPending &&
                     corePlayer?.itemData?.GetActorProfileMetadata().WorldKey == authoritativeWorldKey && IsValidPosition(logical))
            {
                // 仍处于原世界才回滚预测，维度迁移/重生由正式归属流程接管。
                PlayerTeleportService.ApplySurfacePosition(corePlayer, logical);
                ResetTeleportMotion(logical);
            }
            if (hasQueuedTeleport)
            {
                Action<bool> queued = queuedTeleportCompleted;
                queuedTeleportCompleted = null;
                hasQueuedTeleport = false;
                queued?.Invoke(false);
            }
            completed?.Invoke(accepted);
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
            pendingTeleportRealmRevision = 0;
            teleportCompleted = null;
            completed?.Invoke(accepted);
        }

        #endregion
    }
}
