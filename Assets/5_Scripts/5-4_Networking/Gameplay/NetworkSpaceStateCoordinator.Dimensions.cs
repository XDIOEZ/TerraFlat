using System;
using System.Collections;
using System.Collections.Generic;
using FlatWorld.Spaceflight;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.Networking.Gameplay
{
    public struct NetworkDimensionRequest : NetworkMessage
    {
        public uint Sequence;
        public string TargetDimension, SourceWorldKey;
        public uint RealmRevision;
        public int PortalGuid;
        public bool Generated;
    }
    public struct NetworkDimensionReady : NetworkMessage { public uint RealmRevision; public string WorldKey; }
    public struct NetworkDimensionResponse : NetworkMessage
    {
        public uint Sequence;
        public bool Accepted;
        public string Reason, WorldKey;
        public Vector3 Position;
    }

    public sealed partial class NetworkSpaceStateCoordinator
    {
        #region 每玩家独立的维度事务
        private uint dimensionSequence;
        private readonly Dictionary<uint, Action<bool, string>> dimensionReplies = new();
        private readonly Dictionary<NetworkConnectionToClient, uint> dimensionRequests = new();
        private readonly HashSet<string> dimensionProfiles = new(StringComparer.Ordinal);

        private void StartDimensionServerBridge()
        {
            NetworkServer.RegisterHandler<NetworkDimensionRequest>(OnDimensionRequest, true);
            NetworkServer.RegisterHandler<NetworkDimensionReady>(OnDimensionReady, true);
            NetworkDimensionBridge.PresentationReady = SendDimensionReady;
            NetworkDimensionBridge.TransitionSender = SendDimensionTransition;
            NetworkDimensionBridge.RespawnSender = SendDimensionRespawn;
            NetworkDimensionBridge.ExactTransferSender = SendExactDimensionTransfer;
        }
        private void StartDimensionClientBridge()
        {
            NetworkClient.RegisterHandler<NetworkDimensionResponse>(OnDimensionResponse, true);
            NetworkDimensionBridge.PresentationReady = SendDimensionReady;
            NetworkDimensionBridge.TransitionSender = SendDimensionTransition;
            NetworkDimensionBridge.RespawnSender = SendDimensionRespawn;
            NetworkDimensionBridge.ExactTransferSender = SendExactDimensionTransfer;
        }
        private void StopDimensionServerBridge()
        {
            NetworkServer.UnregisterHandler<NetworkDimensionRequest>();
            NetworkServer.UnregisterHandler<NetworkDimensionReady>();
            dimensionRequests.Clear(); dimensionProfiles.Clear();
            ClearDimensionBridge();
        }
        private void StopDimensionClientBridge()
        {
            NetworkClient.UnregisterHandler<NetworkDimensionResponse>();
            var pending = new List<Action<bool, string>>(dimensionReplies.Values);
            dimensionReplies.Clear();
            foreach (Action<bool, string> reply in pending) reply?.Invoke(false, "联机会话已结束。");
            ClearDimensionBridge();
        }
        private void ClearDimensionBridge()
        {
            if (NetworkDimensionBridge.PresentationReady == SendDimensionReady) NetworkDimensionBridge.PresentationReady = null;
            if (NetworkDimensionBridge.TransitionSender == SendDimensionTransition) NetworkDimensionBridge.TransitionSender = null;
            if (NetworkDimensionBridge.RespawnSender == SendDimensionRespawn) NetworkDimensionBridge.RespawnSender = null;
            if (NetworkDimensionBridge.ExactTransferSender == SendExactDimensionTransfer) NetworkDimensionBridge.ExactTransferSender = null;
        }
        private bool SendDimensionTransition(Player actor, string target, int portal, bool generated, Action<bool, string> completed)
        {
            if (actor == null || actor != ItemMgr.Instance?.User_Player || string.IsNullOrWhiteSpace(target)) return false;
            uint sequence = NextToken(ref dimensionSequence);
            if (SpaceSession.Current?.TryGetNetworkObserver(actor.ProfileName, out SpaceNetworkObserver source) != true) return false;
            var request = new NetworkDimensionRequest { Sequence = sequence, TargetDimension = target, PortalGuid = portal, Generated = generated,
                SourceWorldKey = source.WorldKey, RealmRevision = source.RealmRevision };
            if (NetworkServer.active)
            {
                StartCoroutine(ExecuteDimensionTransition(null, actor, request, completed)); return true;
            }
            if (!NetworkClient.ready || !OwnsProfile(actor.ProfileName)) return false;
            dimensionReplies.Add(sequence, completed);
            NetworkClient.Send(request);
            StartCoroutine(WaitDimensionReply(sequence));
            return true;
        }
        private bool SendExactDimensionTransfer(Player actor, WorldAddress target, Vector3 position, Action<bool, string> completed)
        {
            if (!NetworkServer.active || actor == null || actor != ItemMgr.Instance?.User_Player) return false;
            StartCoroutine(ExecuteExactDimensionTransfer(actor, target, position, completed));
            return true;
        }
        private IEnumerator ExecuteExactDimensionTransfer(Player actor, WorldAddress target, Vector3 position, Action<bool, string> completed)
        {
            NetworkWorldPlayer networkActor = NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>();
            if (networkActor?.CorePlayer != actor || !networkActor.TryBeginWorldTransfer())
            { completed?.Invoke(false, "玩家还有尚未完成的操作。"); yield break; }
            bool committed = false;
            string reason = null;
            try
            {
                yield return DimensionTransitionService.ExecuteExact(actor, target, position,
                    (accepted, message) => { committed = accepted; reason = message; },
                    () => actor != null && networkActor.CorePlayer == actor && NetworkServer.active);
                if (committed) { networkActor.RefreshSpaceAuthority(); AcknowledgePresentedLocalTransfer(networkActor); }
                completed?.Invoke(committed, reason);
            }
            finally { networkActor.FinishWorldTransfer(committed); }
        }
        private bool SendDimensionRespawn(Player actor, WorldAddress target, Vector3 position, Action preparePlayer, Action<bool, string> completed)
        {
            if (actor == null || actor != ItemMgr.Instance?.User_Player) return false;
            if (!NetworkServer.active)
                return SpaceNetworkBridge.SendIntent(actor.ProfileName, new SpaceNetworkIntent { Kind = SpaceNetworkIntentKind.Respawn }, completed);
            StartCoroutine(ExecuteDimensionRespawn(null, actor, 0, completed));
            return true;
        }
        private IEnumerator ExecuteDimensionRespawn(NetworkConnectionToClient connection, Player actor, uint sequence, Action<bool, string> completed)
        {
            NetworkWorldPlayer networkActor = connection?.identity?.GetComponent<NetworkWorldPlayer>() ?? NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>();
            if (networkActor == null || !networkActor.TryBeginWorldTransfer())
            { ReplyRespawn(connection, sequence, false, "玩家有尚未完成的操作。", completed); yield break; }
            bool committed = false;
            try
            {
                string reason = null;
                yield return DimensionTransitionService.ExecuteRespawn(actor,
                    (accepted, message) => { committed = accepted; reason = message; },
                    () => actor != null && networkActor.CorePlayer == actor && (connection == null ||
                        connection.identity?.GetComponent<NetworkWorldPlayer>() == networkActor && ResolveConnection(connection, out _, out _)));
                if (committed)
                {
                    networkActor.RefreshSpaceAuthority();
                    if (connection == null) AcknowledgePresentedLocalTransfer(networkActor);
                    if (connection != null) SendSnapshot(connection, actor.ProfileName);
                }
                ReplyRespawn(connection, sequence, committed, reason, completed);
            }
            finally { networkActor.FinishWorldTransfer(committed); }
        }
        private static void AcknowledgePresentedLocalTransfer(NetworkWorldPlayer player)
        {
            Player actor = player.CorePlayer;
            if (actor == null || actor != ItemMgr.Instance?.User_Player || player.TryGetSpaceObserver(out SpaceNetworkObserver observer) != true ||
                observer.InSpace || actor.gameObject.scene.name != observer.WorldKey || UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != observer.WorldKey ||
                !GameplayWorldRegistry.TryGet(observer.WorldKey, out GameplayWorldRuntime world) || !world.Chunks.AreRuntimeWindowPresentationsReady) return;
            player.AcknowledgeWorldTransfer(observer.WorldKey, player.RealmRevision);
        }
        private static void ReplyRespawn(NetworkConnectionToClient connection, uint sequence, bool accepted, string reason, Action<bool, string> completed)
        {
            if (connection != null && connection.isAuthenticated)
                connection.Send(new NetworkSpaceIntentResponse { Sequence = sequence, Accepted = accepted,
                    Reason = string.IsNullOrEmpty(reason) ? string.Empty : reason.Substring(0, Math.Min(240, reason.Length)) });
            else completed?.Invoke(accepted, reason);
        }
        private IEnumerator WaitDimensionReply(uint sequence)
        {
            yield return new WaitForSecondsRealtime(100f);
            if (dimensionReplies.Remove(sequence, out Action<bool, string> completed)) completed?.Invoke(false, "维度切换等待服务器回复超时。");
        }
        private void SendDimensionReady(Player actor, string worldKey)
        {
            NetworkWorldPlayer player = NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>();
            if (player == null || player.CorePlayer != actor) return;
            uint realm = SpaceSession.Current?.TryGetNetworkObserver(actor.ProfileName, out SpaceNetworkObserver observer) == true
                ? observer.RealmRevision : player.RealmRevision;
            if (NetworkServer.active) player.AcknowledgeWorldTransfer(worldKey, realm);
            else if (NetworkClient.ready)
            {
                NetworkClient.Send(new NetworkDimensionReady { WorldKey = worldKey, RealmRevision = realm });
                NetworkClient.Send(new NetworkItemStateRequest { RealmRevision = NetworkGameplayWorldScope.LocalRevision });
            }
        }
        private void OnDimensionReady(NetworkConnectionToClient connection, NetworkDimensionReady ready)
        {
            if (ResolveConnection(connection, out _, out NetworkWorldPlayer player))
                player.AcknowledgeWorldTransfer(ready.WorldKey, ready.RealmRevision);
        }
        private void OnDimensionRequest(NetworkConnectionToClient connection, NetworkDimensionRequest request)
        {
            if (!ResolveConnection(connection, out _, out NetworkWorldPlayer player) ||
                request.Sequence == 0 || (request.TargetDimension?.Length ?? 0) > 128 || (request.SourceWorldKey?.Length ?? 0) > 128 ||
                player.RealmRevision != request.RealmRevision || player.TryGetSpaceObserver(out SpaceNetworkObserver source) != true ||
                source.InSpace || source.WorldKey != request.SourceWorldKey ||
                dimensionRequests.TryGetValue(connection, out uint previous) && !IsNewer(request.Sequence, previous)) return;
            dimensionRequests[connection] = request.Sequence;
            StartCoroutine(ExecuteDimensionTransition(connection, player.CorePlayer, request, null));
        }
        private IEnumerator ExecuteDimensionTransition(NetworkConnectionToClient connection, Player actor, NetworkDimensionRequest request, Action<bool, string> completed)
        {
            string profile = actor?.ProfileName;
            if (string.IsNullOrWhiteSpace(profile) || !dimensionProfiles.Add(profile))
            { ReplyDimension(connection, request.Sequence, false, "该玩家正在切换维度。", null, default, completed); yield break; }
            NetworkWorldPlayer networkActor = connection?.identity?.GetComponent<NetworkWorldPlayer>() ?? NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>();
            bool committed = false;
            if (networkActor == null || !networkActor.TryBeginWorldTransfer())
            { dimensionProfiles.Remove(profile); ReplyDimension(connection, request.Sequence, false, "玩家有尚未完成的拾取、翻滚、战斗或迁移。", null, default, completed); yield break; }
            try
            {
                bool accepted = false;
                string reason = null;
                WorldAddress target = default;
                Vector3 position = default;
                yield return DimensionTransitionService.Execute(actor, request.TargetDimension, request.PortalGuid, request.Generated,
                    (result, message, address, point) => { accepted = result; reason = message; target = address; position = point; },
                    () => actor != null && networkActor.CorePlayer == actor && (connection == null ||
                        connection.identity?.GetComponent<NetworkWorldPlayer>() == networkActor && ResolveConnection(connection, out _, out _)));
                if (!accepted) { ReplyDimension(connection, request.Sequence, false, reason, null, default, completed); yield break; }
                networkActor.RefreshSpaceAuthority();
                committed = true;
                if (connection != null) SendSnapshot(connection, profile);
                ReplyDimension(connection, request.Sequence, true, null, target.WorldKey, position, completed);
            }
            finally { networkActor.FinishWorldTransfer(committed); dimensionProfiles.Remove(profile); }
        }
        private void ReplyDimension(NetworkConnectionToClient connection, uint sequence, bool accepted, string reason, string world, Vector3 position, Action<bool, string> completed)
        {
            if (connection != null && connection.isAuthenticated)
                connection.Send(new NetworkDimensionResponse { Sequence = sequence, Accepted = accepted, Reason = string.IsNullOrEmpty(reason) ? string.Empty : reason.Substring(0, Math.Min(240, reason.Length)),
                    WorldKey = world, Position = position });
            else completed?.Invoke(accepted, reason);
        }
        private void OnDimensionResponse(NetworkDimensionResponse response)
        {
            if (!dimensionReplies.Remove(response.Sequence, out Action<bool, string> completed)) return;
            completed?.Invoke(response.Accepted, response.Reason);
        }
        #endregion
    }
}
