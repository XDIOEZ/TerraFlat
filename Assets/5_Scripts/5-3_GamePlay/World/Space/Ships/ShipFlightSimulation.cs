using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 升空与连续轨道运动
        public BodyState FindSurfaceBody(string worldKey)
        {
            if (Universe == null) return null;
            string planetId = WorldAddress.FromWorldKey(worldKey).PlanetId;
            return Universe.State.Bodies.Find(value => value.PlanetId == planetId);
        }
        private bool SameAssembly(string left, string right)
        {
            if (left == right) return true;
            foreach (ShipAssemblyState assembly in ShipDockingService.RebuildAssemblies(State.Ships, State.Docking))
                if (assembly.MemberShipIds.Contains(left) && assembly.MemberShipIds.Contains(right)) return true;
            return false;
        }
        public ShipAssemblyState GetConnectedAssembly(string shipId)
            => ShipDockingService.RebuildAssemblies(State.Ships, State.Docking).Find(value => value.MemberShipIds.Contains(shipId));
        public bool ToggleIgnition(string consoleId)
        {
            if (!FindPiece(consoleId, out ShipState ship, out ShipPieceState console) || console.Kind != ShipPieceKind.Console || !IsPowered(ship, console)) return false;
            var flight = GetFlight(ship.ShipId);
            if (flight == null) return false;
            var assembly = ShipDockingService.RebuildAssemblies(State.Ships, State.Docking).Find(value => value.MemberShipIds.Contains(ship.ShipId));
            CollectActuators(assembly);
            double thrust = 0d;
            foreach (ShipActuator engine in actuators)
                foreach (FluidInventory tank in fuelTanks)
                    if (ShipFuel.IsEligible(tank, engine.Device.Configuration)) { thrust += engine.Device.Configuration.EngineThrustNewtons; break; }
            double gravity = Universe.GetBody(flight.BodyId).SurfaceGravity;
            if (flight.Phase is ShipFlightPhase.Landed or ShipFlightPhase.Floating && thrust <= assembly.MassKg * gravity) return false;
            bool ignite = !flight.Ignited;
            BodyState sourceBody = Universe.GetBody(flight.BodyId);
            foreach (string id in assembly.MemberShipIds)
            {
                ShipFlightState member = GetFlight(id);
                member.Ignited = ignite;
                if (member.Ignited && member.Phase is ShipFlightPhase.Landed or ShipFlightPhase.Floating)
                {
                    member.Phase = ShipFlightPhase.Lifting; member.LandingResolved = false; member.LandingEventId = null;
                    member.SurfaceX = GetShip(id).PositionX; member.SurfaceY = GetShip(id).PositionY;
                    member.SurfaceAngle = member.SurfaceX / sourceBody.RadiusMeters + Universe.GetRotationRadians(sourceBody.BodyId);
                    member.EntrySurfaceVelocity = default;
                }
            }
            return true;
        }
        private void StepAssembly(ShipAssemblyState assembly, double seconds)
        {
            ShipState primary = GetShip(assembly.MemberShipIds[0]);
            ShipFlightState flight = GetFlight(primary.ShipId);
            if (flight == null) return;
            if (flight.Phase is ShipFlightPhase.Landed or ShipFlightPhase.Floating) return;
            if (flight.Phase is ShipFlightPhase.Lifting or ShipFlightPhase.SurfaceDescending)
            {
                StepLift(assembly, seconds); return;
            }
            ResolveThrust(assembly, seconds, out SpaceVector2 force, out double torque);
            var orbit = flight.Orbit;
            orbit.PositionMeters = new SpaceVector2(assembly.PositionX, assembly.PositionY);
            orbit.VelocityMetersPerSecond = new SpaceVector2(assembly.VelocityX, assembly.VelocityY);
            orbit.RadiusMeters = AssemblyRadius(assembly);
            RecordOrbitContactAcceleration(assembly, force / Math.Max(.001d, assembly.MassKg));
            bool touched = Universe.StepOrbit(orbit, force / Math.Max(.001d, assembly.MassKg), seconds, out SurfaceContact contact);
            assembly.PositionX = orbit.PositionMeters.X; assembly.PositionY = orbit.PositionMeters.Y;
            assembly.VelocityX = orbit.VelocityMetersPerSecond.X; assembly.VelocityY = orbit.VelocityMetersPerSecond.Y;
            assembly.AngularVelocityRadiansPerSecond += torque / Math.Max(.001d, assembly.InertiaKgM2) * seconds;
            assembly.AngleRadians += assembly.AngularVelocityRadiansPerSecond * seconds;
            ShipDockingService.RigidlyCarryMembers(assembly, State.Ships);
            foreach (string id in assembly.MemberShipIds)
            {
                ShipState ship = GetShip(id); ShipFlightState member = GetFlight(id);
                member.Orbit.PositionMeters = new SpaceVector2(ship.PositionX, ship.PositionY);
                member.Orbit.VelocityMetersPerSecond = new SpaceVector2(ship.VelocityX, ship.VelocityY);
                member.Orbit.ReferenceBodyId = orbit.ReferenceBodyId; member.Orbit.CapturedBodyId = orbit.CapturedBodyId;
                Universe.UpdateReference(member.Orbit);
                UpdateDescent(member, ship);
            }
            if (touched) QueueSurfaceContact(assembly, contact, seconds);
        }
        private double AssemblyRadius(ShipAssemblyState assembly)
        {
            double radius = 0d;
            foreach (string id in assembly.MemberShipIds)
            {
                ShipState ship = GetShip(id);
                foreach (ShipPieceState piece in ship.Pieces)
                {
                    if (!piece.IsAlive) continue;
                    ShipGeometry.LocalToWorld(ship, (piece.CellX + .5d) * ship.CellSizeMeters, (piece.CellY + .5d) * ship.CellSizeMeters, out double x, out double y);
                    radius = Math.Max(radius, new SpaceVector2(x - assembly.PositionX, y - assembly.PositionY).Magnitude + ship.CellSizeMeters * .7072d);
                }
            }
            return radius;
        }
        private void StepLift(ShipAssemblyState assembly, double seconds)
        {
            ShipFlightState flight = GetFlight(assembly.MemberShipIds[0]);
            BodyState body = Universe.GetBody(flight.BodyId);
            CollectActuators(assembly);
            bool consolePowered = false;
            foreach (string id in assembly.MemberShipIds)
            {
                ShipState member = GetShip(id);
                consolePowered |= member.Pieces.Exists(piece => piece.IsAlive && piece.Kind == ShipPieceKind.Console && IsPowered(member, piece));
            }
            double thrust = 0d;
            if (flight.Ignited && consolePowered)
                foreach (ShipActuator engine in actuators)
                    thrust += engine.Device.Configuration.EngineThrustNewtons * ConsumeEngineFuel(engine.Device.Configuration, 1d, seconds);
            double gravity = body.GravityParameter / Math.Pow(body.RadiusMeters + Math.Max(0d, flight.HeightMeters), 2d);
            double previousHeight = flight.HeightMeters;
            flight.VerticalSpeed += (thrust / Math.Max(.001d, assembly.MassKg) - gravity) * seconds;
            flight.HeightMeters += flight.VerticalSpeed * seconds;
            // 地表起飞只提供垂直推力，碰撞取得的平面惯性和旋转继续保留。
            assembly.PositionX += assembly.VelocityX * seconds;
            assembly.PositionY += assembly.VelocityY * seconds;
            assembly.AngleRadians += assembly.AngularVelocityRadiansPerSecond * seconds;
            ShipDockingService.RigidlyCarryMembers(assembly, State.Ships);
            flight.EntrySurfaceVelocity = new SpaceVector2(Math.Sqrt(assembly.VelocityX * assembly.VelocityX + assembly.VelocityY * assembly.VelocityY), flight.VerticalSpeed);
            double requiredHeight = Math.Max(SpaceGameplaySettings.Current.MinimumLaunchHeightMeters, body.RadiusMeters * SpaceGameplaySettings.Current.LaunchHeightRadiusFraction);
            double requiredSpeed = Math.Sqrt(body.GravityParameter / (body.RadiusMeters + requiredHeight)) * SpaceGameplaySettings.Current.LaunchCircularSpeedFraction;
            foreach (string id in assembly.MemberShipIds)
            {
                ShipFlightState member = GetFlight(id);
                member.HeightMeters = flight.HeightMeters; member.VerticalSpeed = flight.VerticalSpeed;
            }
            if (flight.HeightMeters >= requiredHeight && flight.VerticalSpeed >= requiredSpeed)
            {
                OrbitState lifted = Universe.CreateLaunchState(flight.BodyId, flight.SurfaceAngle, flight.HeightMeters,
                    flight.VerticalSpeed, clockwise: false);
                SpaceVector2 delta = lifted.PositionMeters - new SpaceVector2(assembly.PositionX, assembly.PositionY);
                assembly.PositionX += delta.X; assembly.PositionY += delta.Y;
                assembly.VelocityX = lifted.VelocityMetersPerSecond.X; assembly.VelocityY = lifted.VelocityMetersPerSecond.Y;
                ShipDockingService.RigidlyCarryMembers(assembly, State.Ships);
                foreach (string id in assembly.MemberShipIds)
                {
                    ShipState ship = GetShip(id); ShipFlightState member = GetFlight(id);
                    member.Phase = ShipFlightPhase.Orbit; member.Ignited = false; ship.PlatformKind = ShipPlatformKind.Space;
                    member.Orbit = new OrbitState { PositionMeters = new SpaceVector2(ship.PositionX, ship.PositionY),
                        VelocityMetersPerSecond = new SpaceVector2(ship.VelocityX, ship.VelocityY), ReferenceBodyId = flight.BodyId };
                }
                if (PlayerInAssembly(assembly)) StartCoroutine(TransferPlayerToSpace());
            }
            else if (previousHeight > 0d && flight.HeightMeters <= 0d && flight.VerticalSpeed < 0d)
            {
                var contact = new SurfaceContact { BodyId = body.BodyId, PlanetId = body.PlanetId,
                    RelativeVelocityMetersPerSecond = new SpaceVector2(flight.EntrySurfaceVelocity.X, flight.VerticalSpeed), SurfaceAngleRadians = flight.SurfaceAngle };
                LandAssembly(assembly, contact);
            }
            else if (flight.HeightMeters < 0d) { flight.HeightMeters = 0d; flight.VerticalSpeed = 0d; }
        }
        private void UpdateDescent(ShipFlightState flight, ShipState ship)
        {
            if (!Universe.TryGetBody(flight.Orbit.ReferenceBodyId, out BodyState body))
            { flight.Phase = ShipFlightPhase.Orbit; return; }
            SpaceVector2 radial = new SpaceVector2(ship.PositionX, ship.PositionY) - body.PositionMeters;
            flight.BodyId = body.BodyId;
            double altitude = radial.Magnitude - body.RadiusMeters;
            double inward = -SpaceVector2.Dot(new SpaceVector2(ship.VelocityX, ship.VelocityY) - body.VelocityMetersPerSecond, radial.Normalized);
            if (altitude < Math.Max(100d, body.RadiusMeters * .25d) && inward > 0d)
            {
                flight.BodyId = body.BodyId; flight.Phase = ShipFlightPhase.Descending;
            }
            else if (flight.Phase == ShipFlightPhase.Descending) flight.Phase = ShipFlightPhase.Orbit;
        }
        public double DescentSeconds(ShipFlightState flight)
        {
            if (flight.Phase is ShipFlightPhase.Lifting or ShipFlightPhase.SurfaceDescending)
            {
                double g = Universe.GetBody(flight.BodyId).SurfaceGravity;
                return g > 0d ? (flight.VerticalSpeed + Math.Sqrt(flight.VerticalSpeed * flight.VerticalSpeed + 2d * g * Math.Max(0d, flight.HeightMeters))) / g
                    : flight.VerticalSpeed < 0d ? Math.Max(0d, flight.HeightMeters / -flight.VerticalSpeed) : double.PositiveInfinity;
            }
            if (!Universe.TryGetBody(flight.BodyId, out BodyState body)) return double.PositiveInfinity;
            SpaceVector2 radial = flight.Orbit.PositionMeters - body.PositionMeters;
            double altitude = Math.Max(0d, radial.Magnitude - body.RadiusMeters - flight.Orbit.RadiusMeters);
            double inward = -SpaceVector2.Dot(flight.Orbit.VelocityMetersPerSecond - body.VelocityMetersPerSecond, radial.Normalized);
            double gravity = body.GravityParameter / Math.Max(1d, radial.SqrMagnitude);
            return gravity > 0d ? (Math.Sqrt(inward * inward + 2d * gravity * altitude) - inward) / gravity
                : inward > 0d ? altitude / inward : double.PositiveInfinity;
        }
        public void SelectLanding(string shipId, Vector2 position)
        {
            ShipFlightState flight = GetFlight(shipId);
            if (flight == null) return;
            Vector2 origin = new((float)flight.SurfaceX, (float)flight.SurfaceY);
            Vector2 chosen = origin + Vector2.ClampMagnitude(position - origin, (float)SpaceGameplaySettings.Current.LandingChoiceRadiusMeters);
            foreach (ShipState ship in State.Ships)
                if (SameAssembly(shipId, ship.ShipId))
                { ShipFlightState member = GetFlight(ship.ShipId); member.LandingX = chosen.x; member.LandingY = chosen.y; member.LandingSelected = true; }
        }
        private bool PlayerInAssembly(ShipAssemblyState assembly)
        {
            Player player = ItemMgr.Instance?.User_Player;
            return player != null && assembly.MemberShipIds.Contains(GetPassenger(player).ShipId);
        }
        #endregion

        #region 唯一触地、冲击与场景迁移
        private float nextTransferRetry;
        private string GetPassengerWorld(SpacePassengerState passenger)
        {
            if (passenger.Supported && GetFlight(passenger.ShipId) is ShipFlightState flight)
                return flight.Phase is ShipFlightPhase.Orbit or ShipFlightPhase.Descending ? "SpaceScene" : flight.SurfaceWorldKey;
            if (passenger.SurfaceDescending) return passenger.LandingWorldKey;
            return passenger.IsInSpace && !passenger.LandingResolved ? "SpaceScene" : null;
        }
        private bool ResumePendingPassengerTransfer()
        {
            Player player = ItemMgr.Instance?.User_Player;
            if (player == null) return false;
            SpacePassengerState passenger = GetPassenger(player);
            string expected = GetPassengerWorld(passenger);
            if (string.IsNullOrWhiteSpace(expected) || expected == SceneManager.GetActiveScene().name) return false;
            // 加载失败仍保留真实运动状态，下一次从同一目标恢复迁移，不在错误场景继续推进。
            if (Time.unscaledTime >= nextTransferRetry)
            {
                nextTransferRetry = Time.unscaledTime + 2f;
                if (expected == "SpaceScene") StartCoroutine(TransferPlayerToSpace());
                else
                {
                    ShipState ship = GetShip(passenger.ShipId);
                    if (passenger.Supported && ship != null)
                    {
                        ShipGeometry.LocalToWorld(ship, passenger.LocalX, passenger.LocalY, out double x, out double y);
                        StartCoroutine(TransferPlayerToSurface(expected, new Vector2((float)x, (float)y)));
                    }
                    else StartCoroutine(TransferPlayerToSurface(expected, new Vector2((float)passenger.LandingX, (float)passenger.LandingY)));
                }
            }
            return true;
        }

        private void LandAssembly(ShipAssemblyState assembly, SurfaceContact contact)
        {
            ShipFlightState primary = GetFlight(assembly.MemberShipIds[0]);
            if (primary.LandingResolved) return;
            string world = Universe.GetSurfaceAddress(contact.BodyId).WorldKey;
            bool carryingPlayer = PlayerInAssembly(assembly);
            double impactSpeed = contact.RelativeVelocityMetersPerSecond.Magnitude;
            float impactRadius = (float)AssemblyRadius(assembly);
            BodyState body = Universe.GetBody(contact.BodyId);
            SpaceGameplaySettings settings = SpaceGameplaySettings.Current;
            SpaceVector2 point;
            if (primary.LandingSelected) point = new SpaceVector2(primary.LandingX, primary.LandingY);
            else
            {
                double theta = (double)FluidRandom.Next01(ref primary.LandingRandomState) * Math.PI * 2d;
                double radius = Math.Sqrt((double)FluidRandom.Next01(ref primary.LandingRandomState)) * settings.LandingChoiceRadiusMeters;
                point = new SpaceVector2(primary.SurfaceX + Math.Cos(theta) * radius, primary.SurfaceY + Math.Sin(theta) * radius);
                primary.LandingX = point.X; primary.LandingY = point.Y; primary.LandingSelected = true;
            }

            var sample = SpaceSurfaceQuery.GetLandingSurface(body.PlanetId, point.ToVector2());
            string impactEvent = Guid.NewGuid().ToString("N");
            double stoppingAcceleration = Math.Abs(contact.RelativeVelocityMetersPerSecond.Y) / Math.Max(.001d, settings.ImpactStoppingSeconds) + body.SurfaceGravity;
            double damage = assembly.MassKg * stoppingAcceleration / Math.Max(1d, settings.ImpactForcePerDurability) +
                .5d * assembly.MassKg * contact.RelativeVelocityMetersPerSecond.X * contact.RelativeVelocityMetersPerSecond.X / Math.Max(1d, settings.ImpactJoulesPerDurability);
            double absorber = 0d;
            foreach (string memberId in assembly.MemberShipIds)
                foreach (ShipPieceState piece in GetShip(memberId).Pieces) if (piece.IsAlive && piece.Kind == ShipPieceKind.Floor) absorber += piece.Health;
            assembly.PositionX = point.X; assembly.PositionY = point.Y;
            assembly.VelocityX = assembly.VelocityY = assembly.AngularVelocityRadiansPerSecond = 0d;
            ShipDockingService.RigidlyCarryMembers(assembly, State.Ships);
            foreach (string id in assembly.MemberShipIds)
            {
                ShipState ship = GetShip(id); ShipFlightState flight = GetFlight(id);
                flight.LandingResolved = true; flight.LandingEventId = impactEvent;
                flight.LandingX = point.X; flight.LandingY = point.Y; flight.LandingSelected = true;
                flight.HeightMeters = 0; flight.VerticalSpeed = 0; flight.BodyId = contact.BodyId; flight.SurfaceWorldKey = world;
                flight.ImpactSpeed = impactSpeed; flight.Ignited = false; flight.SurfaceX = ship.PositionX; flight.SurfaceY = ship.PositionY;
                flight.Phase = sample.IsWater ? ShipFlightPhase.Floating : ShipFlightPhase.Landed; ship.WorldAddress = world;
                ship.PlatformKind = sample.IsWater ? ShipPlatformKind.Ocean : ShipPlatformKind.Land;
                double durabilityDamage = damage * ship.MassKg / Math.Max(.001d, assembly.MassKg);
                int count = ship.Pieces.FindAll(piece => piece.IsAlive).Count;
                foreach (ShipPieceState piece in ship.Pieces.ToArray())
                    if (piece.IsAlive) ShipStructureService.ApplyDamage(ship, piece.PieceId, durabilityDamage / Math.Max(1, count), "触地冲击", DestroyPiece);
            }
            ApplyTerrainImpact(GetShip(assembly.MemberShipIds[0]), impactSpeed, assembly.MassKg, point.ToVector2(), impactRadius);
            if (carryingPlayer && damage > absorber)
            {
                Player player = ItemMgr.Instance?.User_Player;
                double playerMass = Math.Max(1d, player?.itemData?.Stack?.CurrentWeight ?? 70f);
                double remaining = playerMass * stoppingAcceleration / Math.Max(1d, settings.ImpactForcePerDurability) * (1d - absorber / damage);
                float playerDamage = SpacesuitSystem.AbsorbLandingImpact(player, (float)remaining);
                if (playerDamage > 0f) player?.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp)?.ApplyStructuralDamage(playerDamage);
            }
            ProcessStructureChanges();
            if (carryingPlayer && IsSpaceView) StartCoroutine(TransferPlayerToSurface(world, point.ToVector2()));
        }
        public SpaceVector2 SurfaceCenter(string bodyId, double angle)
        {
            BodyState body = Universe.GetBody(bodyId);
            // 太空接触经纬映射与当地生成地址稳定绑定，不使用当前画面浮动原点。
            double longitude = angle - Universe.GetRotationRadians(bodyId);
            longitude = Math.IEEERemainder(longitude, Math.PI * 2d);
            return new SpaceVector2(longitude * body.RadiusMeters, 0d);
        }
        private void BeginSurfaceDescent(ShipAssemblyState assembly, SurfaceContact contact)
        {
            ShipFlightState primary = GetFlight(assembly.MemberShipIds[0]);
            if (primary.Phase == ShipFlightPhase.SurfaceDescending || primary.LandingResolved) return;
            SpaceVector2 center = SurfaceCenter(contact.BodyId, contact.SurfaceAngleRadians);
            SpaceVector2 normal = SpaceVector2.FromAngle(contact.SurfaceAngleRadians);
            double inward = Math.Max(0d, -SpaceVector2.Dot(contact.RelativeVelocityMetersPerSecond, normal));
            double tangent = SpaceVector2.Cross(normal, contact.RelativeVelocityMetersPerSecond);
            bool carrying = PlayerInAssembly(assembly);
            string world = Universe.GetSurfaceAddress(contact.BodyId).WorldKey;
            assembly.PositionX = center.X; assembly.PositionY = center.Y;
            assembly.VelocityX = tangent; assembly.VelocityY = 0d;
            ShipDockingService.RigidlyCarryMembers(assembly, State.Ships);
            foreach (string id in assembly.MemberShipIds)
            {
                ShipState ship = GetShip(id); ShipFlightState flight = GetFlight(id);
                ship.WorldAddress = world; ship.PlatformKind = ShipPlatformKind.Land;
                flight.Phase = ShipFlightPhase.SurfaceDescending; flight.BodyId = contact.BodyId; flight.SurfaceWorldKey = world;
                flight.SurfaceX = center.X; flight.SurfaceY = center.Y; flight.SurfaceAngle = contact.SurfaceAngleRadians;
                flight.HeightMeters = SpaceGameplaySettings.Current.SurfaceEntryHeightMeters;
                flight.VerticalSpeed = -inward; flight.EntrySurfaceVelocity = new SpaceVector2(tangent, -inward);
                flight.Ignited = false; flight.LandingResolved = false; flight.LandingSelected = false;
                flight.LandingEventId = Guid.NewGuid().ToString("N");
            }
            if (carrying && IsSpaceView) StartCoroutine(TransferPlayerToSurface(world, center.ToVector2()));
        }
        private IEnumerator TransferPlayerToSpace()
        {
            if (transitionRunning) yield break;
            Player player = ItemMgr.Instance?.User_Player;
            if (player == null) yield break;
            transitionRunning = true;
            SpacePassengerState passenger = GetPassenger(player);
            passenger.IsInSpace = true; passenger.SurfaceDescending = passenger.LandingResolved = passenger.LandingSelected = false;
            ReleaseViews();
            bool began = DimensionManager.Instance.TryBeginPlanetLanding(player, WorldAddress.FromWorldKey("SpaceScene"), player.transform.position);
            while (began && DimensionManager.Instance.IsTransitioning) yield return null;
            if (IsSpaceView) RestorePassengerPose(ItemMgr.Instance?.User_Player);
            else passenger.IsInSpace = false;
            transitionRunning = false;
        }
        private bool transitionRunning;
        private IEnumerator TransferPlayerToSurface(string world, Vector2 position)
        {
            if (transitionRunning) yield break;
            transitionRunning = true;
            Player player = ItemMgr.Instance?.User_Player;
            if (player == null) { transitionRunning = false; yield break; }
            SpacePassengerState passenger = GetPassenger(player);
            passenger.IsInSpace = false;
            ReleaseViews();
            bool began = DimensionManager.Instance.TryBeginPlanetLanding(player, WorldAddress.FromWorldKey(world), position);
            while (began && DimensionManager.Instance.IsTransitioning) yield return null;
            if (began && SceneManager.GetActiveScene().name == world) RestorePassengerPose(ItemMgr.Instance?.User_Player);
            else if (IsSpaceView) passenger.IsInSpace = true;
            transitionRunning = false;
        }
        #endregion
    }
}
