using System;
using System.Collections.Generic;
using System.Globalization;
using MemoryPack;
using Newtonsoft.Json;
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
        public bool IsSpaceView => SceneManager.GetActiveScene().name == "SpaceScene";
        private GameSaveData owner;
        private bool running;
        private IDisposable placementRegistration;
        private IDisposable dropRegistration;
        private readonly Dictionary<string, ItemData> contents = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ShipPieceView> views = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ShipControlRequest> controlRequests = new(StringComparer.Ordinal);
        private readonly List<string> destroyed = new();
        private float environmentElapsed;

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
            ReleaseViews();
            contents.Clear(); controlRequests.Clear();
            owner = save;
            State = string.IsNullOrWhiteSpace(save.SpaceStateJson)
                ? new SpaceSessionState { Universe = SpaceCatalog.LoadDefault().Generate(save.Seed) }
                : JsonConvert.DeserializeObject<SpaceSessionState>(save.SpaceStateJson);
            if (State == null || State.Version != 1 || State.Universe == null)
                throw new InvalidOperationException("太空存档格式无效。");
            Universe = new UniverseSimulation(State.Universe);
            var earth = Universe.GetBody("earth");
            PlanetData template = GameManager.Instance?.ReadyPlanetData;
            if (string.IsNullOrWhiteSpace(save.SpaceStateJson) && template != null && !string.IsNullOrWhiteSpace(template.Name))
                earth.PlanetId = template.Name;
            foreach (var body in State.Universe.Bodies)
            {
                if (!save.PlanetData_Dict.TryGetValue(body.PlanetId, out PlanetData planet))
                    save.PlanetData_Dict[body.PlanetId] = planet = SpaceCatalog.LoadDefault().CreatePlanetData(body.BodyId, template);
                SpaceCatalog.LoadDefault().ApplyPlanetMetadata(body, planet);
                planet.BodyId = body.BodyId;
                planet.Name = body.PlanetId;
            }
            foreach (ShipState ship in State.Ships)
                foreach (ShipPieceState piece in ship.Pieces)
                    if (piece.IsAlive && !string.IsNullOrWhiteSpace(piece.SnapshotJson)) contents[piece.PieceId] = Decode(piece.SnapshotJson);
            running = true;
            ViewOrigin = default;
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
            Current.ReleaseViews();
            Current.owner = null;
            Current.State = null;
            Current.Universe = null;
            Current.contents.Clear();
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
            if (!running || transitionRunning || State == null || Universe == null || GameManager.Instance?.IsGameplayReady != true || Time.timeScale <= 0f || !GameNetwork.HasStateAuthority) return;
            if (ResumePendingPassengerTransfer()) return;
            double seconds = Time.fixedDeltaTime;
            Universe.AdvanceTime(seconds);
            foreach (ShipState ship in State.Ships.ToArray()) MachineWorld.TickShip(ship.ShipId, (float)seconds);
            RefreshMass();
            CaptureShipContactPoses();
            var assemblies = ShipDockingService.RebuildAssemblies(State.Ships, State.Docking);
            foreach (ShipAssemblyState assembly in assemblies) StepAssembly(assembly, seconds);
            StepShipContacts(seconds);
            StepFreePassengers(seconds);
            StepLooseItems(seconds);
            CommitSurfaceContacts(seconds);
            environmentElapsed += (float)seconds;
            if (environmentElapsed >= .2f)
            {
                float elapsed = environmentElapsed; environmentElapsed = 0f;
                foreach (ShipState ship in State.Ships.ToArray())
                {
                    var outside = GetOutside(ship);
                    ShipAtmosphereService.StepStructureEnvironment(ship, outside.PressureKPa, outside.TemperatureCelsius, elapsed, DestroyPiece);
                    RefillCompartments(ship, elapsed);
                }
                ProcessStructureChanges();
            }
            StepExplosions();
        }
        private void LateUpdate()
        {
            if (!running || State == null) return;
            UpdateViewOrigin();
            SynchronizeViews();
            SynchronizeLooseItems();
            ShipPanelSession.RefreshOpen(this);
            SpaceLandingPanelSession.Refresh(this, ItemMgr.Instance?.User_Player);
        }
        public ShipState GetShip(string id) => State?.Ships.Find(value => value.ShipId == id);
        public ShipFlightState GetFlight(string id) => State?.Flights.Find(value => value.ShipId == id);
        public ShipDeviceState GetDevice(string id) => State?.Devices.Find(value => value.PieceId == id);
        public bool Owns(int guid) => FindPiece(guid.ToString(CultureInfo.InvariantCulture), out _, out _);
        public bool FindPiece(string id, out ShipState ship, out ShipPieceState piece)
        {
            if (State != null) foreach (ShipState candidate in State.Ships)
            {
                var match = candidate.Pieces.Find(value => value.PieceId == id && value.IsAlive);
                if (match != null) { ship = candidate; piece = match; return true; }
            }
            ship = null; piece = null; return false;
        }
        public ItemData GetPieceData(ShipPieceState piece) => contents.TryGetValue(piece.PieceId, out ItemData data) ? data : null;
        public Vector2 ProjectPosition(SpaceVector2 absolute) => new((float)(absolute.X - ViewOrigin.X), (float)(absolute.Y - ViewOrigin.Y));
        public SpaceVector2 UnprojectPosition(Vector2 display) => new(display.x + ViewOrigin.X, display.y + ViewOrigin.Y);
        public Vector2 DisplayPoint(ShipState ship, double localX, double localY)
        {
            ShipGeometry.LocalToWorld(ship, localX, localY, out double x, out double y);
            return IsSpaceView ? ProjectPosition(new SpaceVector2(x, y)) : WorldLocalPresentation.ProjectPosition(new Vector2((float)x, (float)y));
        }
        public bool IsVisible(ShipState ship)
        {
            ShipFlightState flight = GetFlight(ship.ShipId);
            return flight != null && (IsSpaceView ?
                (flight.Phase is ShipFlightPhase.Orbit or ShipFlightPhase.Descending) &&
                    (new SpaceVector2(ship.PositionX, ship.PositionY) - ViewOrigin).Magnitude < 1200d
                : flight.SurfaceWorldKey == SceneManager.GetActiveScene().name &&
                    flight.Phase is ShipFlightPhase.Landed or ShipFlightPhase.Floating or ShipFlightPhase.Lifting or ShipFlightPhase.SurfaceDescending);
        }
        private void UpdateViewOrigin()
        {
            if (!IsSpaceView) return;
            Player player = ItemMgr.Instance?.User_Player;
            SpacePassengerState passenger = player != null ? GetPassenger(player) : null;
            if (passenger == null) return;
            ShipState ship = GetShip(passenger.ShipId);
            SpaceVector2 point = ship != null ? new SpaceVector2(ship.PositionX, ship.PositionY) : passenger.FreeMotion.PositionMeters;
            if ((point - ViewOrigin).Magnitude > 5000d) ViewOrigin = point;
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
