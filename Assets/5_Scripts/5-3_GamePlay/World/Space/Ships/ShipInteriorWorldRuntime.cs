using System;
using System.Collections.Generic;
using System.Threading;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

namespace FlatWorld.Spaceflight
{
    public sealed class ShipInteriorWorldRuntime
    {
        #region 船内区块和原生空间
        private readonly Dictionary<RuntimeWorldAddress, ChunkLease> leases = new();
        private readonly HashSet<RuntimeWorldAddress> requiredChunks = new();
        private readonly List<RuntimeWorldAddress> unusedChunks = new();
        private readonly List<Item> worldItems = new();
        private readonly List<Vector2Int> savedChunkOrigins = new();
        private readonly Dictionary<Item, RuntimeWorldAddress> itemChunks = new(ItemReferenceComparer.Instance);
        private readonly HashSet<ChunkTerrainData> retainedContentTerrains = new();
        private Action retainedContentChanged;
        private readonly EmptyLocalWorldGenerator generator;
        private readonly GameplayWorldRuntime world;
        private ShipState structureOwner;
        private ulong structureRevision = ulong.MaxValue;
        public string WorldKey => world.WorldKey;
        public Scene Scene => world.Scene;
        public ChunkMgr Chunks => world.Chunks;
        public WorldRuntime WorldRuntime => world.Chunks.WorldRuntime;
        public WorldPhysicsService2D Physics => world.Physics;
        public int ChunkSize { get; }
        public IEnumerable<RuntimeWorldAddress> ChunkAddresses => leases.Keys;

        internal ShipInteriorWorldRuntime(string shipId)
        {
            ChunkSize = PlanetData.DefaultChunkDimension;
            generator = new EmptyLocalWorldGenerator(GameRes.RequireWorldLiquidTypes());
            world = GameplayWorldRegistry.CreateLocalWorld(MachineWorld.GetShipScopeKey(shipId), generator, ChunkSize);
            // 船内沿当前世界时间起算，普通农业与生物继续使用统一时钟服务。
            DayTimeSystem time = DayTimeSystem.GetInstance();
            if (time != null)
            {
                time.TryGetActiveTimeData(out TimeData source);
                time.EnsureSceneTimeData(WorldKey, source);
            }
        }

        internal void SynchronizeChunks(ShipState ship, bool force = false)
        {
            if (!force && ReferenceEquals(structureOwner, ship) && structureRevision == ship.StructureRevision) return;
            using var scope = GameplayWorldRegistry.UseWorld(WorldKey);
            requiredChunks.Clear();
            foreach (ShipPieceState piece in ship.Pieces)
            {
                if (!piece.IsAlive) continue;
                foreach (ShipCell cell in ShipGeometry.Footprint(piece))
                {
                    RuntimeWorldAddress address = GetChunkAddress(cell.X, cell.Y);
                    if (requiredChunks.Add(address)) EnsureChunk(address);
                }
            }
            GameplayWorldRegistry.CopyWorldItems(WorldKey, worldItems);
            itemChunks.Clear();
            foreach (Item item in worldItems)
                if (item != null)
                {
                    RuntimeWorldAddress address = GetChunkAddress(Mathf.FloorToInt(item.transform.position.x), Mathf.FloorToInt(item.transform.position.y));
                    itemChunks[item] = address;
                    if (requiredChunks.Add(address)) EnsureChunk(address);
                }
            if (SaveDataMgr.Instance != null)
            {
                SaveDataMgr.Instance.CopyRuntimeLocalWorldSavedChunkOrigins(WorldKey, savedChunkOrigins);
                foreach (Vector2Int origin in savedChunkOrigins) requiredChunks.Add(GetChunkAddress(origin.x, origin.y));
            }
            unusedChunks.Clear();
            if (DroppedItemService.Count == 0)
                foreach (var pair in leases)
                    if (!requiredChunks.Contains(pair.Key) && WorldRuntime.TryGetChunkTerrain(pair.Key, out var terrain) && terrain.Revision == 0)
                        unusedChunks.Add(pair.Key);
            foreach (RuntimeWorldAddress address in unusedChunks)
            {
                Chunks.UnbindLocalWorldChunk(address);
                leases[address].Dispose();
                WorldRuntime.TryGetChunkTerrain(address, out ChunkTerrainData terrain);
                ForgetRetainedTerrain(terrain);
                if (WorldRuntime.EvictChunk(address)) leases.Remove(address);
                else
                {
                    leases[address] = WorldRuntime.AcquireChunkLease(address, ChunkLeaseKind.Simulation);
                    ObserveRetainedTerrain(terrain);
                }
            }
            structureOwner = ship;
            structureRevision = ship.StructureRevision;
        }

        private RuntimeWorldAddress GetChunkAddress(int x, int y)
        {
            int originX = (x / ChunkSize - (x % ChunkSize < 0 ? 1 : 0)) * ChunkSize;
            int originY = (y / ChunkSize - (y % ChunkSize < 0 ? 1 : 0)) * ChunkSize;
            return new RuntimeWorldAddress(WorldKey, new Int2(originX, originY));
        }

        private void EnsureChunk(RuntimeWorldAddress address)
        {
            if (leases.ContainsKey(address)) return;
            ChunkLease lease = WorldRuntime.AcquireChunkLease(address, ChunkLeaseKind.Simulation);
            try
            {
                if (!WorldRuntime.TryGetChunkTerrain(address, out _))
                {
                    ChunkGenerationRequest request = WorldRuntime.BeginChunkGeneration(address, 0, Chunks.ActiveGenerationProfile);
                    using ChunkGenerationResult result = generator.Generate(request, CancellationToken.None);
                    if (!WorldRuntime.TryCommit(result, out string reason))
                        throw new InvalidOperationException("船内区块提交失败：" + reason);
                }
                leases.Add(address, lease);
                if (WorldRuntime.TryGetChunk(address, out var chunk))
                {
                    SaveDataMgr.Instance?.RestoreRuntimeTerrainForChunk(address, chunk);
                    ObserveRetainedTerrain(chunk.Terrain);
                    Chunks.BindLocalWorldChunk(chunk);
                }
            }
            catch { leases.Remove(address); lease.Dispose(); throw; }
        }

        internal void RestoreSavedChunks()
        {
            SaveDataMgr save = SaveDataMgr.Instance;
            if (save == null) return;
            var origins = new List<Vector2Int>();
            save.CopyRuntimeLocalWorldSavedChunkOrigins(WorldKey, origins);
            using var scope = GameplayWorldRegistry.UseWorld(WorldKey);
            foreach (Vector2Int origin in origins) EnsureChunk(GetChunkAddress(origin.x, origin.y));
        }

        internal void UnbindAllChunkViews()
        {
            using var scope = GameplayWorldRegistry.UseWorld(WorldKey);
            foreach (RuntimeWorldAddress address in new List<RuntimeWorldAddress>(leases.Keys))
                Chunks.UnbindLocalWorldChunk(address);
        }

        internal void ObserveRetainedWorldContents(Action onChanged)
        {
            retainedContentChanged = onChanged;
            foreach (ChunkRuntime chunk in WorldRuntime.Chunks.Values) ObserveRetainedTerrain(chunk.Terrain);
        }

        private void ObserveRetainedTerrain(ChunkTerrainData terrain)
        {
            if (retainedContentChanged == null || terrain == null || !retainedContentTerrains.Add(terrain)) return;
            terrain.Changed += OnRetainedTerrainChanged;
            terrain.LiquidBatchChanged += OnRetainedLiquidChanged;
        }

        private void OnRetainedTerrainChanged(ChunkTerrainChanged change) => retainedContentChanged?.Invoke();
        private void OnRetainedLiquidChanged(ChunkLiquidBatchChanged change) => retainedContentChanged?.Invoke();

        private void ForgetRetainedTerrain(ChunkTerrainData terrain)
        {
            if (terrain == null || !retainedContentTerrains.Remove(terrain)) return;
            terrain.Changed -= OnRetainedTerrainChanged;
            terrain.LiquidBatchChanged -= OnRetainedLiquidChanged;
        }

        internal void StopObservingRetainedWorldContents()
        {
            foreach (ChunkTerrainData terrain in retainedContentTerrains)
            {
                terrain.Changed -= OnRetainedTerrainChanged;
                terrain.LiquidBatchChanged -= OnRetainedLiquidChanged;
            }
            retainedContentTerrains.Clear();
            retainedContentChanged = null;
        }

        internal void EnsureChunkAt(Vector2 position)
        {
            using var scope = GameplayWorldRegistry.UseWorld(WorldKey);
            EnsureChunk(GetChunkAddress(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.y)));
        }

        internal bool TrackMovedItemChunk(Item item)
        {
            using var scope = GameplayWorldRegistry.UseWorld(WorldKey);
            RuntimeWorldAddress next = GetChunkAddress(Mathf.FloorToInt(item.transform.position.x), Mathf.FloorToInt(item.transform.position.y));
            EnsureChunk(next);
            if (itemChunks.TryGetValue(item, out RuntimeWorldAddress previous) && previous.Equals(next)) return false;
            itemChunks[item] = next;
            return true;
        }

        internal void Release()
        {
            if (GameplayWorldRegistry.GetWorldItemCount(WorldKey) != 0)
                throw new InvalidOperationException("船内仍有物品，不能释放世界：" + WorldKey);
            Action onChanged = retainedContentChanged;
            StopObservingRetainedWorldContents();
            foreach (ChunkLease lease in leases.Values) lease.Dispose();
            try
            {
                GameplayWorldRegistry.ReleaseLocalWorld(WorldKey);
                leases.Clear();
                DayTimeSystem time = DayTimeSystem.GetInstance();
                time?.WorldTimeDict.Remove(WorldKey);
                time?.SceneLightingRateDict.Remove(WorldKey);
            }
            catch
            {
                if (Chunks.WorldRuntime != null)
                {
                    foreach (RuntimeWorldAddress address in new List<RuntimeWorldAddress>(leases.Keys))
                        leases[address] = WorldRuntime.AcquireChunkLease(address, ChunkLeaseKind.Simulation);
                    if (onChanged != null) ObserveRetainedWorldContents(onChanged);
                }
                throw;
            }
        }
        #endregion

        #region 局部空地形生成
        private sealed class EmptyLocalWorldGenerator : IChunkPureGenerator
        {
            private readonly LiquidTypeCatalog liquidTypes;
            public EmptyLocalWorldGenerator(LiquidTypeCatalog liquidTypes) => this.liquidTypes = liquidTypes;
            public object GetPendingGenerationGroupKey(ChunkGenerationRequest request) => null;
            public ChunkGenerationResult Generate(ChunkGenerationRequest request, CancellationToken cancellationToken,
                ChunkGenerationTiming timing = null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ChunkGenerationResult(request,
                    new ChunkTerrainBuffer(request.Profile.Width, request.Profile.Height, liquidTypes));
            }
        }
        #endregion
    }

    public sealed partial class SpaceSession
    {
        #region 船内世界身份与坐标
        private readonly Dictionary<string, ShipInteriorWorldRuntime> shipInteriorWorlds = new(StringComparer.Ordinal);
        private readonly HashSet<Item> pendingInteriorItemMoves = new();
        private readonly List<Item> interiorItemMoveBatch = new();
        private readonly Dictionary<Item, string> trackedInteriorItemWorlds = new(ItemReferenceComparer.Instance);
        private readonly HashSet<string> pendingInteriorChunkReconciliation = new(StringComparer.Ordinal);
        private readonly List<string> interiorChunkReconciliationBatch = new();
        private readonly HashSet<string> retainedShipWorlds = new(StringComparer.Ordinal);
        private bool retainedWorldsNeedRetirementCheck;
        private ulong retainedItemOwnershipRevision;

        public ShipInteriorWorldRuntime EnsureShipInteriorWorld(ShipState ship)
        {
            if (ship == null || string.IsNullOrWhiteSpace(ship.ShipId)) throw new ArgumentException("船体身份无效。", nameof(ship));
            bool created = false;
            if (!shipInteriorWorlds.TryGetValue(ship.ShipId, out var world))
            {
                shipInteriorWorlds.Add(ship.ShipId, world = new ShipInteriorWorldRuntime(ship.ShipId));
                created = true;
            }
            world.Chunks.BeginLocalWorldChunkRestorationBatch();
            try
            {
                if (created) world.RestoreSavedChunks();
                world.SynchronizeChunks(ship);
                SynchronizeShipInteriorTerrain(ship, world);
            }
            catch { world.Chunks.EndLocalWorldChunkRestorationBatch(false); throw; }
            world.Chunks.EndLocalWorldChunkRestorationBatch(true);
            return world;
        }

        public bool TryGetShipInteriorWorld(string shipId, out ShipInteriorWorldRuntime world)
            => shipInteriorWorlds.TryGetValue(shipId ?? string.Empty, out world);

        public Vector2 ProjectShipInteriorPosition(ShipState ship, Vector2 localPosition)
        {
            if (ship == null) throw new ArgumentNullException(nameof(ship));
            Vector2 display = DisplayPresentationPoint(ship, localPosition.x, localPosition.y);
            return IsSpaceView ? display : display + Vector2.up * (float)ShipSurfaceState.VisualRise(GetFlight(ship.ShipId));
        }

        public Vector2 UnprojectShipInteriorPosition(ShipState ship, Vector2 displayPosition)
        {
            if (ship == null) throw new ArgumentNullException(nameof(ship));
            double x, y;
            if (IsSpaceView)
                PresentationWorldToLocal(ship, UnprojectSectorPosition(displayPosition), out x, out y);
            else
            {
                Vector2 ground = displayPosition - Vector2.up * (float)ShipSurfaceState.VisualRise(GetFlight(ship.ShipId));
                ground = WorldLocalPresentation.ProjectPosition(ground, new Vector2((float)ship.PositionX, (float)ship.PositionY));
                ShipGeometry.WorldToLocal(ship, ground.x, ground.y, out x, out y);
            }
            return new Vector2((float)x, (float)y);
        }

        private void ReleaseShipInteriorWorld(string shipId)
        {
            if (!shipInteriorWorlds.TryGetValue(shipId, out var world)) return;
            if (GameplayWorldRegistry.GetWorldItemCount(world.WorldKey) != 0)
                throw new InvalidOperationException("船内仍有物品，不能释放世界：" + world.WorldKey);
            ReleaseShipInteriorTerrain(shipId);
            try { world.Release(); }
            catch
            {
                if (GetShip(shipId) is ShipState ship) SynchronizeShipInteriorTerrain(ship, world);
                throw;
            }
            ReleaseShipInteriorSortingDomain(world.WorldKey);
            WorldPresentationTransformRegistry.Remove(world.WorldKey);
            ForgetNetworkLocalWorld(world.WorldKey);
            shipInteriorWorlds.Remove(shipId);
        }

        private void ReleaseAllShipInteriorWorlds()
        {
            pendingInteriorItemMoves.Clear();
            interiorItemMoveBatch.Clear();
            trackedInteriorItemWorlds.Clear();
            pendingInteriorChunkReconciliation.Clear();
            interiorChunkReconciliationBatch.Clear();
            retainedShipWorlds.Clear();
            retainedWorldsNeedRetirementCheck = false;
            foreach (string id in new List<string>(shipInteriorWorlds.Keys)) ReleaseShipInteriorWorld(id);
            sentLocalWorldRevisions.Clear();
            pendingLocalWorldRevisions.Clear();
            localWorldNetworkCache.Clear();
            remoteLocalWorldResidents.Clear();
            remoteLocalResidentSnapshots.Clear();
            interiorItemsWithRemoteAuthority.Clear();
            receivedLocalWorldRevisions.Clear();
            remoteLocalDropSnapshots.Clear();
        }

        private void RetireShipInteriorWorld(string shipId)
        {
            string key = MachineWorld.GetShipScopeKey(shipId);
            if (TryGetShipInteriorWorld(shipId, out _))
            {
                if (DroppedItemService.HasRetainedWorldItems(key, owner))
                    throw new InvalidOperationException("船内仍有掉落物，不能删除世界：" + key);
                ReleaseShipInteriorWorld(shipId);
            }
            SaveDataMgr.Instance?.DiscardRuntimeWorldDifferences(key);
            owner?.DroppedItems?.Worlds.Remove(key);
        }

        private void QueueInteriorItemMove(Item item)
        {
            if (item == null) return;
            if (retainedShipWorlds.Count > 0 && retainedItemOwnershipRevision != GameplayWorldRegistry.ItemOwnershipRevision)
                retainedWorldsNeedRetirementCheck = true;
            string key = GameplayWorldRegistry.GetItemWorldKey(item);
            if (trackedInteriorItemWorlds.TryGetValue(item, out string previous) && previous != key)
                pendingInteriorChunkReconciliation.Add(previous.Substring(5));
            if (key != null && key.StartsWith("ship:", StringComparison.Ordinal))
            {
                trackedInteriorItemWorlds[item] = key;
                pendingInteriorItemMoves.Add(item);
            }
            else trackedInteriorItemWorlds.Remove(item);
        }

        private void QueueRetainedWorldRetirementForItem(Item item)
        {
            string key = GameplayWorldRegistry.GetItemWorldKey(item);
            if (item != null && trackedInteriorItemWorlds.Remove(item, out string previous))
                pendingInteriorChunkReconciliation.Add(previous.Substring(5));
            if (key != null && key.StartsWith("ship:", StringComparison.Ordinal) && retainedShipWorlds.Contains(key.Substring(5)))
                retainedWorldsNeedRetirementCheck = true;
        }

        private void QueueRetainedWorldRetirementForCrop(string worldKey, RuntimeWorldAddress address)
        {
            if (worldKey != null && worldKey.StartsWith("ship:", StringComparison.Ordinal) && retainedShipWorlds.Contains(worldKey.Substring(5)))
                retainedWorldsNeedRetirementCheck = true;
        }

        private void QueueRetainedWorldRetirementForDrops(string worldKey)
        {
            if (worldKey != null && worldKey.StartsWith("ship:", StringComparison.Ordinal) && retainedShipWorlds.Contains(worldKey.Substring(5)))
                retainedWorldsNeedRetirementCheck = true;
        }

        private bool HasRetainedShipWorldContents(string shipId, bool includeDrops = false)
        {
            string key = MachineWorld.GetShipScopeKey(shipId);
            if (GameplayWorldRegistry.GetWorldItemCount(key) > 0) return true;
            if (includeDrops && DroppedItemService.HasRetainedWorldItems(key, owner)) return true;
            if (!TryGetShipInteriorWorld(shipId, out var world) || SaveDataMgr.Instance == null) return false;
            using var scope = GameplayWorldRegistry.UseWorld(key);
            if (AiRuntimeBackendService.ExistingEcology?.ResidentCount > 0) return true;
            foreach (RuntimeWorldAddress address in world.ChunkAddresses)
            {
                foreach (AgricultureCellSaveData cell in SaveDataMgr.Instance.GetAgricultureCells(address))
                    if (cell != null && (cell.Crop != null || cell.SourceTileId > 0)) return true;
                if (SaveDataMgr.Instance.TryGetChunkDelta(key, new Vector2Int(address.ChunkOrigin.X, address.ChunkOrigin.Y), out ChunkSaveRecord record) &&
                    ((record.ContaminationCells?.Count ?? 0) > 0 || (record.SnowCells?.Count ?? 0) > 0 ||
                     (record.GroundLayerCells?.Count ?? 0) > 0)) return true;
                if (!world.WorldRuntime.TryGetChunkTerrain(address, out ChunkTerrainData terrain)) continue;
                for (int y = 0; y < terrain.Height; y++)
                for (int x = 0; x < terrain.Width; x++)
                {
                    TerrainCell cell = terrain.GetCell(x, y);
                    terrain.TryGetEnvironmentValue(TerrainSupportLayer.ExternalOwnershipLayer, x, y, out float external);
                    if (cell.GroundTileId != 0 || cell.BackTileId != 0 || terrain.GetLiquidLevel(x, y) != 0 ||
                        external <= 0f && TerrainSupportLayer.GetTileId(terrain, x, y) != 0) return true;
                }
            }
            return false;
        }

        internal void PreserveWreckInteriorWorldIfNeeded(ShipState ship)
        {
            if (!TryGetShipInteriorWorld(ship.ShipId, out var world)) return;
            if (!HasRetainedShipWorldContents(ship.ShipId, includeDrops: true))
            {
                RetireShipInteriorWorld(ship.ShipId);
                return;
            }
            ship.RetainsInteriorWorld = true;
            retainedShipWorlds.Add(ship.ShipId);
            world.ObserveRetainedWorldContents(() => retainedWorldsNeedRetirementCheck = true);
            viewBindingsDirty = true;
        }

        private void RetireEmptyRetainedShipWorlds()
        {
            if (!FlatWorld.Networking.GameNetwork.HasStateAuthority || !retainedWorldsNeedRetirementCheck) return;
            retainedWorldsNeedRetirementCheck = false;
            foreach (string id in new List<string>(retainedShipWorlds))
            {
                ShipState ship = GetShip(id);
                if (ship == null) { retainedShipWorlds.Remove(id); continue; }
                if (!ship.IsWreck && ship.Pieces.Count != 0)
                {
                    ship.RetainsWorldWithoutStructure = false;
                    retainedShipWorlds.Remove(id);
                    if (TryGetShipInteriorWorld(id, out var restored)) restored.StopObservingRetainedWorldContents();
                    continue;
                }
                if (ship.RetainsWorldWithoutStructure && TryGetShipInteriorWorld(id, out var retained))
                    SynchronizeShipInteriorTerrain(ship, retained);
                if (!HasRetainedShipWorldContents(id, includeDrops: ship.IsWreck))
                {
                    try
                    {
                        if (ship.IsWreck && ship.RetainsInteriorWorld)
                        {
                            RetireShipInteriorWorld(id);
                            ship.RetainsInteriorWorld = false;
                            retainedShipWorlds.Remove(id);
                            viewBindingsDirty = true;
                        }
                        else RemoveEmptyShip(ship);
                    }
                    catch (Exception error) { Debug.LogException(error); }
                }
            }
            retainedItemOwnershipRevision = GameplayWorldRegistry.ItemOwnershipRevision;
        }

        private void CommitInteriorItemMoves()
        {
            interiorItemMoveBatch.Clear();
            interiorItemMoveBatch.AddRange(pendingInteriorItemMoves);
            pendingInteriorItemMoves.Clear();
            foreach (Item item in interiorItemMoveBatch)
            {
                if (item == null) continue;
                string key = GameplayWorldRegistry.GetItemWorldKey(item);
                if (key != null && key.StartsWith("ship:", StringComparison.Ordinal) &&
                    shipInteriorWorlds.TryGetValue(key.Substring(5), out var world) && world.TrackMovedItemChunk(item))
                    pendingInteriorChunkReconciliation.Add(key.Substring(5));
            }
            interiorItemMoveBatch.Clear();
            interiorChunkReconciliationBatch.Clear();
            interiorChunkReconciliationBatch.AddRange(pendingInteriorChunkReconciliation);
            pendingInteriorChunkReconciliation.Clear();
            foreach (string shipId in interiorChunkReconciliationBatch)
                if (GetShip(shipId) is ShipState ship && shipInteriorWorlds.TryGetValue(shipId, out var world))
                    world.SynchronizeChunks(ship, force: true);
            interiorChunkReconciliationBatch.Clear();
        }

        private void CaptureShipInteriorWorldStates()
        {
            SaveDataMgr save = SaveDataMgr.Instance;
            if (save == null || !ReferenceEquals(owner, save.SaveData)) return;
            foreach (var world in shipInteriorWorlds.Values)
                save.CaptureRuntimeLocalWorldState(world.WorldKey);
        }
        #endregion
    }
}
