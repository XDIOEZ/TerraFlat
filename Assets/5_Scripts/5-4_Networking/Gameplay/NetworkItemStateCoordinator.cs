// AI-Context: 世界 Item 与建筑的服务端权威协调器；客户端只能请求，GUID、放置校验、生成与广播均由服务端提交。
using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    /// <summary>
    /// 世界 Item 的服务器权威快照通道。状态以 Item Guid 定位，内容由 Item 系统统一序列化，
    /// 因此 DamageReceiver 之外的 Module 也会自动进入同步范围。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkItemStateCoordinator : MonoBehaviour
    {
        private const float ScanInterval = 0.5f;
        private const int MaxScanItemsPerFrame = 4;
        private const int MaxSpawnCandidatesPerFrame = 32;
        private const float MaxDropDistance = 12f;
        private const float PickupReservationSeconds = 6f;
        private const float MaxPickupDistance = 5f;
        private const float MaxBuildingRequestDistance = 12f;
        private const float BuildingRequestTimeout = 8f;

        private sealed class StateRecord
        {
            public string ItemId;
            public uint Revision;
            public uint Hash;
            public byte[] Payload;
            public bool SubmissionPending;
            public bool SpawnIfMissing;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
        }

        private sealed class ClientPickupRequest
        {
            public ItemPicker Picker;
            public float ExpiresAt;
        }

        private sealed class ServerPickupReservation
        {
            public int ConnectionId;
            public uint Token;
            public Item Item;
            public float ExpiresAt;
        }

        private sealed class ClientBuildingRequest
        {
            public Mod_Building Building;
            public int SourceItemGuid;
            public float ExpiresAt;
        }

        private sealed class ClientBuildingDismantleRequest
        {
            public Mod_Building Building;
            public int BuildingGuid;
            public float ExpiresAt;
        }

        private readonly Dictionary<int, StateRecord> serverStates = new();
        private readonly Dictionary<int, StateRecord> clientStates = new();
        private readonly Dictionary<int, NetworkItemStateMessage> pendingClientStates = new();
        private readonly Dictionary<int, float> pendingSpawnCandidates = new();
        private readonly HashSet<int> requestedClientSpawns = new();
        private readonly HashSet<int> networkInstantiationsInProgress = new();
        private readonly Dictionary<int, ClientPickupRequest> pendingClientPickups = new();
        private readonly Dictionary<int, ServerPickupReservation> serverPickupReservations = new();
        private readonly Dictionary<uint, ClientBuildingRequest> pendingClientBuildings = new();
        private readonly Dictionary<uint, ClientBuildingDismantleRequest> pendingClientDismantles = new();
        private float nextScanTime;
        private Item[] scheduledScanItems;
        private int scheduledScanIndex;
        private bool scheduledScanAsServer;
        private bool serverHandlersRegistered;
        private bool clientHandlersRegistered;
        private bool runtimeBridgeRegistered;
        private bool requestedInitialState;
        private uint nextPickupReservationToken;
        private uint nextBuildingRequestToken;
        private uint nextBuildingDismantleRequestToken;

        public void StartServerSide()
        {
            if (serverHandlersRegistered)
                return;

            NetworkServer.RegisterHandler<NetworkItemStateRequest>(OnServerStateRequest, false);
            NetworkServer.RegisterHandler<NetworkItemStateSubmit>(OnServerStateSubmit, false);
            NetworkServer.RegisterHandler<NetworkItemSpawnRequest>(OnServerSpawnRequest, false);
            NetworkServer.RegisterHandler<NetworkItemPickupRequest>(OnServerPickupRequest, false);
            NetworkServer.RegisterHandler<NetworkItemPickupCommit>(OnServerPickupCommit, false);
            NetworkServer.RegisterHandler<NetworkBuildingPlaceRequest>(OnServerBuildingPlaceRequest, false);
            NetworkServer.RegisterHandler<NetworkBuildingDismantleRequest>(OnServerBuildingDismantleRequest, false);
            serverHandlersRegistered = true;
            UpdateRuntimeBridgeRegistration();
        }

        public void StartClientSide()
        {
            if (clientHandlersRegistered)
                return;

            NetworkClient.RegisterHandler<NetworkItemStateMessage>(OnClientStateMessage, false);
            NetworkClient.RegisterHandler<NetworkItemSpawnMessage>(OnClientSpawnMessage, false);
            NetworkClient.RegisterHandler<NetworkItemDespawnMessage>(OnClientDespawnMessage, false);
            NetworkClient.RegisterHandler<NetworkItemPickupResponse>(OnClientPickupResponse, false);
            NetworkClient.RegisterHandler<NetworkBuildingPlaceResponse>(OnClientBuildingPlaceResponse, false);
            NetworkClient.RegisterHandler<NetworkBuildingDismantleResponse>(OnClientBuildingDismantleResponse, false);
            clientHandlersRegistered = true;
            requestedInitialState = false;
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
            NetworkServer.UnregisterHandler<NetworkBuildingPlaceRequest>();
            NetworkServer.UnregisterHandler<NetworkBuildingDismantleRequest>();
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
            NetworkClient.UnregisterHandler<NetworkItemSpawnMessage>();
            NetworkClient.UnregisterHandler<NetworkItemDespawnMessage>();
            NetworkClient.UnregisterHandler<NetworkItemPickupResponse>();
            NetworkClient.UnregisterHandler<NetworkBuildingPlaceResponse>();
            NetworkClient.UnregisterHandler<NetworkBuildingDismantleResponse>();
            clientHandlersRegistered = false;
            clientStates.Clear();
            pendingClientStates.Clear();
            pendingSpawnCandidates.Clear();
            requestedClientSpawns.Clear();
            networkInstantiationsInProgress.Clear();
            pendingClientPickups.Clear();
            RejectAllPendingBuildings("网络连接已关闭");
            RejectAllPendingDismantles("网络连接已关闭");
            requestedInitialState = false;
            ClearScheduledScan();
            UpdateRuntimeBridgeRegistration();
        }

        private void Update()
        {
            ProcessPickupTimeouts();
            ProcessBuildingTimeouts();
            ProcessBuildingDismantleTimeouts();
            ProcessSpawnCandidates();

            if (!NetworkServer.active && NetworkClient.active && !requestedInitialState &&
                GameManager.Instance != null && GameManager.Instance.IsInGameWorld)
            {
                requestedInitialState = true;
                NetworkClient.Send(new NetworkItemStateRequest());
            }

            ApplyPendingClientStates();

            if (scheduledScanItems == null)
            {
                if (Time.unscaledTime < nextScanTime)
                    return;

                BeginScheduledScan();
            }

            ProcessScheduledScan();
        }

        private void ScanServerItems()
        {
            if (ItemMgr.Instance == null)
                return;

            Item[] items = SnapshotRuntimeItems();
            for (int i = 0; i < items.Length; i++)
                ProcessServerItem(items[i]);
        }

        private void ScanClientItems()
        {
            if (ItemMgr.Instance == null)
                return;

            Item[] items = SnapshotRuntimeItems();
            for (int i = 0; i < items.Length; i++)
                ProcessClientItem(items[i]);
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
            if (!ShouldSynchronize(item))
                return;

            byte[] payload = CaptureSafely(item);
            if (!ItemNetworkStateSerialization.IsValidPayload(payload))
                return;

            uint hash = ItemNetworkStateSerialization.CalculateHash(payload);
            int guid = item.itemData.Guid;
            if (serverStates.TryGetValue(guid, out StateRecord state) && state.Hash == hash)
                return;

            state ??= new StateRecord { ItemId = item.itemData.IDName };
            state.ItemId = item.itemData.IDName;
            state.Revision++;
            state.Hash = hash;
            state.Payload = payload;
            CapturePose(item, state);
            serverStates[guid] = state;
            BroadcastServerState(guid, state);
        }

        private void ProcessClientItem(Item item)
        {
            if (!ShouldSynchronize(item))
                return;

            int guid = item.itemData.Guid;
            if (!clientStates.TryGetValue(guid, out StateRecord state) || state.SubmissionPending)
                return;

            byte[] payload = CaptureSafely(item);
            if (!ItemNetworkStateSerialization.IsValidPayload(payload))
                return;

            uint hash = ItemNetworkStateSerialization.CalculateHash(payload);
            if (hash == state.Hash)
                return;

            state.SubmissionPending = true;
            NetworkClient.Send(new NetworkItemStateSubmit
            {
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
                connection.Send(CreateMessage(pair.Key, pair.Value));
        }

        private void OnServerStateSubmit(NetworkConnectionToClient connection, NetworkItemStateSubmit submit)
        {
            if (connection?.identity == null || !ItemNetworkStateSerialization.IsValidPayload(submit.Payload))
                return;

            Item item = ItemMgr.Instance?.GetItemByGuid(submit.ItemGuid);
            if (!ShouldSynchronize(item))
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
            state.ItemId = item.itemData.IDName;
            CapturePose(item, state);
            BroadcastServerState(submit.ItemGuid, state);

            DamageReceiver damageReceiver = item.GetComponentInChildren<DamageReceiver>(true);
            if (damageReceiver != null && damageReceiver.Hp <= 0f)
            {
                BroadcastDespawn(item);
                damageReceiver.ResolveNetworkAuthoritativeDeath();
            }
        }

        private void OnClientStateMessage(NetworkItemStateMessage message)
        {
            if (!ItemNetworkStateSerialization.IsValidPayload(message.Payload))
                return;

            if (clientStates.TryGetValue(message.ItemGuid, out StateRecord known) && message.Revision < known.Revision)
                return;

            StateRecord state = known ?? new StateRecord();
            state.ItemId = message.ItemId;
            state.Revision = message.Revision;
            state.Hash = message.PayloadHash;
            state.Payload = message.Payload;
            state.SubmissionPending = false;
            state.SpawnIfMissing = message.SpawnIfMissing;
            state.Position = message.Position;
            state.Rotation = message.Rotation;
            state.Scale = message.Scale;
            clientStates[message.ItemGuid] = state;

            if (!TryApplyClientState(message))
                pendingClientStates[message.ItemGuid] = message;
            else
                pendingClientStates.Remove(message.ItemGuid);
        }

        private void OnClientDespawnMessage(NetworkItemDespawnMessage message)
        {
            clientStates.Remove(message.ItemGuid);
            pendingClientStates.Remove(message.ItemGuid);
            pendingClientPickups.Remove(message.ItemGuid);

            if (NetworkServer.active)
                return;

            Item item = ItemMgr.Instance?.GetItemByGuid(message.ItemGuid);
            if (item == null || item.itemData == null ||
                !string.Equals(item.itemData.IDName, message.ItemId, StringComparison.Ordinal))
            {
                return;
            }

            ItemMgr.Instance.DespawnItem(item, saveData: false);
        }

        private void ApplyPendingClientStates()
        {
            if (pendingClientStates.Count == 0 || ItemMgr.Instance == null)
                return;

            int[] keys = new int[pendingClientStates.Count];
            pendingClientStates.Keys.CopyTo(keys, 0);
            for (int i = 0; i < keys.Length; i++)
            {
                int guid = keys[i];
                if (pendingClientStates.TryGetValue(guid, out NetworkItemStateMessage message) &&
                    TryApplyClientState(message))
                {
                    pendingClientStates.Remove(guid);
                }
            }
        }

        private bool TryApplyClientState(NetworkItemStateMessage message)
        {
            // Host 已经在服务器路径应用过状态，避免同一实例重复 Load。
            if (NetworkServer.active)
                return true;

            Item item = ItemMgr.Instance?.GetItemByGuid(message.ItemGuid);
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

            return ItemNetworkStateSerialization.Apply(item, message.Payload, true, true);
        }

        private void BroadcastServerState(int guid, StateRecord state)
        {
            if (NetworkServer.active)
                NetworkServer.SendToAll(CreateMessage(guid, state));
        }

        private void BroadcastDespawn(Item item)
        {
            if (!NetworkServer.active || item?.itemData == null)
                return;

            NetworkServer.SendToAll(new NetworkItemDespawnMessage
            {
                ItemGuid = item.itemData.Guid,
                ItemId = item.itemData.IDName
            });

            serverStates.Remove(item.itemData.Guid);
        }

        private bool TryBeginClientPickup(ItemPicker picker, Item worldItem)
        {
            if (!NetworkClient.active || !NetworkClient.ready || picker == null ||
                worldItem?.itemData == null || worldItem.itemData.Guid == 0)
            {
                return false;
            }

            int guid = worldItem.itemData.Guid;
            if (!pendingClientPickups.ContainsKey(guid))
            {
                pendingClientPickups[guid] = new ClientPickupRequest
                {
                    Picker = picker,
                    ExpiresAt = Time.unscaledTime + PickupReservationSeconds
                };
                NetworkClient.Send(new NetworkItemPickupRequest { ItemGuid = guid });
            }

            return true;
        }

        private void OnServerPickupRequest(
            NetworkConnectionToClient connection,
            NetworkItemPickupRequest request)
        {
            if (!CanGrantPickup(connection, request.ItemGuid, out Item item))
            {
                connection?.Send(new NetworkItemPickupResponse
                {
                    ItemGuid = request.ItemGuid,
                    Granted = false,
                    Payload = Array.Empty<byte>()
                });
                return;
            }

            byte[] payload = CaptureSafely(item);
            if (!ItemNetworkStateSerialization.IsValidPayload(payload))
            {
                connection.Send(new NetworkItemPickupResponse
                {
                    ItemGuid = request.ItemGuid,
                    Granted = false,
                    Payload = Array.Empty<byte>()
                });
                return;
            }

            uint token = unchecked(++nextPickupReservationToken);
            if (token == 0)
                token = unchecked(++nextPickupReservationToken);

            item.itemData.Stack.CanBePickedUp = false;
            serverPickupReservations[request.ItemGuid] = new ServerPickupReservation
            {
                ConnectionId = connection.connectionId,
                Token = token,
                Item = item,
                ExpiresAt = Time.unscaledTime + PickupReservationSeconds
            };
            ItemNetworkStateSerialization.NotifyRuntimeStateChanged(item);

            connection.Send(new NetworkItemPickupResponse
            {
                ItemGuid = request.ItemGuid,
                ReservationToken = token,
                Granted = true,
                Payload = payload
            });
        }

        private void OnClientPickupResponse(NetworkItemPickupResponse response)
        {
            pendingClientPickups.TryGetValue(response.ItemGuid, out ClientPickupRequest request);
            pendingClientPickups.Remove(response.ItemGuid);

            if (!response.Granted)
                return;

            bool accepted = request?.Picker != null &&
                            ItemNetworkStateSerialization.TryDeserializeItemData(response.Payload, out ItemData itemData) &&
                            request.Picker.TryAcceptNetworkPickup(itemData);

            if (accepted)
            {
                Item pickupSource = ItemMgr.Instance?.GetItemByGuid(response.ItemGuid);
                request.Picker.PlayPickupSuction(pickupSource, hideSourceRenderers: true);
            }

            NetworkClient.Send(new NetworkItemPickupCommit
            {
                ItemGuid = response.ItemGuid,
                ReservationToken = response.ReservationToken,
                AcceptedIntoInventory = accepted
            });
        }

        private void OnServerPickupCommit(
            NetworkConnectionToClient connection,
            NetworkItemPickupCommit commit)
        {
            if (!serverPickupReservations.TryGetValue(commit.ItemGuid, out ServerPickupReservation reservation) ||
                reservation.ConnectionId != connection.connectionId ||
                reservation.Token != commit.ReservationToken)
            {
                return;
            }

            serverPickupReservations.Remove(commit.ItemGuid);
            Item item = reservation.Item;
            if (item == null || item.itemData == null)
                return;

            if (!commit.AcceptedIntoInventory)
            {
                ReleasePickupReservation(reservation);
                return;
            }

            BroadcastDespawn(item);
            ItemMgr.Instance.DespawnItem(item, saveData: false);
        }

        private bool CanGrantPickup(
            NetworkConnectionToClient connection,
            int itemGuid,
            out Item item)
        {
            item = null;
            if (connection?.identity == null || itemGuid == 0 ||
                serverPickupReservations.ContainsKey(itemGuid) || ItemMgr.Instance == null)
            {
                return false;
            }

            item = ItemMgr.Instance.GetItemByGuid(itemGuid);
            if (!CanSynchronizeWorldItem(item) || item.itemData.Stack == null ||
                !item.itemData.Stack.CanBePickedUp)
            {
                item = null;
                return false;
            }

            if (WorldTopologyRuntime.Distance(connection.identity.transform.position, item.transform.position) > MaxPickupDistance)
            {
                item = null;
                return false;
            }

            return true;
        }

        private void ProcessPickupTimeouts()
        {
            float now = Time.unscaledTime;
            if (pendingClientPickups.Count > 0)
            {
                int[] clientGuids = new int[pendingClientPickups.Count];
                pendingClientPickups.Keys.CopyTo(clientGuids, 0);
                for (int i = 0; i < clientGuids.Length; i++)
                {
                    int guid = clientGuids[i];
                    if (pendingClientPickups.TryGetValue(guid, out ClientPickupRequest request) &&
                        request.ExpiresAt <= now)
                    {
                        pendingClientPickups.Remove(guid);
                    }
                }
            }

            if (!NetworkServer.active || serverPickupReservations.Count == 0)
                return;

            int[] serverGuids = new int[serverPickupReservations.Count];
            serverPickupReservations.Keys.CopyTo(serverGuids, 0);
            for (int i = 0; i < serverGuids.Length; i++)
            {
                int guid = serverGuids[i];
                if (!serverPickupReservations.TryGetValue(guid, out ServerPickupReservation reservation) ||
                    reservation.ExpiresAt > now)
                {
                    continue;
                }

                serverPickupReservations.Remove(guid);
                ReleasePickupReservation(reservation);
            }
        }

        private static void ReleasePickupReservation(ServerPickupReservation reservation)
        {
            Item item = reservation?.Item;
            if (item?.itemData?.Stack == null)
                return;

            item.itemData.Stack.CanBePickedUp = true;
            ItemNetworkStateSerialization.NotifyRuntimeStateChanged(item);
        }

        private void ReleaseAllPickupReservations()
        {
            foreach (ServerPickupReservation reservation in serverPickupReservations.Values)
                ReleasePickupReservation(reservation);

            serverPickupReservations.Clear();
        }

        private void OnRuntimeItemInstantiated(Item item)
        {
            if (item?.itemData == null || networkInstantiationsInProgress.Contains(item.itemData.Guid))
                return;

            pendingSpawnCandidates[item.itemData.Guid] = Time.unscaledTime;
        }

        private void OnRuntimeItemDespawning(Item item)
        {
            if (item?.itemData == null)
                return;

            int guid = item.itemData.Guid;
            pendingSpawnCandidates.Remove(guid);
            requestedClientSpawns.Remove(guid);
            serverPickupReservations.Remove(guid);

            if (NetworkServer.active && serverStates.TryGetValue(guid, out StateRecord state) && state.SpawnIfMissing)
                BroadcastDespawn(item);
        }

        private void ProcessSpawnCandidates()
        {
            if (pendingSpawnCandidates.Count == 0 || ItemMgr.Instance == null)
                return;

            int[] guids = new int[pendingSpawnCandidates.Count];
            pendingSpawnCandidates.Keys.CopyTo(guids, 0);
            int count = Mathf.Min(MaxSpawnCandidatesPerFrame, guids.Length);
            for (int i = 0; i < count; i++)
            {
                int guid = guids[i];
                pendingSpawnCandidates.Remove(guid);

                Item item = ItemMgr.Instance.GetItemByGuid(guid);
                if (!TryGetActiveDrop(item, out Vector2 start, out Vector2 end, out float remaining))
                    continue;

                if (NetworkServer.active)
                    PublishServerSpawn(item, start, end, remaining, true);
                else if (NetworkClient.active)
                    RequestServerSpawn(item, start, end, remaining);
            }
        }

        private void RequestServerSpawn(Item item, Vector2 start, Vector2 end, float duration)
        {
            if (!CanSynchronizeWorldItem(item) || requestedClientSpawns.Contains(item.itemData.Guid))
                return;

            byte[] payload = CaptureSafely(item);
            if (!ItemNetworkStateSerialization.IsValidPayload(payload))
                return;

            requestedClientSpawns.Add(item.itemData.Guid);
            NetworkClient.Send(new NetworkItemSpawnRequest
            {
                ItemGuid = item.itemData.Guid,
                ItemId = item.itemData.IDName,
                Payload = payload,
                StartPosition = start,
                EndPosition = end,
                Duration = Mathf.Clamp(duration, 0.05f, 3f),
                Scale = SanitizeScale(item.transform.localScale)
            });
        }

        private void OnServerSpawnRequest(NetworkConnectionToClient connection, NetworkItemSpawnRequest request)
        {
            if (!ValidateSpawnRequest(connection, request) || ItemMgr.Instance.GetItemByGuid(request.ItemGuid) != null)
                return;

            Vector2 start = WorldTopologyRuntime.NormalizePosition(request.StartPosition);
            Vector2 end = start + Vector2.ClampMagnitude(
                WorldTopologyRuntime.ShortestDelta(start, request.EndPosition),
                MaxDropDistance);
            Vector3 scale = SanitizeScale(request.Scale);
            Item item = null;

            try
            {
                networkInstantiationsInProgress.Add(request.ItemGuid);
                item = ItemMgr.Instance.InstantiateNetworkItem(
                    request.ItemId,
                    request.ItemGuid,
                    start,
                    Quaternion.identity,
                    scale);
                item.Load();
                if (!ItemNetworkStateSerialization.Apply(item, request.Payload, true, true))
                    throw new InvalidOperationException("掉落物快照与物品模板不匹配");

                item.SetInHand(false);
                item.transform.localScale = scale;
                Mod_BaseDroper.StaticDropItem_Pos(item, start, end, Mathf.Clamp(request.Duration, 0.05f, 3f));
                PublishServerSpawn(item, start, end, Mathf.Clamp(request.Duration, 0.05f, 3f), true);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[联机掉落] 服务端拒绝生成 {request.ItemId}/{request.ItemGuid}：{exception.Message}");
                if (item != null)
                    ItemMgr.Instance.DespawnItem(item, saveData: false);
            }
            finally
            {
                networkInstantiationsInProgress.Remove(request.ItemGuid);
            }
        }

        private bool PublishServerSpawn(Item item, Vector2 start, Vector2 end, float duration, bool animateDrop)
        {
            if (!NetworkServer.active || !CanSynchronizeWorldItem(item))
                return false;

            byte[] payload = CaptureSafely(item);
            if (!ItemNetworkStateSerialization.IsValidPayload(payload))
                return false;

            int guid = item.itemData.Guid;
            serverStates.TryGetValue(guid, out StateRecord state);
            state ??= new StateRecord();
            state.ItemId = item.itemData.IDName;
            state.Revision++;
            state.Hash = ItemNetworkStateSerialization.CalculateHash(payload);
            state.Payload = payload;
            state.SpawnIfMissing = true;
            CapturePose(item, state);
            serverStates[guid] = state;

            NetworkServer.SendToAll(new NetworkItemSpawnMessage
            {
                ItemGuid = guid,
                ItemId = state.ItemId,
                Revision = state.Revision,
                PayloadHash = state.Hash,
                Payload = state.Payload,
                StartPosition = start,
                EndPosition = end,
                Duration = Mathf.Clamp(duration, 0.05f, 3f),
                Scale = state.Scale,
                AnimateDrop = animateDrop
            });
            return true;
        }

        private void OnClientSpawnMessage(NetworkItemSpawnMessage message)
        {
            if (NetworkServer.active || !ItemNetworkStateSerialization.IsValidPayload(message.Payload))
                return;

            bool predictedLocally = requestedClientSpawns.Remove(message.ItemGuid);
            Item item = ItemMgr.Instance?.GetItemByGuid(message.ItemGuid);
            if (item == null)
            {
                item = InstantiateClientItem(
                    message.ItemGuid,
                    message.ItemId,
                    message.Payload,
                    message.StartPosition,
                    Quaternion.identity,
                    message.Scale,
                    message.AnimateDrop,
                    message.StartPosition,
                    message.EndPosition,
                    message.Duration);
            }
            else if (!string.Equals(item.itemData?.IDName, message.ItemId, StringComparison.Ordinal))
            {
                return;
            }

            if (item == null)
                return;

            ItemNetworkStateSerialization.Apply(item, message.Payload, true, true);
            if (!predictedLocally)
                item.transform.localScale = SanitizeScale(message.Scale);

            StateRecord state = new StateRecord
            {
                ItemId = message.ItemId,
                Revision = message.Revision,
                Hash = message.PayloadHash,
                Payload = message.Payload,
                SpawnIfMissing = true,
                Position = item.transform.position,
                Rotation = item.transform.rotation,
                Scale = item.transform.localScale
            };
            clientStates[message.ItemGuid] = state;
            pendingClientStates.Remove(message.ItemGuid);
        }

        private Item InstantiateClientItem(
            int guid,
            string itemId,
            byte[] payload,
            Vector3 position,
            Quaternion rotation,
            Vector3 scale,
            bool animateDrop,
            Vector2 dropStart,
            Vector2 dropEnd,
            float dropDuration)
        {
            if (ItemMgr.Instance == null || string.IsNullOrWhiteSpace(itemId) || guid == 0 || !IsFinite(position))
                return null;

            Item item = null;
            try
            {
                networkInstantiationsInProgress.Add(guid);
                item = ItemMgr.Instance.InstantiateNetworkItem(
                    itemId,
                    guid,
                    position,
                    IsFinite(rotation) ? rotation : Quaternion.identity,
                    SanitizeScale(scale));
                item.Load();
                if (!ItemNetworkStateSerialization.Apply(item, payload, true, true))
                    throw new InvalidOperationException("权威快照与客户端物品模板不匹配");

                item.SetInHand(false);
                if (animateDrop)
                {
                    Mod_BaseDroper.StaticDropItem_Pos(
                        item,
                        dropStart,
                        dropEnd,
                        Mathf.Clamp(dropDuration, 0.05f, 3f));
                }

                return item;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[联机掉落] 客户端补建 {itemId}/{guid} 失败：{exception.Message}");
                if (item != null)
                    ItemMgr.Instance.DespawnItem(item, saveData: false);
                return null;
            }
            finally
            {
                networkInstantiationsInProgress.Remove(guid);
            }
        }

        private bool TryBeginClientBuilding(Mod_Building building, Vector3 position)
        {
            if (!NetworkClient.active)
                return false;

            Item source = building?.item;
            if (source?.itemData?.Stack == null || source.itemData.Stack.Amount < 1f ||
                NetworkClient.connection == null || !IsFinite(position))
            {
                building?.RejectNetworkPlacement("网络连接或建造材料无效");
                return true;
            }

            foreach (ClientBuildingRequest existing in pendingClientBuildings.Values)
            {
                if (existing.Building == building)
                    return true;
            }

            byte[] payload = CaptureSafely(source);
            if (!ItemNetworkStateSerialization.IsValidPayload(payload))
            {
                building.RejectNetworkPlacement("无法生成建造材料快照");
                return true;
            }

            uint token = NextNonZeroToken(ref nextBuildingRequestToken);
            pendingClientBuildings[token] = new ClientBuildingRequest
            {
                Building = building,
                SourceItemGuid = source.itemData.Guid,
                ExpiresAt = Time.unscaledTime + BuildingRequestTimeout
            };

            NetworkClient.Send(new NetworkBuildingPlaceRequest
            {
                RequestToken = token,
                SourceItemGuid = source.itemData.Guid,
                ItemId = source.itemData.IDName,
                SourcePayload = payload,
                Position = NormalizeBuildingPosition(position)
            });
            return true;
        }

        private void OnServerBuildingPlaceRequest(
            NetworkConnectionToClient connection,
            NetworkBuildingPlaceRequest request)
        {
            NetworkBuildingPlaceResponse response = new NetworkBuildingPlaceResponse
            {
                RequestToken = request.RequestToken,
                SourceItemGuid = request.SourceItemGuid,
                Accepted = false,
                RemainingAmount = 0f,
                Reason = "建造请求无效"
            };

            Item buildingItem = null;
            int buildingGuid = 0;
            bool materialConsumed = false;
            ItemData authoritativeSourceData = null;
            ItemSlot authoritativeSourceSlot = null;
            Player authoritativeSourcePlayer = null;
            float authoritativeSourceAmount = 0f;
            try
            {
                if (!TryValidateBuildingRequest(
                        connection,
                        request,
                        out ItemData sourceData,
                        out ItemSlot authoritativeSlot,
                        out Player authoritativePlayer,
                        out string reason))
                    throw new InvalidOperationException(reason);

                Vector3 position = NormalizeBuildingPosition(request.Position);
                authoritativeSourceData = sourceData;
                authoritativeSourceSlot = authoritativeSlot;
                authoritativeSourcePlayer = authoritativePlayer;
                authoritativeSourceAmount = sourceData.Stack.Amount;

                if (!Mod_Building.TryCreatePlacementCandidateData(
                        sourceData,
                        position,
                        out ItemData placedData,
                        out bool restoredSnapshot,
                        out reason))
                {
                    throw new InvalidOperationException(reason);
                }

                buildingGuid = placedData.Guid;

                networkInstantiationsInProgress.Add(buildingGuid);
                buildingItem = ItemMgr.Instance.InstantiateItem(
                    placedData,
                    position,
                    Quaternion.identity,
                    Vector3.one);
                buildingItem.Load();

                Mod_Building building = buildingItem.itemMods?.GetMod_ByID<Mod_Building>(ModText.Building);
                if (building == null)
                    throw new MissingComponentException($"{request.ItemId} 缺少建筑模块");

                if (!building.ValidateAuthoritativePlacement(connection.identity.transform.position, out reason))
                    throw new InvalidOperationException(reason);

                materialConsumed = true;
                response.RemainingAmount = ConsumeAuthoritativeBuildingMaterial(
                    authoritativeSlot,
                    authoritativePlayer);

                building.SetAsInstalled(initializeHealth: !restoredSnapshot);
                if (!PublishServerSpawn(buildingItem, position, position, 0.05f, false))
                    throw new InvalidOperationException("服务器无法发布建筑快照");

                response.Accepted = true;
                response.Reason = string.Empty;
            }
            catch (Exception exception)
            {
                response.Reason = LimitReason(exception.Message);
                if (materialConsumed)
                {
                    RestoreAuthoritativeBuildingMaterial(
                        authoritativeSourceSlot,
                        authoritativeSourceData,
                        authoritativeSourceAmount,
                        authoritativeSourcePlayer);
                }

                Debug.LogWarning($"[联机建造] 服务端拒绝 {request.ItemId}：{response.Reason}");
                if (buildingItem != null && ItemMgr.Instance != null)
                {
                    buildingItem.itemMods?.GetMod_ByID<Mod_Building>(ModText.Building)?.ReleasePlacementOccupancy();
                    ItemMgr.Instance.DespawnItem(buildingItem, false);
                }
            }
            finally
            {
                if (buildingGuid != 0)
                    networkInstantiationsInProgress.Remove(buildingGuid);
            }

            connection?.Send(response);
        }

        private void OnClientBuildingPlaceResponse(NetworkBuildingPlaceResponse response)
        {
            if (!pendingClientBuildings.TryGetValue(response.RequestToken, out ClientBuildingRequest request))
                return;

            pendingClientBuildings.Remove(response.RequestToken);
            if (request.SourceItemGuid != response.SourceItemGuid || request.Building == null)
                return;

            if (response.Accepted)
                request.Building.CompleteNetworkPlacement(response.RemainingAmount);
            else
                request.Building.RejectNetworkPlacement(response.Reason);
        }

        private void ProcessBuildingTimeouts()
        {
            if (pendingClientBuildings.Count == 0)
                return;

            float now = Time.unscaledTime;
            uint[] tokens = new uint[pendingClientBuildings.Count];
            pendingClientBuildings.Keys.CopyTo(tokens, 0);
            for (int i = 0; i < tokens.Length; i++)
            {
                uint token = tokens[i];
                if (!pendingClientBuildings.TryGetValue(token, out ClientBuildingRequest request) ||
                    request.ExpiresAt > now)
                {
                    continue;
                }

                pendingClientBuildings.Remove(token);
                request.Building?.RejectNetworkPlacement("服务器响应超时");
            }
        }

        private void RejectAllPendingBuildings(string reason)
        {
            if (pendingClientBuildings.Count == 0)
                return;

            ClientBuildingRequest[] requests = new ClientBuildingRequest[pendingClientBuildings.Count];
            pendingClientBuildings.Values.CopyTo(requests, 0);
            pendingClientBuildings.Clear();
            for (int i = 0; i < requests.Length; i++)
                requests[i].Building?.RejectNetworkPlacement(reason);
        }

        private bool TryBeginNetworkBuildingDismantle(Mod_Building building)
        {
            if (NetworkClient.active)
                return TryBeginClientBuildingDismantle(building);

            if (!NetworkServer.active)
                return false;

            Item buildingItem = building?.item;
            Item summoner = null;
            try
            {
                if (buildingItem?.itemData == null || !building.CanCommitDismantle)
                    throw new InvalidOperationException("目标不是可拆除的建筑");

                if (!building.TryCreateDismantledSummoner(out summoner, out string reason))
                    throw new InvalidOperationException(reason);

                Vector3 position = summoner.transform.position;
                if (!PublishServerSpawn(summoner, position, position, 0.05f, false))
                    throw new InvalidOperationException("服务端无法发布建筑召唤器");

                building.ReleasePlacementOccupancy();
                BroadcastDespawn(buildingItem);
                ItemMgr.Instance.DespawnItem(buildingItem, saveData: false);
                building.CompleteNetworkDismantle();
            }
            catch (Exception exception)
            {
                if (summoner != null && ItemMgr.Instance != null)
                    ItemMgr.Instance.DespawnItem(summoner, saveData: false);
                building?.RejectNetworkDismantle(LimitReason(exception.Message));
            }

            return true;
        }

        private bool TryBeginClientBuildingDismantle(Mod_Building building)
        {
            if (!NetworkClient.active)
                return false;

            Item worldBuilding = building?.item;
            if (worldBuilding?.itemData == null || NetworkClient.connection == null ||
                !building.CanCommitDismantle)
            {
                building?.RejectNetworkDismantle("网络连接或建筑状态无效");
                return true;
            }

            foreach (ClientBuildingDismantleRequest existing in pendingClientDismantles.Values)
            {
                if (existing.Building == building)
                    return true;
            }

            uint token = NextNonZeroToken(ref nextBuildingDismantleRequestToken);
            pendingClientDismantles[token] = new ClientBuildingDismantleRequest
            {
                Building = building,
                BuildingGuid = worldBuilding.itemData.Guid,
                ExpiresAt = Time.unscaledTime + BuildingRequestTimeout
            };

            NetworkClient.Send(new NetworkBuildingDismantleRequest
            {
                RequestToken = token,
                BuildingGuid = worldBuilding.itemData.Guid
            });
            return true;
        }

        private void OnServerBuildingDismantleRequest(
            NetworkConnectionToClient connection,
            NetworkBuildingDismantleRequest request)
        {
            NetworkBuildingDismantleResponse response = new NetworkBuildingDismantleResponse
            {
                RequestToken = request.RequestToken,
                BuildingGuid = request.BuildingGuid,
                Accepted = false,
                Reason = "拆除请求无效"
            };

            Item summoner = null;
            try
            {
                if (connection?.identity == null || request.RequestToken == 0 || request.BuildingGuid == 0)
                    throw new InvalidOperationException("身份或建筑标识无效");

                Item buildingItem = ItemMgr.Instance?.GetItemByGuid(request.BuildingGuid);
                Mod_Building building = buildingItem?.itemMods?.GetMod_ByID<Mod_Building>(ModText.Building);
                if (building == null || !building.CanCommitDismantle)
                    throw new InvalidOperationException("目标不是可拆除的建筑");

                if (WorldTopologyRuntime.Distance(connection.identity.transform.position, buildingItem.transform.position) >
                    MaxPickupDistance)
                {
                    throw new InvalidOperationException("目标超出拆除距离");
                }

                if (!building.TryCreateDismantledSummoner(out summoner, out string reason))
                    throw new InvalidOperationException(reason);

                Vector3 position = summoner.transform.position;
                if (!PublishServerSpawn(summoner, position, position, 0.05f, false))
                    throw new InvalidOperationException("服务端无法发布建筑召唤器");

                building.ReleasePlacementOccupancy();
                BroadcastDespawn(buildingItem);
                ItemMgr.Instance.DespawnItem(buildingItem, saveData: false);
                response.Accepted = true;
                response.Reason = string.Empty;
            }
            catch (Exception exception)
            {
                response.Reason = LimitReason(exception.Message);
                if (summoner != null && ItemMgr.Instance != null)
                    ItemMgr.Instance.DespawnItem(summoner, saveData: false);
            }

            connection?.Send(response);
        }

        private void OnClientBuildingDismantleResponse(NetworkBuildingDismantleResponse response)
        {
            if (!pendingClientDismantles.TryGetValue(
                    response.RequestToken,
                    out ClientBuildingDismantleRequest request))
            {
                return;
            }

            pendingClientDismantles.Remove(response.RequestToken);
            if (request.BuildingGuid != response.BuildingGuid || request.Building == null)
                return;

            if (response.Accepted)
                request.Building.CompleteNetworkDismantle();
            else
                request.Building.RejectNetworkDismantle(response.Reason);
        }

        private void ProcessBuildingDismantleTimeouts()
        {
            if (pendingClientDismantles.Count == 0)
                return;

            float now = Time.unscaledTime;
            uint[] tokens = new uint[pendingClientDismantles.Count];
            pendingClientDismantles.Keys.CopyTo(tokens, 0);
            for (int i = 0; i < tokens.Length; i++)
            {
                uint token = tokens[i];
                if (!pendingClientDismantles.TryGetValue(token, out ClientBuildingDismantleRequest request) ||
                    request.ExpiresAt > now)
                {
                    continue;
                }

                pendingClientDismantles.Remove(token);
                request.Building?.RejectNetworkDismantle("服务端响应超时");
            }
        }

        private void RejectAllPendingDismantles(string reason)
        {
            if (pendingClientDismantles.Count == 0)
                return;

            ClientBuildingDismantleRequest[] requests =
                new ClientBuildingDismantleRequest[pendingClientDismantles.Count];
            pendingClientDismantles.Values.CopyTo(requests, 0);
            pendingClientDismantles.Clear();
            for (int i = 0; i < requests.Length; i++)
                requests[i].Building?.RejectNetworkDismantle(reason);
        }

        private static bool TryValidateBuildingRequest(
            NetworkConnectionToClient connection,
            NetworkBuildingPlaceRequest request,
            out ItemData sourceData,
            out ItemSlot authoritativeSlot,
            out Player authoritativePlayer,
            out string reason)
        {
            sourceData = null;
            authoritativeSlot = null;
            authoritativePlayer = null;
            reason = null;
            if (connection?.identity == null || request.RequestToken == 0 || request.SourceItemGuid == 0 ||
                string.IsNullOrWhiteSpace(request.ItemId) || request.ItemId.Length > 128 ||
                !IsFinite(request.Position) ||
                WorldTopologyRuntime.Distance(connection.identity.transform.position, request.Position) > MaxBuildingRequestDistance)
            {
                reason = "身份、坐标或距离校验失败";
                return false;
            }

            if (!ItemNetworkStateSerialization.IsValidPayload(request.SourcePayload) ||
                !ItemNetworkStateSerialization.TryDeserializeItemData(request.SourcePayload, out sourceData) ||
                sourceData.Guid != request.SourceItemGuid ||
                !string.Equals(sourceData.IDName, request.ItemId, StringComparison.Ordinal) ||
                sourceData.Stack == null || sourceData.Stack.Amount < 1f)
            {
                reason = "材料快照无效";
                return false;
            }

            bool hasBuildingModule = false;
            if (sourceData.ModuleDataDic != null)
            {
                foreach (ModuleData moduleData in sourceData.ModuleDataDic.Values)
                {
                    if (moduleData != null && string.Equals(moduleData.ID, ModText.Building, StringComparison.Ordinal))
                    {
                        hasBuildingModule = true;
                        break;
                    }
                }
            }

            if (!hasBuildingModule)
            {
                reason = "材料不包含建筑模块";
                return false;
            }

            if (GameRes.Instance?.GetPrefab(request.ItemId) == null || ItemMgr.Instance == null)
            {
                reason = "服务器缺少建筑预制体";
                return false;
            }

            NetworkWorldPlayer networkPlayer = connection.identity.GetComponent<NetworkWorldPlayer>();
            authoritativePlayer = networkPlayer?.CorePlayer;
            Inventory_HotBar hotBar = authoritativePlayer?.itemMods?
                .GetMod_ByID<Inventory_HotBar>(ModText.Hotbar);
            if (hotBar?.Data?.itemSlots == null || hotBar.CurrentIndex < 0 ||
                hotBar.CurrentIndex >= hotBar.Data.itemSlots.Count)
            {
                reason = "服务器尚未收到快捷栏状态";
                return false;
            }

            authoritativeSlot = hotBar.Data.itemSlots[hotBar.CurrentIndex];
            ItemData authoritativeData = authoritativeSlot?.itemData;
            if (authoritativeData?.Stack == null || authoritativeData.Stack.Amount < 1f ||
                authoritativeData.Guid != request.SourceItemGuid ||
                !string.Equals(authoritativeData.IDName, request.ItemId, StringComparison.Ordinal))
            {
                reason = "服务端快捷栏材料不匹配";
                return false;
            }

            if (!Mod_Building.IsValidSummonerData(authoritativeData, out reason))
                return false;

            // 客户端快照只用于请求身份校验，实际建筑数据必须来自服务端快捷栏。
            sourceData = authoritativeData;
            return true;
        }

        private static float ConsumeAuthoritativeBuildingMaterial(ItemSlot slot, Player player)
        {
            if (slot?.itemData?.Stack == null || slot.itemData.Stack.Amount < 1f)
                throw new InvalidOperationException("服务端材料已失效");

            slot.itemData.Stack.Amount = Mathf.Max(0f, slot.itemData.Stack.Amount - 1f);
            float remaining = slot.itemData.Stack.Amount;
            if (remaining <= 0f)
                slot.ClearData();
            slot.RefreshUI();

            if (player != null)
                player.Save();
            return remaining;
        }

        private static void RestoreAuthoritativeBuildingMaterial(
            ItemSlot slot,
            ItemData sourceData,
            float sourceAmount,
            Player player)
        {
            if (slot == null || sourceData?.Stack == null)
                return;

            sourceData.Stack.Amount = sourceAmount;
            slot.itemData = sourceData;
            slot.RefreshUI();
            player?.Save();
        }

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

        private static Vector3 NormalizeBuildingPosition(Vector3 position)
            => new Vector3(Mathf.Floor(position.x) + 0.5f, Mathf.Floor(position.y) + 0.5f, 0f);

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

            Mod_Droping dropping = item.GetComponentInChildren<Mod_Droping>(true);
            if (dropping?.drop == null)
                return false;

            start = item.transform.position;
            end = dropping.drop.endPos;
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

            return WorldTopologyRuntime.Distance(connection.identity.transform.position, request.StartPosition) <= 8f;
        }

        private static void CapturePose(Item item, StateRecord state)
        {
            state.Position = item.transform.position;
            state.Rotation = item.transform.rotation;
            state.Scale = SanitizeScale(item.transform.localScale);
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

        private void OnRuntimeStateChanged(Item item)
        {
            if (!ShouldSynchronize(item))
                return;

            byte[] payload = CaptureSafely(item);
            if (!ItemNetworkStateSerialization.IsValidPayload(payload))
                return;

            int guid = item.itemData.Guid;
            uint hash = ItemNetworkStateSerialization.CalculateHash(payload);
            if (NetworkServer.active)
            {
                serverStates.TryGetValue(guid, out StateRecord state);
                if (state != null && state.Hash == hash)
                    return;

                state ??= new StateRecord();
                state.ItemId = item.itemData.IDName;
                state.Revision++;
                state.Hash = hash;
                state.Payload = payload;
                CapturePose(item, state);
                serverStates[guid] = state;
                BroadcastServerState(guid, state);

                DamageReceiver receiver = item.GetComponentInChildren<DamageReceiver>(true);
                if (receiver != null && receiver.Hp <= 0f)
                    BroadcastDespawn(item);
                return;
            }

            if (!NetworkClient.active || !clientStates.TryGetValue(guid, out StateRecord clientState) ||
                clientState.SubmissionPending || clientState.Hash == hash)
            {
                return;
            }

            clientState.SubmissionPending = true;
            NetworkClient.Send(new NetworkItemStateSubmit
            {
                ItemGuid = guid,
                BaseRevision = clientState.Revision,
                Payload = payload
            });
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
                ItemNetworkStateSerialization.TryBeginNetworkBuilding = TryBeginClientBuilding;
                ItemNetworkStateSerialization.TryBeginNetworkBuildingDismantle =
                    TryBeginNetworkBuildingDismantle;
                runtimeBridgeRegistered = true;
            }
            else if (!shouldRegister && runtimeBridgeRegistered)
            {
                ItemNetworkStateSerialization.RuntimeStateChanged -= OnRuntimeStateChanged;
                ItemMgr.RuntimeItemInstantiated -= OnRuntimeItemInstantiated;
                ItemMgr.RuntimeItemDespawning -= OnRuntimeItemDespawning;
                ItemNetworkStateSerialization.ShouldDeferLocalDestruction = null;
                ItemNetworkStateSerialization.TryBeginNetworkPickup = null;
                ItemNetworkStateSerialization.TryBeginNetworkBuilding = null;
                ItemNetworkStateSerialization.TryBeginNetworkBuildingDismantle = null;
                runtimeBridgeRegistered = false;
            }
        }

        private static NetworkItemStateMessage CreateMessage(int guid, StateRecord state)
        {
            return new NetworkItemStateMessage
            {
                ItemGuid = guid,
                ItemId = state.ItemId,
                Revision = state.Revision,
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
            ItemMgr.Instance.WorldRunTimeItems.Values.CopyTo(items, 0);
            return items;
        }

        private static bool ShouldSynchronize(Item item)
        {
            return CanSynchronizeWorldItem(item) &&
                   item.itemData.ModuleDataDic != null && item.itemData.ModuleDataDic.Count > 0;
        }

        private static bool CanSynchronizeWorldItem(Item item)
            => item != null && item.itemData != null && item.IsInitialized &&
               item is not Player && item is not Map && !item.InHand;

        private static byte[] CaptureSafely(Item item)
        {
            try
            {
                return ItemNetworkStateSerialization.Capture(item, false);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[联机物品] 捕获 {item?.name} 状态失败：{exception.Message}", item);
                return Array.Empty<byte>();
            }
        }
    }
}
