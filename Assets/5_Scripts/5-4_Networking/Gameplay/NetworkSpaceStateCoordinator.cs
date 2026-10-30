using System;
using System.Collections.Generic;
using System.IO;
using FlatWorld.Diagnostics;
using FlatWorld.Spaceflight;
using Mirror;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    [DisallowMultipleComponent]
    public sealed partial class NetworkSpaceStateCoordinator : MonoBehaviour
    {
        #region 太空复制性能诊断
        private static readonly PerformanceDebugPoint SnapshotBatchPoint = new("FlatWorld.Space.Network.Batch");
        private static readonly PerformanceDebugPoint SnapshotSendPoint = new("FlatWorld.Space.Network.Send");
        private static readonly PerformanceDebugPoint SnapshotFragmentPoint = new("FlatWorld.Space.Network.FragmentSend");
        private static readonly PerformanceDebugPoint SnapshotBytesPoint = new("FlatWorld.Space.Network.PayloadBytes");
        private static readonly PerformanceDebugPoint SnapshotAssemblePoint = new("FlatWorld.Space.Network.Assemble");
        private static readonly PerformanceDebugPoint SnapshotApplyPoint = new("FlatWorld.Space.Network.Apply");
        #endregion

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
        private uint latestCombatIntentSequence;
        private ulong snapshotSequence, lastAppliedSequence;
        private Incoming incoming;
        private bool needsSnapshot;
        private bool observedLocalRealm, previousLocalInSpace;
        private readonly SpaceNetworkSnapshotDeltaCodec.Receiver snapshotReceiver = new();
        private sealed class ConnectionState
        {
            public NetworkConnectionToClient Connection;
            public uint InputSequence, IntentSequence;
            public float NextRequest, InputTime;
            public string Profile, MetadataWorldKey;
            public readonly SpaceNetworkSnapshotDeltaCursor SnapshotCursor = new();
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
            StartFixedShipServerBridge();
            NetworkServer.RegisterHandler<NetworkSpaceSnapshotRequest>(OnSnapshotRequest, true);
            NetworkServer.RegisterHandler<NetworkSpaceInput>(OnInput, true);
            NetworkServer.RegisterHandler<NetworkSpaceIntentRequest>(OnIntent, true);
            FlatWorld.NaturalEntities.NaturalEntityEcsService.NetworkStateCommitted += OnNaturalHarvestCommitted;
            FlatWorld.NaturalEntities.NaturalEntityEcsService.IndependentPhysicsStateCommitted += OnIndependentNaturalStateCommitted;
            StartDimensionServerBridge();
            serverStarted = true;
            nextServerSnapshot = 0f;
        }
        public void StartClientSide()
        {
            if (clientStarted) return;
            StartFixedShipClientBridge();
            NetworkClient.RegisterHandler<NetworkSpaceSnapshotBegin>(OnSnapshotBegin, true);
            NetworkClient.RegisterHandler<NetworkSpaceSnapshotChunk>(OnSnapshotChunk, true);
            NetworkClient.RegisterHandler<NetworkSpaceIntentResponse>(OnIntentResponse, true);
            NetworkClient.RegisterHandler<NetworkSpaceProjectilePoses>(OnProjectilePoses, true);
            NetworkClient.RegisterHandler<NetworkEcologySnapshotPart>(OnEcologySnapshot, true);
            NetworkClient.RegisterHandler<NetworkNaturalHarvestState>(OnNaturalHarvestState, true);
            NetworkClient.RegisterHandler<NetworkNaturalPhysicsPoseBatch>(OnIndependentNaturalPhysicsPoseBatch, true);
            SpaceNetworkBridge.InputSender = SendInput;
            SpaceNetworkBridge.IntentSender = SendIntent;
            SpaceNetworkBridge.IntentWithReplySender = SendIntentWithReply;
            SpaceNetworkBridge.CombatPreparationSender = PrepareLocalCombat;
            StartDimensionClientBridge();
            StartWorldSnapshotClientBridge();
            clientStarted = true;
            snapshotReceiver.Reset();
            needsSnapshot = true;
            nextClientRequest = 0f; lastAppliedSequence = 0; inputSequence = intentSequence = 0;
        }
        public void StopServerSide()
        {
            if (!serverStarted) return;
            StopFixedShipServerBridge();
            foreach (ConnectionState state in connections.Values)
            {
                SpaceSession.Current?.RemoveNetworkPassengerInput(state.Profile);
                SpaceSession.Current?.EndNetworkAttack(state.Profile);
            }
            connections.Clear();
            knownIndependentPhysicsSources.Clear();
            NetworkServer.UnregisterHandler<NetworkSpaceSnapshotRequest>();
            NetworkServer.UnregisterHandler<NetworkSpaceInput>();
            NetworkServer.UnregisterHandler<NetworkSpaceIntentRequest>();
            FlatWorld.NaturalEntities.NaturalEntityEcsService.NetworkStateCommitted -= OnNaturalHarvestCommitted;
            FlatWorld.NaturalEntities.NaturalEntityEcsService.IndependentPhysicsStateCommitted -= OnIndependentNaturalStateCommitted;
            StopDimensionServerBridge();
            serverStarted = false;
        }
        public void StopClientSide()
        {
            snapshotReceiver.Reset();
            if (!clientStarted) return;
            StopFixedShipClientBridge();
            NetworkClient.UnregisterHandler<NetworkSpaceSnapshotBegin>();
            NetworkClient.UnregisterHandler<NetworkSpaceSnapshotChunk>();
            NetworkClient.UnregisterHandler<NetworkSpaceIntentResponse>();
            NetworkClient.UnregisterHandler<NetworkSpaceProjectilePoses>();
            NetworkClient.UnregisterHandler<NetworkEcologySnapshotPart>();
            NetworkClient.UnregisterHandler<NetworkNaturalHarvestState>();
            NetworkClient.UnregisterHandler<NetworkNaturalPhysicsPoseBatch>();
            ClearEcologySnapshots();
            ClearProjectileViews();
            SpaceSession.ClearClientNetworkCombat();
            if (SpaceNetworkBridge.InputSender == SendInput) SpaceNetworkBridge.InputSender = null;
            if (SpaceNetworkBridge.IntentSender == SendIntent) SpaceNetworkBridge.IntentSender = null;
            if (SpaceNetworkBridge.IntentWithReplySender == SendIntentWithReply) SpaceNetworkBridge.IntentWithReplySender = null;
            ClearIntentReplies();
            if (SpaceNetworkBridge.CombatPreparationSender == PrepareLocalCombat) SpaceNetworkBridge.CombatPreparationSender = null;
            StopDimensionClientBridge();
            StopWorldSnapshotClientBridge();
            incoming = null; clientStarted = false;
        }
        public void RemoveConnection(NetworkConnectionToClient connection)
        {
            RemoveFixedShipConnection(connection);
            // 请求表按连接对象做键，断线立即释放，旧连接清理也不会影响复用同 ID 的新连接。
            if (connection != null) dimensionRequests.Remove(connection);
            // 只清理由原连接持有的输入与攻击状态，防止过期断线回调误删新连接。
            if (connection == null || !connections.TryGetValue(connection.connectionId, out ConnectionState state) ||
                !ReferenceEquals(state.Connection, connection)) return;
            SpaceSession.Current?.RemoveNetworkPassengerInput(state.Profile);
            SpaceSession.Current?.EndNetworkAttack(state.Profile);
            connections.Remove(connection.connectionId);
            knownIndependentPhysicsSources.Remove(connection);
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
            NetworkClient.Send(new NetworkSpaceInput { RealmRevision = NetworkGameplayWorldScope.LocalRevision, Sequence = NextToken(ref inputSequence), Input = input }, Channels.Unreliable);
            return true;
        }
        private bool SendIntent(string profile, SpaceNetworkIntent intent)
        {
            if (ItemNetworkStateSerialization.HasPendingLocalInventoryTransaction() &&
                intent.Kind is SpaceNetworkIntentKind.Attack or SpaceNetworkIntentKind.UseSkill or SpaceNetworkIntentKind.UsePlayerItem or SpaceNetworkIntentKind.OffhandParry) return false;
            if (NetworkServer.active || !NetworkClient.ready || !OwnsProfile(profile)) return false;
            intent.RealmRevision = NetworkClient.localPlayer.GetComponent<NetworkWorldPlayer>().RealmRevision;
            uint sequence = NextToken(ref intentSequence);
            if (intent.StartsGroundCombat || !SpaceSession.ClientGroundCombatPending &&
                (intent.Kind is SpaceNetworkIntentKind.Attack or SpaceNetworkIntentKind.AttackEnd or SpaceNetworkIntentKind.UseSkill or SpaceNetworkIntentKind.UsePlayerItem or SpaceNetworkIntentKind.OffhandParry))
                latestCombatIntentSequence = sequence;
            NetworkClient.Send(new NetworkSpaceIntentRequest { Sequence = sequence, Intent = intent });
            return true;
        }
        private bool PrepareLocalCombat(string profile)
            => OwnsProfile(profile) && NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>()?.SubmitGroundCombatBaseline() == true;
        private void OnInput(NetworkConnectionToClient connection, NetworkSpaceInput message)
        {
            if (!ResolveConnection(connection, out ConnectionState state, out NetworkWorldPlayer player) ||
                player.IsWorldTransferPending || player.RealmRevision != message.RealmRevision ||
                !IsNewer(message.Sequence, state.InputSequence) || !ValidInput(message.Input)) return;
            state.InputSequence = message.Sequence; state.InputTime = Time.unscaledTime;
            SpaceSession.Current?.SubmitPassengerInput(state.Profile, message.Input);
        }
        private void OnIntent(NetworkConnectionToClient connection, NetworkSpaceIntentRequest message)
        {
            if (!ResolveConnection(connection, out ConnectionState state, out NetworkWorldPlayer player) || !IsNewer(message.Sequence, state.IntentSequence)) return;
            state.IntentSequence = message.Sequence;
            if (player.IsWorldTransferPending)
            {
                connection.Send(new NetworkSpaceIntentResponse { Sequence = message.Sequence, Accepted = false, Reason = "正在切换维度，请等待目标世界准备完成。" });
                return;
            }
            using var worldScope = NetworkGameplayWorldScope.Use(player);
            SpaceNetworkIntent intent = message.Intent;
            if (intent.RealmRevision != player.RealmRevision || player.IsWorldTransferPending)
            {
                connection.Send(new NetworkSpaceIntentResponse { Sequence = message.Sequence, Accepted = false, Reason = "玩家正在切换世界或操作来自旧世界" });
                return;
            }
            if (intent.Kind == SpaceNetworkIntentKind.Respawn)
            { StartCoroutine(ExecuteDimensionRespawn(connection, player.CorePlayer, message.Sequence, null)); return; }
            if (player.HasServerPickupTransaction && intent.Kind is SpaceNetworkIntentKind.Attack or SpaceNetworkIntentKind.UseSkill or SpaceNetworkIntentKind.UsePlayerItem or SpaceNetworkIntentKind.OffhandParry)
            {
                connection.Send(new NetworkSpaceIntentResponse { Sequence = message.Sequence, Accepted = false,
                    Reason = "正在等待服务器确认拾取库存" });
                return;
            }
            if (intent.Kind == SpaceNetworkIntentKind.CombatStateApplied)
            { player.AcknowledgeGroundCombat(unchecked((uint)intent.Action)); return; }
            bool wasGroundCombatPending = player.GroundCombatPending;
            if (ValidIntent(intent) && (intent.Kind is SpaceNetworkIntentKind.Attack or SpaceNetworkIntentKind.AttackEnd or SpaceNetworkIntentKind.UseSkill or SpaceNetworkIntentKind.UsePlayerItem or SpaceNetworkIntentKind.OffhandParry))
                player.PrepareGroundCombat(intent.Kind);
            string reason = "太空操作请求无效";
            bool accepted = ValidIntent(intent) && SpaceSession.Current != null &&
                SpaceSession.Current.ExecuteNetworkIntent(state.Profile, intent, out reason);
            if (!accepted && !wasGroundCombatPending) player.CancelRejectedGroundCombat();
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
        private void OnIntentResponse(NetworkSpaceIntentResponse response)
        {
            CompleteIntentReply(response);
            if (response.Accepted) return;
            if (response.Sequence == latestCombatIntentSequence)
            { SpaceSession.ClearClientNetworkCombat(); latestCombatIntentSequence = 0; }
            Debug.LogWarning($"[太空联机] 操作被拒绝：{response.Reason}");
        }
        #endregion

        #region 快照发送与有限分片接收
        private void Update()
        {
            UpdateWorldSnapshotTimeout();
            UpdateEcologyRealm();
            UpdateFixedShipRequests();
            UpdateProjectileViews();
            NetworkWorldPlayer local = NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>();
            if (clientStarted && !NetworkServer.active && NetworkClient.ready && local != null)
            {
                if (observedLocalRealm && previousLocalInSpace && !local.IsInSpace) NetworkClient.Send(new NetworkItemStateRequest { RealmRevision = NetworkGameplayWorldScope.LocalRevision });
                previousLocalInSpace = local.IsInSpace; observedLocalRealm = true;
            }
            if (serverStarted && NetworkServer.active && Time.unscaledTime >= nextServerSnapshot)
            {
                using var batchSample = SnapshotBatchPoint.Measure();
                SnapshotBatchPoint.SetGauge(NetworkServer.connections.Count);
                nextServerSnapshot = Time.unscaledTime + SnapshotInterval;
                using var captureBatch = SpaceSession.Current?.BeginNetworkSnapshotCaptureBatch();
                foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
                {
                    if (!ResolveConnection(connection, out ConnectionState state, out _) || connection is LocalConnectionToClient) continue;
                    if (state.InputTime > 0f && Time.unscaledTime - state.InputTime > .5f)
                    { SpaceSession.Current?.SubmitPassengerInput(state.Profile, default); state.InputTime = 0f; }
                    SendSnapshot(connection, state.Profile);
                    SnapshotBatchPoint.AddWork(1);
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
            state.MetadataWorldKey = null;
            state.SnapshotCursor.Reset();
            SpaceSession.Current?.ResetNetworkLocalWorldSnapshotCache(state.Profile);
            SendSnapshot(connection, state.Profile);
        }
        private void SendSnapshot(NetworkConnectionToClient connection, string profile)
        {
            if (SpaceSession.Current?.State == null || connection is LocalConnectionToClient ||
                !ResolveConnection(connection, out ConnectionState state, out _)) return;
            using var sendSample = SnapshotSendPoint.Measure(1);
            if (SpaceSession.Current.TryGetNetworkObserver(profile, out SpaceNetworkObserver realm) && !realm.InSpace &&
                state.MetadataWorldKey != realm.WorldKey)
            {
                SendWorldSnapshot(connection, realm.WorldKey);
                state.MetadataWorldKey = realm.WorldKey;
            }
            SendProjectilePoses(connection, profile);
            SendEcologySnapshot(connection, profile);
            if (++snapshotSequence == 0) ++snapshotSequence;
            SpaceNetworkSnapshotDeltaPacket packet = SpaceSession.Current.CaptureNetworkSnapshotDelta(profile, ObserverRangeMeters,
                state.SnapshotCursor, snapshotSequence);
            if (packet == null) return;
            byte[] payload = packet.Payload;
            int parts = (payload.Length + NetworkGameplayProtocol.SnapshotChunkBytes - 1) / NetworkGameplayProtocol.SnapshotChunkBytes;
            long sentBytes = 0;
            int sentParts = 0;
            using (SnapshotFragmentPoint.Measure())
            {
                try
                {
                    connection.Send(new NetworkSpaceSnapshotBegin { Sequence = snapshotSequence, PayloadBytes = payload.Length,
                        ChunkCount = parts, Hash = NetworkGameplayProtocol.CalculatePayloadHash(payload) });
                    for (int i = 0; i < parts; i++)
                    {
                        int offset = i * NetworkGameplayProtocol.SnapshotChunkBytes;
                        byte[] part = new byte[Math.Min(NetworkGameplayProtocol.SnapshotChunkBytes, payload.Length - offset)];
                        Buffer.BlockCopy(payload, offset, part, 0, part.Length);
                        connection.Send(new NetworkSpaceSnapshotChunk { Sequence = snapshotSequence, Index = i, Data = part });
                        sentBytes += part.LongLength;
                        sentParts++;
                    }
                }
                finally
                {
                    SnapshotBytesPoint.AddWork(sentBytes);
                    SnapshotFragmentPoint.AddWork(sentParts);
                }
            }
            SnapshotFragmentPoint.SetGauge(parts);
            SnapshotBytesPoint.SetGauge(payload.LongLength);
            packet.ConfirmSent(state.SnapshotCursor);
            SpaceSession.Current?.ConfirmNetworkLocalWorldSnapshotSent(profile);
        }
        private void OnSnapshotBegin(NetworkSpaceSnapshotBegin header)
        {
            if (NetworkServer.active || header.Sequence <= lastAppliedSequence || incoming != null && header.Sequence <= incoming.Header.Sequence) return;
            if (header.PayloadBytes <= 0 || header.PayloadBytes > MaxCompressedBytes || header.ChunkCount !=
                (header.PayloadBytes + NetworkGameplayProtocol.SnapshotChunkBytes - 1) / NetworkGameplayProtocol.SnapshotChunkBytes)
            { RejectSnapshot("太空快照头超出限额或长度不一致"); return; }
            incoming = new Incoming { Header = header, Parts = new byte[header.ChunkCount][], Deadline = Time.unscaledTime + TransferTimeout };
        }
        private void OnSnapshotChunk(NetworkSpaceSnapshotChunk chunk)
        {
            Incoming value = incoming;
            if (NetworkServer.active || value == null || chunk.Sequence != value.Header.Sequence) return;
            if ((uint)chunk.Index >= (uint)value.Parts.Length || chunk.Data == null || chunk.Data.Length !=
                Math.Min(NetworkGameplayProtocol.SnapshotChunkBytes, value.Header.PayloadBytes - chunk.Index * NetworkGameplayProtocol.SnapshotChunkBytes))
            { RejectSnapshot("太空快照分片序号或长度无效"); return; }
            if (value.Parts[chunk.Index] != null) return;
            value.Parts[chunk.Index] = chunk.Data;
            // 接收超时只检测分片停滞，持续传输的大快照不能因总时长超过五秒被丢弃。
            value.Deadline = Time.unscaledTime + TransferTimeout;
            if (++value.Received != value.Parts.Length) return;
            incoming = null;
            byte[] payload = new byte[value.Header.PayloadBytes];
            using (SnapshotAssemblePoint.Measure(payload.LongLength))
                for (int i = 0; i < value.Parts.Length; i++)
                    Buffer.BlockCopy(value.Parts[i], 0, payload, i * NetworkGameplayProtocol.SnapshotChunkBytes, value.Parts[i].Length);
            if (NetworkGameplayProtocol.CalculatePayloadHash(payload) != value.Header.Hash)
            { RejectSnapshot("太空快照校验失败"); return; }
            try
            {
                if (worldSnapshotInvalid || worldIncoming != null) { RejectSnapshot("正在重新准备目标世界资料"); return; }
                using (SnapshotApplyPoint.Measure(payload.LongLength))
                {
                    var snapshot = snapshotReceiver.Decode(payload, value.Header.Sequence);
                    if (!SpaceSession.EnsureLoaded().ApplyNetworkSnapshot(snapshot.State, snapshot.IsFullSnapshot))
                    { RejectSnapshot("太空快照时钟早于当前权威状态"); return; }
                    snapshotReceiver.ConfirmApplied(snapshot);
                }
                lastAppliedSequence = value.Header.Sequence;
                needsSnapshot = false;
            }
            catch (InvalidDataException exception) { RejectSnapshot(exception.Message); }
            catch (EndOfStreamException exception) { RejectSnapshot(exception.Message); }
            catch (System.Text.DecoderFallbackException exception) { RejectSnapshot(exception.Message); }
            catch (Newtonsoft.Json.JsonException exception) { RejectSnapshot(exception.Message); }
            catch (InvalidOperationException exception) { RejectSnapshot(exception.Message); }
            catch (FormatException exception) { RejectSnapshot(exception.Message); }
        }
        private void RejectSnapshot(string reason)
        {
            incoming = null;
            if (!needsSnapshot) nextClientRequest = Time.unscaledTime + .5f;
            needsSnapshot = true;
            Debug.LogWarning($"[太空联机] 丢弃无效快照并重新请求：{reason}");
        }
        #endregion
    }
}
