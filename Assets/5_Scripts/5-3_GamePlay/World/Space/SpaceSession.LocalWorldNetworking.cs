using System;
using System.Collections.Generic;
using System.IO;
using FlatWorld.Networking;
using MemoryPack;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 局部世界联机复制
        private sealed class LocalWorldNetworkCache
        {
            internal int Frame = -1;
            internal ulong Revision;
            internal byte[] Payload;
        }
        private sealed class IncomingLocalWorld
        {
            internal ulong Revision;
            internal NetworkWorldSaveSnapshot Snapshot;
            internal List<ChunkSaveRecord> Chunks;
        }
        private readonly Dictionary<string, LocalWorldNetworkCache> localWorldNetworkCache = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, ulong>> sentLocalWorldRevisions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, ulong>> pendingLocalWorldRevisions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<int>> remoteLocalWorldResidents = new(StringComparer.Ordinal);
        private readonly Dictionary<int, byte[]> remoteLocalResidentSnapshots = new();
        private readonly Dictionary<string, ulong> receivedLocalWorldRevisions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, byte[]> remoteLocalDropSnapshots = new(StringComparer.Ordinal);

        public void ResetNetworkLocalWorldSnapshotCache(string profile)
        {
            if (string.IsNullOrEmpty(profile)) return;
            sentLocalWorldRevisions.Remove(profile);
            pendingLocalWorldRevisions.Remove(profile);
        }

        public void ConfirmNetworkLocalWorldSnapshotSent(string profile)
        {
            if (!pendingLocalWorldRevisions.Remove(profile, out var pending)) return;
            if (!sentLocalWorldRevisions.TryGetValue(profile, out var sent))
                sentLocalWorldRevisions.Add(profile, sent = new Dictionary<string, ulong>(StringComparer.Ordinal));
            foreach (var pair in pending) sent[pair.Key] = pair.Value;
        }

        private void CaptureNetworkLocalWorldStates(string profile, SpaceSessionState snapshot)
        {
            if (!sentLocalWorldRevisions.TryGetValue(profile, out var sent))
                sentLocalWorldRevisions.Add(profile, sent = new Dictionary<string, ulong>(StringComparer.Ordinal));
            var pending = new Dictionary<string, ulong>(StringComparer.Ordinal);
            pendingLocalWorldRevisions[profile] = pending;
            var visible = new HashSet<string>(StringComparer.Ordinal);
            foreach (ShipState ship in snapshot.Ships)
            {
                if (ship.IsWreck && !ship.RetainsInteriorWorld) continue;
                ShipInteriorWorldRuntime world = EnsureShipInteriorWorld(ship);
                visible.Add(world.WorldKey);
                if (!localWorldNetworkCache.TryGetValue(world.WorldKey, out LocalWorldNetworkCache cache))
                    localWorldNetworkCache.Add(world.WorldKey, cache = new LocalWorldNetworkCache());
                if (cache.Frame != Time.frameCount)
                {
                    byte[] payload = SaveDataMgr.Instance.CaptureNetworkLocalWorldSnapshot(world.WorldKey);
                    cache.Frame = Time.frameCount;
                    if (cache.Payload == null || !cache.Payload.AsSpan().SequenceEqual(payload))
                    { cache.Payload = payload; if (++cache.Revision == 0) cache.Revision++; }
                }
                if (!sent.TryGetValue(world.WorldKey, out ulong revision) || revision != cache.Revision)
                {
                    snapshot.LocalWorldStates.Add(new SpaceLocalWorldNetworkState
                        { WorldKey = world.WorldKey, Revision = cache.Revision, Payload = cache.Payload });
                    pending[world.WorldKey] = cache.Revision;
                }
            }
            foreach (string key in new List<string>(sent.Keys))
                if (!visible.Contains(key)) sent.Remove(key);
        }

        private Dictionary<string, IncomingLocalWorld> DecodeNetworkLocalWorldStates(SpaceSessionState received,
            HashSet<string> ships)
        {
            var result = new Dictionary<string, IncomingLocalWorld>(StringComparer.Ordinal);
            foreach (SpaceLocalWorldNetworkState state in received.LocalWorldStates ?? new List<SpaceLocalWorldNetworkState>())
            {
                if (state == null || state.Payload == null || state.Revision == 0 || string.IsNullOrWhiteSpace(state.WorldKey) ||
                    !state.WorldKey.StartsWith("ship:", StringComparison.Ordinal) || !ships.Contains(state.WorldKey.Substring(5)))
                    throw new InvalidDataException("船内联机世界身份无效。");
                List<ChunkSaveRecord> chunks = SaveDataMgr.DecodeNetworkLocalWorldChunks(state.Payload, state.WorldKey,
                    out NetworkWorldSaveSnapshot snapshot);
                if (!result.TryAdd(state.WorldKey, new IncomingLocalWorld
                    { Revision = state.Revision, Snapshot = snapshot, Chunks = chunks }))
                    throw new InvalidDataException("船内联机世界重复。");
            }
            foreach (string shipId in ships)
            {
                string worldKey = MachineWorld.GetShipScopeKey(shipId);
                if (!result.ContainsKey(worldKey) && !receivedLocalWorldRevisions.ContainsKey(worldKey))
                    throw new InvalidDataException("首次接收船体时缺少船内世界资料。");
            }
            return result;
        }

        private void ApplyNetworkLocalWorldStates(Dictionary<string, IncomingLocalWorld> incoming)
        {
            var desired = new Dictionary<string, Dictionary<int, ItemData>>(StringComparer.Ordinal);
            var incomingIds = new HashSet<int>();
            var cropBatches = new List<(ChunkAgricultureRenderer Renderer, IReadOnlyList<AgricultureCellSaveData> Cells)>();
            foreach (var pair in incoming)
            {
                var residents = new Dictionary<int, ItemData>();
                foreach (ChunkSaveRecord chunk in pair.Value.Chunks)
                    foreach (ItemData data in chunk.ChangedItems)
                        if (data == null || data.Guid == 0 || data.transform == null ||
                            !residents.TryAdd(data.Guid, data) || !incomingIds.Add(data.Guid))
                            throw new InvalidDataException("船内联机居民身份无效或重复。");
                desired.Add(pair.Key, residents);
            }

            ItemMgr manager = ItemMgr.GetExistingInstance() ?? throw new InvalidOperationException("物品管理器尚未就绪。");
            foreach (var pair in desired)
                if (remoteLocalWorldResidents.TryGetValue(pair.Key, out var oldIds))
                    foreach (int id in oldIds)
                        if (!pair.Value.ContainsKey(id) && !incomingIds.Contains(id))
                        {
                            Item old = manager.GetItemByGuid(id);
                            if (old != null && GameplayWorldRegistry.GetItemWorldKey(old) == pair.Key &&
                                !ItemNetworkStateSerialization.HasCurrentRemoteAuthority(old))
                                using (GameplayWorldRegistry.UseWorld(pair.Key)) manager.DespawnItem(old, false);
                            remoteLocalResidentSnapshots.Remove(id);
                        }

            foreach (var pair in incoming)
            {
                string key = pair.Key;
                if (!TryGetShipInteriorWorld(key.Substring(5), out ShipInteriorWorldRuntime world))
                    throw new InvalidOperationException("联机船内世界尚未创建：" + key);
                using var scope = GameplayWorldRegistry.UseWorld(key);
                foreach (ChunkSaveRecord chunk in pair.Value.Chunks)
                {
                    if (chunk.ChunkPosition.x % world.ChunkSize != 0 || chunk.ChunkPosition.y % world.ChunkSize != 0)
                        throw new InvalidDataException("船内联机区块坐标未对齐。");
                    world.EnsureChunkAt(chunk.ChunkPosition);
                }
                var cropBefore = new Dictionary<FlatWorld.WorldModel.WorldAddress, byte[]>();
                foreach (var address in new List<FlatWorld.WorldModel.WorldAddress>(world.ChunkAddresses))
                    if (world.Chunks.TryGetRuntimeChunkView(address, out _))
                        cropBefore[address] = CaptureCropSnapshotSignature(SaveDataMgr.Instance.GetAgricultureCells(address));
                SaveDataMgr.Instance.ApplyNetworkLocalWorldChunks(key, pair.Value.Chunks);
                ShipState ship = GetShip(key.Substring(5)) ?? throw new InvalidDataException("联机局部世界缺少船体。");
                SynchronizeShipInteriorTerrain(ship, world, force: true);
                foreach (ChunkSaveRecord chunk in pair.Value.Chunks)
                {
                    var address = new FlatWorld.WorldModel.WorldAddress(key,
                        new FlatWorld.WorldModel.Int2(chunk.ChunkPosition.x, chunk.ChunkPosition.y));
                    if (world.Chunks.TryGetRuntimeChunkView(address, out _)) continue;
                    foreach (AgricultureCellSaveData cell in chunk.AgricultureCells)
                        if (cell.Crop != null && ChunkAgricultureRenderer.HasBoundCrop(cell.Crop.Guid))
                        {
                            world.Chunks.EnsureRuntimeChunkViewBoundForStateTransfer(address);
                            break;
                        }
                }
                foreach (var previous in cropBefore)
                {
                    var cells = SaveDataMgr.Instance.GetAgricultureCells(previous.Key);
                    if (!previous.Value.AsSpan().SequenceEqual(CaptureCropSnapshotSignature(cells)) &&
                        world.Chunks.TryGetRuntimeChunkView(previous.Key, out ChunkView view) && view != null)
                    {
                        ChunkAgricultureRenderer renderer = view.GetComponentInChildren<ChunkAgricultureRenderer>(true);
                        if (renderer != null) cropBatches.Add((renderer, cells));
                    }
                }
                byte[] dropSnapshot = MemoryPackSerializer.Serialize(pair.Value.Snapshot.Drops);
                if (!remoteLocalDropSnapshots.TryGetValue(key, out byte[] oldDrops) ||
                    !oldDrops.AsSpan().SequenceEqual(dropSnapshot))
                {
                    DroppedItemService.ApplyNetworkWorldItems(key, pair.Value.Snapshot.Drops);
                    remoteLocalDropSnapshots[key] = dropSnapshot;
                }
                foreach (var resident in desired[key]) ApplyNetworkLocalWorldResident(manager, key, resident.Value);
                remoteLocalWorldResidents[key] = new HashSet<int>(desired[key].Keys);
                receivedLocalWorldRevisions[key] = pair.Value.Revision;
            }
            ChunkAgricultureRenderer.ApplyAuthoritativeCropSnapshotBatch(cropBatches);
        }

        private static byte[] CaptureCropSnapshotSignature(IReadOnlyList<AgricultureCellSaveData> cells)
        {
            var crops = new List<AgricultureCellSaveData>();
            foreach (AgricultureCellSaveData cell in cells)
                if (cell.Crop != null) crops.Add(new AgricultureCellSaveData { LocalPosition = cell.LocalPosition, Crop = cell.Crop });
            return MemoryPackSerializer.Serialize(crops);
        }

        private void ApplyNetworkLocalWorldResident(ItemMgr manager, string worldKey, ItemData data)
        {
            if (!ItemNetworkStateSerialization.TrySerializeItemData(data, out byte[] payload))
                throw new InvalidDataException("船内居民快照无效：" + data.Guid);
            Item item = manager.GetItemByGuid(data.Guid);
            if (item != null && GameplayWorldRegistry.GetItemWorldKey(item) == worldKey &&
                ItemNetworkStateSerialization.HasCurrentRemoteAuthority(item))
            {
                Mod_Building installed = item.itemMods?.GetMod_ByID<Mod_Building>(ModText.Building);
                if (installed?.Data?.Role == BuildingRole.PlacedBuilding &&
                    (item.transform.position != data.transform.position || item.transform.rotation != data.transform.rotation))
                    TransferInstalledItemToWorld(item, worldKey, data.transform.position,
                        Mathf.RoundToInt(data.transform.rotation.eulerAngles.z / 90f), null);
                remoteLocalResidentSnapshots.Remove(data.Guid);
                return;
            }
            if (item == null)
            {
                item = manager.InstantiateItem(data, data.transform.position, data.transform.rotation, data.transform.scale);
                if (item == null) throw new InvalidOperationException("无法创建船内联机居民：" + data.Guid);
                item.Load();
            }
            else
            {
                string oldWorld = GameplayWorldRegistry.GetItemWorldKey(item);
                if (item.PersistenceOwner != null || item.itemData.IDName != data.IDName ||
                    oldWorld == null || !oldWorld.StartsWith("ship:", StringComparison.Ordinal))
                    throw new InvalidOperationException("船内居民身份被其他物品占用：" + data.Guid);
                bool moved = oldWorld != worldKey || item.transform.position != data.transform.position ||
                    item.transform.rotation != data.transform.rotation;
                Mod_Building building = item.itemMods?.GetMod_ByID<Mod_Building>(ModText.Building);
                if (moved && building?.Data?.Role == BuildingRole.PlacedBuilding)
                    TransferInstalledItemToWorld(item, worldKey, data.transform.position,
                        Mathf.RoundToInt(data.transform.rotation.eulerAngles.z / 90f), null);
                else if (oldWorld != worldKey) GameplayWorldRegistry.AssignItemToWorld(item, worldKey);
                if (!remoteLocalResidentSnapshots.TryGetValue(data.Guid, out byte[] previous) ||
                    !previous.AsSpan().SequenceEqual(payload))
                    if (!ItemNetworkStateSerialization.Apply(item, payload, reloadRuntimeModules: true, requireMatchingGuid: true))
                        throw new InvalidOperationException("船内联机居民状态更新失败：" + data.Guid);
            }
            item.transform.SetPositionAndRotation(data.transform.position, data.transform.rotation);
            item.transform.localScale = data.transform.scale;
            Rigidbody2D body = item.GetComponent<Rigidbody2D>();
            if (body != null)
            {
                body.position = data.transform.position;
                body.rotation = data.transform.rotation.eulerAngles.z;
                if (body.bodyType != RigidbodyType2D.Static && data.PhysicsState != null)
                { body.velocity = data.PhysicsState.Velocity; body.angularVelocity = data.PhysicsState.AngularVelocity; }
            }
            GameplayWorldRegistry.RegisterItemPhysics(item);
            manager.NotifyRuntimeItemMoved(item);
            remoteLocalResidentSnapshots[data.Guid] = payload;
        }

        private void ForgetNetworkLocalWorld(string worldKey)
        {
            if (remoteLocalWorldResidents.Remove(worldKey, out var residents))
                foreach (int id in residents) remoteLocalResidentSnapshots.Remove(id);
            receivedLocalWorldRevisions.Remove(worldKey);
            remoteLocalDropSnapshots.Remove(worldKey);
            localWorldNetworkCache.Remove(worldKey);
            foreach (var sent in sentLocalWorldRevisions.Values) sent.Remove(worldKey);
            foreach (var pending in pendingLocalWorldRevisions.Values) pending.Remove(worldKey);
        }
        #endregion
    }
}
