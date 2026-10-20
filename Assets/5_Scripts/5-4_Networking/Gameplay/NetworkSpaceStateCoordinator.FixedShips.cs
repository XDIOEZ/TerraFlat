using System;
using System.Collections;
using System.Collections.Generic;
using FlatWorld.Spaceflight;
using FlatWorld.Structures;
using Mirror;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    #region 固定飞船请求协议
    public struct NetworkFixedShipRequest : NetworkMessage
    {
        public uint Token;
        public uint RealmRevision;
        public string WorldKey;
        public bool Refill;
        public string TemplateId;
    }

    public struct NetworkFixedShipResponse : NetworkMessage
    {
        public uint Token;
        public bool Accepted;
        public string Reason;
    }
    #endregion

    public sealed partial class NetworkSpaceStateCoordinator
    {
        #region 固定飞船权威请求与回执
        private sealed class FixedShipConnection
        {
            public NetworkConnectionToClient Connection;
            public uint Token;
            public bool Busy;
            public float NextRequest;
        }

        private readonly Dictionary<int, FixedShipConnection> fixedShipConnections = new();
        private uint fixedShipToken, pendingFixedShipToken;
        private float fixedShipDeadline;
        private Action<bool, string> fixedShipReply;

        private void StartFixedShipServerBridge()
        {
            NetworkServer.RegisterHandler<NetworkFixedShipRequest>(OnFixedShipRequest, true);
            ShipGroundBuildingStore.NetworkPublisher = PublishFixedShipBuildings;
        }

        private void StopFixedShipServerBridge()
        {
            NetworkServer.UnregisterHandler<NetworkFixedShipRequest>();
            fixedShipConnections.Clear();
            if (ShipGroundBuildingStore.NetworkPublisher == PublishFixedShipBuildings)
                ShipGroundBuildingStore.NetworkPublisher = null;
        }

        private bool PublishFixedShipBuildings(IReadOnlyList<ShipGroundBuildingStore.Entry> entries)
            => GetComponent<NetworkItemStateCoordinator>()?.PublishGroundBuildings(entries) == true;

        private void StartFixedShipClientBridge()
        {
            NetworkClient.RegisterHandler<NetworkFixedShipResponse>(OnFixedShipResponse, true);
            ShipFixedStructureBuilder.RemoteRequest = SendFixedShipRequest;
        }

        private void StopFixedShipClientBridge()
        {
            NetworkClient.UnregisterHandler<NetworkFixedShipResponse>();
            if (ShipFixedStructureBuilder.RemoteRequest == SendFixedShipRequest)
                ShipFixedStructureBuilder.RemoteRequest = null;
            CompleteFixedShipRequest(false, "联机连接已关闭。");
        }

        private void RemoveFixedShipConnection(NetworkConnectionToClient connection)
        {
            if (connection != null && fixedShipConnections.TryGetValue(connection.connectionId, out FixedShipConnection state) &&
                ReferenceEquals(state.Connection, connection)) fixedShipConnections.Remove(connection.connectionId);
        }

        private bool SendFixedShipRequest(Player actor, string templateId, bool refill, Action<bool, string> reply)
        {
            if (NetworkServer.active || !NetworkClient.ready || actor == null || !OwnsProfile(actor.ProfileName)) return false;
            if (pendingFixedShipToken != 0)
            { reply?.Invoke(false, "正在等待上一次飞船请求完成。"); return true; }
            if (SpaceSession.Current?.TryGetNetworkObserver(actor.ProfileName, out SpaceNetworkObserver observer) != true) return false;
            pendingFixedShipToken = NextToken(ref fixedShipToken);
            fixedShipReply = reply;
            fixedShipDeadline = Time.unscaledTime + 30f;
            try
            {
                NetworkClient.Send(new NetworkFixedShipRequest
                { Token = pendingFixedShipToken, RealmRevision = observer.RealmRevision, WorldKey = observer.WorldKey, TemplateId = templateId ?? string.Empty, Refill = refill });
            }
            catch (Exception error) { CompleteFixedShipRequest(false, "发送飞船请求失败：" + error.Message); }
            return true;
        }

        private void UpdateFixedShipRequests()
        {
            if (pendingFixedShipToken != 0 && (!NetworkClient.ready || Time.unscaledTime > fixedShipDeadline))
                CompleteFixedShipRequest(false, "飞船请求未及时收到服务器回执，请先检查现场再重试。");
        }

        private void OnFixedShipResponse(NetworkFixedShipResponse response)
        {
            if (response.Token == 0 || response.Token != pendingFixedShipToken) return;
            CompleteFixedShipRequest(response.Accepted, response.Reason);
        }

        private void CompleteFixedShipRequest(bool accepted, string reason)
        {
            Action<bool, string> reply = fixedShipReply;
            fixedShipReply = null; pendingFixedShipToken = 0;
            reply?.Invoke(accepted, reason);
        }

        private void OnFixedShipRequest(NetworkConnectionToClient connection, NetworkFixedShipRequest request)
        {
            if (!ResolveConnection(connection, out _, out NetworkWorldPlayer player) || request.Token == 0) return;
            if (!fixedShipConnections.TryGetValue(connection.connectionId, out FixedShipConnection state) ||
                !ReferenceEquals(state.Connection, connection))
                fixedShipConnections[connection.connectionId] = state = new FixedShipConnection { Connection = connection };
            if (!IsNewer(request.Token, state.Token)) return;
            state.Token = request.Token;
            if (player.IsWorldTransferPending || request.RealmRevision != player.RealmRevision ||
                !player.TryGetSpaceObserver(out var observer) || !string.Equals(observer.WorldKey, request.WorldKey, StringComparison.Ordinal))
            { SendFixedShipResponse(connection, request.Token, false, "飞船请求来自旧世界，请等待切换完成。"); return; }
            if (state.Busy || Time.unscaledTime < state.NextRequest || (request.TemplateId?.Length ?? 0) > 128 ||
                !request.Refill && string.IsNullOrWhiteSpace(request.TemplateId))
            { SendFixedShipResponse(connection, request.Token, false, "飞船请求无效或上一次请求仍在处理中。"); return; }
            state.Busy = true;
            state.NextRequest = Time.unscaledTime + .5f;
            StartCoroutine(ExecuteFixedShipRequest(connection, player, state, request));
        }

        private IEnumerator ExecuteFixedShipRequest(NetworkConnectionToClient connection, NetworkWorldPlayer player,
            FixedShipConnection state, NetworkFixedShipRequest request)
        {
            IReadOnlyList<FixedStructureDefinition> templates = null;
            Exception error = null;
            yield return FixedStructureCatalog.LoadAsync(value => templates = value, value => error = value);
            // 异步目录完成后重新核对连接和玩家，旧连接不能借复用编号写入世界。
            if (!NetworkServer.active || !fixedShipConnections.TryGetValue(connection.connectionId, out FixedShipConnection current) ||
                !ReferenceEquals(current, state) || !ReferenceEquals(current.Connection, connection)) yield break;
            state.Busy = false;
            if (!ResolveConnection(connection, out _, out NetworkWorldPlayer livePlayer) || !ReferenceEquals(livePlayer, player)) yield break;
            if (player.IsWorldTransferPending || request.RealmRevision != player.RealmRevision ||
                !player.TryGetSpaceObserver(out var observer) || !string.Equals(observer.WorldKey, request.WorldKey, StringComparison.Ordinal))
            { SendFixedShipResponse(connection, request.Token, false, "玩家世界已变化，本次飞船操作已取消。"); yield break; }
            bool accepted = false;
            string reason;
            try
            {
                if (error != null) throw error;
                if (templates == null) throw new InvalidOperationException("服务器飞船模板尚未就绪。");
                if (request.Refill) accepted = ShipFixedStructureBuilder.TryRefill(player.CorePlayer, templates, out reason);
                else
                {
                    FixedStructureDefinition template = null;
                    foreach (FixedStructureDefinition candidate in templates)
                        if (string.Equals(candidate.Id, request.TemplateId, StringComparison.Ordinal)) { template = candidate; break; }
                    if (template == null) throw new InvalidOperationException("服务器没有这个飞船模板。");
                    accepted = ShipFixedStructureBuilder.TryBuild(player.CorePlayer, template, out _, out reason);
                }
            }
            catch (Exception failure) { reason = "飞船操作失败：" + failure.Message; }
            SendFixedShipResponse(connection, request.Token, accepted, reason);
            if (accepted) SendSnapshot(connection, player.DisplayName);
        }

        private static void SendFixedShipResponse(NetworkConnectionToClient connection, uint token, bool accepted, string reason)
        {
            reason ??= string.Empty;
            connection.Send(new NetworkFixedShipResponse
            { Token = token, Accepted = accepted, Reason = reason.Substring(0, Math.Min(256, reason.Length)) });
        }
        #endregion
    }
}
