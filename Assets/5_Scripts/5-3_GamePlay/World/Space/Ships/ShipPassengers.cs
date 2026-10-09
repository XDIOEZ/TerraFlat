using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 乘员支撑、抓墙与惯性
        private readonly Dictionary<string, SpaceVector2> passengerImpulses = new(StringComparer.Ordinal);
        public SpacePassengerState GetPassenger(Player player)
        {
            SpacePassengerState passenger = State.Passengers.Find(value => value.ProfileId == player.ProfileName);
            if (passenger != null) return passenger;
            passenger = new SpacePassengerState { ProfileId = player.ProfileName, IsInSpace = IsSpaceView,
                FreeMotion = new OrbitState { PositionMeters = IsSpaceView ? UnprojectPosition(player.transform.position)
                    : SpaceVector2.FromVector2(player.transform.position) } };
            State.Passengers.Add(passenger); return passenger;
        }
        public bool HandlePassengerInput(Player player, Mod_Mover mover, Mod_GameController controller,
            Vector2 screenInput, float turn, bool shift, float seconds)
        {
            if (State == null || transitionRunning) return false;
            SpacePassengerState passenger = GetPassenger(player);
            if (GetPassengerWorld(passenger) is string expectedWorld && expectedWorld != UnityEngine.SceneManagement.SceneManager.GetActiveScene().name) return true;
            ShipState ship = GetShip(passenger.ShipId);
            if (passenger.SurfaceDescending)
            {
                Vector2 position = WorldLocalPresentation.ProjectPosition(new Vector2((float)passenger.LandingX, (float)passenger.LandingY));
                player.transform.position = position;
                return true;
            }
            if (!string.IsNullOrWhiteSpace(passenger.ConsolePieceId))
            {
                if (!FindPiece(passenger.ConsolePieceId, out ship, out ShipPieceState console) || !IsPowered(ship, console))
                    ExitConsole(player);
                else
                {
                    SetControl(console.PieceId, screenInput, turn);
                    passenger.LocalX = (console.CellX + .5d) * ship.CellSizeMeters;
                    passenger.LocalY = (console.CellY + .5d) * ship.CellSizeMeters;
                    passenger.Supported = true;
                    PlacePassenger(player, passenger, ship);
                    return true;
                }
            }
            Vector2 worldInput = controller?._mainCamera != null
                ? (Vector2)controller._mainCamera.transform.TransformDirection(screenInput) : screenInput;
            bool floor = TryGetSupport(player.transform.position, out ShipState floorShip, out _, out Vector2 local);
            bool gravity = floor && HasArtificialGravity(floorShip, local.x, local.y);
            ShipFlightState floorFlight = floor ? GetFlight(floorShip.ShipId) : null;
            bool ground = floorFlight != null && floorFlight.Phase is ShipFlightPhase.Landed or ShipFlightPhase.Floating or ShipFlightPhase.Lifting or ShipFlightPhase.SurfaceDescending;
            if (floor && (gravity || ground))
            {
                if (passenger.ShipId != floorShip.ShipId || !passenger.Supported)
                {
                    passenger.ShipId = floorShip.ShipId; passenger.LocalX = local.x; passenger.LocalY = local.y;
                    passenger.RelativeHeadingRadians = player.transform.eulerAngles.z * Math.PI / 180d - floorShip.AngleRadians;
                }
                passenger.Supported = true; passenger.GripPieceId = null; ship = floorShip;
                ShipGeometry.Rotate(worldInput.x, worldInput.y, -ship.AngleRadians, out double x, out double y);
                double multiplier = gravity ? 1d : SpaceGameplaySettings.Current.WalkingMultiplier(Universe.GetBody(floorFlight.BodyId).SurfaceGravity);
                double speed = mover.Speed.Value * seconds * multiplier;
                passenger.WalkVelocityX = x * mover.Speed.Value * multiplier; passenger.WalkVelocityY = y * mover.Speed.Value * multiplier;
                double nextX = passenger.LocalX + x * speed, nextY = passenger.LocalY + y * speed;
                int cx = (int)Math.Floor(nextX / ship.CellSizeMeters), cy = (int)Math.Floor(nextY / ship.CellSizeMeters);
                bool wall = ship.Pieces.Exists(piece => piece.IsAlive && piece.BlocksMovement &&
                    System.Linq.Enumerable.Contains(ShipGeometry.Footprint(piece), new ShipCell(cx, cy)));
                if (!wall) { passenger.LocalX = nextX; passenger.LocalY = nextY; }
                PlacePassenger(player, passenger, ship);
                Vector2 display = DisplayPoint(ship, passenger.LocalX, passenger.LocalY);
                if (!TryGetSupport(display, out _, out _, out _))
                { ReleasePassenger(passenger, ship); SetTerrainSuppression(player, false); }
                else SetTerrainSuppression(player, true);
                passenger.PreviousShift = shift;
                return true;
            }
            if (!IsSpaceView)
            {
                passenger.Supported = false; passenger.ShipId = null; passenger.ConsolePieceId = null;
                SetTerrainSuppression(player, false); return false;
            }
            passenger.IsInSpace = true;
            if (passenger.Supported && ship != null && string.IsNullOrWhiteSpace(passenger.GripPieceId)) ReleasePassenger(passenger, ship);
            if (passenger.Supported && !string.IsNullOrWhiteSpace(passenger.GripPieceId) && ship != null)
            {
                ShipGeometry.LocalToWorld(ship, passenger.LocalX, passenger.LocalY, out double gx, out double gy);
                passenger.FreeMotion.PositionMeters = new SpaceVector2(gx, gy);
            }
            ShipPieceState nearWall = null; ShipState nearShip = null;
            foreach (ShipState candidate in State.Ships)
            {
                if (GetFlight(candidate.ShipId)?.Phase is not (ShipFlightPhase.Orbit or ShipFlightPhase.Descending)) continue;
                SpaceVector2 absolute = passenger.FreeMotion.PositionMeters;
                ShipGeometry.WorldToLocal(candidate, absolute.X, absolute.Y, out double x, out double y);
                foreach (ShipPieceState piece in candidate.Pieces)
                    if (piece.IsAlive && piece.Kind is ShipPieceKind.Wall or ShipPieceKind.Door or ShipPieceKind.DockingPort &&
                        Math.Abs(x / candidate.CellSizeMeters - piece.CellX - .5d) <= 1.5d && Math.Abs(y / candidate.CellSizeMeters - piece.CellY - .5d) <= 1.5d)
                    { nearWall = piece; nearShip = candidate; break; }
                if (nearWall != null) break;
            }
            if (shift && worldInput.sqrMagnitude < .001f && nearWall != null)
            {
                if (passenger.GripPieceId != nearWall.PieceId)
                {
                    ShipGeometry.WorldToLocal(nearShip, passenger.FreeMotion.PositionMeters.X, passenger.FreeMotion.PositionMeters.Y, out passenger.LocalX, out passenger.LocalY);
                    passenger.RelativeHeadingRadians = player.transform.eulerAngles.z * Math.PI / 180d - nearShip.AngleRadians;
                    passenger.ShipId = nearShip.ShipId; passenger.GripPieceId = nearWall.PieceId;
                }
                passenger.Supported = true; PlacePassenger(player, passenger, nearShip);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(passenger.GripPieceId) && ship != null) ReleasePassenger(passenger, ship);
                if (shift && !passenger.PreviousShift && worldInput.sqrMagnitude > .001f && NearStructure(passenger.FreeMotion.PositionMeters))
                    passenger.FreeMotion.VelocityMetersPerSecond += SpaceVector2.FromVector2(worldInput.normalized) * SpaceGameplaySettings.Current.WallPushSpeedMetersPerSecond;
                if (!passenger.LandingResolved &&
                    SpacesuitSystem.TryApplyPropulsion(player, seconds, worldInput, out Vector2 deltaVelocity))
                {
                    passengerImpulses.TryGetValue(passenger.ProfileId, out SpaceVector2 pending);
                    passengerImpulses[passenger.ProfileId] = pending + SpaceVector2.FromVector2(deltaVelocity);
                }
                if (Math.Abs(turn) > .001f) player.transform.Rotate(Vector3.forward, turn * SpaceGameplaySettings.Current.PlayerTurnDegreesPerSecond * seconds, UnityEngine.Space.World);
            }
            passenger.PreviousShift = shift;
            passenger.FreeHeadingRadians = player.transform.eulerAngles.z * Math.PI / 180d;
            SetTerrainSuppression(player, floor);
            return true;
        }
        private bool NearStructure(SpaceVector2 point)
        {
            foreach (ShipState ship in State.Ships)
            {
                if (GetFlight(ship.ShipId)?.Phase is not (ShipFlightPhase.Orbit or ShipFlightPhase.Descending)) continue;
                ShipGeometry.WorldToLocal(ship, point.X, point.Y, out double x, out double y);
                foreach (ShipPieceState piece in ship.Pieces)
                    if (piece.IsAlive && Math.Abs(x / ship.CellSizeMeters - piece.CellX - .5d) <= 1.5d &&
                        Math.Abs(y / ship.CellSizeMeters - piece.CellY - .5d) <= 1.5d) return true;
            }
            return false;
        }
        public bool HasArtificialGravity(ShipState ship, double x, double y)
        {
            foreach (ShipPieceState piece in ship.Pieces)
            {
                if (!piece.IsAlive || piece.Kind != ShipPieceKind.GravityGenerator || !IsPowered(ship, piece)) continue;
                ShipDeviceState device = GetDevice(piece.PieceId);
                var environment = ShipAtmosphereService.GetEnvironment(ship, (int)Math.Floor(x / ship.CellSizeMeters), (int)Math.Floor(y / ship.CellSizeMeters), GetOutside(ship));
                if (!environment.HasFloorSupport) continue;
                double range = (device?.Configuration.GravityRadiusCells ?? 8d) * ship.CellSizeMeters;
                if (Math.Abs(x - (piece.CellX + .5d) * ship.CellSizeMeters) <= range && Math.Abs(y - (piece.CellY + .5d) * ship.CellSizeMeters) <= range) return true;
            }
            return false;
        }
        public void ReleasePassenger(SpacePassengerState passenger, ShipState ship)
        {
            ShipGeometry.LocalToWorld(ship, passenger.LocalX, passenger.LocalY, out double x, out double y);
            ShipGeometry.PointVelocity(ship, x, y, out double vx, out double vy);
            passenger.FreeMotion.PositionMeters = new SpaceVector2(x, y);
            ShipGeometry.Rotate(passenger.WalkVelocityX, passenger.WalkVelocityY, ship.AngleRadians, out double wx, out double wy);
            passenger.FreeMotion.VelocityMetersPerSecond = new SpaceVector2(vx + wx, vy + wy);
            passenger.FreeHeadingRadians = ship.AngleRadians + passenger.RelativeHeadingRadians;
            passenger.WalkVelocityX = passenger.WalkVelocityY = 0d;
            passenger.FreeMotion.ReferenceBodyId = GetFlight(ship.ShipId)?.Orbit.ReferenceBodyId;
            ShipFlightState flight = GetFlight(ship.ShipId);
            if (flight is { HeightMeters: > 0d } && flight.Phase is ShipFlightPhase.Lifting or ShipFlightPhase.SurfaceDescending)
            {
                passenger.SurfaceDescending = true; passenger.IsInSpace = false; passenger.LandingResolved = false;
                passenger.LandingBodyId = flight.BodyId; passenger.LandingWorldKey = flight.SurfaceWorldKey;
                passenger.SurfaceHeight = flight.HeightMeters; passenger.SurfaceVerticalSpeed = flight.VerticalSpeed;
                passenger.SurfaceHorizontalSpeed = flight.EntrySurfaceVelocity.X + wx;
                passenger.LandingOriginX = passenger.LandingX = x; passenger.LandingOriginY = passenger.LandingY = y;
                passenger.LandingSelected = false;
            }
            passenger.Supported = false; passenger.ShipId = null; passenger.GripPieceId = null; passenger.ConsolePieceId = null;
        }
        private void PlacePassenger(Player player, SpacePassengerState passenger, ShipState ship)
        {
            Vector2 position = DisplayPoint(ship, passenger.LocalX, passenger.LocalY);
            player.transform.position = new Vector3(position.x, position.y, player.transform.position.z);
            player.transform.rotation = Quaternion.Euler(0f, 0f, (float)((ship.AngleRadians + passenger.RelativeHeadingRadians) * 180d / Math.PI));
            Mod_Mover mover = player.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover);
            if (mover?.rb != null) { mover.rb.position = position; mover.rb.rotation = player.transform.eulerAngles.z; }
            player.itemData.transform.position = player.transform.position;
            ItemMgr.Instance.NotifyRuntimeItemMoved(player);
        }
        private void SetTerrainSuppression(Player player, bool value)
            => player.itemMods.GetMod_ByID<Mod_TileEffectReceiver>(ModText.Mod_TileEffectReceiver)?.SetEffectsSuppressed(this, value);
        public bool EnterConsole(Player player, string pieceId)
        {
            if (!FindPiece(pieceId, out ShipState ship, out ShipPieceState piece) || piece.Kind != ShipPieceKind.Console ||
                !IsPowered(ship, piece) || GetFlight(ship.ShipId).Phase == ShipFlightPhase.Floating) return false;
            if (Vector2.Distance(player.transform.position, DisplayPoint(ship, piece.CellX + .5d, piece.CellY + .5d)) > 3f) return false;
            SpacePassengerState passenger = GetPassenger(player);
            passenger.ShipId = ship.ShipId; passenger.ConsolePieceId = piece.PieceId; passenger.Supported = true;
            passenger.RelativeHeadingRadians = 0d;
            Mod_Cam camera = player.itemMods.GetMod_ByID<Mod_Cam>(ModText.Camera);
            passenger.SavedCameraSize = camera?.CurrentOrthographicSize ?? 10f;
            camera?.SetOrthographicSize(SpaceGameplaySettings.Current.DrivingCameraSize);
            return true;
        }
        public bool ExitConsole(Player player)
        {
            if (player == null || State == null) return false;
            SpacePassengerState passenger = GetPassenger(player);
            if (string.IsNullOrWhiteSpace(passenger.ConsolePieceId)) return false;
            ClearControl(passenger.ConsolePieceId); passenger.ConsolePieceId = null;
            player.itemMods.GetMod_ByID<Mod_Cam>(ModText.Camera)?.SetOrthographicSize((float)Math.Max(1d, passenger.SavedCameraSize));
            return true;
        }
        private void StepFreePassengers(double seconds)
        {
            foreach (SpacePassengerState passenger in State.Passengers)
            {
                Player player = ItemMgr.Instance?.User_Player;
                if (passenger.SurfaceDescending)
                {
                    BodyState body = Universe.GetBody(passenger.LandingBodyId);
                    passenger.SurfaceVerticalSpeed -= body.SurfaceGravity * seconds;
                    passenger.SurfaceHeight += passenger.SurfaceVerticalSpeed * seconds;
                    if (passenger.SurfaceHeight > 0d) continue;
                    passenger.SurfaceHeight = 0d; passenger.SurfaceDescending = false; passenger.LandingResolved = true;
                    if (player != null && player.ProfileName == passenger.ProfileId)
                    {
                        float damage = (float)(Math.Max(0d, -passenger.SurfaceVerticalSpeed / SpaceGameplaySettings.Current.ImpactStoppingSeconds + body.SurfaceGravity) *
                            Math.Max(1d, player.itemData.Stack.CurrentWeight) / SpaceGameplaySettings.Current.ImpactForcePerDurability);
                        damage += (float)(.5d * Math.Max(1d, player.itemData.Stack.CurrentWeight) * passenger.SurfaceHorizontalSpeed * passenger.SurfaceHorizontalSpeed /
                            Math.Max(1d, SpaceGameplaySettings.Current.ImpactJoulesPerDurability));
                        damage = SpacesuitSystem.AbsorbLandingImpact(player, damage);
                        player.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp)?.ApplyStructuralDamage(damage);
                        Vector2 point = WorldLocalPresentation.ProjectPosition(new Vector2((float)passenger.LandingX, (float)passenger.LandingY));
                        player.transform.position = point;
                        player.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover)?.rb?.MovePosition(point);
                    }
                    continue;
                }
                if (!passenger.IsInSpace || passenger.Supported || passenger.LandingResolved) continue;
                if (passengerImpulses.Remove(passenger.ProfileId, out SpaceVector2 delta)) passenger.FreeMotion.VelocityMetersPerSecond += delta;
                passenger.FreeMotion.RadiusMeters = .3d;
                SpaceVector2 previousPosition = passenger.FreeMotion.PositionMeters;
                bool touched = Universe.StepOrbit(passenger.FreeMotion, default, seconds, out SurfaceContact contact);
                bool hitShip = ResolveFreePassengerContact(passenger, previousPosition, seconds, out _, touched ? contact.Fraction : 1d);
                if (hitShip || !touched) continue;
                SpaceVector2 normal = SpaceVector2.FromAngle(contact.SurfaceAngleRadians);
                SpaceVector2 center = SurfaceCenter(contact.BodyId, contact.SurfaceAngleRadians);
                passenger.SurfaceDescending = true; passenger.IsInSpace = false;
                passenger.LandingBodyId = contact.BodyId; passenger.LandingWorldKey = Universe.GetSurfaceAddress(contact.BodyId).WorldKey;
                passenger.LandingOriginX = center.X; passenger.LandingOriginY = center.Y;
                passenger.SurfaceHeight = SpaceGameplaySettings.Current.SurfaceEntryHeightMeters;
                passenger.SurfaceVerticalSpeed = Math.Min(0d, SpaceVector2.Dot(contact.RelativeVelocityMetersPerSecond, normal));
                passenger.SurfaceHorizontalSpeed = SpaceVector2.Cross(normal, contact.RelativeVelocityMetersPerSecond);
                passenger.LandingRandomState = passenger.LandingRandomState == 1 ? unchecked((ulong)(uint)Universe.GetBody(contact.BodyId).Seed) | 1UL : passenger.LandingRandomState;
                double randomAngle = (double)FluidRandom.Next01(ref passenger.LandingRandomState) * Math.PI * 2d;
                double randomRadius = Math.Sqrt((double)FluidRandom.Next01(ref passenger.LandingRandomState)) * SpaceGameplaySettings.Current.LandingChoiceRadiusMeters;
                passenger.LandingX = center.X + Math.Cos(randomAngle) * randomRadius;
                passenger.LandingY = center.Y + Math.Sin(randomAngle) * randomRadius;
                passenger.LandingSelected = false;
                if (player != null && player.ProfileName == passenger.ProfileId)
                    StartCoroutine(TransferPlayerToSurface(passenger.LandingWorldKey, new Vector2((float)passenger.LandingX, (float)passenger.LandingY)));
            }
        }
        public void RestorePassengerPose(Player player)
        {
            if (player == null || State == null) return;
            SpacePassengerState passenger = GetPassenger(player);
            ShipState ship = GetShip(passenger.ShipId);
            if (IsSpaceView)
                ViewOrigin = ship != null && passenger.Supported ? new SpaceVector2(ship.PositionX, ship.PositionY) : passenger.FreeMotion.PositionMeters;
            if (ship != null && passenger.Supported && IsVisible(ship)) PlacePassenger(player, passenger, ship);
            else if (passenger.SurfaceDescending && !IsSpaceView && passenger.LandingWorldKey == UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)
                player.transform.position = WorldLocalPresentation.ProjectPosition(new Vector2((float)passenger.LandingX, (float)passenger.LandingY));
            else if (IsSpaceView && passenger.IsInSpace)
            {
                player.transform.position = ProjectPosition(passenger.FreeMotion.PositionMeters);
                player.transform.rotation = Quaternion.Euler(0f, 0f, (float)(passenger.FreeHeadingRadians * 180d / Math.PI));
            }
            Mod_Mover mover = player.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover);
            if (mover?.rb != null) { mover.rb.position = player.transform.position; mover.rb.rotation = player.transform.eulerAngles.z; }
        }
        #endregion
    }
}
