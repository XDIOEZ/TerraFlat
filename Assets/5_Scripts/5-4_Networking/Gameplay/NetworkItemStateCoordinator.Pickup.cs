using System;
using System.Collections.Generic;
using FlatWorld.Spaceflight;
using FlatWorld.Gameplay.Progress;
using Mirror;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    public sealed partial class NetworkItemStateCoordinator
    {
        #region 拾取请求与只读接收计划
        private const float PickupReceiptRetrySeconds = 2f;
        private const float PickupReceiptLifetimeSeconds = 30f;
        private const float PickupAmountTolerance = 0.0001f;

        private sealed class ServerPickupReceipt
        {
            public ServerPickupReservation Reservation;
            public NetworkItemPickupReceipt Message;
            public bool Applied;
            public float RetryAt;
            public float ExpiresAt;
        }

        private readonly Dictionary<uint, ServerPickupReceipt> serverPickupReceipts = new();
        private readonly Dictionary<uint, NetworkItemPickupApplied> appliedClientPickupReceipts = new();
        private readonly List<int> clientPickupTimeoutGuids = new();
        private readonly List<int> serverPickupTimeoutGuids = new();
        private readonly List<uint> serverPickupReceiptTokens = new();

        private bool TryBeginClientPickup(Mod_ItemPicker picker, Item worldItem)
        {
            if (!NetworkClient.active || !NetworkClient.ready || picker == null ||
                worldItem?.itemData == null || worldItem.itemData.Guid == 0) return false;
            int guid = worldItem.itemData.Guid;
            if (pendingClientPickups.ContainsKey(guid)) return true;
            // 已经放不下一份时沿用原生容量事件重试，不为满包玩家反复申请世界租约。
            if (picker.GetAcceptablePickupSourceAmount(worldItem.itemData) <= PickupAmountTolerance) return true;
            NetworkWorldPlayer actor = NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>();
            if (buildingRequests.HasPending(NetworkBuildingRequestKind.Place) || pendingMachineTransfer != 0 ||
                pendingMachineInventoryOperation != 0 || pendingContainerIntent != 0 ||
                SpaceSession.ClientGroundCombatPending || SpaceSession.ClientAttackHeld || actor?.GroundCombatPending == true)
                return true;
            if (actor?.CorePlayer == null || picker.item != actor.CorePlayer ||
                !actor.SubmitPickupInventoryBaseline()) return true;
            ClientPickupRequest pending = new()
            {
                Picker = picker,
                RequestToken = NextNonZeroToken(ref nextPickupRequestToken),
                ExpiresAt = Time.unscaledTime + PickupReservationSeconds
            };
            pendingClientPickups.Add(guid, pending);
            SendClientPickupRequest(guid, pending);
            return true;
        }

        private static void SendClientPickupRequest(int guid, ClientPickupRequest request)
            => NetworkClient.Send(new NetworkItemPickupRequest
                { RealmRevision = NetworkGameplayWorldScope.LocalRevision, ItemGuid = guid, RequestToken = request.RequestToken });

        private void OnServerPickupRequest(NetworkConnectionToClient connection, NetworkItemPickupRequest request)
        {
            if (!IsCurrentPickupConnection(connection) || request.RequestToken == 0) return;
            foreach (ServerPickupReceipt receipt in serverPickupReceipts.Values)
                if (MatchesPickupRequest(receipt.Reservation, connection, request.ItemGuid, request.RequestToken))
                { connection.Send(receipt.Message); return; }
            if (serverPickupReservations.TryGetValue(request.ItemGuid, out ServerPickupReservation previous) &&
                MatchesPickupRequest(previous, connection, request.ItemGuid, request.RequestToken))
            { SendServerPickupGrant(previous); return; }

            if (!CanGrantPickup(connection, request.ItemGuid, out Item item, out ItemData spaceData))
            { RejectServerPickup(connection, request); return; }
            ItemData source = spaceData ?? item.itemData;
            if (!IsFinitePickupAmount(source.Stack.Amount) || source.Stack.Amount <= 0f)
            { RejectServerPickup(connection, request); return; }
            byte[] payload;
            if (spaceData != null) ItemNetworkStateSerialization.TrySerializeItemData(spaceData, out payload);
            else payload = CaptureSafely(item);
            if (!ItemNetworkStateSerialization.IsValidPayload(payload))
            { RejectServerPickup(connection, request); return; }
            NetworkWorldPlayer actor = connection.identity.GetComponent<NetworkWorldPlayer>();
            uint token = NextNonZeroToken(ref nextPickupReservationToken);
            if (actor?.BeginServerPickupTransaction(token) != true)
            { RejectServerPickup(connection, request); return; }
            ServerPickupReservation reservation = new()
            {
                Connection = connection, Actor = actor, RequestToken = request.RequestToken, Token = token,
                Item = item, SpaceData = spaceData, SourceData = source, SourceAmount = source.Stack.Amount,
                Payload = payload, ExpiresAt = Time.unscaledTime + PickupReservationSeconds
            };
            serverPickupReservations.Add(request.ItemGuid, reservation);
            source.Stack.CanBePickedUp = false;
            if (spaceData != null) SpaceSession.Current.UpdateNetworkLooseItem(spaceData);
            else ItemNetworkStateSerialization.NotifyRuntimeStateChanged(item);
            SendServerPickupGrant(reservation);
        }

        private static void RejectServerPickup(NetworkConnectionToClient connection, NetworkItemPickupRequest request)
            => connection.Send(new NetworkItemPickupResponse
            {
                ItemGuid = request.ItemGuid, RequestToken = request.RequestToken,
                Granted = false, Payload = Array.Empty<byte>()
            });

        private static void SendServerPickupGrant(ServerPickupReservation reservation)
        {
            Mod_ItemPicker picker = reservation.Actor?.CorePlayer?.GetComponentInChildren<Mod_ItemPicker>(true);
            reservation.Connection.Send(new NetworkItemPickupResponse
            {
                ItemGuid = reservation.SourceData.Guid, RequestToken = reservation.RequestToken,
                ReservationToken = reservation.Token, Granted = true, Payload = reservation.Payload,
                AmountMultiplier = picker?.PickupAmountMultiplier ?? 1
            });
        }

        private void OnClientPickupResponse(NetworkItemPickupResponse response)
        {
            if (!pendingClientPickups.TryGetValue(response.ItemGuid, out ClientPickupRequest request) ||
                request.RequestToken != response.RequestToken) return;
            if (!response.Granted)
            {
                if (!request.CommitSent) pendingClientPickups.Remove(response.ItemGuid);
                return;
            }
            if (response.ReservationToken == 0) return;
            if (request.CommitSent)
            {
                if (request.ReservationToken == response.ReservationToken) SendClientPickupCommit(response.ItemGuid, request);
                return;
            }
            request.ReservationToken = response.ReservationToken;
            request.RequestedSourceAmount = 0f;
            if (request.Picker != null &&
                ItemNetworkStateSerialization.TryDeserializeItemData(response.Payload, out ItemData payload) &&
                payload.Guid == response.ItemGuid && payload.Stack != null)
            {
                request.ItemId = payload.IDName;
                request.RequestedSourceAmount = request.Picker.GetAcceptablePickupSourceAmount(payload, response.AmountMultiplier);
            }
            request.CommitSent = true;
            request.ExpiresAt = Time.unscaledTime + PickupReceiptRetrySeconds;
            SendClientPickupCommit(response.ItemGuid, request);
        }

        private static void SendClientPickupCommit(int guid, ClientPickupRequest request)
            => NetworkClient.Send(new NetworkItemPickupCommit
            { RealmRevision = NetworkGameplayWorldScope.LocalRevision,
                ItemGuid = guid, RequestToken = request.RequestToken,
                ReservationToken = request.ReservationToken, RequestedSourceAmount = request.RequestedSourceAmount
            });
        #endregion

        #region 服务端真实入包与世界余量提交
        private void OnServerPickupCommit(NetworkConnectionToClient connection, NetworkItemPickupCommit commit)
        {
            if (!IsCurrentPickupConnection(connection)) return;
            if (serverPickupReceipts.TryGetValue(commit.ReservationToken, out ServerPickupReceipt previous))
            {
                if (MatchesPickupCommit(previous.Reservation, connection, commit)) connection.Send(previous.Message);
                return;
            }
            if (!serverPickupReservations.TryGetValue(commit.ItemGuid, out ServerPickupReservation reservation) ||
                !MatchesPickupCommit(reservation, connection, commit)) return;
            serverPickupReservations.Remove(commit.ItemGuid);
            float consumed = 0f, added = 0f;
            bool accepted = false;
            try
            {
                if (reservation.ExpiresAt > Time.unscaledTime && IsValidPickupCommitAmount(commit, reservation) &&
                    IsReservedSourceCurrent(reservation) && IsReservedSourceInRange(reservation))
                {
                    Mod_ItemPicker picker = reservation.Actor.CorePlayer.GetComponentInChildren<Mod_ItemPicker>(true);
                    if (picker != null)
                    {
                        ItemData candidate = FastCloner.FastCloner.DeepClone(reservation.SourceData);
                        candidate.Stack.Amount = commit.RequestedSourceAmount;
                        candidate.Stack.CanBePickedUp = true;
                        accepted = picker.TryAcceptNetworkPickup(candidate, out consumed, out added, allowPartial: true);
                    }
                }
            }
            catch (Exception exception) { Debug.LogError($"[联机拾取] 结算失败：{exception}"); }

            // out 数量来自实际入包事务，不能按客户端希望的数量删除整堆来源。
            accepted = accepted && IsFinitePickupAmount(consumed) && IsFinitePickupAmount(added) &&
                consumed > 0f && consumed <= reservation.SourceAmount + PickupAmountTolerance && added > 0f;
            if (!accepted)
            {
                ReleasePickupReservation(reservation);
                FinishServerPickup(reservation, false, 0f, 0f);
                return;
            }

            float remaining = Mathf.Max(0f, reservation.SourceAmount - consumed);
            if (remaining > PickupAmountTolerance)
            {
                reservation.SourceData.Stack.Amount = remaining;
                reservation.SourceData.Stack.CanBePickedUp = true;
                if (reservation.SpaceData != null) SpaceSession.Current?.UpdateNetworkLooseItem(reservation.SourceData);
                else ItemNetworkStateSerialization.NotifyRuntimeStateChanged(reservation.Item);
            }
            // 先排入回执，让真实客户端在世界表现回收前创建吸入动画。
            FinishServerPickup(reservation, true, consumed, added);
            if (connection is LocalConnectionToClient)
            {
                try
                {
                    Item sourceView = reservation.Item ?? ItemMgr.Instance?.GetItemByGuid(reservation.SourceData.Guid);
                    reservation.Actor.CorePlayer.GetComponentInChildren<Mod_ItemPicker>(true)?.PlayPickupSuction(sourceView);
                }
                catch (Exception exception) { Debug.LogWarning($"[联机拾取] 入包已提交，吸入表现失败：{exception.Message}"); }
            }
            if (remaining > PickupAmountTolerance) return;
            if (reservation.SpaceData != null) SpaceSession.Current?.RemoveNetworkLooseItem(commit.ItemGuid);
            else if (reservation.Item != null)
            {
                BroadcastDespawn(reservation.Item);
                ItemMgr.Instance.DespawnItem(reservation.Item, saveData: false);
            }
        }

        private static bool IsValidPickupCommitAmount(NetworkItemPickupCommit commit, ServerPickupReservation reservation)
        {
            float requested = commit.RequestedSourceAmount;
            if (!IsFinitePickupAmount(requested) || requested <= 0f ||
                requested > reservation.SourceAmount + PickupAmountTolerance) return false;
            bool wholeSource = !reservation.SourceData.Stack.Stackable ||
                Mathf.Abs(reservation.SourceAmount - Mathf.Round(reservation.SourceAmount)) <= PickupAmountTolerance;
            return !wholeSource || Mathf.Abs(requested - Mathf.Round(requested)) <= PickupAmountTolerance;
        }

        private void FinishServerPickup(ServerPickupReservation reservation, bool accepted, float consumed, float added)
        {
            byte[] payload = Array.Empty<byte>();
            try { payload = reservation.Actor?.CapturePickupInventoryState() ?? Array.Empty<byte>(); }
            catch (Exception exception) { Debug.LogError($"[联机拾取] 库存回执捕获失败：{exception}"); }
            NetworkItemPickupReceipt message = new()
            {
                ItemGuid = reservation.SourceData.Guid, RequestToken = reservation.RequestToken,
                ReservationToken = reservation.Token, Accepted = accepted, ConsumedSourceAmount = consumed,
                AddedAmount = added, RemainingSourceAmount = Mathf.Max(0f, reservation.SourceAmount - consumed),
                InventoryPayload = payload
            };
            ServerPickupReceipt receipt = new()
            {
                Reservation = reservation, Message = message,
                RetryAt = Time.unscaledTime + PickupReceiptRetrySeconds,
                ExpiresAt = Time.unscaledTime + PickupReceiptLifetimeSeconds
            };
            serverPickupReceipts[reservation.Token] = receipt;
            if (IsCurrentPickupConnection(reservation.Connection)) reservation.Connection.Send(message);
        }

        private void OnClientPickupReceipt(NetworkItemPickupReceipt receipt)
        {
            if (appliedClientPickupReceipts.TryGetValue(receipt.ReservationToken, out NetworkItemPickupApplied previous))
            {
                if (previous.ItemGuid == receipt.ItemGuid && previous.RequestToken == receipt.RequestToken)
                    NetworkClient.Send(previous);
                return;
            }
            if (!pendingClientPickups.TryGetValue(receipt.ItemGuid, out ClientPickupRequest request) ||
                request.RequestToken != receipt.RequestToken ||
                request.ReservationToken != 0 && request.ReservationToken != receipt.ReservationToken ||
                !IsFinitePickupAmount(receipt.ConsumedSourceAmount) || !IsFinitePickupAmount(receipt.AddedAmount) ||
                !IsFinitePickupAmount(receipt.RemainingSourceAmount) || receipt.ConsumedSourceAmount < 0f ||
                receipt.AddedAmount < 0f || receipt.RemainingSourceAmount < 0f ||
                receipt.Accepted && (receipt.ConsumedSourceAmount <= 0f || receipt.AddedAmount <= 0f ||
                    receipt.ConsumedSourceAmount > request.RequestedSourceAmount + PickupAmountTolerance)) return;
            NetworkWorldPlayer actor = NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>();
            if (actor?.ApplyPickupInventoryState(receipt.InventoryPayload) != true) return;
            NetworkItemPickupApplied applied = new()
            { RealmRevision = NetworkGameplayWorldScope.LocalRevision,
                ItemGuid = receipt.ItemGuid, RequestToken = receipt.RequestToken, ReservationToken = receipt.ReservationToken
            };
            if (appliedClientPickupReceipts.Count >= 256) appliedClientPickupReceipts.Clear();
            appliedClientPickupReceipts[receipt.ReservationToken] = applied;
            NetworkClient.Send(applied);
            pendingClientPickups.Remove(receipt.ItemGuid);
            if (receipt.Accepted && !NetworkServer.active)
            {
                try
                {
                    Item source = ItemMgr.Instance?.GetItemByGuid(receipt.ItemGuid);
                    request.Picker?.PlayPickupSuction(source, hideSourceRenderers: receipt.RemainingSourceAmount <= PickupAmountTolerance);
                    if (!string.IsNullOrWhiteSpace(request.ItemId))
                        GameplayProgressEvents.PublishPickupSucceeded(actor.CorePlayer, request.ItemId, receipt.AddedAmount,
                            DimensionManager.ExistingInstance?.ActiveAddress.DimensionId);
                }
                catch (Exception exception) { Debug.LogWarning($"[联机拾取] 收货已确认，反馈失败：{exception.Message}"); }
            }
        }

        private void OnServerPickupApplied(NetworkConnectionToClient connection, NetworkItemPickupApplied applied)
        {
            if (!serverPickupReceipts.TryGetValue(applied.ReservationToken, out ServerPickupReceipt receipt) ||
                !MatchesPickupRequest(receipt.Reservation, connection, applied.ItemGuid, applied.RequestToken)) return;
            receipt.Applied = true;
            receipt.Reservation.Actor?.EndServerPickupTransaction(receipt.Reservation.Token);
        }
        #endregion

        #region 租约身份、距离、超时和断线收尾
        private static bool MatchesPickupRequest(ServerPickupReservation reservation, NetworkConnectionToClient connection,
            int guid, uint requestToken)
            => ReferenceEquals(reservation.Connection, connection) && reservation.RequestToken == requestToken &&
               reservation.SourceData?.Guid == guid;

        private static bool MatchesPickupCommit(ServerPickupReservation reservation, NetworkConnectionToClient connection,
            NetworkItemPickupCommit commit)
            => reservation.Token == commit.ReservationToken &&
               MatchesPickupRequest(reservation, connection, commit.ItemGuid, commit.RequestToken);

        private static bool IsCurrentPickupConnection(NetworkConnectionToClient connection)
            => connection?.identity != null && connection.isAuthenticated && connection.isReady &&
               NetworkServer.connections.TryGetValue(connection.connectionId, out NetworkConnectionToClient current) &&
               ReferenceEquals(current, connection);

        private static bool IsFinitePickupAmount(float amount) => !float.IsNaN(amount) && !float.IsInfinity(amount);

        private static bool IsReservedSourceCurrent(ServerPickupReservation reservation)
        {
            ItemData current;
            if (reservation.SpaceData != null)
            {
                if (SpaceSession.Current?.TryGetNetworkLooseItem(reservation.SourceData.Guid, out current, out _) != true) return false;
            }
            else current = reservation.Item?.itemData;
            return current != null && (reservation.SpaceData != null || ReferenceEquals(current, reservation.SourceData)) &&
                current.Guid == reservation.SourceData.Guid && current.IDName == reservation.SourceData.IDName && current.Stack != null &&
                IsFinitePickupAmount(current.Stack.Amount) &&
                Mathf.Abs(current.Stack.Amount - reservation.SourceAmount) <= PickupAmountTolerance;
        }

        private static bool IsReservedSourceInRange(ServerPickupReservation reservation)
        {
            if (reservation.SpaceData != null)
                return reservation.Actor?.TryGetSpaceObserver(out SpaceNetworkObserver observer) == true && observer.InSpace &&
                    !GameplayWorldRegistry.UsesLocalCoordinates(GameplayWorldRegistry.GetItemWorldKey(reservation.Actor.CorePlayer)) &&
                    SpaceSession.Current?.TryGetNetworkLooseItem(reservation.SourceData.Guid, out _, out SpaceSectorPosition position) == true &&
                    SpaceSectorCoordinates.Difference(position, observer.Position).Magnitude <= MaxPickupDistance;
            return reservation.Item != null && NetworkGameplayWorldScope.OwnsItem(reservation.Connection, reservation.Item) &&
                WorldItemWaterSystem.CanPickUpAt(reservation.Item, GetConnectionLogicalPosition(reservation.Connection)) &&
                WorldTopologyRuntime.Distance(GetConnectionLogicalPosition(reservation.Connection),
                    GetLogicalItemPosition(reservation.Item)) <= MaxPickupDistance;
        }

        private bool CanGrantPickup(NetworkConnectionToClient connection, int itemGuid, out Item item, out ItemData spaceData)
        {
            item = null; spaceData = null;
            if (!IsCurrentPickupConnection(connection) || itemGuid == 0 || serverPickupReservations.ContainsKey(itemGuid) ||
                ItemMgr.Instance == null) return false;
            NetworkWorldPlayer actor = connection.identity.GetComponent<NetworkWorldPlayer>();
            if (actor?.GroundCombatPending == true) return false;
            bool localWorld = GameplayWorldRegistry.UsesLocalCoordinates(GameplayWorldRegistry.GetItemWorldKey(actor?.CorePlayer));
            if (!localWorld && actor?.TryGetSpaceObserver(out SpaceNetworkObserver observer) == true && observer.InSpace)
            {
                if (SpaceSession.Current?.TryGetNetworkLooseItem(itemGuid, out ItemData data, out SpaceSectorPosition position) != true ||
                    data.Stack?.CanBePickedUp != true ||
                    SpaceSectorCoordinates.Difference(position, observer.Position).Magnitude > MaxPickupDistance) return false;
                spaceData = data;
                return true;
            }
            item = ItemMgr.Instance.GetItemByGuid(itemGuid);
            if (!CanSynchronizeWorldItem(item) || !NetworkGameplayWorldScope.OwnsItem(connection, item) || item.itemData.Stack?.CanBePickedUp != true ||
                !WorldItemWaterSystem.CanPickUpAt(item, GetConnectionLogicalPosition(connection)) ||
                WorldTopologyRuntime.Distance(GetConnectionLogicalPosition(connection), GetLogicalItemPosition(item)) > MaxPickupDistance)
            { item = null; return false; }
            return true;
        }

        private void ProcessPickupTimeouts()
        {
            float now = Time.unscaledTime;
            if (pendingClientPickups.Count > 0)
            {
                clientPickupTimeoutGuids.Clear();
                clientPickupTimeoutGuids.AddRange(pendingClientPickups.Keys);
                foreach (int guid in clientPickupTimeoutGuids)
                {
                    if (!pendingClientPickups.TryGetValue(guid, out ClientPickupRequest request) ||
                        request.ExpiresAt > now || !NetworkClient.ready) continue;
                    request.ExpiresAt = now + PickupReceiptRetrySeconds;
                    if (request.CommitSent) SendClientPickupCommit(guid, request);
                    else SendClientPickupRequest(guid, request);
                }
            }
            if (!NetworkServer.active || serverPickupReservations.Count == 0 && serverPickupReceipts.Count == 0) return;
            if (serverPickupReservations.Count > 0)
            {
                serverPickupTimeoutGuids.Clear();
                serverPickupTimeoutGuids.AddRange(serverPickupReservations.Keys);
                foreach (int guid in serverPickupTimeoutGuids)
                {
                    if (!serverPickupReservations.TryGetValue(guid, out ServerPickupReservation reservation)) continue;
                    bool connected = IsCurrentPickupConnection(reservation.Connection);
                    if (connected && reservation.ExpiresAt > now) continue;
                    serverPickupReservations.Remove(guid);
                    ReleasePickupReservation(reservation);
                    if (connected) FinishServerPickup(reservation, false, 0f, 0f);
                    else reservation.Actor?.EndServerPickupTransaction(reservation.Token);
                }
            }
            if (serverPickupReceipts.Count > 0)
            {
                serverPickupReceiptTokens.Clear();
                serverPickupReceiptTokens.AddRange(serverPickupReceipts.Keys);
                foreach (uint token in serverPickupReceiptTokens)
                {
                    if (!serverPickupReceipts.TryGetValue(token, out ServerPickupReceipt receipt)) continue;
                    bool connected = IsCurrentPickupConnection(receipt.Reservation.Connection);
                    if (!connected || receipt.ExpiresAt <= now)
                    {
                        serverPickupReceipts.Remove(token);
                        receipt.Reservation.Actor?.EndServerPickupTransaction(token);
                        // 确认长期缺失时保留服务器库存并断线，避免恢复写入过期的客户端数量。
                        if (connected && !receipt.Applied) receipt.Reservation.Connection.Disconnect();
                    }
                    else if (!receipt.Applied && receipt.RetryAt <= now)
                    {
                        receipt.RetryAt = now + PickupReceiptRetrySeconds;
                        receipt.Reservation.Connection.Send(receipt.Message);
                    }
                }
            }
        }

        private static void ReleasePickupReservation(ServerPickupReservation reservation)
        {
            if (reservation?.SourceData?.Stack == null) return;
            if (reservation.SpaceData == null && !ReferenceEquals(reservation.Item?.itemData, reservation.SourceData)) return;
            if (reservation.SpaceData != null)
            {
                if (SpaceSession.Current?.TryGetNetworkLooseItem(reservation.SourceData.Guid, out ItemData current, out _) != true ||
                    current.IDName != reservation.SourceData.IDName || current.Stack == null) return;
                current.Stack.CanBePickedUp = true;
                SpaceSession.Current.UpdateNetworkLooseItem(current);
                return;
            }
            reservation.SourceData.Stack.CanBePickedUp = true;
            ItemNetworkStateSerialization.NotifyRuntimeStateChanged(reservation.Item);
        }

        private void ReleaseAllPickupReservations()
        {
            foreach (ServerPickupReservation reservation in serverPickupReservations.Values)
            {
                ReleasePickupReservation(reservation);
                reservation.Actor?.EndServerPickupTransaction(reservation.Token);
            }
            foreach (ServerPickupReceipt receipt in serverPickupReceipts.Values)
                receipt.Reservation.Actor?.EndServerPickupTransaction(receipt.Reservation.Token);
            serverPickupReservations.Clear();
            serverPickupReceipts.Clear();
        }
        #endregion
    }
}
