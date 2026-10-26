using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using FlatWorld.Networking;
using FlatWorld.Localization;

namespace FlatWorld.Spaceflight
{
    #region 点火推力判定
    internal enum ShipIgnitionThrustFailure { None, NoFuel, Overweight }
    #endregion

    public sealed partial class SpaceSession
    {
        #region 发射与惯性太空航行
        public BodyState FindSurfaceBody(string worldKey)
        {
            if (Universe == null) return null;
            string planetId = WorldAddress.FromWorldKey(worldKey).PlanetId;
            foreach (UniverseSimulation simulation in sectorDirectory.Values)
                foreach (BodyState body in simulation.State.Bodies) if (body.PlanetId == planetId) return body;
            return null;
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
        public bool ToggleIgnition(string consoleId) => ToggleIgnition(consoleId, out _);

        // 点火失败由权威规则给出具体原因，地面装配和联机回执沿用同一结果。
        public bool ToggleIgnition(string consoleId, out string reason)
        {
            reason = null;
            if (!FlatWorld.Networking.GameNetwork.HasStateAuthority) return SubmitLocalIntent(SpaceNetworkIntentKind.ToggleIgnition, consoleId);
            if (!TryGetPoweredConsole(consoleId, out ShipState ship, out _, out reason)) return false;
            var flight = GetFlight(ship.ShipId);
            var assembly = GetConnectedAssembly(ship.ShipId);
            if (flight == null || assembly == null)
            { reason = FlatWorldLocalizationService.GetUiText("点火失败：飞船状态尚未就绪。"); return false; }
            if (flight.Ignited)
            {
                foreach (string id in assembly.MemberShipIds) GetFlight(id).Ignited = false;
                reason = FlatWorldLocalizationService.GetUiText("引擎已停机，飞船继续按惯性运动。");
                return true;
            }
            // 从停靠转入动力模拟前同步实际载荷，避免使用静态阶段的旧质量。
            RefreshMass();
            assembly = GetConnectedAssembly(ship.ShipId);
            CollectActuators(assembly);
            if (actuators.Count == 0)
            { reason = FlatWorldLocalizationService.GetUiText("点火失败：没有可用引擎，请检查引擎供电、安装方向和设备状态。"); return false; }
            double thrust = GetAvailableEngineThrust();
            double gravity = flight.Phase == ShipFlightPhase.Cruising ? 0d : GetBody(flight.BodyId).SurfaceGravity;
            ShipIgnitionThrustFailure failure = GetIgnitionThrustFailure(flight.Phase, thrust, assembly.MassKg, gravity);
            if (failure != ShipIgnitionThrustFailure.None)
            { reason = DescribeIgnitionThrustFailure(failure, thrust, assembly.MassKg, gravity); return false; }
            bool ignite = !flight.Ignited;
            BodyState sourceBody = flight.Phase == ShipFlightPhase.Cruising ? null : GetBody(flight.BodyId);
            foreach (string id in assembly.MemberShipIds)
            {
                ShipFlightState member = GetFlight(id);
                member.Ignited = ignite;
                if (ignite) member.PoweredDescent = false;
                if (member.Ignited && member.Phase is ShipFlightPhase.Landed or ShipFlightPhase.Floating)
                {
                    member.Phase = ShipFlightPhase.Lifting; member.LandingResolved = false; member.LandingEventId = null;
                    member.RequestedLandingBodyId = null;
                    member.SurfaceX = GetShip(id).PositionX; member.SurfaceY = GetShip(id).PositionY;
                    member.SurfaceAngle = member.SurfaceX / sourceBody.RadiusMeters + GetUniverse(sourceBody.Sector).GetRotationRadians(sourceBody.BodyId);
                    member.EntrySurfaceVelocity = default;
                    member.SurfaceStageElapsedSeconds = 0d;
                }
            }
            reason = FlatWorldLocalizationService.GetUiText("点火已开启。");
            return true;
        }

        internal static ShipIgnitionThrustFailure GetIgnitionThrustFailure(ShipFlightPhase phase,
            double thrust, double mass, double gravity)
        {
            if (thrust <= 0d) return ShipIgnitionThrustFailure.NoFuel;
            return phase is ShipFlightPhase.Landed or ShipFlightPhase.Floating && thrust <= mass * gravity
                ? ShipIgnitionThrustFailure.Overweight : ShipIgnitionThrustFailure.None;
        }

        internal static string DescribeIgnitionThrustFailure(ShipIgnitionThrustFailure failure,
            double thrust, double mass, double gravity) => failure switch
        {
            ShipIgnitionThrustFailure.NoFuel => FlatWorldLocalizationService.GetUiText(
                "点火失败：引擎没有可用燃料，请检查燃料配比、管路和阀门。"),
            ShipIgnitionThrustFailure.Overweight => FlatWorldLocalizationService.GetUiFormat(
                "点火失败：飞船超重。当前总质量 {0:0.##} kg，现有推力在当前重力下最多支撑 {1:0.##} kg，请减载或增加可用推力。",
                mass, gravity > 0d ? thrust / gravity : 0d),
            _ => null
        };

        // 客户端等待服务器的实际点火结果，不能把成功发包显示成点火成功。
        public void RequestToggleIgnition(string consoleId, Action<bool, string> completed)
        {
            if (GameNetwork.HasStateAuthority)
            {
                bool success = ToggleIgnition(consoleId, out string reason);
                completed?.Invoke(success, reason);
                return;
            }
            Player player = ItemMgr.Instance?.User_Player;
            if (player == null || !SpaceNetworkBridge.SendIntent(player.ProfileName,
                new SpaceNetworkIntent { Kind = SpaceNetworkIntentKind.ToggleIgnition, PieceId = consoleId }, completed))
                completed?.Invoke(false, FlatWorldLocalizationService.GetUiText("联机连接尚未就绪，请稍后重试。"));
        }
        private void StepAssembly(ShipAssemblyState assembly, double seconds)
        {
            ShipState primary = GetShip(assembly.MemberShipIds[0]);
            ShipFlightState flight = GetFlight(primary.ShipId);
            if (flight == null) return;
            if (primary.Anchor != null) return;
            if (flight.Phase is ShipFlightPhase.Landed or ShipFlightPhase.Floating) return;
            if (flight.Phase is ShipFlightPhase.Lifting or ShipFlightPhase.SurfaceDescending)
            {
                StepLift(assembly, seconds); return;
            }
            ResolveCruiseCommand(assembly, seconds, out SpaceVector2 velocity);
            var motion = flight.Motion;
            motion.PositionMeters = new SpaceVector2(assembly.PositionX, assembly.PositionY);
            motion.VelocityMetersPerSecond = velocity;
            motion.RadiusMeters = AssemblyRadius(assembly);
            motion.Sector = assembly.Sector;
            assembly.VelocityX = velocity.X; assembly.VelocityY = velocity.Y;
            // 控制只提交平移速度，船体朝向由整船规则和原生刚体约束共同锁定。
            ShipDockingService.RigidlyCarryMembers(assembly, State.Ships);
            foreach (string id in assembly.MemberShipIds)
            {
                ShipState ship = GetShip(id); ShipFlightState member = GetFlight(id);
                member.Motion.Sector = ship.Sector;
                member.Motion.PositionMeters = new SpaceVector2(ship.PositionX, ship.PositionY);
                member.Motion.VelocityMetersPerSecond = new SpaceVector2(ship.VelocityX, ship.VelocityY);
                member.Phase = ShipFlightPhase.Cruising;
            }
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
                    SpaceVector2 ownPoint = SpaceSectorCoordinates.Relative(ship.Sector, new SpaceVector2(x, y), assembly.Sector);
                    x = ownPoint.X; y = ownPoint.Y;
                    double halfDiagonal = ship.CellSizeMeters * .5d * Math.Sqrt((double)width * width + (double)height * height);
                    radius = Math.Max(radius, new SpaceVector2(x - assembly.PositionX, y - assembly.PositionY).Magnitude + halfDiagonal);
                }
            }
            return radius;
        }
        private void StepLift(ShipAssemblyState assembly, double seconds)
        {
            ShipFlightState flight = GetFlight(assembly.MemberShipIds[0]);
            BodyState body = GetBody(flight.BodyId);
            SpaceGameplaySettings settings = SpaceGameplaySettings.Current;
            double gravity = body.SurfaceGravity;
            double thrust = 0d;
            bool waitingForGround = flight.Phase == ShipFlightPhase.SurfaceDescending && flight.PoweredDescent && WaitForGroundPreparation(assembly);
            if (flight.Ignited || flight.PoweredDescent)
            {
                // 自动起降只申请当前速度需要的推力，实际出力仍由供电、引擎和真实管路燃料决定。
                double desiredSpeed = flight.PoweredDescent
                    ? ShipLandingGuidance.LandingSpeed(flight.SurfaceStageElapsedSeconds, flight.HeightMeters, seconds, settings, gravity)
                    : ShipLandingGuidance.TakeoffSpeed(settings);
                // 最后半米等待隐藏建筑备齐，悬停仍由真实引擎与燃料承担。
                if (waitingForGround) desiredSpeed = Math.Max(desiredSpeed, Math.Min(0d, (GroundPreparationHoldHeight - flight.HeightMeters) / seconds));
                double requestedThrust = ShipLandingGuidance.RequiredVerticalThrust(assembly.MassKg, gravity,
                    flight.VerticalSpeed, desiredSpeed, seconds);
                thrust = ConsumeVerticalThrust(assembly, requestedThrust, seconds);
                if (flight.PoweredDescent && thrust <= 0d && requestedThrust > 0d)
                    foreach (string id in assembly.MemberShipIds) GetFlight(id).PoweredDescent = false;
            }
            flight.SurfaceStageElapsedSeconds += seconds;
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
                ResolveCruiseCommand(assembly, movementSeconds, out SpaceVector2 velocity, surfaceDescent: true);
                assembly.VelocityX = velocity.X; assembly.VelocityY = velocity.Y;
                assembly.PositionX += velocity.X * movementSeconds;
                assembly.PositionY += velocity.Y * movementSeconds;
                ShipDockingService.RigidlyCarryMembers(assembly, State.Ships);
            }
            flight.EntrySurfaceVelocity = new SpaceVector2(Math.Sqrt(assembly.VelocityX * assembly.VelocityX + assembly.VelocityY * assembly.VelocityY), flight.VerticalSpeed);
            foreach (string id in assembly.MemberShipIds)
            {
                ShipFlightState member = GetFlight(id);
                member.HeightMeters = flight.HeightMeters; member.VerticalSpeed = flight.VerticalSpeed;
                member.SurfaceStageElapsedSeconds = flight.SurfaceStageElapsedSeconds;
                member.EntrySurfaceVelocity = flight.EntrySurfaceVelocity;
                if (flight.Phase == ShipFlightPhase.Lifting)
                    member.SurfaceAngle = member.SurfaceX / body.RadiusMeters + GetUniverse(body.Sector).GetRotationRadians(body.BodyId);
                if (flight.Phase == ShipFlightPhase.SurfaceDescending)
                {
                    ShipState ship = GetShip(id);
                    member.SurfaceX = member.LandingX = ship.PositionX;
                    member.SurfaceY = member.LandingY = ship.PositionY;
                    member.LandingSelected = true;
                    member.SurfaceAngle = ship.PositionX / body.RadiusMeters + GetUniverse(body.Sector).GetRotationRadians(body.BodyId);
                }
            }
            if (ShipLandingGuidance.CanCompleteTakeoff(flight.VerticalSpeed, flight.SurfaceStageElapsedSeconds, settings))
            {
                if (PlayerInAssembly(assembly)) GameManager.Instance.PrepareShipTakeoffPresentation(this, ItemMgr.Instance?.User_Player);
                SpaceMotionState lifted = ShipLandingGuidance.CreateParkedSpaceEntry(GetUniverse(body.Sector), body,
                    flight.SurfaceAngle, AssemblyRadius(assembly), settings);
                assembly.Sector = body.Sector;
                foreach (string id in assembly.MemberShipIds) GetShip(id).Sector = body.Sector;
                SpaceVector2 delta = lifted.PositionMeters - new SpaceVector2(assembly.PositionX, assembly.PositionY);
                assembly.PositionX += delta.X; assembly.PositionY += delta.Y;
                // 正常起飞完成后停在星体边缘，继承公转速度但不带向外冲刺的速度。
                SpaceVector2 exitVelocity = lifted.VelocityMetersPerSecond;
                assembly.VelocityX = exitVelocity.X; assembly.VelocityY = exitVelocity.Y;
                ShipDockingService.RigidlyCarryMembers(assembly, State.Ships);
                foreach (string id in assembly.MemberShipIds)
                {
                    ShipState ship = GetShip(id); ShipFlightState member = GetFlight(id);
                    member.Phase = ShipFlightPhase.Cruising; ship.PlatformKind = ShipPlatformKind.Space;
                    member.PoweredDescent = false;
                    member.Motion = new SpaceMotionState { Sector = ship.Sector, PositionMeters = new SpaceVector2(ship.PositionX, ship.PositionY),
                        VelocityMetersPerSecond = new SpaceVector2(ship.VelocityX, ship.VelocityY) };
                }
                if (PlayerInAssembly(assembly)) StartCoroutine(TransferPlayerToSpace());
            }
            else if (previousHeight > 0d && flight.HeightMeters <= 0d && flight.VerticalSpeed < 0d)
            {
                var contact = new SurfaceContact { Sector = body.Sector, BodyId = body.BodyId, PlanetId = body.PlanetId,
                    RelativeVelocityMetersPerSecond = new SpaceVector2(flight.EntrySurfaceVelocity.X, flight.VerticalSpeed),
                    SurfaceAngleRadians = flight.SurfaceAngle, Fraction = movementSeconds / seconds };
                bool safelyPowered = flight.PoweredDescent && Math.Abs(flight.VerticalSpeed) <= settings.MaximumSafeLandingSpeedMetersPerSecond + 1e-6d;
                QueueSurfaceLanding(assembly, contact, applyImpact: !safelyPowered);
                foreach (string id in assembly.MemberShipIds) GetFlight(id).HeightMeters = 0d;
            }
            else if (flight.HeightMeters < 0d) { flight.HeightMeters = 0d; flight.VerticalSpeed = 0d; }
        }
        public double DescentSeconds(ShipFlightState flight)
        {
            if (flight.Phase is ShipFlightPhase.Lifting or ShipFlightPhase.SurfaceDescending)
            {
                if (flight.PoweredDescent)
                    return Math.Max(0d, ShipLandingGuidance.ResolvePoweredLandingDurationSeconds(
                        GetBody(flight.BodyId).SurfaceGravity, SpaceGameplaySettings.Current) - flight.SurfaceStageElapsedSeconds);
                double g = GetBody(flight.BodyId).SurfaceGravity;
                return g > 0d ? (flight.VerticalSpeed + Math.Sqrt(flight.VerticalSpeed * flight.VerticalSpeed + 2d * g * Math.Max(0d, flight.HeightMeters))) / g
                    : flight.VerticalSpeed < 0d ? Math.Max(0d, flight.HeightMeters / -flight.VerticalSpeed) : double.PositiveInfinity;
            }
            return double.PositiveInfinity;
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
                return flight.Phase is ShipFlightPhase.Cruising ? SpaceSectorCoordinates.SceneKey(GetPassengerPosition(passenger).Sector) : flight.SurfaceWorldKey;
            if (passenger.SurfaceDescending) return passenger.LandingWorldKey;
            return passenger.IsInSpace && !passenger.LandingResolved ? SpaceSectorCoordinates.SceneKey(GetPassengerPosition(passenger).Sector) : null;
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
                if (SpaceMgr.IsSpaceScene(expected))
                {
                    if (!IsSpaceView) StartCoroutine(TransferPlayerToSpace());
                    else SpaceMgr.Instance.StartCoroutine(SpaceMgr.Instance.ActivateLocalSector(GetPassengerPosition(passenger).Sector));
                }
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

        private void LandAssembly(ShipAssemblyState assembly, SurfaceContact contact, bool applyImpact = true)
        {
            ShipFlightState primary = GetFlight(assembly.MemberShipIds[0]);
            if (primary.LandingResolved) return;
            string world = GetBody(contact.BodyId).SurfaceAddress.WorldKey;
            bool carryingPlayer = PlayerInAssembly(assembly);
            double impactSpeed = applyImpact ? contact.RelativeVelocityMetersPerSecond.Magnitude : 0d;
            float impactRadius = (float)AssemblyRadius(assembly);
            BodyState body = GetBody(contact.BodyId);
            SpaceGameplaySettings settings = SpaceGameplaySettings.Current;
            // 陆海判断和冲击都使用整船实际接触位置，不再在触地时跳到选点或随机点。
            SpaceVector2 point = new(assembly.PositionX, assembly.PositionY);

            var sample = SpaceSurfaceQuery.GetLandingSurface(body.PlanetId, point.ToVector2());
            string impactEvent = Guid.NewGuid().ToString("N");
            double stoppingAcceleration = applyImpact
                ? Math.Abs(contact.RelativeVelocityMetersPerSecond.Y) / Math.Max(.001d, settings.ImpactStoppingSeconds) + body.SurfaceGravity : 0d;
            double damage = applyImpact ? assembly.MassKg * stoppingAcceleration / Math.Max(1d, settings.ImpactForcePerDurability) +
                .5d * assembly.MassKg * contact.RelativeVelocityMetersPerSecond.X * contact.RelativeVelocityMetersPerSecond.X / Math.Max(1d, settings.ImpactJoulesPerDurability) : 0d;
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
                flight.RequestedLandingBodyId = null;
                flight.LandingX = ship.PositionX; flight.LandingY = ship.PositionY; flight.LandingSelected = true;
                flight.HeightMeters = 0; flight.VerticalSpeed = 0; flight.BodyId = contact.BodyId; flight.SurfaceWorldKey = world;
                flight.PoweredDescent = false;
                flight.ImpactSpeed = impactSpeed; flight.Ignited = false; flight.SurfaceX = ship.PositionX; flight.SurfaceY = ship.PositionY;
                flight.Phase = sample.IsWater ? ShipFlightPhase.Floating : ShipFlightPhase.Landed; ship.WorldAddress = world;
                ship.PlatformKind = sample.IsWater ? ShipPlatformKind.Ocean : ShipPlatformKind.Land;
                double durabilityDamage = damage * ship.MassKg / Math.Max(.001d, assembly.MassKg);
                ApplyLineImpact(ship, durabilityDamage, "触地冲击");
            }
            if (applyImpact)
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
            if (carryingPlayer && !applyImpact) ExitConsole(ItemMgr.Instance?.User_Player);
            if (carryingPlayer && IsSpaceView) StartCoroutine(TransferPlayerToSurface(world, point.ToVector2()));
        }
        public SpaceVector2 SurfaceCenter(string bodyId, double angle)
        {
            BodyState body = GetBody(bodyId);
            // 太空接触经纬映射与当地生成地址稳定绑定，不使用当前画面浮动原点。
            double longitude = angle - GetUniverse(body.Sector).GetRotationRadians(bodyId);
            longitude = Math.IEEERemainder(longitude, Math.PI * 2d);
            return new SpaceVector2(longitude * body.RadiusMeters, 0d);
        }
        private void BeginSurfaceDescent(ShipAssemblyState assembly, SurfaceContact contact, bool poweredLanding)
        {
            ShipFlightState primary = GetFlight(assembly.MemberShipIds[0]);
            if (primary.Phase == ShipFlightPhase.SurfaceDescending || primary.LandingResolved) return;
            SpaceVector2 center = SurfaceCenter(contact.BodyId, contact.SurfaceAngleRadians);
            SpaceVector2 normal = SpaceVector2.FromAngle(contact.SurfaceAngleRadians);
            double inward = Math.Max(0d, -SpaceVector2.Dot(contact.RelativeVelocityMetersPerSecond, normal));
            double tangent = SpaceVector2.Cross(normal, contact.RelativeVelocityMetersPerSecond);
            bool carrying = PlayerInAssembly(assembly);
            string world = GetBody(contact.BodyId).SurfaceAddress.WorldKey;
            assembly.Sector = contact.Sector;
            foreach (string id in assembly.MemberShipIds) GetShip(id).Sector = contact.Sector;
            assembly.PositionX = center.X; assembly.PositionY = center.Y;
            assembly.VelocityX = tangent; assembly.VelocityY = 0d;
            ShipDockingService.RigidlyCarryMembers(assembly, State.Ships);
            foreach (string id in assembly.MemberShipIds)
            {
                ShipState ship = GetShip(id); ShipFlightState flight = GetFlight(id);
                ship.WorldAddress = world; ship.PlatformKind = ShipPlatformKind.Land;
                flight.Phase = ShipFlightPhase.SurfaceDescending; flight.BodyId = contact.BodyId; flight.SurfaceWorldKey = world;
                flight.RequestedLandingBodyId = null;
                flight.SurfaceX = flight.LandingX = ship.PositionX;
                flight.SurfaceY = flight.LandingY = ship.PositionY; flight.SurfaceAngle = contact.SurfaceAngleRadians;
                flight.HeightMeters = SpaceGameplaySettings.Current.SurfaceEntryHeightMeters;
                flight.VerticalSpeed = -inward; flight.EntrySurfaceVelocity = new SpaceVector2(tangent, -inward);
                flight.Ignited = false; flight.LandingResolved = false; flight.LandingSelected = true;
                flight.PoweredDescent = poweredLanding; flight.SurfaceStageElapsedSeconds = 0d;
                flight.LandingEventId = Guid.NewGuid().ToString("N");
            }
            BeginGroundPreparation(assembly, world);
            // 有动力也保留完整下降阶段，期间燃料或供电中断会转为真实坠落。
            if (carrying) ShipPanelSession.CloseForPlayer(this, ItemMgr.Instance?.User_Player);
            if (carrying && IsSpaceView) StartCoroutine(TransferPlayerToSurface(world, center.ToVector2()));
        }
        private IEnumerator TransferPlayerToSpace()
        {
            if (GameNetwork.IsOnline)
            {
                Player actor = ItemMgr.Instance?.User_Player;
                if (actor != null && TryGetNetworkObserver(actor.ProfileName, out SpaceNetworkObserver observer))
                    GameManager.Instance.ApplyNetworkLocalSpaceObserver(actor, observer);
                yield break;
            }
            if (transitionRunning) yield break;
            Player player = ItemMgr.Instance?.User_Player;
            if (player == null) yield break;
            transitionRunning = true;
            SpacePassengerState passenger = GetPassenger(player);
            passenger.IsInSpace = true; passenger.SurfaceDescending = passenger.LandingResolved = passenger.LandingSelected = false;
            bool began = DimensionManager.Instance.TryBeginPlanetLanding(player, WorldAddress.FromWorldKey(SpaceSectorCoordinates.SceneKey(GetPassengerPosition(passenger).Sector)), player.transform.position);
            if (began) SuspendViews();
            while (began && DimensionManager.Instance.IsTransitioning) yield return null;
            if (IsSpaceView)
            {
                Player currentPlayer = ItemMgr.Instance?.User_Player;
                RestorePassengerPose(currentPlayer);
                if (currentPlayer != null && passenger.Supported && GetConnectedAssembly(passenger.ShipId) is ShipAssemblyState assembly)
                {
                    // 离地后的首次镜头保留船体和旁边星体边缘，之后玩家仍可自由缩放。
                    SpaceGameplaySettings settings = SpaceGameplaySettings.Current;
                    float size = (float)Math.Max(settings.DrivingCameraSize,
                        AssemblyRadius(assembly) + settings.SpaceEntryClearanceMeters + 8d);
                    currentPlayer.itemMods.GetMod_ByID<Mod_Cam>(ModText.Camera)?.SetOrthographicSize(size);
                }
            }
            else passenger.IsInSpace = false;
            transitionRunning = false;
        }
        private bool transitionRunning;
        private IEnumerator TransferPlayerToSurface(string world, Vector2 position)
        {
            if (GameNetwork.IsOnline)
            {
                TryBeginNetworkSurfaceTransfer(ItemMgr.Instance?.User_Player, world, position);
                yield break;
            }
            if (transitionRunning) yield break;
            transitionRunning = true;
            Player player = ItemMgr.Instance?.User_Player;
            if (player == null) { transitionRunning = false; yield break; }
            SpacePassengerState passenger = GetPassenger(player);
            passenger.IsInSpace = false;
            SuspendViews();
            bool began = DimensionManager.Instance.TryBeginPlanetLanding(player, WorldAddress.FromWorldKey(world), position);
            while (began && DimensionManager.Instance.IsTransitioning) yield return null;
            if (began && SceneManager.GetActiveScene().name == world) RestorePassengerPose(ItemMgr.Instance?.User_Player);
            else if (IsSpaceView) passenger.IsInSpace = true;
            transitionRunning = false;
        }
        #endregion
    }
}
