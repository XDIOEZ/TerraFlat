using System;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 乘员支撑与惯性
        public SpacePassengerState GetPassenger(Player player)
        {
            SpacePassengerState passenger = State.Passengers.Find(value => value.ProfileId == player.ProfileName);
            if (passenger != null) return passenger;
            passenger = new SpacePassengerState { ProfileId = player.ProfileName, IsInSpace = IsSpaceView,
                FreeMotion = new SpaceMotionState { PositionMeters = IsSpaceView ? UnprojectPosition(player.transform.position)
                    : SpaceVector2.FromVector2(player.transform.position) } };
            State.Passengers.Add(passenger); return passenger;
        }
        public bool HandlePassengerInput(Player player, Mod_Mover mover, Mod_GameController controller,
            Vector2 screenInput, float turn, float seconds)
        {
            if (State == null || transitionRunning) return false;
            SpacePassengerState passenger = GetPassenger(player);
            if (GetPassengerWorld(passenger) is string expectedWorld && expectedWorld != UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)
            {
                mover.SetPointerBodyRotation(false);
                return true;
            }
            ShipState ship = GetShip(passenger.ShipId);
            UpdateDescentControl(player, passenger, ship);
            if (passenger.SurfaceDescending)
            {
                mover.SetPointerBodyRotation(false);
                Vector2 position = WorldLocalPresentation.ProjectPosition(new Vector2((float)passenger.LandingX, (float)passenger.LandingY));
                player.transform.position = position;
                return true;
            }
            if (!string.IsNullOrWhiteSpace(passenger.ConsolePieceId))
            {
                if (!FindPiece(passenger.ConsolePieceId, out ship, out ShipPieceState console) || !console.IsAlive ||
                    console.Kind != ShipPieceKind.Console || !passenger.DescentDriving && !IsPowered(ship, console))
                    ExitConsole(player);
                else
                {
                    mover.SetPointerBodyRotation(false);
                    SetControl(console.PieceId, screenInput, turn);
                    if (!passenger.DescentDriving)
                    {
                        passenger.LocalX = (console.CellX + .5d) * ship.CellSizeMeters;
                        passenger.LocalY = (console.CellY + .5d) * ship.CellSizeMeters;
                    }
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
                mover.SetPointerBodyRotation(false);
                if (passenger.ShipId != floorShip.ShipId || !passenger.Supported)
                {
                    passenger.ShipId = floorShip.ShipId; passenger.LocalX = local.x; passenger.LocalY = local.y;
                    passenger.RelativeHeadingRadians = player.transform.eulerAngles.z * Math.PI / 180d - floorShip.AngleRadians;
                }
                passenger.Supported = true; ship = floorShip;
                ShipGeometry.Rotate(worldInput.x, worldInput.y, -ship.AngleRadians, out double x, out double y);
                double multiplier = gravity ? 1d : SpaceGameplaySettings.Current.WalkingMultiplier(Universe.GetBody(floorFlight.BodyId).SurfaceGravity);
                double speed = mover.Speed.Value * seconds * multiplier;
                passenger.WalkVelocityX = x * mover.Speed.Value * multiplier; passenger.WalkVelocityY = y * mover.Speed.Value * multiplier;
                double nextX = passenger.LocalX + x * speed, nextY = passenger.LocalY + y * speed;
                int cx = (int)Math.Floor(nextX / ship.CellSizeMeters), cy = (int)Math.Floor(nextY / ship.CellSizeMeters);
                bool wall = ship.Pieces.Exists(piece => piece.IsAlive && piece.BlocksPassage &&
                    System.Linq.Enumerable.Contains(ShipGeometry.Footprint(piece), new ShipCell(cx, cy)));
                if (!wall) { passenger.LocalX = nextX; passenger.LocalY = nextY; }
                PlacePassenger(player, passenger, ship);
                Vector2 display = DisplayPoint(ship, passenger.LocalX, passenger.LocalY);
                if (!TryGetSupport(display, out _, out _, out _))
                { ReleasePassenger(passenger, ship); SetTerrainSuppression(player, false); }
                else SetTerrainSuppression(player, true);
                return true;
            }
            if (!IsSpaceView)
            {
                mover.SetPointerBodyRotation(false);
                passenger.Supported = false; passenger.ShipId = null; passenger.ConsolePieceId = null;
                SetTerrainSuppression(player, false); return false;
            }
            mover.SetPointerBodyRotation(true);
            passenger.IsInSpace = true;
            if (passenger.Supported && ship != null) ReleasePassenger(passenger, ship);
            TurnPassengerToPointer(player, mover, controller, seconds);
            passenger.FreeHeadingRadians = player.transform.eulerAngles.z * Math.PI / 180d;
            SetTerrainSuppression(player, floor);
            return true;
        }

        private void TurnPassengerToPointer(Player player, Mod_Mover mover, Mod_GameController controller, float seconds)
        {
            if (mover.rb != null) mover.rb.angularVelocity = 0f;
            if (controller == null || controller.IsGameplayInputLocked ||
                player.itemMods.GetMod_ByID<Mod_TurnBack>(ModText.TrunBody)?.IsDirectionLocked == true ||
                !mover.TryGetPointerBodyTargetHeading(controller, out float targetHeading)) return;

            // 失重时沿最短角度转动整个玩家，鼠标贴近中心时保留原来的朝向。
            float heading = Mathf.MoveTowardsAngle(player.transform.eulerAngles.z, targetHeading,
                SpaceGameplaySettings.Current.PlayerTurnDegreesPerSecond * seconds);
            player.transform.rotation = Quaternion.Euler(0f, 0f, heading);
            if (mover.rb != null) mover.rb.rotation = heading;
        }

        public bool HasArtificialGravity(ShipState ship, double x, double y)
        {
            // 重力只需要确认地板支撑，不读取昂贵的船外地表与舱气状态。
            if (!ShipAtmosphereService.HasFloor(ship, (int)Math.Floor(x / ship.CellSizeMeters),
                    (int)Math.Floor(y / ship.CellSizeMeters))) return false;
            foreach (ShipPieceState piece in ship.Pieces)
            {
                if (!piece.IsAlive || piece.Kind != ShipPieceKind.GravityGenerator || !IsPowered(ship, piece)) continue;
                ShipDeviceState device = GetDevice(piece.PieceId);
                double range = (device?.Configuration.GravityRadiusCells ?? 8d) * ship.CellSizeMeters;
                if (Math.Abs(x - (piece.CellX + .5d) * ship.CellSizeMeters) <= range && Math.Abs(y - (piece.CellY + .5d) * ship.CellSizeMeters) <= range) return true;
            }
            return false;
        }
        public void ReleasePassenger(SpacePassengerState passenger, ShipState ship)
        {
            Player localPlayer = ItemMgr.Instance?.User_Player;
            if (localPlayer != null && passenger.ProfileId == localPlayer.ProfileName) ExitConsole(localPlayer);
            ShipGeometry.LocalToWorld(ship, passenger.LocalX, passenger.LocalY, out double x, out double y);
            ShipGeometry.PointVelocity(ship, x, y, out double vx, out double vy);
            passenger.FreeMotion.PositionMeters = new SpaceVector2(x, y);
            ShipGeometry.Rotate(passenger.WalkVelocityX, passenger.WalkVelocityY, ship.AngleRadians, out double wx, out double wy);
            passenger.FreeMotion.VelocityMetersPerSecond = new SpaceVector2(vx + wx, vy + wy);
            passenger.FreeHeadingRadians = ship.AngleRadians + passenger.RelativeHeadingRadians;
            passenger.WalkVelocityX = passenger.WalkVelocityY = 0d;
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
            passenger.Supported = false; passenger.ShipId = null; passenger.ConsolePieceId = null;
        }
        private void PlacePassenger(Player player, SpacePassengerState passenger, ShipState ship)
        {
            Vector2 position = DisplayPoint(ship, passenger.LocalX, passenger.LocalY);
            float heading = (float)((ship.AngleRadians + passenger.RelativeHeadingRadians) * 180d / Math.PI);
            Mod_Mover mover = player.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover);
            // 视觉起飞时玩家物理位置未变，不重复触发物品空间索引和物理同步。
            if (GetFlight(ship.ShipId)?.Phase == ShipFlightPhase.Lifting &&
                ((Vector2)player.transform.position - position).sqrMagnitude < .000001f &&
                Mathf.Abs(Mathf.DeltaAngle(player.transform.eulerAngles.z, heading)) < .001f &&
                (mover?.rb == null || ((mover.rb.position - position).sqrMagnitude < .000001f &&
                    Mathf.Abs(Mathf.DeltaAngle(mover.rb.rotation, heading)) < .001f)))
                return;
            player.transform.position = new Vector3(position.x, position.y, player.transform.position.z);
            player.transform.rotation = Quaternion.Euler(0f, 0f, heading);
            if (mover?.rb != null) { mover.rb.position = position; mover.rb.rotation = heading; }
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
            bool alreadyDriving = !string.IsNullOrWhiteSpace(passenger.ConsolePieceId);
            if (alreadyDriving) ClearControl(passenger.ConsolePieceId);
            passenger.ShipId = ship.ShipId; passenger.ConsolePieceId = piece.PieceId; passenger.Supported = true;
            passenger.RelativeHeadingRadians = 0d;
            ShipFlightState flight = GetFlight(ship.ShipId);
            if (flight.Phase == ShipFlightPhase.SurfaceDescending || flight.Phase == ShipFlightPhase.Lifting && flight.VerticalSpeed < 0d)
                passenger.DescentControlEntered = passenger.DescentDriving = true;
            Mod_Cam camera = player.itemMods.GetMod_ByID<Mod_Cam>(ModText.Camera);
            if (!alreadyDriving) passenger.SavedCameraSize = camera?.CurrentOrthographicSize ?? 10f;
            camera?.SetOrthographicSize(SpaceGameplaySettings.Current.DrivingCameraSize);
            return true;
        }
        public bool ExitConsole(Player player)
        {
            if (player == null || State == null) return false;
            SpacePassengerState passenger = GetPassenger(player);
            if (string.IsNullOrWhiteSpace(passenger.ConsolePieceId)) return false;
            ClearControl(passenger.ConsolePieceId); passenger.ConsolePieceId = null;
            passenger.DescentDriving = false;
            player.itemMods.GetMod_ByID<Mod_Cam>(ModText.Camera)?.SetOrthographicSize((float)Math.Max(1d, passenger.SavedCameraSize));
            if (player == ItemMgr.Instance?.User_Player) ReleaseDrivingCamera();
            return true;
        }

        // 一次下降只自动接管一次，Esc 退出后可在控制台自行重新驾驶。
        private void UpdateDescentControl(Player player, SpacePassengerState passenger, ShipState ship)
        {
            ShipFlightState flight = ship != null ? GetFlight(ship.ShipId) : null;
            bool surfaceFlight = flight != null && flight.Phase is ShipFlightPhase.Lifting or ShipFlightPhase.SurfaceDescending;
            if (!surfaceFlight)
            {
                EndDescentControl(player);
                passenger.DescentControlEntered = false;
                return;
            }
            if (!passenger.Supported || passenger.DescentControlEntered || flight.HeightMeters <= 0d ||
                flight.VerticalSpeed >= 0d || player != ItemMgr.Instance?.User_Player) return;
            ShipPieceState console = ship.Pieces.Find(piece => piece.IsAlive && piece.Kind == ShipPieceKind.Console &&
                piece.PieceId == passenger.ConsolePieceId);
            console ??= ship.Pieces.Find(piece => piece.IsAlive && piece.Kind == ShipPieceKind.Console && IsPowered(ship, piece));
            console ??= ship.Pieces.Find(piece => piece.IsAlive && piece.Kind == ShipPieceKind.Console);
            if (console == null) return;
            if (string.IsNullOrWhiteSpace(passenger.ConsolePieceId))
                passenger.SavedCameraSize = player.itemMods.GetMod_ByID<Mod_Cam>(ModText.Camera)?.CurrentOrthographicSize ?? 10f;
            else ClearControl(passenger.ConsolePieceId);
            passenger.ConsolePieceId = console.PieceId;
            passenger.DescentControlEntered = passenger.DescentDriving = true;
            CancelNavigation(ship.ShipId);
            pickingNavigationConsole = null;
            ShipPanelSession.CloseForPlayer(this, player);
        }

        private void EndDescentControl(Player player)
        {
            if (player == null || State == null) return;
            SpacePassengerState passenger = GetPassenger(player);
            if (!passenger.DescentDriving) return;
            ExitConsole(player);
            passenger.DescentDriving = false;
        }

        private Transform drivingCameraTarget;
        private Mod_Cam drivingCamera;

        private void UpdateDrivingCamera(Player player, SpacePassengerState passenger, ShipState ship)
        {
            if (!passenger.Supported || ship == null || !IsVisible(ship) || string.IsNullOrWhiteSpace(passenger.ConsolePieceId))
            {
                ReleaseDrivingCamera();
                return;
            }
            Mod_Cam camera = player.itemMods.GetMod_ByID<Mod_Cam>(ModText.Camera);
            if (camera == null || !camera.IsCameraReady) return;
            if (drivingCameraTarget == null)
            {
                drivingCameraTarget = new GameObject("飞船驾驶镜头中心").transform;
                drivingCameraTarget.SetParent(transform, false);
            }
            ShipAssemblyState assembly = GetConnectedAssembly(ship.ShipId);
            SpaceVector2 center = assembly != null ? new SpaceVector2(assembly.PositionX, assembly.PositionY)
                : new SpaceVector2(ship.PositionX, ship.PositionY);
            Vector2 position = IsSpaceView ? ProjectPosition(center) : WorldLocalPresentation.ProjectPosition(center.ToVector2());
            ShipFlightState flight = GetFlight(ship.ShipId);
            double height = flight != null && flight.Phase is ShipFlightPhase.Lifting or ShipFlightPhase.SurfaceDescending ? flight.HeightMeters : 0d;
            float rise = (float)Math.Min(4d, Math.Max(0d, height) * .04d);
            drivingCameraTarget.position = new Vector3(position.x, position.y + rise, player.transform.position.z);
            drivingCameraTarget.rotation = Quaternion.Euler(0f, 0f, (float)(ship.AngleRadians * 180d / Math.PI));
            if (drivingCamera != camera)
            {
                drivingCamera?.ClearFollowOverride(drivingCameraTarget);
                drivingCamera = camera;
                camera.SetOrthographicSize(SpaceGameplaySettings.Current.DrivingCameraSize);
            }
            camera.SetFollowTargetRotation(true);
            camera.SetFollowOverride(drivingCameraTarget);
        }

        private void ReleaseDrivingCamera()
        {
            if (drivingCameraTarget != null) drivingCamera?.ClearFollowOverride(drivingCameraTarget);
            drivingCamera = null;
        }

        private void DestroyDrivingCameraTarget()
        {
            ReleaseDrivingCamera();
            if (drivingCameraTarget != null) Destroy(drivingCameraTarget.gameObject);
            drivingCameraTarget = null;
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
                SpaceGameplaySettings config = SpaceGameplaySettings.Current;
                SpaceVector2 velocity = passenger.FreeMotion.VelocityMetersPerSecond;
                Mod_Mover mover = player != null && player.ProfileName == passenger.ProfileId && IsSpaceView
                    ? player.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover) : null;
                // WASD 的速度响应在权威固定步内结算，松键与反向输入都保留惯性。
                if (mover != null) velocity = SpaceVector2.FromVector2(mover.SmoothSpaceVelocity(velocity.ToVector2(), (float)seconds));
                double speed = velocity.Magnitude;
                if (speed > config.FreeFloatMaxSpeedMetersPerSecond)
                    velocity *= config.FreeFloatMaxSpeedMetersPerSecond / speed;
                passenger.FreeMotion.VelocityMetersPerSecond = velocity;
                passenger.FreeMotion.RadiusMeters = .3d;
                SpaceVector2 previousPosition = passenger.FreeMotion.PositionMeters;
                bool touched = Universe.StepMotion(passenger.FreeMotion, default, seconds, out SurfaceContact contact);
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
                ViewOrigin = GetPassengerViewPoint(passenger);
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
