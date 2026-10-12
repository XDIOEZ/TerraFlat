using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 多星区数据目录与单时钟
        private readonly Dictionary<SpaceSectorId, UniverseSimulation> sectorDirectory = new();
        private readonly SpaceSectorStreaming sectorStreaming = new();
        public IReadOnlyCollection<SpaceSectorId> RequiredSectors => sectorStreaming.Required;
        private void RebuildSectorDirectory()
        {
            sectorDirectory.Clear();
            foreach (UniverseState sector in State.Sectors)
            {
                SpaceCatalog.LoadDefault().RebindUniverse(sector);
                if (sectorDirectory.ContainsKey(sector.Sector)) throw new InvalidOperationException("太空存档存在重复星区。");
                sector.SimulationSeconds = State.SimulationSeconds;
                sectorDirectory.Add(sector.Sector, new UniverseSimulation(sector));
            }
            Universe = GetUniverse(default);
            State.Universe = Universe.State;
        }
        public UniverseSimulation GetUniverse(SpaceSectorId sector)
        {
            if (sectorDirectory.TryGetValue(sector, out UniverseSimulation existing)) return existing;
            UniverseState data = SpaceCatalog.LoadDefault().GenerateSector(owner.Seed, sector);
            data.SimulationSeconds = State.SimulationSeconds;
            var simulation = new UniverseSimulation(data);
            State.Sectors.Add(data); sectorDirectory.Add(sector, simulation);
            RegisterSectorPlanets(data, null);
            return simulation;
        }
        private void RegisterSectorPlanets(UniverseState sector, PlanetData template)
        {
            SpaceCatalog catalog = SpaceCatalog.LoadDefault();
            foreach (BodyState body in sector.Bodies)
            {
                if (!owner.PlanetData_Dict.TryGetValue(body.PlanetId, out PlanetData planet))
                    owner.PlanetData_Dict[body.PlanetId] = planet = catalog.CreatePlanetData(body.BodyId, template);
                catalog.ApplyPlanetMetadata(body, planet);
                planet.BodyId = body.BodyId; planet.Name = body.PlanetId;
            }
        }
        public bool TryGetBody(string bodyId, out BodyState body)
        {
            body = null;
            if (string.IsNullOrWhiteSpace(bodyId)) return false;
            if (!SpaceSectorCoordinates.TryParseIdentity(bodyId, out _, out _))
                return Universe != null && Universe.TryGetBody(bodyId, out body);
            foreach (UniverseSimulation simulation in sectorDirectory.Values)
                if (simulation.TryGetBody(bodyId, out body)) return true;
            return false;
        }
        public BodyState GetBody(string bodyId) => TryGetBody(bodyId, out BodyState body) ? body :
            throw new KeyNotFoundException("星体不存在：" + bodyId);
        private void AdvanceSectorTime(double seconds)
        {
            State.SimulationSeconds += seconds;
            foreach (UniverseSimulation simulation in sectorDirectory.Values) simulation.AdvanceTime(seconds);
        }
        // 移动只积一次，跨边界沿线的星体接触仍按最早接触时间结算。
        private bool StepSectorMotion(SpaceMotionState motion, double seconds, out SurfaceContact contact)
        {
            SpaceVector2 previous = motion.PositionMeters;
            double start = State.SimulationSeconds - seconds;
            GetUniverse(motion.Sector).StepFreeMotion(motion, default, seconds);
            bool touched = SweepSectorSurface(motion.Sector, previous, motion.PositionMeters,
                motion.VelocityMetersPerSecond, motion.VelocityMetersPerSecond, motion.RadiusMeters, start, seconds, out contact);
            if (touched)
                motion.PositionMeters = SpaceSectorCoordinates.Relative(contact.Sector, contact.PositionMeters, motion.Sector);
            return touched;
        }
        public bool SweepSectorSurface(SpaceSectorId sector, SpaceVector2 from, SpaceVector2 to,
            SpaceVector2 before, SpaceVector2 after, double radius, double start, double seconds, out SurfaceContact contact)
        {
            contact = default;
            SpaceSectorPosition end = SpaceSectorCoordinates.Normalize(sector, to);
            GetUniverse(sector); GetUniverse(end.Sector);
            foreach (SpaceSectorId configured in SpaceCatalog.LoadDefault().ConfiguredSectors)
            {
                SpaceVector2 a = SpaceSectorCoordinates.Relative(sector, from, configured);
                SpaceVector2 b = SpaceSectorCoordinates.Relative(sector, to, configured);
                double half = SpaceSectorCoordinates.SizeMeters * .5d + radius;
                if (Math.Min(a.X, b.X) <= half && Math.Max(a.X, b.X) >= -half &&
                    Math.Min(a.Y, b.Y) <= half && Math.Max(a.Y, b.Y) >= -half) GetUniverse(configured);
            }
            bool found = false;
            foreach (UniverseSimulation simulation in sectorDirectory.Values)
                if (simulation.TrySweepSurfaceContact(sector, from, to, before, after, radius, start, seconds, out SurfaceContact candidate) &&
                    (!found || candidate.Fraction < contact.Fraction))
                { contact = candidate; found = true; }
            return found;
        }
        private void NormalizeSectorOwnership()
        {
            foreach (ShipState ship in State.Ships)
            {
                ShipFlightState flight = GetFlight(ship.ShipId);
                if (flight?.Phase != ShipFlightPhase.Cruising) continue;
                SpaceSectorPosition position = SpaceSectorCoordinates.Normalize(ship.Sector, new SpaceVector2(ship.PositionX, ship.PositionY));
                ship.Sector = position.Sector; ship.PositionX = position.LocalMeters.X; ship.PositionY = position.LocalMeters.Y;
                flight.Motion.Sector = ship.Sector; flight.Motion.PositionMeters = position.LocalMeters;
                GetUniverse(ship.Sector);
            }
            foreach (SpacePassengerState passenger in State.Passengers)
            {
                if (passenger.Supported && GetShip(passenger.ShipId) is ShipState carrier)
                    passenger.FreeMotion.Sector = carrier.Sector;
                else if (passenger.IsInSpace && !passenger.LandingResolved)
                {
                    SpaceSectorPosition position = SpaceSectorCoordinates.Normalize(passenger.FreeMotion.Sector, passenger.FreeMotion.PositionMeters);
                    passenger.FreeMotion.Sector = position.Sector; passenger.FreeMotion.PositionMeters = position.LocalMeters;
                    GetUniverse(position.Sector);
                }
            }
            foreach (SpaceLooseItemState loose in State.LooseItems)
                if (loose.InSpace)
                {
                    SpaceSectorPosition position = SpaceSectorCoordinates.Normalize(loose.Motion.Sector, loose.Motion.PositionMeters);
                    loose.Motion.Sector = position.Sector; loose.Motion.PositionMeters = position.LocalMeters;
                    loose.WorldKey = SpaceSectorCoordinates.SceneKey(position.Sector);
                    GetUniverse(position.Sector);
                }
        }
        #endregion

        #region 本机投影与场景租约
        public SpaceSectorPosition GetPassengerPosition(SpacePassengerState passenger)
        {
            ShipState ship = GetShip(passenger.ShipId);
            if (passenger.Supported && ship != null)
            {
                ShipGeometry.LocalToWorld(ship, passenger.LocalX, passenger.LocalY, out double x, out double y);
                return SpaceSectorCoordinates.Normalize(ship.Sector, new SpaceVector2(x, y));
            }
            return SpaceSectorCoordinates.Normalize(passenger.FreeMotion.Sector, passenger.FreeMotion.PositionMeters);
        }
        private void UpdateProjection(SpaceSectorPosition position)
        {
            if (!position.LocalMeters.IsFinite) return;
            // 换归属先保留同一显示平面，只有精度需要时才整体平移并通知镜头。
            if (ViewSector != position.Sector)
            {
                ViewOrigin = SpaceSectorCoordinates.Relative(ViewSector, ViewOrigin, position.Sector);
                ViewSector = position.Sector;
            }
            SpaceVector2 delta = position.LocalMeters - ViewOrigin;
            if (delta.SqrMagnitude < 4096d * 4096d) return;
            ViewOrigin = position.LocalMeters;
            Vector2 shift = -delta.ToVector2();
            Player player = ItemMgr.Instance?.User_Player;
            if (player != null)
            {
                player.transform.position += (Vector3)shift;
                Rigidbody2D body = player.GetComponent<Rigidbody2D>();
                if (body != null) body.position = player.transform.position;
                if (drivingCameraTarget != null) drivingCameraTarget.position += (Vector3)shift;
                player.itemMods.GetMod_ByID<Mod_Cam>(ModText.Camera)?.ShiftSpacePresentation(shift);
            }
        }
        private void UpdateSectorStreaming()
        {
            sectorStreaming.BeginFrame();
            Player local = ItemMgr.Instance?.User_Player;
            foreach (SpacePassengerState passenger in State.Passengers)
            {
                if (GameNetwork.IsOnline && GameNetwork.HasStateAuthority && ItemMgr.Instance?.Player_DIC.ContainsKey(passenger.ProfileId) != true) continue;
                if (!TryGetNetworkObserver(passenger.ProfileId, out SpaceNetworkObserver observer) || !observer.InSpace) continue;
                if (!GameNetwork.HasStateAuthority && local?.ProfileName != passenger.ProfileId) continue;
                if (!GameNetwork.IsOnline && local?.ProfileName != passenger.ProfileId) continue;
                sectorStreaming.Observe(passenger.ProfileId, observer.Position, observer.Velocity);
            }
            sectorStreaming.EndFrame();
            foreach (SpaceSectorId sector in sectorStreaming.Required) GetUniverse(sector);
            if (SpaceMgr.ExistingInstance != null) SpaceMgr.ExistingInstance.UpdateSectorLeases(this, sectorStreaming.Required);
        }
        public bool TryGetNetworkObserver(string profileId, out SpaceNetworkObserver observer)
        {
            observer = default;
            SpacePassengerState passenger = State?.Passengers.Find(value => value.ProfileId == profileId);
            if (passenger == null) return false;
            observer.RealmRevision = passenger.RealmRevision;
            ShipState ship = passenger.Supported ? GetShip(passenger.ShipId) : null;
            ShipFlightState flight = ship == null ? null : GetFlight(ship.ShipId);
            observer.InSpace = flight != null ? flight.Phase == ShipFlightPhase.Cruising : passenger.IsInSpace && !passenger.LandingResolved;
            observer.Position = GetPassengerPosition(passenger);
            if (!observer.InSpace && passenger.SurfaceDescending)
                observer.Position = new SpaceSectorPosition(GetBody(passenger.LandingBodyId).Sector, new SpaceVector2(passenger.LandingX, passenger.LandingY));
            observer.Velocity = ship != null ? new SpaceVector2(ship.VelocityX, ship.VelocityY) : passenger.FreeMotion.VelocityMetersPerSecond;
            observer.ShipId = ship?.ShipId;
            observer.WorldKey = observer.InSpace ? SpaceSectorCoordinates.SceneKey(observer.Position.Sector)
                : flight?.SurfaceWorldKey ?? passenger.LandingWorldKey;
            return true;
        }
        #endregion
    }
}
