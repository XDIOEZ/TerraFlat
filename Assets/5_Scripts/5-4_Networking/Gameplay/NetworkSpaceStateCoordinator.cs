using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using FlatWorld.Spaceflight;
using Mirror;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    [DisallowMultipleComponent]
    public sealed partial class NetworkSpaceStateCoordinator : MonoBehaviour
    {
        #region 生命周期与连接身份
        public const double ObserverRangeMeters = 8192d;
        private const float SnapshotInterval = .2f;
        private const float TransferTimeout = 5f;
        private const int MaxCompressedBytes = 8 * 1024 * 1024;
        private const int MaxExpandedBytes = 64 * 1024 * 1024;
        private readonly Dictionary<int, ConnectionState> connections = new();
        private bool serverStarted, clientStarted;
        private float nextServerSnapshot, nextClientRequest, nextInputSend;
        private uint inputSequence, intentSequence;
        private ulong snapshotSequence, lastAppliedSequence;
        private Incoming incoming;
        private bool needsSnapshot;
        private bool observedLocalRealm, previousLocalInSpace;
        private sealed class ConnectionState
        {
            public NetworkConnectionToClient Connection;
            public uint InputSequence, IntentSequence;
            public float NextRequest, InputTime;
            public string Profile;
        }
        private sealed class Incoming
        {
            public NetworkSpaceSnapshotBegin Header;
            public byte[][] Parts;
            public int Received;
            public float Deadline;
        }
        public void StartServerSide()
        {
            if (serverStarted) return;
            NetworkServer.RegisterHandler<NetworkSpaceSnapshotRequest>(OnSnapshotRequest, true);
            NetworkServer.RegisterHandler<NetworkSpaceInput>(OnInput, true);
            NetworkServer.RegisterHandler<NetworkSpaceIntentRequest>(OnIntent, true);
            serverStarted = true;
            nextServerSnapshot = 0f;
        }
        public void StartClientSide()
        {
            if (clientStarted) return;
            NetworkClient.RegisterHandler<NetworkSpaceSnapshotBegin>(OnSnapshotBegin, true);
            NetworkClient.RegisterHandler<NetworkSpaceSnapshotChunk>(OnSnapshotChunk, true);
            NetworkClient.RegisterHandler<NetworkSpaceIntentResponse>(OnIntentResponse, true);
            NetworkClient.RegisterHandler<NetworkSpaceProjectilePoses>(OnProjectilePoses, true);
            SpaceNetworkBridge.InputSender = SendInput;
            SpaceNetworkBridge.IntentSender = SendIntent;
            clientStarted = true;
            needsSnapshot = true;
            nextClientRequest = 0f; lastAppliedSequence = 0; inputSequence = intentSequence = 0;
        }
        public void StopServerSide()
        {
            if (!serverStarted) return;
            foreach (ConnectionState state in connections.Values)
            {
                SpaceSession.Current?.RemoveNetworkPassengerInput(state.Profile);
                SpaceSession.Current?.EndNetworkAttack(state.Profile);
            }
            connections.Clear();
            NetworkServer.UnregisterHandler<NetworkSpaceSnapshotRequest>();
            NetworkServer.UnregisterHandler<NetworkSpaceInput>();
            NetworkServer.UnregisterHandler<NetworkSpaceIntentRequest>();
            serverStarted = false;
        }
        public void StopClientSide()
        {
            if (!clientStarted) return;
            NetworkClient.UnregisterHandler<NetworkSpaceSnapshotBegin>();
            NetworkClient.UnregisterHandler<NetworkSpaceSnapshotChunk>();
            NetworkClient.UnregisterHandler<NetworkSpaceIntentResponse>();
            NetworkClient.UnregisterHandler<NetworkSpaceProjectilePoses>();
            ClearProjectileViews();
            if (SpaceNetworkBridge.InputSender == SendInput) SpaceNetworkBridge.InputSender = null;
            if (SpaceNetworkBridge.IntentSender == SendIntent) SpaceNetworkBridge.IntentSender = null;
            incoming = null; clientStarted = false;
        }
        public void RemoveConnection(NetworkConnectionToClient connection)
        {
            if (connection == null || !connections.TryGetValue(connection.connectionId, out ConnectionState state)) return;
            SpaceSession.Current?.RemoveNetworkPassengerInput(state.Profile);
            SpaceSession.Current?.EndNetworkAttack(state.Profile);
            connections.Remove(connection.connectionId);
        }
        private bool ResolveConnection(NetworkConnectionToClient connection, out ConnectionState state, out NetworkWorldPlayer player)
        {
            state = null; player = connection?.identity?.GetComponent<NetworkWorldPlayer>();
            if (connection == null || !connection.isAuthenticated || !connection.isReady || player == null ||
                string.IsNullOrWhiteSpace(player.DisplayName) || player.CorePlayer == null) return false;
            // 请求身份只取服务器绑定，客户端无法指定另一个玩家的存档档案。
            if (!connections.TryGetValue(connection.connectionId, out state) || !ReferenceEquals(state.Connection, connection))
                connections[connection.connectionId] = state = new ConnectionState { Connection = connection, Profile = player.DisplayName };
            return string.Equals(state.Profile, player.DisplayName, StringComparison.Ordinal);
        }
        private void OnDestroy() { StopClientSide(); StopServerSide(); }
        #endregion

        #region 客户端输入与服务端操作
        private bool OwnsProfile(string profile)
            => NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>() is NetworkWorldPlayer player &&
               string.Equals(profile, player.DisplayName, StringComparison.Ordinal);
        private bool SendInput(string profile, SpacePassengerInput input)
        {
            if (NetworkServer.active || !NetworkClient.ready || !OwnsProfile(profile)) return false;
            if (Time.unscaledTime < nextInputSend) return true;
            nextInputSend = Time.unscaledTime + .033f;
            NetworkClient.Send(new NetworkSpaceInput { Sequence = NextToken(ref inputSequence), Input = input }, Channels.Unreliable);
            return true;
        }
        private bool SendIntent(string profile, SpaceNetworkIntent intent)
        {
            if (NetworkServer.active || !NetworkClient.ready || !OwnsProfile(profile)) return false;
            NetworkClient.Send(new NetworkSpaceIntentRequest { Sequence = NextToken(ref intentSequence), Intent = intent });
            return true;
        }
        private void OnInput(NetworkConnectionToClient connection, NetworkSpaceInput message)
        {
            if (!ResolveConnection(connection, out ConnectionState state, out _) ||
                !IsNewer(message.Sequence, state.InputSequence) || !ValidInput(message.Input)) return;
            state.InputSequence = message.Sequence; state.InputTime = Time.unscaledTime;
            SpaceSession.Current?.SubmitPassengerInput(state.Profile, message.Input);
        }
        private void OnIntent(NetworkConnectionToClient connection, NetworkSpaceIntentRequest message)
        {
            if (!ResolveConnection(connection, out ConnectionState state, out _) || !IsNewer(message.Sequence, state.IntentSequence)) return;
            state.IntentSequence = message.Sequence;
            SpaceNetworkIntent intent = message.Intent;
            string reason = "太空操作请求无效";
            bool accepted = ValidIntent(intent) && SpaceSession.Current != null &&
                SpaceSession.Current.ExecuteNetworkIntent(state.Profile, intent, out reason);
            connection.Send(new NetworkSpaceIntentResponse { Sequence = message.Sequence, Accepted = accepted,
                Reason = string.IsNullOrEmpty(reason) ? string.Empty : reason.Substring(0, Math.Min(160, reason.Length)) });
            if (accepted) SendSnapshot(connection, state.Profile);
        }
        private static bool ValidInput(SpacePassengerInput input)
            => float.IsFinite(input.MoveX) && float.IsFinite(input.MoveY) && float.IsFinite(input.Turn) && float.IsFinite(input.Thrust) &&
               Math.Abs(input.MoveX) <= 1.001f && Math.Abs(input.MoveY) <= 1.001f && Math.Abs(input.Turn) <= 1.001f && Math.Abs(input.Thrust) <= 1.001f;
        private static bool ValidIntent(SpaceNetworkIntent intent)
            => (intent.ShipId?.Length ?? 0) <= 128 && (intent.PieceId?.Length ?? 0) <= 128 &&
               (intent.TargetId?.Length ?? 0) <= 128 && double.IsFinite(intent.X) && double.IsFinite(intent.Y) &&
               Enum.IsDefined(intent.Kind.GetType(), intent.Kind);
        private static uint NextToken(ref uint value) { if (++value == 0) ++value; return value; }
        private static bool IsNewer(uint value, uint previous) => value != 0 && unchecked((int)(value - previous)) > 0;
        private static void OnIntentResponse(NetworkSpaceIntentResponse response)
        { if (!response.Accepted) Debug.LogWarning($"[太空联机] 操作被拒绝：{response.Reason}"); }
        #endregion

        #region 快照发送与有限分片接收
        private void Update()
        {
            UpdateProjectileViews();
            NetworkWorldPlayer local = NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>();
            if (clientStarted && !NetworkServer.active && NetworkClient.ready && local != null)
            {
                if (observedLocalRealm && previousLocalInSpace && !local.IsInSpace) NetworkClient.Send(new NetworkItemStateRequest());
                previousLocalInSpace = local.IsInSpace; observedLocalRealm = true;
            }
            if (serverStarted && NetworkServer.active && Time.unscaledTime >= nextServerSnapshot)
            {
                nextServerSnapshot = Time.unscaledTime + SnapshotInterval;
                foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
                {
                    if (!ResolveConnection(connection, out ConnectionState state, out _) || connection is LocalConnectionToClient) continue;
                    if (state.InputTime > 0f && Time.unscaledTime - state.InputTime > .5f)
                    { SpaceSession.Current?.SubmitPassengerInput(state.Profile, default); state.InputTime = 0f; }
                    SendSnapshot(connection, state.Profile);
                }
            }
            if (!clientStarted || !NetworkClient.ready || NetworkServer.active) return;
            if (incoming != null && Time.unscaledTime > incoming.Deadline)
            { incoming = null; needsSnapshot = true; nextClientRequest = 0f; }
            if (!needsSnapshot || Time.unscaledTime < nextClientRequest) return;
            nextClientRequest = Time.unscaledTime + 1f;
            NetworkClient.Send(new NetworkSpaceSnapshotRequest());
        }
        private void OnSnapshotRequest(NetworkConnectionToClient connection, NetworkSpaceSnapshotRequest request)
        {
            if (!ResolveConnection(connection, out ConnectionState state, out _) || Time.unscaledTime < state.NextRequest) return;
            state.NextRequest = Time.unscaledTime + .5f;
            SendSnapshot(connection, state.Profile);
        }
        private void SendSnapshot(NetworkConnectionToClient connection, string profile)
        {
            if (SpaceSession.Current?.State == null || connection is LocalConnectionToClient) return;
            SendProjectilePoses(connection, profile);
            string json = SpaceSession.Current.CaptureNetworkSnapshot(profile, ObserverRangeMeters);
            if (string.IsNullOrEmpty(json)) return;
            byte[] expanded = Encoding.UTF8.GetBytes(json);
            if (expanded.Length > MaxExpandedBytes) throw new InvalidOperationException("太空同步快照超过未压缩数据限额。");
            byte[] compressed;
            using (var stream = new MemoryStream())
            {
                using (var gzip = new GZipStream(stream, System.IO.Compression.CompressionLevel.Fastest, true)) gzip.Write(expanded, 0, expanded.Length);
                compressed = stream.ToArray();
            }
            if (compressed.Length > MaxCompressedBytes) throw new InvalidOperationException("太空同步快照超过压缩数据限额。");
            if (++snapshotSequence == 0) ++snapshotSequence;
            int parts = (compressed.Length + NetworkGameplayProtocol.SnapshotChunkBytes - 1) / NetworkGameplayProtocol.SnapshotChunkBytes;
            connection.Send(new NetworkSpaceSnapshotBegin { Sequence = snapshotSequence, CompressedBytes = compressed.Length,
                ExpandedBytes = expanded.Length, ChunkCount = parts, Hash = NetworkGameplayProtocol.CalculatePayloadHash(compressed) });
            for (int i = 0; i < parts; i++)
            {
                int offset = i * NetworkGameplayProtocol.SnapshotChunkBytes;
                byte[] part = new byte[Math.Min(NetworkGameplayProtocol.SnapshotChunkBytes, compressed.Length - offset)];
                Buffer.BlockCopy(compressed, offset, part, 0, part.Length);
                connection.Send(new NetworkSpaceSnapshotChunk { Sequence = snapshotSequence, Index = i, Data = part });
            }
        }
        private void OnSnapshotBegin(NetworkSpaceSnapshotBegin header)
        {
            if (NetworkServer.active || header.Sequence <= lastAppliedSequence || incoming != null && header.Sequence <= incoming.Header.Sequence) return;
            if (header.CompressedBytes <= 0 || header.CompressedBytes > MaxCompressedBytes || header.ExpandedBytes <= 0 ||
                header.ExpandedBytes > MaxExpandedBytes || header.ChunkCount !=
                (header.CompressedBytes + NetworkGameplayProtocol.SnapshotChunkBytes - 1) / NetworkGameplayProtocol.SnapshotChunkBytes)
            { RejectSnapshot("太空快照头超出限额或长度不一致"); return; }
            incoming = new Incoming { Header = header, Parts = new byte[header.ChunkCount][], Deadline = Time.unscaledTime + TransferTimeout };
        }
        private void OnSnapshotChunk(NetworkSpaceSnapshotChunk chunk)
        {
            Incoming value = incoming;
            if (NetworkServer.active || value == null || chunk.Sequence != value.Header.Sequence) return;
            if ((uint)chunk.Index >= (uint)value.Parts.Length || chunk.Data == null || chunk.Data.Length !=
                Math.Min(NetworkGameplayProtocol.SnapshotChunkBytes, value.Header.CompressedBytes - chunk.Index * NetworkGameplayProtocol.SnapshotChunkBytes))
            { RejectSnapshot("太空快照分片序号或长度无效"); return; }
            if (value.Parts[chunk.Index] != null) return;
            value.Parts[chunk.Index] = chunk.Data;
            if (++value.Received != value.Parts.Length) return;
            incoming = null;
            byte[] compressed = new byte[value.Header.CompressedBytes];
            for (int i = 0; i < value.Parts.Length; i++)
                Buffer.BlockCopy(value.Parts[i], 0, compressed, i * NetworkGameplayProtocol.SnapshotChunkBytes, value.Parts[i].Length);
            if (NetworkGameplayProtocol.CalculatePayloadHash(compressed) != value.Header.Hash)
            { RejectSnapshot("太空快照校验失败"); return; }
            byte[] expanded = new byte[value.Header.ExpandedBytes];
            try
            {
                using (var source = new MemoryStream(compressed, false))
                using (var gzip = new GZipStream(source, CompressionMode.Decompress))
                {
                    int offset = 0;
                    while (offset < expanded.Length)
                    {
                        int read = gzip.Read(expanded, offset, expanded.Length - offset);
                        if (read == 0) break;
                        offset += read;
                    }
                    if (offset != expanded.Length || gzip.ReadByte() != -1)
                    { RejectSnapshot("太空快照解压长度不一致"); return; }
                }
                SpaceSession.EnsureLoaded().ApplyNetworkSnapshot(new UTF8Encoding(false, true).GetString(expanded));
                lastAppliedSequence = value.Header.Sequence;
                needsSnapshot = false;
            }
            catch (InvalidDataException exception) { RejectSnapshot(exception.Message); }
            catch (DecoderFallbackException exception) { RejectSnapshot(exception.Message); }
            catch (Newtonsoft.Json.JsonException exception) { RejectSnapshot(exception.Message); }
        }
        private void RejectSnapshot(string reason)
        {
            incoming = null; needsSnapshot = true; nextClientRequest = Time.unscaledTime + .5f;
            Debug.LogWarning($"[太空联机] 丢弃无效快照并重新请求：{reason}");
        }
        #endregion
    }
}
