using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 船内普通世界的显示投影
        private readonly Dictionary<ChunkView, WorldPresentationProjection> interiorChunkProjections = new();
        private readonly Dictionary<Item, WorldPresentationProjection> interiorItemProjections = new();
        private readonly Dictionary<string, WorldPresentationSortingDomain> interiorSortingDomains = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ShipInteriorPresentationMembers> interiorPresentationMembers = new(StringComparer.Ordinal);
        private readonly Dictionary<ChunkView, ShipInteriorChunkPresentationBinding> interiorChunkBindings = new();
        private readonly Dictionary<Item, ShipInteriorPresentationMembers> interiorItemOwners = new();
        private readonly List<Item> interiorWorldItems = new();
        private readonly List<string> expiredInteriorWorlds = new();
        private readonly Dictionary<string, ShipEngineExhaustPresenter> interiorEngineExhausts = new();
        private readonly HashSet<string> visibleInteriorEngines = new();
        private readonly List<string> expiredInteriorEngines = new();
        private ulong interiorPresentationGeneration;
        private bool interiorPresentationItemEventsSubscribed;

        private sealed class ShipInteriorPresentationMembers
        {
            internal readonly ShipInteriorWorldRuntime World;
            internal readonly WorldPresentationSortingDomain Domain;
            internal readonly Dictionary<RuntimeWorldAddress, ShipInteriorChunkPresentationBinding> Chunks = new();
            internal readonly HashSet<Item> Items = new();
            internal readonly List<WorldPresentationProjection> ItemProjections = new();
            internal readonly List<ShipPieceState> Engines = new();
            internal List<ShipPieceState> PieceSource;
            internal ulong StructureRevision;
            internal readonly HashSet<RuntimeWorldAddress> DirtyChunks = new();
            internal readonly List<RuntimeWorldAddress> ChunkBatch = new();
            internal readonly HashSet<Item> SeenItems = new();
            internal readonly HashSet<Item> ChangedItemLifecycles = new();
            internal readonly List<Item> RemovedItems = new();
            internal readonly IDisposable ChunkSubscription;
            internal ulong OwnershipRevision;
            internal ulong LastSeenGeneration;
            internal bool ItemsDirty = true;

            internal ShipInteriorPresentationMembers(ShipInteriorWorldRuntime world, WorldPresentationSortingDomain domain)
            {
                World = world;
                Domain = domain;
                ChunkSubscription = world.WorldRuntime.Events.Subscribe<ChunkPresentationStatusChanged>(OnChunkStatusChanged);
                foreach (RuntimeWorldAddress address in world.ChunkAddresses) DirtyChunks.Add(address);
            }

            private void OnChunkStatusChanged(ChunkPresentationStatusChanged change) => DirtyChunks.Add(change.Address);
            internal void OnChunkPresentationChanged(RuntimeWorldAddress address) => DirtyChunks.Add(address);
            internal void OnItemInHandChanged(bool inHand) => ItemsDirty = true;
        }

        private sealed class ShipInteriorChunkPresentationBinding
        {
            internal ShipInteriorPresentationMembers Members;
            internal RuntimeWorldAddress Address;
            internal ChunkView View;
            internal ChunkTilemapRenderer Renderer;
            internal WorldPresentationProjection Projection;
        }

        private void BeginShipInteriorPresentation()
        {
            interiorPresentationGeneration++;
            visibleInteriorEngines.Clear(); visibleInteriorPassengers.Clear();
        }

        private Matrix4x4 ResolveShipInteriorPresentationMatrix(ShipState ship)
        {
            Vector2 origin = ProjectShipInteriorPosition(ship, Vector2.zero);
            Vector2 axisX = ProjectShipInteriorPosition(ship, Vector2.right) - origin;
            Vector2 axisY = ProjectShipInteriorPosition(ship, Vector2.up) - origin;
            Matrix4x4 projection = Matrix4x4.identity;
            projection.m00 = axisX.x; projection.m10 = axisX.y;
            projection.m01 = axisY.x; projection.m11 = axisY.y;
            projection.m03 = origin.x; projection.m13 = origin.y;
            return projection;
        }

        // 同一船内世界只创建一个外层域，所有投影共用这一层和船姿。
        private WorldPresentationSortingDomain SynchronizeShipInteriorSortingDomain(
            ShipInteriorWorldRuntime world, Matrix4x4 projection, bool visible)
        {
            if (!interiorSortingDomains.TryGetValue(world.WorldKey, out WorldPresentationSortingDomain domain) || domain.IsDisposed)
                interiorSortingDomains[world.WorldKey] = domain = WorldPresentationSortingDomain.Create(
                    transform, world.WorldKey, WorldSortingManager.ProjectedWorldCategory);
            domain.Synchronize(projection, visible);
            WorldPresentationTransformRegistry.Set(world.WorldKey, projection, visible, domain);
            return domain;
        }

        private void SynchronizeShipInteriorPresentation(ShipState ship, bool visible)
        {
            if (!TryGetShipInteriorWorld(ship.ShipId, out ShipInteriorWorldRuntime world)) return;
            Matrix4x4 projection = ResolveShipInteriorPresentationMatrix(ship);
            WorldPresentationSortingDomain domain = SynchronizeShipInteriorSortingDomain(world, projection, visible);
            ShipInteriorPresentationMembers members = GetShipInteriorPresentationMembers(world, domain);
            members.LastSeenGeneration = interiorPresentationGeneration;
            using var scope = GameplayWorldRegistry.UseWorld(world.WorldKey);
            ReconcileShipInteriorChunkPresentationBindings(members);
            ReconcileShipInteriorItemPresentationBindings(members);
            if (!ReferenceEquals(members.PieceSource, ship.Pieces) || members.StructureRevision != ship.StructureRevision)
            {
                members.PieceSource = ship.Pieces; members.StructureRevision = ship.StructureRevision;
                members.Engines.Clear();
                foreach (ShipPieceState piece in ship.Pieces)
                    if (piece.IsAlive && piece.Kind == ShipPieceKind.Engine) members.Engines.Add(piece);
            }
            foreach (ShipInteriorChunkPresentationBinding binding in members.Chunks.Values)
            {
                binding.Projection.ScheduleSynchronizationBeforeRendering(projection, visible);
                if (binding.Renderer != null)
                    ChunkBatchRendererGroupService.SetOwnerExternalProjection(binding.Renderer, projection, visible, domain);
            }
            foreach (WorldPresentationProjection display in members.ItemProjections)
                display.ScheduleSynchronizationBeforeRendering(projection, visible);
            if (!ship.IsWreck)
                foreach (ShipPieceState piece in members.Engines)
                    SynchronizeInteriorEngineExhaust(ship, piece, projection, visible, domain);
        }

        // 区块绑定和物品归属变化只重建对应成员，动态姿态仍逐帧同步。
        private ShipInteriorPresentationMembers GetShipInteriorPresentationMembers(
            ShipInteriorWorldRuntime world, WorldPresentationSortingDomain domain)
        {
            if (interiorPresentationMembers.TryGetValue(world.WorldKey, out ShipInteriorPresentationMembers members))
            {
                if (ReferenceEquals(members.World, world) && ReferenceEquals(members.Domain, domain)) return members;
                ReleaseShipInteriorPresentationMembers(world.WorldKey);
            }
            if (!interiorPresentationItemEventsSubscribed)
            {
                ItemMgr.RuntimeItemRegistered += MarkShipInteriorItemBindingsDirty;
                ItemMgr.RuntimeItemUnregistered += MarkShipInteriorItemBindingsDirty;
                interiorPresentationItemEventsSubscribed = true;
            }
            members = new ShipInteriorPresentationMembers(world, domain);
            interiorPresentationMembers.Add(world.WorldKey, members);
            return members;
        }

        private void MarkShipInteriorItemBindingsDirty(Item item)
        {
            if (ReferenceEquals(item, null)) return;
            if (interiorItemOwners.TryGetValue(item, out ShipInteriorPresentationMembers owner))
            {
                owner.ItemsDirty = true;
                owner.ChangedItemLifecycles.Add(item);
            }
            string worldKey = GameplayWorldRegistry.GetItemWorldKey(item);
            if (worldKey != null && interiorPresentationMembers.TryGetValue(worldKey, out ShipInteriorPresentationMembers members))
                members.ItemsDirty = true;
        }

        private void ReconcileShipInteriorChunkPresentationBindings(ShipInteriorPresentationMembers members)
        {
            if (members.DirtyChunks.Count == 0) return;
            members.ChunkBatch.Clear();
            members.ChunkBatch.AddRange(members.DirtyChunks);
            members.DirtyChunks.Clear();
            foreach (RuntimeWorldAddress address in members.ChunkBatch)
            {
                if (!members.World.Chunks.TryGetRuntimeChunkView(address, out ChunkView view) ||
                    !HasShipInteriorChunkAddress(members.World, address)) view = null;
                if (members.Chunks.TryGetValue(address, out ShipInteriorChunkPresentationBinding previous))
                {
                    if (previous.View == view && view != null && previous.Projection != null && !previous.Projection.IsReleased) continue;
                    ReleaseShipInteriorChunkPresentationBinding(members, address, previous);
                }
                if (view == null) continue;
                if (interiorChunkBindings.TryGetValue(view, out ShipInteriorChunkPresentationBinding reused))
                    ReleaseShipInteriorChunkPresentationBinding(reused.Members, reused.Address, reused);
                WorldPresentationProjection display = WorldPresentationProjection.Create(view.transform, transform);
                display.SetSortingDomain(members.Domain);
                var binding = new ShipInteriorChunkPresentationBinding
                { Members = members, Address = address, View = view, Projection = display,
                    Renderer = view.GetComponentInChildren<ChunkTilemapRenderer>(true) };
                members.Chunks.Add(address, binding);
                interiorChunkBindings.Add(view, binding);
                interiorChunkProjections[view] = display;
                view.PresentationChanged += members.OnChunkPresentationChanged;
            }
            members.ChunkBatch.Clear();
        }

        private static bool HasShipInteriorChunkAddress(ShipInteriorWorldRuntime world, RuntimeWorldAddress address)
        {
            if (world.ChunkAddresses is ICollection<RuntimeWorldAddress> addresses) return addresses.Contains(address);
            foreach (RuntimeWorldAddress current in world.ChunkAddresses)
                if (current.Equals(address)) return true;
            return false;
        }

        private void ReleaseShipInteriorChunkPresentationBinding(ShipInteriorPresentationMembers members,
            RuntimeWorldAddress address, ShipInteriorChunkPresentationBinding binding)
        {
            members.Chunks.Remove(address);
            if (binding.View != null) binding.View.PresentationChanged -= members.OnChunkPresentationChanged;
            if (interiorChunkBindings.TryGetValue(binding.View, out ShipInteriorChunkPresentationBinding current) &&
                ReferenceEquals(current, binding))
            {
                interiorChunkBindings.Remove(binding.View);
                interiorChunkProjections.Remove(binding.View);
            }
            if (binding.Renderer != null) ChunkBatchRendererGroupService.ClearOwnerExternalProjection(binding.Renderer);
            if (binding.Projection != null) binding.Projection.Release();
        }

        private void ReconcileShipInteriorItemPresentationBindings(ShipInteriorPresentationMembers members)
        {
            ulong revision = GameplayWorldRegistry.GetWorldItemOwnershipRevision(members.World.WorldKey);
            if (!members.ItemsDirty && members.OwnershipRevision == revision) return;
            members.ItemsDirty = false;
            members.OwnershipRevision = revision;
            GameplayWorldRegistry.CopyWorldItems(members.World.WorldKey, interiorWorldItems);
            ItemMgr manager = ItemMgr.GetExistingInstance();
            members.SeenItems.Clear();
            foreach (Item item in interiorWorldItems)
            {
                if (item == null || item.itemData == null || item is Player ||
                    manager == null || !manager.IsRuntimeItemRegistered(item)) continue;
                members.SeenItems.Add(item);
                if (interiorItemOwners.TryGetValue(item, out ShipInteriorPresentationMembers previous) && previous != members)
                    ReleaseShipInteriorItemPresentationBinding(previous, item);
                bool added = members.Items.Add(item);
                interiorItemOwners[item] = members;
                bool lifecycleChanged = members.ChangedItemLifecycles.Remove(item);
                if (added || lifecycleChanged)
                {
                    item.OnInHandChanged -= members.OnItemInHandChanged;
                    item.OnInHandChanged += members.OnItemInHandChanged;
                }
                bool inHand = item.InHand;
                if (lifecycleChanged || inHand)
                    ReleaseShipInteriorItemProjection(members, item);
                if (inHand) continue;
                if (interiorItemProjections.TryGetValue(item, out WorldPresentationProjection display))
                {
                    if (display != null && !display.IsReleased) continue;
                    ReleaseShipInteriorItemProjection(members, item);
                }
                display = WorldPresentationProjection.Create(item.transform, transform);
                display.SetSortingDomain(members.Domain);
                interiorItemProjections[item] = display;
                members.ItemProjections.Add(display);
            }
            members.RemovedItems.Clear();
            foreach (Item item in members.Items)
                if (!members.SeenItems.Contains(item)) members.RemovedItems.Add(item);
            foreach (Item item in members.RemovedItems)
                ReleaseShipInteriorItemPresentationBinding(members, item);
        }

        private void ReleaseShipInteriorItemProjection(ShipInteriorPresentationMembers members, Item item)
        {
            if (!interiorItemProjections.Remove(item, out WorldPresentationProjection display)) return;
            members.ItemProjections.Remove(display);
            if (display != null) display.Release();
        }

        private void ReleaseShipInteriorItemPresentationBinding(ShipInteriorPresentationMembers members, Item item)
        {
            members.Items.Remove(item);
            members.ChangedItemLifecycles.Remove(item);
            if (item != null) item.OnInHandChanged -= members.OnItemInHandChanged;
            if (!interiorItemOwners.TryGetValue(item, out ShipInteriorPresentationMembers owner) || owner != members) return;
            interiorItemOwners.Remove(item);
            ReleaseShipInteriorItemProjection(members, item);
        }

        private void ReleaseShipInteriorPresentationMembers(string worldKey)
        {
            if (!interiorPresentationMembers.Remove(worldKey, out ShipInteriorPresentationMembers members)) return;
            members.ChunkSubscription.Dispose();
            members.ChunkBatch.Clear();
            members.ChunkBatch.AddRange(members.Chunks.Keys);
            foreach (RuntimeWorldAddress address in members.ChunkBatch)
                ReleaseShipInteriorChunkPresentationBinding(members, address, members.Chunks[address]);
            members.RemovedItems.Clear();
            members.RemovedItems.AddRange(members.Items);
            foreach (Item item in members.RemovedItems)
                ReleaseShipInteriorItemPresentationBinding(members, item);
        }

        private void SynchronizeInteriorEngineExhaust(ShipState ship, ShipPieceState piece, Matrix4x4 projection, bool visible,
            WorldPresentationSortingDomain domain)
        {
            visibleInteriorEngines.Add(piece.PieceId);
            if (!interiorEngineExhausts.TryGetValue(piece.PieceId, out ShipEngineExhaustPresenter exhaust) || exhaust == null)
            {
                var root = new GameObject("ShipEngineExhaustAnchor");
                root.transform.SetParent(domain.Root, false);
                SpriteRenderer source = root.AddComponent<SpriteRenderer>();
                source.color = Color.clear;
                if (GameRes.ExistingInstance.TryGetItemDefinition(piece.ItemId, out RuntimeItemDefinition definition))
                {
                    source.sprite = definition.Sprite;
                    root.transform.localScale = definition.Visual?.RendererLocalScale ?? Vector3.one;
                }
                exhaust = root.AddComponent<ShipEngineExhaustPresenter>();
                exhaust.Bind(this, piece, source);
                interiorEngineExhausts[piece.PieceId] = exhaust;
            }
            if (exhaust.transform.parent != domain.Root) exhaust.transform.SetParent(domain.Root, true);
            exhaust.SetSortingDomain(domain);
            Vector3 position = projection.MultiplyPoint3x4(ShipInteriorPiecePosition(ship, piece));
            exhaust.transform.SetPositionAndRotation(position, projection.rotation * Quaternion.Euler(0f, 0f, piece.QuarterTurns * 90f));
            if (exhaust.gameObject.activeSelf != visible) exhaust.gameObject.SetActive(visible);
        }

        private void EndShipInteriorPresentation()
        {
            expiredInteriorWorlds.Clear();
            foreach (var pair in interiorPresentationMembers)
                if (pair.Value.LastSeenGeneration != interiorPresentationGeneration) expiredInteriorWorlds.Add(pair.Key);
            foreach (string worldKey in expiredInteriorWorlds) ReleaseShipInteriorPresentationMembers(worldKey);
            expiredInteriorEngines.Clear();
            foreach (string id in interiorEngineExhausts.Keys)
                if (!visibleInteriorEngines.Contains(id)) expiredInteriorEngines.Add(id);
            foreach (string id in expiredInteriorEngines)
                if (interiorEngineExhausts.Remove(id, out ShipEngineExhaustPresenter exhaust) && exhaust != null)
                    Destroy(exhaust.gameObject);
        }

        private void HideShipInteriorPresentation()
        {
            ReleaseExpiredPassengerInteriorProjections(true);
            foreach (ShipInteriorWorldRuntime world in shipInteriorWorlds.Values)
            {
                interiorSortingDomains.TryGetValue(world.WorldKey, out WorldPresentationSortingDomain domain);
                WorldPresentationTransformRegistry.TryGet(world.WorldKey, out Matrix4x4 projection, out _);
                domain?.Synchronize(projection, false);
                WorldPresentationTransformRegistry.Set(world.WorldKey, projection, false, domain);
            }
            foreach (WorldPresentationProjection display in interiorChunkProjections.Values)
                if (display != null) display.SetVisible(false);
            foreach (ShipInteriorChunkPresentationBinding binding in interiorChunkBindings.Values)
            {
                ChunkTilemapRenderer renderer = binding.Renderer;
                if (renderer == null) continue;
                WorldPresentationProjection display = binding.Projection;
                WorldPresentationSortingDomain domain = display != null && !display.IsReleased ? display.SortingDomain : null;
                if (domain == null) ChunkBatchRendererGroupService.TryGetOwnerNativeProjectionDomain(renderer, out domain);
                WorldPresentationTransformRegistry.TryGet(domain?.WorldKey, out Matrix4x4 projection, out _);
                ChunkBatchRendererGroupService.SetOwnerExternalProjection(renderer, projection, false, domain);
            }
            foreach (WorldPresentationProjection display in interiorItemProjections.Values)
                if (display != null) display.SetVisible(false);
            foreach (ShipEngineExhaustPresenter exhaust in interiorEngineExhausts.Values)
                if (exhaust != null) exhaust.gameObject.SetActive(false);
        }

        internal void ReleaseAllShipInteriorPresentations()
        {
            HideShipInteriorPresentation();
            ReleaseAllLooseItemSortingProjections();
            foreach (WorldPresentationProjection display in wreckSortingProjections.Values)
                if (display != null) display.Release();
            wreckSortingProjections.Clear();
            expiredInteriorWorlds.Clear();
            expiredInteriorWorlds.AddRange(interiorPresentationMembers.Keys);
            foreach (string worldKey in expiredInteriorWorlds) ReleaseShipInteriorPresentationMembers(worldKey);
            if (interiorPresentationItemEventsSubscribed)
            {
                ItemMgr.RuntimeItemRegistered -= MarkShipInteriorItemBindingsDirty;
                ItemMgr.RuntimeItemUnregistered -= MarkShipInteriorItemBindingsDirty;
                interiorPresentationItemEventsSubscribed = false;
            }
            foreach (ShipEngineExhaustPresenter exhaust in interiorEngineExhausts.Values)
                if (exhaust != null) Destroy(exhaust.gameObject);
            interiorChunkProjections.Clear();
            interiorItemProjections.Clear(); interiorEngineExhausts.Clear();
            interiorChunkBindings.Clear(); interiorItemOwners.Clear();
            foreach (ShipInteriorWorldRuntime world in shipInteriorWorlds.Values)
                WorldPresentationTransformRegistry.Remove(world.WorldKey);
            foreach (var pair in interiorSortingDomains)
            {
                WorldPresentationTransformRegistry.Remove(pair.Key);
                pair.Value.Dispose();
            }
            interiorSortingDomains.Clear();
        }

        private void ReleaseShipInteriorSortingDomain(string worldKey)
        {
            ReleaseShipInteriorPresentationMembers(worldKey);
            if (!interiorSortingDomains.Remove(worldKey, out WorldPresentationSortingDomain domain)) return;
            Transform root = domain.Root;
            expiredInteriorPassengers.Clear();
            foreach (var pair in passengerProjections)
                if (pair.Value != null && root != null && pair.Value.transform.IsChildOf(root)) expiredInteriorPassengers.Add(pair.Key);
            foreach (Player player in expiredInteriorPassengers) ReleasePassengerInteriorProjection(player);
            expiredInteriorEngines.Clear();
            foreach (var pair in interiorEngineExhausts)
                if (pair.Value != null && root != null && pair.Value.transform.IsChildOf(root)) expiredInteriorEngines.Add(pair.Key);
            foreach (string id in expiredInteriorEngines)
                if (interiorEngineExhausts.Remove(id, out ShipEngineExhaustPresenter exhaust) && exhaust != null) Destroy(exhaust.gameObject);
            var loose = new List<Item>();
            foreach (var pair in looseShipProjections)
                if (pair.Value != null && root != null && pair.Value.transform.IsChildOf(root)) loose.Add(pair.Key);
            foreach (Item item in loose) ReleaseLooseItemSortingProjection(item);
            expiredInteriorEngines.Clear();
            foreach (var pair in wreckSortingProjections)
                if (pair.Value != null && root != null && pair.Value.transform.IsChildOf(root)) expiredInteriorEngines.Add(pair.Key);
            foreach (string id in expiredInteriorEngines) ReleaseWreckSortingProjection(id);
            WorldPresentationTransformRegistry.Remove(worldKey);
            domain.Dispose();
        }
        #endregion

        #region 船体世界表现调度
        private bool viewBindingsDirty;

        public Vector3 ProjectMachinePosition(MachineEntity node)
        {
            if (node?.ScopeKey == null || !node.ScopeKey.StartsWith("ship:", StringComparison.Ordinal))
                return node?.Snapshot.transform.position ?? default;
            ShipState ship = GetShip(node.ScopeKey.Substring(5));
            return ship == null ? node.Snapshot.transform.position :
                DisplayPresentationPoint(ship, (node.Cell.x + .5d) * ship.CellSizeMeters,
                    (node.Cell.y + .5d) * ship.CellSizeMeters);
        }

        private void SynchronizeViews(bool refreshBindings = false)
        {
            if (transitionRunning || DimensionManager.ExistingInstance?.IsTransitioning == true ||
                GameManager.Instance?.IsWorldEntryInProgress == true) return;
            EnsureShipViewResourceGeneration();
            if (FlatWorld.Networking.GameNetwork.IsOnline) ClearShipViewPool();
            bool forceBindings = refreshBindings || viewBindingsDirty;
            viewBindingsDirty = false;
            visibleSurfaceShadows.Clear();
            visibleShipColliderPresenters.Clear();
            BeginShipInteriorPresentation();
            foreach (ShipState ship in State.Ships)
            {
                if (ship.IsWreck)
                {
                    SynchronizeWreckView(ship);
                    if (ship.RetainsInteriorWorld) SynchronizeShipInteriorPresentation(ship, IsVisible(ship));
                    continue;
                }
                bool visible = IsVisible(ship);
                SynchronizeShipInteriorPresentation(ship, visible);
                ShipFlightState flight = visible ? GetFlight(ship.ShipId) : null;
                float angle = (float)(GetShipPresentationPose(ship).AngleRadians * 180d / Math.PI);
                ShipColliderPresenter presenter = visible ? SynchronizeShipColliderPresenter(ship, angle) : null;
                double height = flight != null && flight.Phase is ShipFlightPhase.Lifting or ShipFlightPhase.SurfaceDescending
                    ? flight.HeightMeters : 0d;
                SynchronizeSurfaceShadow(ship, visible, height, forceBindings);
                float rise = (float)ShipLandingGuidance.SurfaceVisualRise(height);
                float radians = angle * Mathf.Deg2Rad;
                Vector3 visualLiftOffset = new(Mathf.Sin(radians) * rise, Mathf.Cos(radians) * rise, 0f);
                presenter?.SynchronizeSurfaceState(flight, visualLiftOffset);
            }
            RemoveExpiredPooledShipPresenters();
            RemoveExpiredShipColliderPresenters();
            RemoveExpiredSurfaceShadows();
            SynchronizeRemotePassengerSurfaceVisuals();
            Player player = ItemMgr.Instance?.User_Player;
            if (player == null)
            {
                EndShipInteriorPresentation();
                ReleaseExpiredPassengerInteriorProjections();
                return;
            }
            SpacePassengerState passenger = GetPassenger(player);
            SetTerrainSuppression(player, IsSpaceView || passenger.Supported || passenger.SurfaceDescending);
            ShipState carrier = GetShip(passenger.ShipId);
            if (passenger.Supported && IsInPresentationWorld(carrier)) PlacePassenger(player, passenger, carrier, true);
            else if (IsPassengerSurfaceDescendingHere(passenger)) PlaceDescendingPassenger(player, passenger);
            else if (!passenger.Supported && IsSpaceView && passenger.IsInSpace && !passenger.LandingResolved)
            {
                RestorePassengerSurface(player);
                Vector2 position = GetPassengerPresentationPosition(passenger, null);
                player.transform.position = new Vector3(position.x, position.y, 0f);
                Rigidbody2D playerBody = player.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover)?.rb;
                if (playerBody != null) playerBody.position = position;
            }
            else RestorePassengerSurface(player);
            UpdateDrivingCamera(player, passenger, carrier);
            EndShipInteriorPresentation();
            ReleaseExpiredPassengerInteriorProjections();
        }

        private void RemovePieceView(string id)
        {
            if (int.TryParse(id, out int guid) && ItemMgr.GetExistingInstance() is ItemMgr manager &&
                manager.WorldRunTimeItems.TryGetValue(guid, out Item item) && item != null &&
                interiorItemOwners.TryGetValue(item, out ShipInteriorPresentationMembers members))
            {
                ReleaseShipInteriorItemProjection(members, item);
                members.ItemsDirty = true;
            }
            if (interiorEngineExhausts.Remove(id, out ShipEngineExhaustPresenter exhaust) && exhaust != null)
                Destroy(exhaust.gameObject);
        }

        private void ReleaseViews()
        {
            HideShipInteriorPresentation();
            ReleasePassengerSurfacePresentation();
            ReleaseSurfaceShadows();
            foreach (string id in new List<string>(wreckViews.Keys)) RemoveWreckView(id);
            ReleaseDrivingCamera();
            if (State != null) ReleaseLooseViews();
            ReleaseShipColliderPresenters();
        }

        private void OnDisable()
        {
            GameplayFixedStepSimulation.Unregister(this);
            ResetPresentationHistory();
            ReleaseSurfaceShadows();
        }

        internal void CollectAssemblyPresentationRenderers(string shipId, List<SpriteRenderer> renderers,
            List<WorldVisualSnapshot> snapshots)
        {
            foreach (var pair in interiorItemProjections)
            {
                if (pair.Key == null || pair.Value == null) continue;
                string world = GameplayWorldRegistry.GetItemWorldKey(pair.Key);
                if (world?.StartsWith("ship:", StringComparison.Ordinal) == true &&
                    SameAssembly(world.Substring(5), shipId))
                    pair.Value.CollectProjectedSprites(renderers);
            }
            foreach (var pair in passengerProjections)
            {
                string world = GameplayWorldRegistry.GetItemWorldKey(pair.Key);
                if (pair.Key != null && pair.Value != null && world?.StartsWith("ship:", StringComparison.Ordinal) == true &&
                    SameAssembly(world.Substring(5), shipId)) pair.Value.CollectProjectedSprites(renderers);
            }
            foreach (var pair in shipInteriorWorlds)
            {
                if (!SameAssembly(pair.Key, shipId) ||
                    !WorldPresentationTransformRegistry.TryGet(pair.Value.WorldKey, out Matrix4x4 matrix, out _)) continue;
                foreach (var address in pair.Value.ChunkAddresses)
                {
                    if (!pair.Value.Chunks.TryGetRuntimeChunkView(address, out ChunkView view) ||
                        !interiorChunkBindings.TryGetValue(view, out ShipInteriorChunkPresentationBinding binding) ||
                        binding.Renderer == null) continue;
                    binding.Renderer.CollectVisualSnapshots(matrix, snapshots);
                }
            }
        }
        #endregion
    }
}
