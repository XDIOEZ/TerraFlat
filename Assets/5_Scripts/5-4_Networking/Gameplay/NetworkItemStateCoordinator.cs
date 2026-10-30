// AI-Context: 世界 Item 与建筑的服务端权威协调器；客户端只能请求，GUID、放置校验、生成与广播均由服务端提交。
using System;
using System.Collections.Generic;
using FlatWorld.Gameplay.Building;
using FlatWorld.Spaceflight;
using Mirror;
using Unity.Profiling;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    /// <summary>
    /// 世界 Item 的服务器权威快照通道。状态以 Item Guid 定位，内容由 Item 系统统一序列化，
    /// 因此 Mod_DamageReceiver 之外的 Module 也会自动进入同步范围。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class NetworkItemStateCoordinator : MonoBehaviour
    {
        #region 物品通道状态与连接生命周期

        private const float ScanInterval = 0.5f;
        private const int MaxScanItemsPerFrame = 4;
        private const int MaxSpawnCandidatesPerFrame = 32;
        private const float MaxDropDistance = 12f;
        private const float PickupReservationSeconds = 6f;
        private const float MaxPickupDistance = 5f;
        private static readonly ProfilerMarker ScheduledScanMarker = new("FlatWorld.Network.Items.ScheduledScan");
        private static readonly ProfilerMarker CaptureMarker = new("FlatWorld.Network.Items.Capture");
        private static readonly ProfilerMarker SpawnCandidatesMarker = new("FlatWorld.Network.Items.SpawnCandidates");
        private static readonly ProfilerMarker PendingStatesMarker = new("FlatWorld.Network.Items.PendingStates");

        private sealed class StateRecord
        {
            public string ItemId, WorldKey;
            public uint Revision;
            public uint PoseRevision;
            public uint Hash;
            public ulong CaptureVersion;
            public byte[] LastCapturedPayload;
            public byte[] Payload;
            public bool SubmissionPending;
            public bool SpawnIfMissing;
            public bool Applied;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
        }

        private sealed class ClientPickupRequest
        {
            public Mod_ItemPicker Picker;
            public string ItemId;
            public uint RequestToken;
            public uint ReservationToken;
            public float RequestedSourceAmount;
            public bool CommitSent;
            public float ExpiresAt;
        }

        private sealed class ServerPickupReservation
        {
            public NetworkConnectionToClient Connection;
            public NetworkWorldPlayer Actor;
            public uint RequestToken;
            public uint Token;
            public Item Item;
            public ItemData SpaceData;
            public ItemData SourceData;
            public float SourceAmount;
            public byte[] Payload;
            public float ExpiresAt;
        }

        private readonly Dictionary<int, StateRecord> serverStates = new();
        private readonly Dictionary<int, StateRecord> clientStates = new();
        private readonly Dictionary<int, NetworkItemStateMessage> pendingClientStates = new();
        private readonly List<int> pendingStateKeys = new();
        private readonly Dictionary<int, ClientPickupRequest> pendingClientPickups = new();
        private readonly Dictionary<int, ServerPickupReservation> serverPickupReservations = new();
        private float nextScanTime;
        private Item[] scheduledScanItems;
        private int scheduledScanIndex;
        private bool scheduledScanAsServer;
        private bool serverHandlersRegistered;
        private bool clientHandlersRegistered;
        private bool runtimeBridgeRegistered;
        private bool requestedInitialState;
        private string requestedItemWorld;
        private uint requestedItemRealm;
        private string requestedSurfaceMechanicalWorld;
        private uint requestedSurfaceMechanicalRealm;
        private uint nextPickupReservationToken;
        private uint nextPickupRequestToken;

        public void StartServerSide()
        {
            if (serverHandlersRegistered)
                return;

            NetworkServer.RegisterHandler<NetworkItemStateRequest>(NetworkGameplayWorldScope.Wrap<NetworkItemStateRequest>(OnServerStateRequest), false);
            NetworkServer.RegisterHandler<NetworkItemStateSubmit>(NetworkGameplayWorldScope.Wrap<NetworkItemStateSubmit>(OnServerStateSubmit), false);
            NetworkServer.RegisterHandler<NetworkItemSpawnRequest>(NetworkGameplayWorldScope.Wrap<NetworkItemSpawnRequest>(OnServerSpawnRequest), false);
            NetworkServer.RegisterHandler<NetworkItemPickupRequest>(NetworkGameplayWorldScope.Wrap<NetworkItemPickupRequest>(OnServerPickupRequest), false);
            NetworkServer.RegisterHandler<NetworkItemPickupCommit>(NetworkGameplayWorldScope.Wrap<NetworkItemPickupCommit>(OnServerPickupCommit), false);
            NetworkServer.RegisterHandler<NetworkItemPickupApplied>(NetworkGameplayWorldScope.Wrap<NetworkItemPickupApplied>(OnServerPickupApplied), false);
            NetworkServer.RegisterHandler<NetworkBuildingPlaceRequest>(NetworkGameplayWorldScope.Wrap<NetworkBuildingPlaceRequest>(
                OnServerBuildingPlaceRequest, (connection, request, reason) => connection.Send(new NetworkBuildingPlaceResponse
                { RequestToken = request.RequestToken, SourceItemGuid = request.SourceItemGuid, Accepted = false, Reason = reason })), false);
            NetworkServer.RegisterHandler<NetworkBuildingDismantleRequest>(NetworkGameplayWorldScope.Wrap<NetworkBuildingDismantleRequest>(
                OnServerBuildingDismantleRequest, (connection, request, reason) => connection.Send(new NetworkBuildingDismantleResponse
                { RequestToken = request.RequestToken, BuildingGuid = request.BuildingGuid, Accepted = false, Reason = reason })), false);
            machineStateReplication.StartServerSide();
            StartMachineServerBridge();
            StartTerrainServerBridge();
            serverHandlersRegistered = true;
            UpdateRuntimeBridgeRegistration();
        }

        public void StartClientSide()
        {
            if (clientHandlersRegistered)
                return;

            NetworkClient.RegisterHandler<NetworkItemStateMessage>(OnClientStateMessage, false);
            NetworkClient.RegisterHandler<NetworkItemPoseMessage>(OnClientPoseMessage, false);
            NetworkClient.RegisterHandler<NetworkItemSpawnMessage>(OnClientSpawnMessage, false);
            NetworkClient.RegisterHandler<NetworkItemDespawnMessage>(OnClientDespawnMessage, false);
            NetworkClient.RegisterHandler<NetworkItemPickupResponse>(OnClientPickupResponse, false);
            NetworkClient.RegisterHandler<NetworkItemPickupReceipt>(OnClientPickupReceipt, false);
            NetworkClient.RegisterHandler<NetworkBuildingPlaceResponse>(OnClientBuildingPlaceResponse, false);
            NetworkClient.RegisterHandler<NetworkBuildingDismantleResponse>(OnClientBuildingDismantleResponse, false);
            NetworkClient.RegisterHandler<NetworkMechanicalNodeMessage>(machineStateReplication.ApplyServerSnapshot, false);
            StartMachineClientBridge();
            StartTerrainClientBridge();
            clientHandlersRegistered = true;
            requestedInitialState = false;
            requestedItemWorld = null;
            requestedSurfaceMechanicalWorld = null;
            UpdateRuntimeBridgeRegistration();
        }

        public void StopServerSide()
        {
            if (!serverHandlersRegistered)
                return;

            NetworkServer.UnregisterHandler<NetworkItemStateRequest>();
            NetworkServer.UnregisterHandler<NetworkItemStateSubmit>();
            NetworkServer.UnregisterHandler<NetworkItemSpawnRequest>();
            NetworkServer.UnregisterHandler<NetworkItemPickupRequest>();
            NetworkServer.UnregisterHandler<NetworkItemPickupCommit>();
            NetworkServer.UnregisterHandler<NetworkItemPickupApplied>();
            NetworkServer.UnregisterHandler<NetworkBuildingPlaceRequest>();
            NetworkServer.UnregisterHandler<NetworkBuildingDismantleRequest>();
            machineStateReplication.StopServerSide();
            StopMachineServerBridge();
            StopTerrainServerBridge();
            ReleaseAllPickupReservations();
            serverHandlersRegistered = false;
            serverStates.Clear();
            ClearScheduledScan();
            UpdateRuntimeBridgeRegistration();
        }

        public void StopClientSide()
        {
            if (!clientHandlersRegistered)
                return;

            NetworkClient.UnregisterHandler<NetworkItemStateMessage>();
            NetworkClient.UnregisterHandler<NetworkItemPoseMessage>();
            NetworkClient.UnregisterHandler<NetworkItemSpawnMessage>();
            NetworkClient.UnregisterHandler<NetworkItemDespawnMessage>();
            NetworkClient.UnregisterHandler<NetworkItemPickupResponse>();
            NetworkClient.UnregisterHandler<NetworkItemPickupReceipt>();
            NetworkClient.UnregisterHandler<NetworkBuildingPlaceResponse>();
            NetworkClient.UnregisterHandler<NetworkBuildingDismantleResponse>();
            NetworkClient.UnregisterHandler<NetworkMechanicalNodeMessage>();
            StopMachineClientBridge();
            StopTerrainClientBridge();
            clientHandlersRegistered = false;
            clientStates.Clear();
            pendingClientStates.Clear();
            pendingSpawnCandidates.Clear();
            requestedClientSpawns.Clear();
            networkInstantiationsInProgress.Clear();
            pendingClientPickups.Clear();
            appliedClientPickupReceipts.Clear();
            buildingRequests.RejectAll("网络连接已关闭");
            requestedInitialState = false;
            requestedItemWorld = null;
            ClearScheduledScan();
            UpdateRuntimeBridgeRegistration();
        }

        private void Update()
        {
            TickMachineOperationReceipts();
            TickTerrainReplication();
            if (pendingQuickTransfer.HasValue && Time.unscaledTime > machineTransferDeadline)
                pendingQuickTransfer = null;
            TickFluidNetwork();
            ProcessPickupTimeouts();
            buildingRequests.Expire(Time.unscaledTime);
            ProcessSpawnCandidates();
            machineStateReplication.Tick(Time.unscaledTime);

            if (clientHandlersRegistered && NetworkGameplayWorldScope.TryGetReadyLocalItemWorld(
                out uint readyRealmRevision, out string readyItemWorld))
            {
                if (requestedItemWorld != readyItemWorld || requestedItemRealm != readyRealmRevision)
                {
                    requestedItemWorld = readyItemWorld; requestedItemRealm = readyRealmRevision;
                    requestedInitialState = false;
                    clientStates.Clear(); pendingClientStates.Clear();
                }
                if (!requestedInitialState)
                {
                    requestedInitialState = true;
                    NetworkClient.Send(new NetworkItemStateRequest { RealmRevision = readyRealmRevision });
                    if (NetworkGameplayWorldScope.TryGetReadyLocalSurface(out uint initialSurfaceRealm))
                    {
                        requestedSurfaceMechanicalWorld = GameplayWorldRegistry.LocalPresentationWorldKey;
                        requestedSurfaceMechanicalRealm = initialSurfaceRealm;
                    }
                }
                else if (GameplayWorldRegistry.UsesLocalCoordinates(readyItemWorld) &&
                    NetworkGameplayWorldScope.TryGetReadyLocalSurface(out uint readySurfaceRealm) &&
                    (requestedSurfaceMechanicalWorld != GameplayWorldRegistry.LocalPresentationWorldKey ||
                     requestedSurfaceMechanicalRealm != readySurfaceRealm))
                {
                    // 局部世界比外部地表先就绪时，地表机器在视窗就绪后补拉。
                    requestedSurfaceMechanicalWorld = GameplayWorldRegistry.LocalPresentationWorldKey;
                    requestedSurfaceMechanicalRealm = readySurfaceRealm;
                    NetworkClient.Send(new NetworkItemStateRequest { RealmRevision = readyRealmRevision });
                }
            }

            ApplyPendingClientStates();

            // 私有 Host 没有远端接收者时不持续序列化世界物品；远端入世请求会同步捕获完整基线。
            if (NetworkServer.active && !HasRemoteReadyClient())
            {
                ClearScheduledScan();
                nextScanTime = Time.unscaledTime;
                return;
            }

            if (scheduledScanItems == null)
            {
                if (Time.unscaledTime < nextScanTime)
                    return;

                BeginScheduledScan();
            }

            ProcessScheduledScan();
        }

        #endregion

        #region 物品状态捕获与复制

        private void ScanServerItems()
        {
            if (ItemMgr.Instance == null)
                return;

            Item[] items = SnapshotRuntimeItems();
            for (int i = 0; i < items.Length; i++)
                ProcessServerItem(items[i]);
        }

        private void BeginScheduledScan()
        {
            if (ItemMgr.Instance == null)
            {
                nextScanTime = Time.unscaledTime + ScanInterval;
                return;
            }

            scheduledScanItems = SnapshotRuntimeItems();
            scheduledScanIndex = 0;
            scheduledScanAsServer = NetworkServer.active;
        }

        private void ProcessScheduledScan()
        {
            using var marker = ScheduledScanMarker.Auto();
            if (scheduledScanItems == null)
                return;

            int end = Mathf.Min(scheduledScanIndex + MaxScanItemsPerFrame, scheduledScanItems.Length);
            for (; scheduledScanIndex < end; scheduledScanIndex++)
            {
                Item item = scheduledScanItems[scheduledScanIndex];
                if (scheduledScanAsServer)
                    ProcessServerItem(item);
                else if (NetworkClient.active)
                    ProcessClientItem(item);
            }

            if (scheduledScanIndex < scheduledScanItems.Length)
                return;

            ClearScheduledScan();
            nextScanTime = Time.unscaledTime + ScanInterval;
        }

        private void ProcessServerItem(Item item)
        {
            string itemWorld = GameplayWorldRegistry.GetItemWorldKey(item);
            using var worldScope = GameplayWorldRegistry.TryGet(itemWorld, out _)
                ? GameplayWorldRegistry.UseWorld(itemWorld) : default;
            if (!ShouldSynchronize(item))
                return;

            byte[] payload = CaptureSafely(item, out ulong captureVersion, out uint hash);
            if (!ItemNetworkStateSerialization.IsValidPayload(payload))
                return;

            int guid = item.itemData.Guid;
            serverStates.TryGetValue(guid, out StateRecord state);
            state ??= new StateRecord { ItemId = item.itemData.IDName };
            string previousWorld = state.WorldKey;
            bool poseChanged = CapturePose(item, state);
            bool worldChanged = !string.IsNullOrEmpty(previousWorld) && previousWorld != state.WorldKey;
            if (worldChanged) SendItemDespawn(guid, state.ItemId, previousWorld);
            if (IsSameCapturedState(state, payload, captureVersion, hash))
            {
                if (worldChanged) { state.Revision++; BroadcastServerState(guid, state); }
                else if (poseChanged) BroadcastServerPose(guid, state);
                return;
            }
            state.ItemId = item.itemData.IDName;
            state.Revision++;
            state.Hash = hash;
            state.Payload = payload;
            state.CaptureVersion = captureVersion;
            state.LastCapturedPayload = payload;
            serverStates[guid] = state;
            BroadcastServerState(guid, state);
        }

        private void ProcessClientItem(Item item)
        {
            if (!ShouldSynchronize(item))
                return;

            int guid = item.itemData.Guid;
            if (!clientStates.TryGetValue(guid, out StateRecord state) || !state.Applied || state.SubmissionPending ||
                requestedItemRealm != NetworkGameplayWorldScope.LocalRevision ||
                state.WorldKey != requestedItemWorld || GameplayWorldRegistry.GetItemWorldKey(item) != requestedItemWorld)
                return;

            using var worldScope = GameplayWorldRegistry.TryGet(state.WorldKey, out _)
                ? GameplayWorldRegistry.UseWorld(state.WorldKey) : default;
            byte[] payload = CaptureSafely(item, out ulong captureVersion, out uint hash);
            if (!ItemNetworkStateSerialization.IsValidPayload(payload))
                return;

            if (IsSameCapturedState(state, payload, captureVersion, hash))
                return;

            state.SubmissionPending = true;
            NetworkClient.Send(new NetworkItemStateSubmit
            { RealmRevision = NetworkGameplayWorldScope.LocalRevision,
                ItemGuid = guid,
                BaseRevision = state.Revision,
                Payload = payload
            });
        }

        private void ClearScheduledScan()
        {
            scheduledScanItems = null;
            scheduledScanIndex = 0;
        }

        private void OnServerStateRequest(NetworkConnectionToClient connection, NetworkItemStateRequest request)
        {
            ScanServerItems();
            foreach (KeyValuePair<int, StateRecord> pair in serverStates)
                if (pair.Value.WorldKey == GameplayWorldRegistry.ExecutionWorldKey) connection.Send(CreateMessage(pair.Key, pair.Value));
            if (GameplayWorldRegistry.UsesLocalCoordinates(GameplayWorldRegistry.ExecutionWorldKey))
            {
                NetworkWorldPlayer player = connection?.identity?.GetComponent<NetworkWorldPlayer>();
                if (player?.TryGetSpaceObserver(out SpaceNetworkObserver observer) != true || observer.InSpace ||
                    !GameplayWorldRegistry.TryGet(observer.WorldKey, out _)) return;
                using (GameplayWorldRegistry.UseWorld(observer.WorldKey)) machineStateReplication.SendBaseline(connection);
                return;
            }
            machineStateReplication.SendBaseline(connection);
        }


        private void OnServerStateSubmit(NetworkConnectionToClient connection, NetworkItemStateSubmit submit)
        {
            if (connection?.identity == null || !ItemNetworkStateSerialization.IsValidPayload(submit.Payload))
                return;

            Item item = ItemMgr.Instance?.GetItemByGuid(submit.ItemGuid);
            if (!ShouldSynchronize(item) || !NetworkGameplayWorldScope.OwnsItem(connection, item))
                return;

            if (!serverStates.TryGetValue(submit.ItemGuid, out StateRecord state))
            {
                ScanServerItems();
                if (serverStates.TryGetValue(submit.ItemGuid, out state))
                    connection.Send(CreateMessage(submit.ItemGuid, state));
                return;
            }

            if (submit.BaseRevision != state.Revision)
            {
                connection.Send(CreateMessage(submit.ItemGuid, state));
                return;
            }

            if (!ItemNetworkStateSerialization.Apply(item, submit.Payload, true, true))
            {
                connection.Send(CreateMessage(submit.ItemGuid, state));
                return;
            }

            state.Revision++;
            state.Hash = ItemNetworkStateSerialization.CalculateHash(submit.Payload);
            state.Payload = submit.Payload;
            state.CaptureVersion = 0;
            state.LastCapturedPayload = null;
            state.ItemId = item.itemData.IDName;
            CapturePose(item, state);
            BroadcastServerState(submit.ItemGuid, state);

            Mod_DamageReceiver damageReceiver = item.GetComponentInChildren<Mod_DamageReceiver>(true);
            if (damageReceiver != null && damageReceiver.Hp <= 0f)
            {
                // 零血先同步死亡表现，真实回收时再统一广播删除。
                damageReceiver.ResolveNetworkAuthoritativeDeath();
            }
        }

        private void OnClientStateMessage(NetworkItemStateMessage message)
        {
            if (!AcceptLocalWorld(message.WorldKey)) return;
            if (!ItemNetworkStateSerialization.IsValidPayload(message.Payload))
                return;

            clientStates.TryGetValue(message.ItemGuid, out StateRecord known);
            if (known != null && known.WorldKey != message.WorldKey) known = null;
            if (known != null && message.Revision < known.Revision)
                return;
            if (known != null && known.Applied && !known.SubmissionPending && known.Revision == message.Revision &&
                known.Hash == message.PayloadHash && known.ItemId == message.ItemId)
            {
                OnClientPoseMessage(new NetworkItemPoseMessage { ItemGuid = message.ItemGuid, ItemId = message.ItemId,
                    WorldKey = message.WorldKey, StateRevision = message.Revision, PoseRevision = message.PoseRevision,
                    Position = message.Position, Rotation = message.Rotation, Scale = message.Scale });
                return;
            }

            StateRecord state = known ?? new StateRecord();
            state.ItemId = message.ItemId;
            state.WorldKey = message.WorldKey;
            state.Applied = false;
            state.Revision = message.Revision;
            state.Hash = message.PayloadHash;
            state.Payload = message.Payload;
            state.CaptureVersion = 0;
            state.LastCapturedPayload = null;
            state.SubmissionPending = false;
            state.SpawnIfMissing = message.SpawnIfMissing;
            if (message.PoseRevision >= state.PoseRevision)
            {
                state.PoseRevision = message.PoseRevision;
                state.Position = message.Position;
                state.Rotation = message.Rotation;
                state.Scale = message.Scale;
            }
            else
            {
                message.PoseRevision = state.PoseRevision;
                message.Position = state.Position;
                message.Rotation = state.Rotation;
                message.Scale = state.Scale;
            }
            clientStates[message.ItemGuid] = state;

            if (!TryApplyClientState(message))
                pendingClientStates[message.ItemGuid] = message;
            else
                pendingClientStates.Remove(message.ItemGuid);
        }

        private void OnClientDespawnMessage(NetworkItemDespawnMessage message)
        {
            if (!AcceptLocalWorld(message.WorldKey)) return;
            clientStates.Remove(message.ItemGuid);
            pendingClientStates.Remove(message.ItemGuid);

            if (NetworkServer.active)
                return;

            Item item = ItemMgr.Instance?.GetItemByGuid(message.ItemGuid);
            if (item == null || item.itemData == null ||
                GameplayWorldRegistry.GetItemWorldKey(item) != message.WorldKey ||
                !string.Equals(item.itemData.IDName, message.ItemId, StringComparison.Ordinal))
            {
                return;
            }

            // 局部世界的船件和作物由各自的权威快照负责卸载与迁移。
            if (GameplayWorldRegistry.UsesLocalCoordinates(message.WorldKey) &&
                item.PersistenceOwner is SpaceSession or ChunkAgricultureRenderer) return;
            using var worldScope = GameplayWorldRegistry.TryGet(message.WorldKey, out _)
                ? GameplayWorldRegistry.UseWorld(message.WorldKey) : default;
            ItemMgr.Instance.DespawnItem(item, saveData: false);
        }

        private void ApplyPendingClientStates()
        {
            if (pendingClientStates.Count == 0 || ItemMgr.Instance == null)
                return;

            using var marker = PendingStatesMarker.Auto();
            // 尚未加载的世界实体可能跨多帧等待，复用键缓冲避免每帧分配。
            pendingStateKeys.Clear();
            pendingStateKeys.AddRange(pendingClientStates.Keys);
            for (int i = 0; i < pendingStateKeys.Count; i++)
            {
                int guid = pendingStateKeys[i];
                if (pendingClientStates.TryGetValue(guid, out NetworkItemStateMessage message) &&
                    TryApplyClientState(message))
                {
                    pendingClientStates.Remove(guid);
                }
            }
            pendingStateKeys.Clear();
        }

        private bool TryApplyClientState(NetworkItemStateMessage message)
        {
            // Host 已经在服务器路径应用过状态，避免同一实例重复 Load。
            if (NetworkServer.active)
                return true;

            if (!AcceptLocalWorld(message.WorldKey) || !GameplayWorldRegistry.TryGet(message.WorldKey, out _)) return false;
            using var scope = GameplayWorldRegistry.UseWorld(message.WorldKey);
            Item item = ItemMgr.Instance?.GetItemByGuid(message.ItemGuid);
            if (item != null && GameplayWorldRegistry.GetItemWorldKey(item) != message.WorldKey) return false;
            bool created = item == null && message.SpawnIfMissing;
            if (item == null && message.SpawnIfMissing)
            {
                item = InstantiateClientItem(
                    message.ItemGuid,
                    message.ItemId,
                    message.Payload,
                    message.Position,
                    message.Rotation,
                    message.Scale,
                    false,
                    Vector2.zero,
                    Vector2.zero,
                    0f);
            }

            if (!ShouldSynchronize(item) || !string.Equals(item.itemData.IDName, message.ItemId, StringComparison.Ordinal))
                return false;

            if (!created && !ItemNetworkStateSerialization.Apply(item, message.Payload, true, true)) return false;
            ApplyClientPose(item, message.Position, message.Rotation, message.Scale);
            if (clientStates.TryGetValue(message.ItemGuid, out StateRecord state) && state.WorldKey == message.WorldKey)
                state.Applied = true;
            return true;
        }

        private void BroadcastServerState(int guid, StateRecord state)
        {
            if (NetworkServer.active)
                SendToGroundObservers(CreateMessage(guid, state), state.WorldKey);
        }

        private static bool AcceptLocalWorld(string worldKey)
        {
            Player player = ItemMgr.Instance?.User_Player;
            string ownedWorld = GameplayWorldRegistry.GetItemWorldKey(player) ?? player?.Data?.CurrentSceneName;
            return !string.IsNullOrWhiteSpace(worldKey) && worldKey == ownedWorld;
        }

        private static void SendToGroundObservers<T>(T message, string worldKey = null, bool observerWorld = false) where T : struct, NetworkMessage
        {
            worldKey ??= GameplayWorldRegistry.ExecutionWorldKey;
            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
                if (connection != null && connection.isAuthenticated && connection.isReady &&
                    connection.identity?.GetComponent<NetworkWorldPlayer>() is NetworkWorldPlayer observer)
                {
                    if (observerWorld)
                    {
                        if (observer.TryGetSpaceObserver(out SpaceNetworkObserver surface) && !surface.InSpace && surface.WorldKey == worldKey)
                            connection.Send(message);
                        continue;
                    }
                    string ownedWorld = GameplayWorldRegistry.GetItemWorldKey(observer.CorePlayer);
                    if (string.IsNullOrEmpty(ownedWorld) && observer.TryGetSpaceObserver(out SpaceNetworkObserver realm) && !realm.InSpace)
                        ownedWorld = realm.WorldKey;
                    if (ownedWorld == worldKey) connection.Send(message);
                }
        }

        private static void SendItemDespawn(int guid, string itemId, string worldKey)
        {
            if (string.IsNullOrEmpty(worldKey)) return;
            SendToGroundObservers(new NetworkItemDespawnMessage { ItemGuid = guid, ItemId = itemId, WorldKey = worldKey }, worldKey);
        }

        #endregion

        #region 通道公共身份与运行时桥接

        private static uint NextNonZeroToken(ref uint token)
        {
            token++;
            if (token == 0)
                token++;
            return token;
        }

        private static int GenerateUniqueRuntimeGuid()
        {
            int guid;
            do
            {
                guid = ItemMgr.Instance.GenerateGuid();
            }
            while (guid == 0 || ItemMgr.Instance.GetItemByGuid(guid) != null);
            return guid;
        }

        private static string LimitReason(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return "服务器拒绝放置";
            return reason.Length <= 160 ? reason : reason.Substring(0, 160);
        }

        private static bool TryGetActiveDrop(Item item, out Vector2 start, out Vector2 end, out float remaining)
        {
            start = default;
            end = default;
            remaining = 0f;
            if (!CanSynchronizeWorldItem(item))
                return false;

            string world = GameplayWorldRegistry.GetItemWorldKey(item);
            using var scope = GameplayWorldRegistry.TryGet(world, out _) ? GameplayWorldRegistry.UseWorld(world) : default;

            Mod_Droping dropping = item.GetComponentInChildren<Mod_Droping>(true);
            if (dropping?.drop == null)
                return false;

            start = WorldTopologyRuntime.NormalizePosition(item.transform.position);
            end = WorldTopologyRuntime.NormalizePosition(dropping.drop.endPos);
            remaining = Mathf.Max(0.05f, dropping.drop.time - dropping.drop.progressTime);
            return IsFinite(start) && IsFinite(end);
        }

        private static bool ValidateSpawnRequest(NetworkConnectionToClient connection, NetworkItemSpawnRequest request)
        {
            if (connection?.identity == null || request.ItemGuid == 0 || string.IsNullOrWhiteSpace(request.ItemId) ||
                request.ItemId.Length > 128 || !ItemNetworkStateSerialization.IsValidPayload(request.Payload) ||
                !ItemNetworkStateSerialization.TryReadIdentity(request.Payload, out int payloadGuid, out string payloadId) ||
                payloadGuid != request.ItemGuid || !string.Equals(payloadId, request.ItemId, StringComparison.Ordinal) ||
                !IsFinite(request.StartPosition) || !IsFinite(request.EndPosition))
            {
                return false;
            }

            return WorldTopologyRuntime.Distance(GetConnectionLogicalPosition(connection), request.StartPosition) <= 8f;
        }

        private static bool CapturePose(Item item, StateRecord state)
        {
            state.WorldKey = GameplayWorldRegistry.GetItemWorldKey(item);
            Vector3 position = GetLogicalItemPosition(item);
            Quaternion rotation = item.transform.rotation;
            Vector3 scale = SanitizeScale(item.transform.localScale);
            if (state.PoseRevision != 0 && state.Position.Equals(position) &&
                state.Rotation.Equals(rotation) && state.Scale.Equals(scale)) return false;
            state.Position = position;
            state.Rotation = rotation;
            state.Scale = scale;
            if (++state.PoseRevision == 0) state.PoseRevision++;
            return true;
        }

        private static Vector3 SanitizeScale(Vector3 scale)
        {
            if (!IsFinite(scale) || scale.sqrMagnitude < 0.0001f)
                return Vector3.one;

            return new Vector3(
                Mathf.Clamp(Mathf.Abs(scale.x), 0.05f, 8f),
                Mathf.Clamp(Mathf.Abs(scale.y), 0.05f, 8f),
                Mathf.Clamp(Mathf.Abs(scale.z), 0.05f, 8f));
        }

        private static bool IsFinite(Vector2 value)
            => !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
               !float.IsNaN(value.y) && !float.IsInfinity(value.y);

        private static bool IsFinite(Vector3 value)
            => IsFinite((Vector2)value) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        private static bool IsFinite(Quaternion value)
            => !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
               !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
               !float.IsNaN(value.z) && !float.IsInfinity(value.z) &&
               !float.IsNaN(value.w) && !float.IsInfinity(value.w);

        /// <summary>Host 上 identity Transform 可能是本机远端表现镜像，服务端判定只能读权威逻辑坐标。</summary>
        private static Vector3 GetConnectionLogicalPosition(NetworkConnectionToClient connection)
        {
            if (connection?.identity == null)
                return default;

            NetworkWorldPlayer networkPlayer = connection.identity.GetComponent<NetworkWorldPlayer>();
            Player player = networkPlayer?.CorePlayer;
            if (player != null && GameplayWorldRegistry.UsesLocalCoordinates(GameplayWorldRegistry.GetItemWorldKey(player)))
                return player.transform.position;
            return networkPlayer != null
                ? networkPlayer.AuthoritativeLogicalPosition
                : WorldTopologyRuntime.NormalizePosition(connection.identity.transform.position);
        }

        private static Vector3 GetLogicalItemPosition(Item item)
            => item == null ? default : GameplayWorldRegistry.UsesLocalCoordinates(GameplayWorldRegistry.GetItemWorldKey(item))
                ? item.transform.position : WorldTopologyRuntime.NormalizePosition(item.transform.position);

        private void OnRuntimeStateChanged(Item item)
        {
            if (!ShouldSynchronize(item))
                return;

            if (NetworkServer.active)
            {
                // 没有远端玩家时，权威状态保留在运行时；新连接补拉时再捕获完整快照。
                if (!HasRemoteReadyClient()) return;
                ProcessServerItem(item);
                return;
            }

            if (NetworkClient.active) ProcessClientItem(item);
        }

        private void UpdateRuntimeBridgeRegistration()
        {
            bool shouldRegister = serverHandlersRegistered || clientHandlersRegistered;
            if (shouldRegister && !runtimeBridgeRegistered)
            {
                ItemNetworkStateSerialization.RuntimeStateChanged += OnRuntimeStateChanged;
                ItemMgr.RuntimeItemInstantiated += OnRuntimeItemInstantiated;
                ItemMgr.RuntimeItemDespawning += OnRuntimeItemDespawning;
                ItemNetworkStateSerialization.ShouldDeferLocalDestruction =
                    () => NetworkClient.active && !NetworkServer.active;
                ItemNetworkStateSerialization.TryBeginNetworkPickup = TryBeginClientPickup;
                ItemNetworkStateSerialization.IsLocalInventoryTransactionPending =
                    () => NetworkClient.active && !NetworkServer.active && pendingClientPickups.Count > 0;
                ItemNetworkStateSerialization.TryBeginNetworkBuilding = TryBeginClientBuilding;
                ItemNetworkStateSerialization.TryBeginNetworkBuildingDismantle =
                    TryBeginNetworkBuildingDismantle;
                ItemNetworkStateSerialization.TryBeginNetworkMechanicalDismantle =
                    TryBeginClientMechanicalDismantle;
                ItemNetworkStateSerialization.IsRemoteItemStateCurrent = HasCurrentRemoteItemState;
                runtimeBridgeRegistered = true;
            }
            else if (!shouldRegister && runtimeBridgeRegistered)
            {
                ItemNetworkStateSerialization.RuntimeStateChanged -= OnRuntimeStateChanged;
                ItemMgr.RuntimeItemInstantiated -= OnRuntimeItemInstantiated;
                ItemMgr.RuntimeItemDespawning -= OnRuntimeItemDespawning;
                ItemNetworkStateSerialization.ShouldDeferLocalDestruction = null;
                ItemNetworkStateSerialization.TryBeginNetworkPickup = null;
                ItemNetworkStateSerialization.IsLocalInventoryTransactionPending = null;
                ItemNetworkStateSerialization.TryBeginNetworkBuilding = null;
                ItemNetworkStateSerialization.TryBeginNetworkBuildingDismantle = null;
                ItemNetworkStateSerialization.TryBeginNetworkMechanicalDismantle = null;
                ItemNetworkStateSerialization.IsRemoteItemStateCurrent = null;
                runtimeBridgeRegistered = false;
            }
        }

        private static NetworkItemStateMessage CreateMessage(int guid, StateRecord state)
        {
            return new NetworkItemStateMessage
            {
                ItemGuid = guid,
                ItemId = state.ItemId,
                WorldKey = state.WorldKey,
                Revision = state.Revision,
                PoseRevision = state.PoseRevision,
                PayloadHash = state.Hash,
                Payload = state.Payload,
                SpawnIfMissing = state.SpawnIfMissing,
                Position = state.Position,
                Rotation = state.Rotation,
                Scale = state.Scale
            };
        }

        private static Item[] SnapshotRuntimeItems()
        {
            Item[] items = new Item[ItemMgr.Instance.WorldRunTimeItems.Count];
            int index = 0;
            foreach (Item item in ItemMgr.Instance.WorldRunTimeItems.Values)
                items[index++] = item;
            return items;
        }

        private static bool HasRemoteReadyClient()
        {
            if (!NetworkServer.active) return false;
            // Mirror 的 Host 本地连接共用权威世界，不需要序列化给自己。
            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
                if (connection != null && connection is not LocalConnectionToClient &&
                    connection.isAuthenticated && connection.isReady)
                    return true;
            return false;
        }

        private static bool ShouldSynchronize(Item item)
        {
            string world = GameplayWorldRegistry.GetItemWorldKey(item);
            bool local = GameplayWorldRegistry.UsesLocalCoordinates(world);
            if (!local && item != null && SpaceSession.TryGetCombatFrame(item, out _, out _)) return false;
            return CanSynchronizeWorldItem(item) &&
                   (local || !(item.PersistenceOwner is SpaceSession)) &&
                   item.itemData.ModuleDataDic != null && item.itemData.ModuleDataDic.Count > 0;
        }

        private bool HasCurrentRemoteItemState(Item item)
        {
            if (!NetworkClient.active || NetworkServer.active || item?.itemData == null ||
                !clientStates.TryGetValue(item.itemData.Guid, out StateRecord state) || !state.Applied ||
                state.ItemId != item.itemData.IDName || requestedItemRealm != NetworkGameplayWorldScope.LocalRevision) return false;
            string world = GameplayWorldRegistry.GetItemWorldKey(item);
            Player player = NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>()?.CorePlayer;
            return !string.IsNullOrEmpty(world) && state.WorldKey == world && requestedItemWorld == world &&
                GameplayWorldRegistry.GetItemWorldKey(player) == world;
        }

        private static bool CanSynchronizeWorldItem(Item item)
            => item != null && item.itemData != null && item.IsInitialized &&
               item is not Player && item is not Map && !item.InHand;

        private static byte[] CaptureSafely(Item item)
            => CaptureSafely(item, out _, out _);

        private static byte[] CaptureSafely(Item item, out ulong version, out uint hash)
        {
            using var marker = CaptureMarker.Auto();
            version = 0;
            hash = 0;
            try
            {
                return ItemNetworkStateSerialization.CaptureCachedNetworkPayload(item, true, out version, out hash);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[联机物品] 捕获 {item?.name} 状态失败：{exception.Message}", item);
                return Array.Empty<byte>();
            }
        }
        #endregion
    }
}
