using System;
using System.Collections.Generic;
using System.Globalization;
using MemoryPack;
using Newtonsoft.Json;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using FlatWorld.Networking;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession : MonoBehaviour, IItemEnvironmentSource
    {
        #region 会话与持久化
        public static SpaceSession Current { get; private set; }
        public SpaceSessionState State { get; private set; }
        public UniverseSimulation Universe { get; private set; }
        public SpaceVector2 ViewOrigin { get; private set; }
        public SpaceSectorId ViewSector { get; private set; }
        public bool IsSpaceView => SpaceMgr.IsSpaceScene(SceneManager.GetActiveScene().name);
        private GameSaveData owner;
        private bool running;
        private IDisposable placementRegistration;
        private IDisposable dropRegistration;
        private readonly Dictionary<string, ItemData> contents = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ShipPieceView> views = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ShipControlRequest> controlRequests = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (ShipState Ship, ShipPieceState Piece)> pieceIndex = new(StringComparer.Ordinal);
        private bool pieceIndexDirty;
        private readonly List<string> destroyed = new();
        private readonly List<ShipState> fixedShips = new();
        private readonly ShipAssemblyCache simulationAssemblies = new();
        private readonly ShipAssemblyCache queryAssemblies = new();
        private float environmentElapsed;

        private static readonly ProfilerMarker ShipMachineMarker = new("SpaceSession.Fixed.Machines");
        private static readonly ProfilerMarker ShipMassMarker = new("SpaceSession.Fixed.Mass");
        private static readonly ProfilerMarker ShipMotionMarker = new("SpaceSession.Fixed.Motion");
        private static readonly ProfilerMarker ShipContactMarker = new("SpaceSession.Fixed.Contacts");
        private static readonly ProfilerMarker FreePassengerMarker = new("SpaceSession.Fixed.FreePassengers");
        private static readonly ProfilerMarker LooseItemMarker = new("SpaceSession.Fixed.LooseItems");
        private static readonly ProfilerMarker ShipEnvironmentMarker = new("SpaceSession.Fixed.Environment");
        private static readonly ProfilerMarker ShipViewMarker = new("SpaceSession.Late.Pieces");

        public static SpaceSession EnsureLoaded()
        {
            if (Current == null)
            {
                var root = new GameObject("SpaceSession");
                Current = root.AddComponent<SpaceSession>();
                DontDestroyOnLoad(root);
            }
            Current.Load(SaveDataMgr.Instance?.SaveData);
            return Current;
        }
        private void Awake()
        {
            Current = this;
            RegisterShipDamageFeedback();
            ItemEnvironmentSources.Register(this);
            GameplayCombatBridge.Register(ShipCombatBridge.Instance);
            PressureExplosionQueue.WorldHandler = HandleShipExplosion;
            PressureExplosionQueue.SnapshotHandler = CaptureShipExplosionContext;
            PressureExplosionQueue.PresentationHandler = PresentShipExplosion;
            ItemMgr.RuntimeItemDespawning += OnLooseItemDespawning;
            dropRegistration = RegisterDroppedItemProvider();
            placementRegistration = FlatWorld.Gameplay.Building.BuildingPlacementService.Register(
                "ship-local-grid", new ShipBuildingPlacementBackend(), 300);
        }
        private void OnDestroy()
        {
            ResetSpacePhysics();
            ReleaseShipColliderPresenters();
            gravitySources.Clear();
            ReleaseSurfaceShadows();
            UnregisterShipDamageFeedback();
            DestroyDrivingCameraTarget();
            placementRegistration?.Dispose();
            dropRegistration?.Dispose();
            ItemMgr.RuntimeItemDespawning -= OnLooseItemDespawning;
            ItemEnvironmentSources.Unregister(this);
            GameplayCombatBridge.Unregister(ShipCombatBridge.Instance);
            if (PressureExplosionQueue.WorldHandler == HandleShipExplosion) PressureExplosionQueue.WorldHandler = null;
            if (PressureExplosionQueue.SnapshotHandler == CaptureShipExplosionContext) PressureExplosionQueue.SnapshotHandler = null;
            if (PressureExplosionQueue.PresentationHandler == PresentShipExplosion) PressureExplosionQueue.PresentationHandler = null;
            if (Current == this) Current = null;
        }
        private void Load(GameSaveData save)
        {
            if (save == null || ReferenceEquals(owner, save)) return;
            ResetSpacePhysics();
            ReleaseViews();
            contents.Clear(); controlRequests.Clear();
            ClearQueryIndexes();
            engineOutputs.Clear();
            pieceIndex.Clear(); pieceIndexDirty = false;
            fixedShips.Clear();
            simulationAssemblies.Clear(); queryAssemblies.Clear();
            freeContactAssemblies.Clear();
            ResetShipContactWorkspace();
            pickingNavigationConsole = null;
            ResetMassWorkspace();
            viewBindingsDirty = false;
            owner = save;
            State = string.IsNullOrWhiteSpace(save.SpaceStateJson)
                ? new SpaceSessionState { Sectors = new List<UniverseState> { SpaceCatalog.LoadDefault().GenerateSector(save.Seed, default) } }
                : SaveDataMgr.DeserializeSpaceState(save.SpaceStateJson);
            if (State == null || State.Version != 2 || State.Sectors == null || State.Sectors.Count == 0)
                throw new InvalidOperationException("太空存档格式无效。");
            RebuildSectorDirectory();
            var earth = Universe.GetBody("earth");
            PlanetData template = GameManager.Instance?.ReadyPlanetData;
            if (string.IsNullOrWhiteSpace(save.SpaceStateJson) && template != null && !string.IsNullOrWhiteSpace(template.Name))
                earth.PlanetId = template.Name;
            foreach (UniverseState sector in State.Sectors) RegisterSectorPlanets(sector, template);
            foreach (ShipState ship in State.Ships)
                foreach (ShipPieceState piece in ship.Pieces)
                    if (piece.IsAlive && !string.IsNullOrWhiteSpace(piece.SnapshotJson)) contents[piece.PieceId] = Decode(piece.SnapshotJson);
            RebuildPieceIndex();
            running = true;
            ViewOrigin = default;
            ViewSector = default;
        }
        public void SetRunning(bool value) => running = value;
        public static void Capture(GameSaveData save)
        {
            if (Current == null || !ReferenceEquals(Current.owner, save)) return;
            Current.CaptureContents();
            Current.CaptureLooseItems();
            save.SpaceStateJson = JsonConvert.SerializeObject(Current.State);
        }
        public static void EndSession()
        {
            if (Current == null) return;
            Current.running = false;
            Current.ResetSpacePhysics();
            Current.ReleaseViews();
            Current.DestroyDrivingCameraTarget();
            Current.owner = null;
            Current.State = null;
            Current.Universe = null;
            Current.sectorDirectory.Clear();
            Current.passengerInputs.Clear();
            ClientAttackHeld = false;
            Current.sectorStreaming.Clear();
            Current.contents.Clear();
            Current.ClearQueryIndexes();
            Current.pieceIndex.Clear();
            Current.pieceIndexDirty = false;
            Current.fixedShips.Clear();
            Current.simulationAssemblies.Clear(); Current.queryAssemblies.Clear();
            Current.freeContactAssemblies.Clear();
            Current.ResetShipContactWorkspace();
            Current.controlRequests.Clear(); Current.pickingNavigationConsole = null;
            Current.engineOutputs.Clear();
            Current.ResetMassWorkspace();
            Current.viewBindingsDirty = false;
            SpaceSurfaceQuery.Reset();
            SpaceLandingPanelSession.Refresh(null, null);
        }
        public static string Encode(ItemData data) => Convert.ToBase64String(ItemSnapshotSerialization.SerializePayload(data.InstanceSnapshot));
        public static ItemData Decode(string encoded)
        {
            ItemData data = MemoryPackSerializer.Deserialize<ItemInstanceSnapshot>(Convert.FromBase64String(encoded)).CreateColdData();
            ItemDefinitionRuntime.RebasePersistedData(GameRes.ExistingInstance, data);
            return data;
        }
        private void CaptureContents()
        {
            foreach (ShipState ship in State.Ships)
            using (MachineWorld.UseScope("ship:" + ship.ShipId))
            foreach (ShipPieceState piece in ship.Pieces)
            {
                if (!piece.IsAlive) continue;
                if (int.TryParse(piece.PieceId, out int id) && MachineWorld.GetById(id) is MachineEntity node)
                    contents[piece.PieceId] = MachineWorld.CaptureSnapshot(node);
                else if (views.TryGetValue(piece.PieceId, out ShipPieceView view) && view.Item != null)
                { view.Item.Save(); contents[piece.PieceId] = view.Item.itemData; }
                if (contents.TryGetValue(piece.PieceId, out ItemData data)) piece.SnapshotJson = Encode(data);
            }
        }
        #endregion

        #region 唯一固定模拟时钟
        private void FixedUpdate()
        {
            if (!running || State == null || Universe == null || GameManager.Instance?.IsGameplayReady != true || Time.timeScale <= 0f || !GameNetwork.HasStateAuthority) return;
            ResumePendingPassengerTransfer();
            double seconds = Time.fixedDeltaTime;
            engineOutputs.Clear();
            AdvanceSectorTime(seconds);
            StepRemotePassengerInput(seconds);
            fixedShips.Clear();
            fixedShips.AddRange(State.Ships);
            using (ShipMachineMarker.Auto())
            {
                MachineWorld.TickBackgroundWorlds((float)seconds);
                foreach (ShipState ship in fixedShips)
                    if (!ship.IsWreck) MachineWorld.TickShip(ship.ShipId, (float)seconds);
            }

            // 视觉升空仍推进高度与燃料，但不触发每帧的全船质量重算和动态碰撞。
            bool hasMovingShips = false;
            bool hasPhysicalShips = false;
            foreach (ShipFlightState flight in State.Flights)
            {
                if (flight.Phase is ShipFlightPhase.Landed or ShipFlightPhase.Floating) continue;
                hasMovingShips = true;
                if (flight.Phase != ShipFlightPhase.Lifting) hasPhysicalShips = true;
                if (hasPhysicalShips) break;
            }

            if (hasPhysicalShips)
                using (ShipMassMarker.Auto()) RefreshMass();
            CaptureShipContactPoses();
            List<ShipAssemblyState> assemblies = simulationAssemblies.Refresh(State.Ships, State.Docking);
            if (hasMovingShips)
            {
                using (ShipMotionMarker.Auto())
                {
                    foreach (ShipAssemblyState assembly in assemblies) StepAssembly(assembly, seconds);
                }
                if (assemblies.Count > 1 && hasPhysicalShips)
                    using (ShipContactMarker.Auto()) StepShipContacts(seconds, assemblies);
            }
            using (FreePassengerMarker.Auto())
                StepFreePassengers(seconds);
            using (LooseItemMarker.Auto()) StepLooseItems(seconds);
            using (ShipContactMarker.Auto()) StepSpacePhysics(assemblies, seconds);
            CommitSurfaceContacts(seconds);
            NormalizeSectorOwnership();
            environmentElapsed += (float)seconds;
            if (environmentElapsed >= .2f)
            {
                float elapsed = environmentElapsed; environmentElapsed = 0f;
                using (ShipEnvironmentMarker.Auto())
                {
                    foreach (ShipState ship in fixedShips)
                    {
                        if (ship.IsWreck) continue;
                        var outside = GetOutside(ship);
                        ShipAtmosphereService.StepStructureEnvironment(ship, outside.PressureKPa, outside.TemperatureCelsius, elapsed, DestroyPiece);
                        RefillCompartments(ship, elapsed);
                    }
                    ProcessStructureChanges();
                }
            }
            StepExplosions();
        }
        private void LateUpdate()
        {
            if (!running || State == null) return;
            UpdateViewOrigin();
            UpdateSectorStreaming();
            FlushShipDamageFeedback();
            // 星球、船体和玩家在同一 LateUpdate 使用同一个浮动原点。
            if (IsSpaceView && SpaceMgr.ExistingInstance != null) SpaceMgr.ExistingInstance.TickRuntimePlanets(0f);
            using (ShipViewMarker.Auto()) SynchronizeViews();
            SynchronizeLooseItems();
            ShipPanelSession.RefreshOpen(this);
            SpaceLandingPanelSession.Refresh(this, ItemMgr.Instance?.User_Player);
        }
        public ShipState GetShip(string id)
            => shipQueryIndex.Find(State?.Ships, id);
        public ShipFlightState GetFlight(string id)
            => flightQueryIndex.Find(State?.Flights, id);
        public ShipDeviceState GetDevice(string id)
            => deviceQueryIndex.Find(State?.Devices, id);
        public bool Owns(int guid) => FindPiece(guid.ToString(CultureInfo.InvariantCulture), out _, out _);
        public bool FindPiece(string id, out ShipState ship, out ShipPieceState piece)
        {
            if (!pieceIndexDirty && !string.IsNullOrEmpty(id) &&
                pieceIndex.TryGetValue(id, out var indexed) && indexed.Piece.IsAlive)
            {
                ship = indexed.Ship; piece = indexed.Piece; return true;
            }
            // 结构分裂事务中集合正在迁移，临时按当前权威状态查找。
            if (pieceIndexDirty && State != null)
                foreach (ShipState candidate in State.Ships)
                    foreach (ShipPieceState current in candidate.Pieces)
                        if (current.IsAlive && current.PieceId == id)
                        { ship = candidate; piece = current; return true; }
            ship = null; piece = null; return false;
        }

        private void RegisterPieceIndex(ShipState ship, ShipPieceState piece)
        {
            if (!pieceIndexDirty && piece.IsAlive)
                pieceIndex.Add(piece.PieceId, (ship, piece));
        }

        private void RebuildPieceIndex()
        {
            pieceIndex.Clear();
            if (State != null)
                foreach (ShipState ship in State.Ships)
                    foreach (ShipPieceState piece in ship.Pieces)
                        if (piece.IsAlive) pieceIndex.Add(piece.PieceId, (ship, piece));
            pieceIndexDirty = false;
        }
        public ItemData GetPieceData(ShipPieceState piece) => contents.TryGetValue(piece.PieceId, out ItemData data) ? data : null;
        public Vector2 ProjectPosition(SpaceSectorPosition position)
            => (SpaceSectorCoordinates.Relative(position, ViewSector) - ViewOrigin).ToVector2();
        public SpaceSectorPosition UnprojectSectorPosition(Vector2 display)
            => SpaceSectorCoordinates.Normalize(ViewSector, UnprojectPosition(display));
        public Vector2 ProjectPosition(SpaceVector2 absolute) => new((float)(absolute.X - ViewOrigin.X), (float)(absolute.Y - ViewOrigin.Y));
        public SpaceVector2 UnprojectPosition(Vector2 display) => new(display.x + ViewOrigin.X, display.y + ViewOrigin.Y);
        public Vector2 DisplayPoint(ShipState ship, double localX, double localY)
        {
            ShipGeometry.LocalToWorld(ship, localX, localY, out double x, out double y);
            return IsSpaceView ? ProjectPosition(new SpaceSectorPosition(ship.Sector, new SpaceVector2(x, y))) : WorldLocalPresentation.ProjectPosition(new Vector2((float)x, (float)y));
        }
        private bool IsInPresentationWorld(ShipState ship)
        {
            if (ship == null) return false;
            ShipFlightState flight = GetFlight(ship.ShipId);
            return flight != null && (IsSpaceView ?
                flight.Phase == ShipFlightPhase.Cruising
                : flight.SurfaceWorldKey == SceneManager.GetActiveScene().name &&
                    flight.Phase is ShipFlightPhase.Landed or ShipFlightPhase.Floating or ShipFlightPhase.Lifting or ShipFlightPhase.SurfaceDescending);
        }
        public bool IsVisible(ShipState ship)
        {
            if (!IsInPresentationWorld(ship)) return false;
            if (!IsSpaceView) return true;
            Player player = ItemMgr.Instance?.User_Player;
            SpacePassengerState observer = player != null ? State.Passengers.Find(value => value.ProfileId == player.ProfileName) : null;
            if (observer != null && observer.Supported && observer.ShipId == ship.ShipId) return true;
            // 浮动原点只保障坐标精度，可见距离必须围绕当前乘员的权威位置计算。
            SpaceSectorPosition focus = observer != null ? GetPassengerPosition(observer) : new SpaceSectorPosition(ViewSector, ViewOrigin);
            SpaceVector2 delta = SpaceSectorCoordinates.Relative(ship.Sector, new SpaceVector2(ship.PositionX, ship.PositionY), focus.Sector) - focus.LocalMeters;
            return delta.SqrMagnitude < 1200d * 1200d;
        }
        private void UpdateViewOrigin()
        {
            if (!IsSpaceView) return;
            Player player = ItemMgr.Instance?.User_Player;
            SpacePassengerState passenger = player != null ? GetPassenger(player) : null;
            if (passenger == null) return;
            UpdateProjection(GetPassengerPosition(passenger));
        }
        #endregion
    }
    public sealed class ShipControlRequest
    {
        public string ConsolePieceId;
        public Vector2 Move;
        public float Turn;
    }
}
