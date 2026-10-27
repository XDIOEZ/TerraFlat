using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 船内普通世界的显示投影
        private readonly Dictionary<ChunkView, WorldPresentationProjection> interiorChunkProjections = new();
        private readonly Dictionary<ChunkView, ChunkTilemapRenderer> interiorChunkRenderers = new();
        private readonly Dictionary<Item, WorldPresentationProjection> interiorItemProjections = new();
        private readonly HashSet<ChunkView> visibleInteriorChunks = new();
        private readonly HashSet<Item> visibleInteriorItems = new();
        private readonly List<Item> interiorWorldItems = new();
        private readonly List<ChunkView> expiredInteriorChunks = new();
        private readonly List<Item> expiredInteriorItems = new();
        private readonly Dictionary<string, ShipEngineExhaustPresenter> interiorEngineExhausts = new();
        private readonly HashSet<string> visibleInteriorEngines = new();
        private readonly List<string> expiredInteriorEngines = new();

        private void BeginShipInteriorPresentation()
        {
            visibleInteriorChunks.Clear(); visibleInteriorItems.Clear(); visibleInteriorEngines.Clear();
        }

        private void SynchronizeShipInteriorPresentation(ShipState ship, bool visible)
        {
            if (!TryGetShipInteriorWorld(ship.ShipId, out ShipInteriorWorldRuntime world)) return;
            Vector2 origin = ProjectShipInteriorPosition(ship, Vector2.zero);
            Vector2 axisX = ProjectShipInteriorPosition(ship, Vector2.right) - origin;
            Vector2 axisY = ProjectShipInteriorPosition(ship, Vector2.up) - origin;
            Matrix4x4 projection = Matrix4x4.identity;
            projection.m00 = axisX.x; projection.m10 = axisX.y;
            projection.m01 = axisY.x; projection.m11 = axisY.y;
            projection.m03 = origin.x; projection.m13 = origin.y;
            WorldPresentationTransformRegistry.Set(world.WorldKey, projection, visible);
            using var scope = GameplayWorldRegistry.UseWorld(world.WorldKey);
            foreach (var address in world.ChunkAddresses)
            {
                if (!world.Chunks.TryGetRuntimeChunkView(address, out ChunkView view)) continue;
                visibleInteriorChunks.Add(view);
                if (!interiorChunkProjections.TryGetValue(view, out WorldPresentationProjection display) || display == null)
                {
                    display = WorldPresentationProjection.Create(view.transform, transform);
                    interiorChunkProjections[view] = display;
                    interiorChunkRenderers[view] = view.GetComponentInChildren<ChunkTilemapRenderer>(true);
                }
                display.Synchronize(projection, visible);
                if (interiorChunkRenderers.TryGetValue(view, out ChunkTilemapRenderer renderer) && renderer != null)
                    ChunkBatchRendererGroupService.SetOwnerExternalProjection(renderer, projection, visible);
            }
            GameplayWorldRegistry.CopyWorldItems(world.WorldKey, interiorWorldItems);
            foreach (Item item in interiorWorldItems)
            {
                if (item == null || item is Player || item.InHand) continue;
                visibleInteriorItems.Add(item);
                if (!interiorItemProjections.TryGetValue(item, out WorldPresentationProjection display) || display == null)
                    interiorItemProjections[item] = display = WorldPresentationProjection.Create(item.transform, transform);
                display.Synchronize(projection, visible);
            }
            if (!ship.IsWreck)
                foreach (ShipPieceState piece in ship.Pieces)
                {
                    if (!piece.IsAlive) continue;
                    if (piece.Kind == ShipPieceKind.Engine) SynchronizeInteriorEngineExhaust(piece, projection, visible);
                }
        }

        private void SynchronizeInteriorEngineExhaust(ShipPieceState piece, Matrix4x4 projection, bool visible)
        {
            visibleInteriorEngines.Add(piece.PieceId);
            if (!interiorEngineExhausts.TryGetValue(piece.PieceId, out ShipEngineExhaustPresenter exhaust) || exhaust == null)
            {
                var root = new GameObject("ShipEngineExhaustAnchor");
                root.transform.SetParent(transform, false);
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
            ShipState ship = GetShipForPiece(piece);
            Vector3 position = projection.MultiplyPoint3x4(ShipInteriorPiecePosition(ship, piece));
            exhaust.transform.SetPositionAndRotation(position, projection.rotation * Quaternion.Euler(0f, 0f, piece.QuarterTurns * 90f));
            if (exhaust.gameObject.activeSelf != visible) exhaust.gameObject.SetActive(visible);
        }

        private ShipState GetShipForPiece(ShipPieceState piece)
        {
            if (!FindPiece(piece.PieceId, out ShipState ship, out _))
                throw new System.InvalidOperationException("引擎缺少船体归属：" + piece.PieceId);
            return ship;
        }

        private void EndShipInteriorPresentation()
        {
            expiredInteriorChunks.Clear();
            foreach (ChunkView view in interiorChunkProjections.Keys)
                if (view == null || !visibleInteriorChunks.Contains(view)) expiredInteriorChunks.Add(view);
            foreach (ChunkView view in expiredInteriorChunks)
            {
                if (interiorChunkRenderers.Remove(view, out ChunkTilemapRenderer renderer) && renderer != null)
                    ChunkBatchRendererGroupService.SetOwnerExternalProjection(renderer, Matrix4x4.identity, false);
                if (interiorChunkProjections.Remove(view, out WorldPresentationProjection display) && display != null)
                    Destroy(display.gameObject);
            }
            expiredInteriorItems.Clear();
            foreach (Item item in interiorItemProjections.Keys)
                if (item == null || !visibleInteriorItems.Contains(item)) expiredInteriorItems.Add(item);
            foreach (Item item in expiredInteriorItems)
                if (interiorItemProjections.Remove(item, out WorldPresentationProjection display) && display != null)
                    Destroy(display.gameObject);
            expiredInteriorEngines.Clear();
            foreach (string id in interiorEngineExhausts.Keys)
                if (!visibleInteriorEngines.Contains(id)) expiredInteriorEngines.Add(id);
            foreach (string id in expiredInteriorEngines)
                if (interiorEngineExhausts.Remove(id, out ShipEngineExhaustPresenter exhaust) && exhaust != null)
                    Destroy(exhaust.gameObject);
        }

        private void HideShipInteriorPresentation()
        {
            foreach (ShipInteriorWorldRuntime world in shipInteriorWorlds.Values)
                WorldPresentationTransformRegistry.Set(world.WorldKey, Matrix4x4.identity, false);
            foreach (WorldPresentationProjection display in interiorChunkProjections.Values)
                if (display != null) display.gameObject.SetActive(false);
            foreach (ChunkTilemapRenderer renderer in interiorChunkRenderers.Values)
                if (renderer != null) ChunkBatchRendererGroupService.SetOwnerExternalProjection(renderer, Matrix4x4.identity, false);
            foreach (WorldPresentationProjection display in interiorItemProjections.Values)
                if (display != null) display.gameObject.SetActive(false);
            foreach (ShipEngineExhaustPresenter exhaust in interiorEngineExhausts.Values)
                if (exhaust != null) exhaust.gameObject.SetActive(false);
        }

        internal void ReleaseAllShipInteriorPresentations()
        {
            HideShipInteriorPresentation();
            foreach (WorldPresentationProjection display in interiorChunkProjections.Values)
                if (display != null) Destroy(display.gameObject);
            foreach (WorldPresentationProjection display in interiorItemProjections.Values)
                if (display != null) Destroy(display.gameObject);
            foreach (ShipEngineExhaustPresenter exhaust in interiorEngineExhausts.Values)
                if (exhaust != null) Destroy(exhaust.gameObject);
            interiorChunkProjections.Clear(); interiorChunkRenderers.Clear();
            interiorItemProjections.Clear(); interiorEngineExhausts.Clear();
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
            EndShipInteriorPresentation();
            RemoveExpiredPooledShipPresenters();
            RemoveExpiredShipColliderPresenters();
            RemoveExpiredSurfaceShadows();
            SynchronizeRemotePassengerSurfaceVisuals();
            Player player = ItemMgr.Instance?.User_Player;
            if (player == null) return;
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
        }

        private void RemovePieceView(string id)
        {
            if (int.TryParse(id, out int guid) && ItemMgr.GetExistingInstance() is ItemMgr manager &&
                manager.WorldRunTimeItems.TryGetValue(guid, out Item item) && item != null &&
                interiorItemProjections.Remove(item, out WorldPresentationProjection display) && display != null)
                Destroy(display.gameObject);
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
                        !interiorChunkRenderers.TryGetValue(view, out ChunkTilemapRenderer renderer) || renderer == null) continue;
                    renderer.CollectVisualSnapshots(matrix, snapshots);
                }
            }
        }
        #endregion
    }
}
