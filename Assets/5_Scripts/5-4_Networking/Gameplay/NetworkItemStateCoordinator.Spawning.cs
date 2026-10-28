using System;
using System.Collections.Generic;
using FlatWorld.Gameplay.Building;
using FlatWorld.Spaceflight;
using Mirror;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    public sealed partial class NetworkItemStateCoordinator
    {
        #region 物品生成与销毁复制

        private readonly Dictionary<int, float> pendingSpawnCandidates = new();
        private readonly List<int> spawnCandidateKeys = new(MaxSpawnCandidatesPerFrame);
        private readonly HashSet<int> requestedClientSpawns = new();
        private readonly HashSet<int> networkInstantiationsInProgress = new();

        private void BroadcastDespawn(Item item)
        {
            if (!NetworkServer.active || item?.itemData == null)
                return;

            SendItemDespawn(item.itemData.Guid, item.itemData.IDName, GameplayWorldRegistry.GetItemWorldKey(item));

            serverStates.Remove(item.itemData.Guid);
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
            if (serverPickupReservations.Remove(guid, out ServerPickupReservation reservation))
                FinishServerPickup(reservation, false, 0f, 0f);

            if (NetworkServer.active && serverStates.TryGetValue(guid, out StateRecord state))
            {
                Mod_DamageReceiver receiver = item.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
                if (state.SpawnIfMissing || GameplayWorldRegistry.UsesLocalCoordinates(state.WorldKey) ||
                    receiver != null && receiver.Hp <= 0f)
                    BroadcastDespawn(item);
            }
        }

        private void ProcessSpawnCandidates()
        {
            if (pendingSpawnCandidates.Count == 0 || ItemMgr.Instance == null)
                return;

            using var marker = SpawnCandidatesMarker.Auto();
            // 只复制本帧预算内的 GUID，避免大量新实体出现时按整个队列分配数组。
            spawnCandidateKeys.Clear();
            foreach (int guid in pendingSpawnCandidates.Keys)
            {
                spawnCandidateKeys.Add(guid);
                if (spawnCandidateKeys.Count >= MaxSpawnCandidatesPerFrame) break;
            }
            for (int i = 0; i < spawnCandidateKeys.Count; i++)
            {
                int guid = spawnCandidateKeys[i];
                pendingSpawnCandidates.Remove(guid);

                Item item = ItemMgr.Instance.GetItemByGuid(guid);
                if (!TryGetActiveDrop(item, out Vector2 start, out Vector2 end, out float remaining))
                    continue;

                if (NetworkServer.active)
                    PublishServerSpawn(item, start, end, remaining, true);
                else if (NetworkClient.active)
                    RequestServerSpawn(item, start, end, remaining);
            }
            spawnCandidateKeys.Clear();
        }

        private void RequestServerSpawn(Item item, Vector2 start, Vector2 end, float duration)
        {
            if (!CanSynchronizeWorldItem(item) || item.PersistenceOwner is SpaceSession || requestedClientSpawns.Contains(item.itemData.Guid))
                return;

            string world = GameplayWorldRegistry.GetItemWorldKey(item);
            using var scope = GameplayWorldRegistry.TryGet(world, out _) ? GameplayWorldRegistry.UseWorld(world) : null;

            byte[] payload = CaptureSafely(item);
            if (!ItemNetworkStateSerialization.IsValidPayload(payload))
                return;

            start = WorldTopologyRuntime.NormalizePosition(start);
            end = WorldTopologyRuntime.NormalizePosition(end);
            requestedClientSpawns.Add(item.itemData.Guid);
            NetworkClient.Send(new NetworkItemSpawnRequest
            { RealmRevision = NetworkGameplayWorldScope.LocalRevision,
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
            Vector2 end = WorldTopologyRuntime.NormalizePosition(start + Vector2.ClampMagnitude(
                WorldTopologyRuntime.ShortestDelta(start, request.EndPosition),
                MaxDropDistance));
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

        private bool PublishServerSpawn(Item item, Vector2 start, Vector2 end, float duration, bool animateDrop,
            byte[] preparedPayload = null)
        {
            string itemWorld = GameplayWorldRegistry.GetItemWorldKey(item);
            using IDisposable worldScope = GameplayWorldRegistry.TryGet(itemWorld, out _)
                ? GameplayWorldRegistry.UseWorld(itemWorld) : null;
            if (!NetworkServer.active || !CanSynchronizeWorldItem(item) || item.PersistenceOwner is SpaceSession)
                return false;

            start = WorldTopologyRuntime.NormalizePosition(start);
            end = WorldTopologyRuntime.NormalizePosition(end);
            byte[] payload = preparedPayload ?? CaptureSafely(item);
            if (!ItemNetworkStateSerialization.IsValidPayload(payload))
                return false;

            int guid = item.itemData.Guid;
            serverStates.TryGetValue(guid, out StateRecord state);
            state ??= new StateRecord();
            state.ItemId = item.itemData.IDName;
            state.WorldKey = itemWorld;
            state.Revision++;
            state.Hash = ItemNetworkStateSerialization.CalculateHash(payload);
            state.Payload = payload;
            state.SpawnIfMissing = true;
            CapturePose(item, state);
            serverStates[guid] = state;

            SendToGroundObservers(new NetworkItemSpawnMessage
            {
                ItemGuid = guid,
                ItemId = state.ItemId,
                WorldKey = state.WorldKey,
                Revision = state.Revision,
                PoseRevision = state.PoseRevision,
                PayloadHash = state.Hash,
                Payload = state.Payload,
                Position = state.Position,
                Rotation = state.Rotation,
                StartPosition = start,
                EndPosition = end,
                Duration = Mathf.Clamp(duration, 0.05f, 3f),
                Scale = state.Scale,
                AnimateDrop = animateDrop
            }, state.WorldKey);
            return true;
        }

        private void OnClientSpawnMessage(NetworkItemSpawnMessage message)
        {
            if (!AcceptLocalWorld(message.WorldKey)) return;
            if (NetworkServer.active || !ItemNetworkStateSerialization.IsValidPayload(message.Payload))
                return;
            if (!GameplayWorldRegistry.TryGet(message.WorldKey, out _))
            {
                QueueClientSpawnState(message);
                return;
            }
            using var worldScope = GameplayWorldRegistry.UseWorld(message.WorldKey);
            if (clientStates.TryGetValue(message.ItemGuid, out StateRecord known) &&
                (message.Revision < known.Revision ||
                 (message.Revision == known.Revision && message.PoseRevision < known.PoseRevision))) return;

            bool predictedLocally = requestedClientSpawns.Remove(message.ItemGuid);
            Item item = ItemMgr.Instance?.GetItemByGuid(message.ItemGuid);
            if (item != null && GameplayWorldRegistry.GetItemWorldKey(item) != message.WorldKey)
            {
                QueueClientSpawnState(message);
                return;
            }
            bool created = item == null;
            if (item == null)
            {
                item = InstantiateClientItem(
                    message.ItemGuid,
                    message.ItemId,
                    message.Payload,
                    message.AnimateDrop ? message.StartPosition : message.Position,
                    message.Rotation,
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

            if (!created && !ItemNetworkStateSerialization.Apply(item, message.Payload, true, true)) return;
            if (!message.AnimateDrop)
                ApplyClientPose(item, message.Position, message.Rotation, message.Scale);
            if (!predictedLocally)
                item.transform.localScale = SanitizeScale(message.Scale);

            StateRecord state = new StateRecord
            {
                ItemId = message.ItemId,
                WorldKey = message.WorldKey,
                Revision = message.Revision,
                PoseRevision = message.PoseRevision,
                Hash = message.PayloadHash,
                Payload = message.Payload,
                SpawnIfMissing = true,
                Applied = true,
                Position = GetLogicalItemPosition(item),
                Rotation = item.transform.rotation,
                Scale = item.transform.localScale
            };
            clientStates[message.ItemGuid] = state;
            pendingClientStates.Remove(message.ItemGuid);
        }

        private void QueueClientSpawnState(NetworkItemSpawnMessage message)
        {
            OnClientStateMessage(new NetworkItemStateMessage { ItemGuid = message.ItemGuid, ItemId = message.ItemId,
                WorldKey = message.WorldKey, Revision = message.Revision, PoseRevision = message.PoseRevision,
                PayloadHash = message.PayloadHash, Payload = message.Payload, SpawnIfMissing = true,
                Position = message.Position, Rotation = message.Rotation, Scale = message.Scale });
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

        #endregion
    }
}
