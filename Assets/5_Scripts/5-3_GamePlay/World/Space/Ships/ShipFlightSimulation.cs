using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 发射与街机式太空航行
        public BodyState FindSurfaceBody(string worldKey)
        {
            if (Universe == null) return null;
            string planetId = WorldAddress.FromWorldKey(worldKey).PlanetId;
            return Universe.State.Bodies.Find(value => value.PlanetId == planetId);
        }
        private bool SameAssembly(string left, string right)
        {
            return State != null && queryAssemblies.AreConnected(State.Ships, State.Docking, left, right);
        }
        public ShipAssemblyState GetConnectedAssembly(string shipId)
        {
            if (State == null) return null;
            queryAssemblies.Refresh(State.Ships, State.Docking);
            return queryAssemblies.Find(shipId);
        }
        public bool ToggleIgnition(string consoleId)
        {
            if (!FindPiece(consoleId, out ShipState ship, out ShipPieceState console) || console.Kind != ShipPieceKind.Console || !IsPowered(ship, console)) return false;
            var flight = GetFlight(ship.ShipId);
            if (flight == null) return false;
            // 从停靠转入动力模拟前同步实际载荷，避免使用静态阶段的旧质量。
            RefreshMass();
            var assembly = GetConnectedAssembly(ship.ShipId);
            CollectActuators(assembly);
            double thrust = GetAvailableEngineThrust();
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
            ResolveCruiseCommand(assembly, seconds, out SpaceVector2 targetVelocity, out double turn, out double driveAuthority);
            SpaceGameplaySettings config = SpaceGameplaySettings.Current;
            SpaceVector2 velocity = new(assembly.VelocityX, assembly.VelocityY);
            velocity = ShipCruiseGuidance.StepVelocity(velocity, targetVelocity, config, seconds, driveAuthority);
            var motion = flight.Motion;
            motion.PositionMeters = new SpaceVector2(assembly.PositionX, assembly.PositionY);
            motion.VelocityMetersPerSecond = velocity;
            motion.RadiusMeters = AssemblyRadius(assembly);
            bool touched = Universe.StepMotion(motion, default, seconds, out SurfaceContact contact);
            assembly.PositionX = motion.PositionMeters.X; assembly.PositionY = motion.PositionMeters.Y;
            assembly.VelocityX = motion.VelocityMetersPerSecond.X; assembly.VelocityY = motion.VelocityMetersPerSecond.Y;
            // Q/E 直接指定本步角速度，松键停转、反向立即换向，不累加角加速度。
            assembly.AngularVelocityRadiansPerSecond = Math.Clamp(turn, -1d, 1d) * config.CruiseTurnDegreesPerSecond * Math.PI / 180d;
            assembly.AngleRadians += assembly.AngularVelocityRadiansPerSecond * seconds;
            ShipDockingService.RigidlyCarryMembers(assembly, State.Ships);
            foreach (string id in assembly.MemberShipIds)
            {
                ShipState ship = GetShip(id); ShipFlightState member = GetFlight(id);
                member.Motion.PositionMeters = new SpaceVector2(ship.PositionX, ship.PositionY);
                member.Motion.VelocityMetersPerSecond = new SpaceVector2(ship.VelocityX, ship.VelocityY);
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
                    int width = Math.Max(1, (piece.QuarterTurns & 1) == 0 ? piece.Width : piece.Height);
                    int height = Math.Max(1, (piece.QuarterTurns & 1) == 0 ? piece.Height : piece.Width);
                    ShipGeometry.LocalToWorld(ship, (piece.CellX + width * .5d) * ship.CellSizeMeters, (piece.CellY + height * .5d) * ship.CellSizeMeters, out double x, out double y);
                    double halfDiagonal = ship.CellSizeMeters * .5d * Math.Sqrt((double)width * width + (double)height * height);
                    radius = Math.Max(radius, new SpaceVector2(x - assembly.PositionX, y - assembly.PositionY).Magnitude + halfDiagonal);
                }
            }
            return radius;
        }
        private void StepLift(ShipAssemblyState assembly, double seconds)
        {
            ShipFlightState flight = GetFlight(assembly.MemberShipIds[0]);
            BodyState body = Universe.GetBody(flight.BodyId);
            double thrust = 0d;
            if (flight.Ignited)
            {
                CollectActuators(assembly);
                bool consolePowered = false;
                foreach (string id in assembly.MemberShipIds)
                {
                    ShipState member = GetShip(id);
                    consolePowered |= member.Pieces.Exists(piece => piece.IsAlive && piece.Kind == ShipPieceKind.Console && IsPowered(member, piece));
                }
                if (consolePowered)
                    foreach (ShipActuator engine in actuators)
                        thrust += engine.Device.Configuration.EngineThrustNewtons * ConsumeEngineFuel(engine, 1d, seconds);
            }
            double gravity = body.SurfaceGravity;
            double previousHeight = flight.HeightMeters;
            flight.VerticalSpeed += (thrust / Math.Max(.001d, assembly.MassKg) - gravity) * seconds;
            flight.HeightMeters += flight.VerticalSpeed * seconds;
            if (flight.Phase == ShipFlightPhase.Lifting && previousHeight > 0d && flight.VerticalSpeed < 0d)
                foreach (string id in assembly.MemberShipIds) GetFlight(id).Phase = ShipFlightPhase.SurfaceDescending;
            double movementSeconds = seconds;
            // 下降时直接操纵真实地表位置，触地那一步只积到实际接触时刻。
            if (flight.Phase == ShipFlightPhase.SurfaceDescending)
            {
                movementSeconds = flight.HeightMeters <= 0d && previousHeight > 0d
                    ? seconds * Math.Clamp(previousHeight / (previousHeight - flight.HeightMeters), 0d, 1d) : seconds;
                ResolveCruiseCommand(assembly, movementSeconds, out SpaceVector2 targetVelocity, out double turn,
                    out double driveAuthority, surfaceDescent: true);
                SpaceGameplaySettings config = SpaceGameplaySettings.Current;
                SpaceVector2 velocity = new(assembly.VelocityX, assembly.VelocityY);
                if (driveAuthority > 0d)
                    velocity = ShipCruiseGuidance.StepVelocity(velocity, targetVelocity, config, movementSeconds, driveAuthority);
                assembly.VelocityX = velocity.X; assembly.VelocityY = velocity.Y;
                assembly.PositionX += velocity.X * movementSeconds;
                assembly.PositionY += velocity.Y * movementSeconds;
                assembly.AngularVelocityRadiansPerSecond = turn * config.CruiseTurnDegreesPerSecond * Math.PI / 180d;
                assembly.AngleRadians += assembly.AngularVelocityRadiansPerSecond * movementSeconds;
                ShipDockingService.RigidlyCarryMembers(assembly, State.Ships);
            }
            flight.EntrySurfaceVelocity = new SpaceVector2(Math.Sqrt(assembly.VelocityX * assembly.VelocityX + assembly.VelocityY * assembly.VelocityY), flight.VerticalSpeed);
            double requiredHeight = Math.Max(SpaceGameplaySettings.Current.MinimumLaunchHeightMeters, body.RadiusMeters * SpaceGameplaySettings.Current.LaunchHeightRadiusFraction);
            double requiredSpeed = SpaceGameplaySettings.Current.MinimumSpaceExitSpeedMetersPerSecond;
            foreach (string id in assembly.MemberShipIds)
            {
                ShipFlightState member = GetFlight(id);
                member.HeightMeters = flight.HeightMeters; member.VerticalSpeed = flight.VerticalSpeed;
                member.EntrySurfaceVelocity = flight.EntrySurfaceVelocity;
                if (flight.Phase == ShipFlightPhase.SurfaceDescending)
                {
                    ShipState ship = GetShip(id);
                    member.SurfaceX = member.LandingX = ship.PositionX;
                    member.SurfaceY = member.LandingY = ship.PositionY;
                    member.LandingSelected = true;
                    member.SurfaceAngle = ship.PositionX / body.RadiusMeters + Universe.GetRotationRadians(body.BodyId);
                }
            }
            if (flight.HeightMeters >= requiredHeight && flight.VerticalSpeed >= requiredSpeed)
            {
                SpaceMotionState lifted = Universe.CreateSpaceEntryState(flight.BodyId, flight.SurfaceAngle, flight.HeightMeters,
                    Math.Min(flight.VerticalSpeed, SpaceGameplaySettings.Current.CruiseMaxSpeedMetersPerSecond));
                SpaceVector2 delta = lifted.PositionMeters - new SpaceVector2(assembly.PositionX, assembly.PositionY);
                assembly.PositionX += delta.X; assembly.PositionY += delta.Y;
                // 水平地表速度保留为离开星球时的切向速度，但整体受巡航速度限制。
                SpaceVector2 radial = SpaceVector2.FromAngle(flight.SurfaceAngle);
                SpaceVector2 tangent = new(-radial.Y, radial.X);
                SpaceVector2 exitVelocity = lifted.VelocityMetersPerSecond + tangent * assembly.VelocityX;
                if (exitVelocity.Magnitude > SpaceGameplaySettings.Current.CruiseMaxSpeedMetersPerSecond)
                    exitVelocity = exitVelocity.Normalized * SpaceGameplaySettings.Current.CruiseMaxSpeedMetersPerSecond;
                assembly.VelocityX = exitVelocity.X; assembly.VelocityY = exitVelocity.Y;
                ShipDockingService.RigidlyCarryMembers(assembly, State.Ships);
                foreach (string id in assembly.MemberShipIds)
                {
                    ShipState ship = GetShip(id); ShipFlightState member = GetFlight(id);
                    member.Phase = ShipFlightPhase.Cruising; member.Ignited = false; ship.PlatformKind = ShipPlatformKind.Space;
                    member.Motion = new SpaceMotionState { PositionMeters = new SpaceVector2(ship.PositionX, ship.PositionY),
                        VelocityMetersPerSecond = new SpaceVector2(ship.VelocityX, ship.VelocityY) };
                }
                if (PlayerInAssembly(assembly)) StartCoroutine(TransferPlayerToSpace());
            }
            else if (previousHeight > 0d && flight.HeightMeters <= 0d && flight.VerticalSpeed < 0d)
            {
                var contact = new SurfaceContact { BodyId = body.BodyId, PlanetId = body.PlanetId,
                    RelativeVelocityMetersPerSecond = new SpaceVector2(flight.EntrySurfaceVelocity.X, flight.VerticalSpeed),
                    SurfaceAngleRadians = flight.SurfaceAngle, Fraction = movementSeconds / seconds };
                QueueSurfaceLanding(assembly, contact);
                foreach (string id in assembly.MemberShipIds) GetFlight(id).HeightMeters = 0d;
            }
            else if (flight.HeightMeters < 0d) { flight.HeightMeters = 0d; flight.VerticalSpeed = 0d; }
        }
        private void UpdateDescent(ShipFlightState flight, ShipState ship)
        {
            BodyState approaching = null;
            double nearest = double.PositiveInfinity;
            SpaceVector2 position = new(ship.PositionX, ship.PositionY);
            SpaceVector2 velocity = new(ship.VelocityX, ship.VelocityY);
            foreach (BodyState body in Universe.State.Bodies)
            {
                SpaceVector2 radial = position - body.PositionMeters;
                double altitude = radial.Magnitude - body.RadiusMeters - flight.Motion.RadiusMeters;
                if (altitude < nearest && altitude < Math.Max(100d, body.RadiusMeters * .25d) &&
                    SpaceVector2.Dot(velocity, radial.Normalized) < 0d)
                { approaching = body; nearest = altitude; }
            }
            if (approaching != null) flight.BodyId = approaching.BodyId;
            flight.Phase = approaching == null ? ShipFlightPhase.Cruising : ShipFlightPhase.Descending;
        }
        public double DescentSeconds(ShipFlightState flight)
        {
            if (flight.Phase is ShipFlightPhase.Lifting or ShipFlightPhase.SurfaceDescending)
            {
                double g = Universe.GetBody(flight.BodyId).SurfaceGravity;
                return g > 0d ? (flight.VerticalSpeed + Math.Sqrt(flight.VerticalSpeed * flight.VerticalSpeed + 2d * g * Math.Max(0d, flight.HeightMeters))) / g
                    : flight.VerticalSpeed < 0d ? Math.Max(0d, flight.HeightMeters / -flight.VerticalSpeed) : double.PositiveInfinity;
            }
            if (flight.Phase != ShipFlightPhase.Descending || !Universe.TryGetBody(flight.BodyId, out BodyState body))
                return double.PositiveInfinity;
            SpaceVector2 radial = flight.Motion.PositionMeters - body.PositionMeters;
            double altitude = Math.Max(0d, radial.Magnitude - body.RadiusMeters - flight.Motion.RadiusMeters);
            double inward = -SpaceVector2.Dot(flight.Motion.VelocityMetersPerSecond, radial.Normalized);
            return inward > 0d ? altitude / inward : double.PositiveInfinity;
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
                return flight.Phase is ShipFlightPhase.Cruising or ShipFlightPhase.Descending ? "SpaceScene" : flight.SurfaceWorldKey;
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
            // 陆海判断和冲击都使用整船实际接触位置，不再在触地时跳到选点或随机点。
            SpaceVector2 point = new(assembly.PositionX, assembly.PositionY);

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
                flight.LandingX = ship.PositionX; flight.LandingY = ship.PositionY; flight.LandingSelected = true;
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
            if (carryingPlayer) EndDescentControl(ItemMgr.Instance?.User_Player);
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
                flight.SurfaceX = flight.LandingX = ship.PositionX;
                flight.SurfaceY = flight.LandingY = ship.PositionY; flight.SurfaceAngle = contact.SurfaceAngleRadians;
                flight.HeightMeters = SpaceGameplaySettings.Current.SurfaceEntryHeightMeters;
                flight.VerticalSpeed = -inward; flight.EntrySurfaceVelocity = new SpaceVector2(tangent, -inward);
                flight.Ignited = false; flight.LandingResolved = false; flight.LandingSelected = true;
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
