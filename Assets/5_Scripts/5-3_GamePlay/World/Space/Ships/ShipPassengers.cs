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
            SpaceSectorPosition position = IsSpaceView ? UnprojectSectorPosition(player.transform.position)
                : new SpaceSectorPosition(default, SpaceVector2.FromVector2(WorldLocalPresentation.ToLogical(player.transform.position)));
            passenger = new SpacePassengerState { ProfileId = player.ProfileName, IsInSpace = IsSpaceView,
                LandingWorldKey = IsSpaceView ? null : player.Data.CurrentSceneName,
                FreeMotion = new SpaceMotionState { Sector = position.Sector, PositionMeters = position.LocalMeters } };
            State.Passengers.Add(passenger); return passenger;
        }
        public bool HandlePassengerInput(Player player, Mod_Mover mover, Mod_GameController controller,
            Vector2 screenInput, float turn, float seconds)
        {
            if (State == null || transitionRunning) return false;
            mover.SetSpaceUpright(IsSpaceView);
            SpacePassengerState passenger = GetPassenger(player);
            if (!FlatWorld.Networking.GameNetwork.HasStateAuthority && (passenger.IsInSpace || passenger.Supported))
            {
                Vector2 command = !string.IsNullOrEmpty(passenger.ConsolePieceId) || controller?._mainCamera == null
                    ? screenInput : (Vector2)controller._mainCamera.transform.TransformDirection(screenInput);
                SpaceNetworkBridge.SubmitInput(player.ProfileName, new SpacePassengerInput { MoveX = command.x, MoveY = command.y, Turn = turn });
                return true;
            }
            if (GetPassengerWorld(passenger) is string expectedWorld && !SpaceMgr.IsSpaceScene(expectedWorld) && expectedWorld != UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)
            {
                return true;
            }
            ShipState ship = GetShip(passenger.ShipId);
            UpdateDescentControl(player, passenger, ship);
            if (passenger.SurfaceDescending)
            {
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
                    SetControl(console.PieceId, screenInput, turn);
                    passenger.Supported = true;
                    PlacePassenger(player, passenger, ship);
                    return true;
                }
            }
            Vector2 worldInput = controller?._mainCamera != null
                ? (Vector2)controller._mainCamera.transform.TransformDirection(screenInput) : screenInput;
            // 已登船乘员用权威本地位置校验支撑，避免高速航行时旧画面位置把乘员甩出船。
            ShipState floorShip = ship;
            Vector2 local = new((float)passenger.LocalX, (float)passenger.LocalY);
            bool floor = passenger.Supported && ship != null && ShipAtmosphereService.HasFloor(ship,
                (int)Math.Floor(passenger.LocalX / ship.CellSizeMeters), (int)Math.Floor(passenger.LocalY / ship.CellSizeMeters));
            if (!floor) floor = TryGetSupport(player.transform.position, out floorShip, out _, out local);
            bool gravity = floor && HasArtificialGravity(floorShip, local.x, local.y);
            ShipFlightState floorFlight = floor ? GetFlight(floorShip.ShipId) : null;
            bool ground = floorFlight != null && floorFlight.Phase is ShipFlightPhase.Landed or ShipFlightPhase.Floating or ShipFlightPhase.Lifting or ShipFlightPhase.SurfaceDescending;
            if (floor && (gravity || ground))
            {
                if (passenger.ShipId != floorShip.ShipId || !passenger.Supported)
                {
                    passenger.ShipId = floorShip.ShipId; passenger.LocalX = local.x; passenger.LocalY = local.y;
                }
                passenger.Supported = true; ship = floorShip;
                ShipGeometry.Rotate(worldInput.x, worldInput.y, -ship.AngleRadians, out double x, out double y);
                double multiplier = gravity ? 1d : SpaceGameplaySettings.Current.WalkingMultiplier(GetBody(floorFlight.BodyId).SurfaceGravity);
                double speed = mover.Speed.Value * seconds * multiplier;
                passenger.WalkVelocityX = x * mover.Speed.Value * multiplier; passenger.WalkVelocityY = y * mover.Speed.Value * multiplier;
                double nextX = passenger.LocalX + x * speed, nextY = passenger.LocalY + y * speed;
                int cx = (int)Math.Floor(nextX / ship.CellSizeMeters), cy = (int)Math.Floor(nextY / ship.CellSizeMeters);
                bool wall = ship.Pieces.Exists(piece => piece.IsAlive && piece.BlocksPassage &&
                    System.Linq.Enumerable.Contains(ShipGeometry.Footprint(piece), new ShipCell(cx, cy)));
                if (!wall) { passenger.LocalX = nextX; passenger.LocalY = nextY; }
                PlacePassenger(player, passenger, ship);
                if (!ShipAtmosphereService.HasFloor(ship, (int)Math.Floor(passenger.LocalX / ship.CellSizeMeters),
                        (int)Math.Floor(passenger.LocalY / ship.CellSizeMeters)))
                { ReleasePassenger(passenger, ship); SetTerrainSuppression(player, false); }
                else SetTerrainSuppression(player, true);
                return true;
            }
            if (!IsSpaceView)
            {
                passenger.Supported = false; passenger.ShipId = null; passenger.ConsolePieceId = null;
                SetTerrainSuppression(player, false); return false;
            }
            passenger.IsInSpace = true;
            if (passenger.Supported && ship != null) ReleasePassenger(passenger, ship);
            SetTerrainSuppression(player, floor);
            return true;
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
            passenger.FreeMotion.Sector = ship.Sector;
            passenger.FreeMotion.PositionMeters = new SpaceVector2(x, y);
            ShipGeometry.Rotate(passenger.WalkVelocityX, passenger.WalkVelocityY, ship.AngleRadians, out double wx, out double wy);
            passenger.FreeMotion.VelocityMetersPerSecond = new SpaceVector2(vx + wx, vy + wy);
            passenger.FreeWalkVelocity = new SpaceVector2(wx, wy);
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
            const float heading = 0f;
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
            if (SpaceNetworkBridge.SendIntent(player.ProfileName, new SpaceNetworkIntent { Kind = SpaceNetworkIntentKind.EnterConsole, PieceId = pieceId })) return true;
            if (!FindPiece(pieceId, out ShipState ship, out ShipPieceState piece) || piece.Kind != ShipPieceKind.Console ||
                !IsPowered(ship, piece) || GetFlight(ship.ShipId).Phase == ShipFlightPhase.Floating) return false;
            if (Vector2.Distance(player.transform.position, DisplayPoint(ship, piece.CellX + .5d, piece.CellY + .5d)) > 3f) return false;
            SpacePassengerState passenger = GetPassenger(player);
            bool alreadyDriving = !string.IsNullOrWhiteSpace(passenger.ConsolePieceId);
            SpaceSectorPosition position = IsSpaceView ? UnprojectSectorPosition(player.transform.position)
                : new SpaceSectorPosition(default, SpaceVector2.FromVector2(WorldLocalPresentation.ToLogical(player.transform.position)));
            BindConsoleControl(passenger, ship, piece, position, IsSpaceView);
            ShipFlightState flight = GetFlight(ship.ShipId);
            if (flight.Phase == ShipFlightPhase.SurfaceDescending || flight.Phase == ShipFlightPhase.Lifting && flight.VerticalSpeed < 0d)
                passenger.DescentControlEntered = passenger.DescentDriving = true;
            Mod_Cam camera = player.itemMods.GetMod_ByID<Mod_Cam>(ModText.Camera);
            if (!alreadyDriving) passenger.SavedCameraSize = camera?.CurrentOrthographicSize ?? 10f;
            camera?.SetOrthographicSize(SpaceGameplaySettings.Current.DrivingCameraSize);
            return true;
        }
        private void BindConsoleControl(SpacePassengerState passenger, ShipState ship, ShipPieceState console,
            SpaceSectorPosition position, bool inSpace)
        {
            // 原地接管保留船上站位，首次绑定也从玩家当前位置换算，避免落入控制台碰撞体。
            if (!passenger.Supported || passenger.ShipId != ship.ShipId)
            {
                SpaceVector2 point = inSpace ? SpaceSectorCoordinates.Relative(position, ship.Sector)
                    : SpaceVector2.FromVector2(WorldLocalPresentation.ProjectPosition(position.LocalMeters.ToVector2(),
                        new Vector2((float)ship.PositionX, (float)ship.PositionY)));
                ShipGeometry.WorldToLocal(ship, point.X, point.Y, out passenger.LocalX, out passenger.LocalY);
            }
            if (!string.IsNullOrWhiteSpace(passenger.ConsolePieceId)) ClearControl(passenger.ConsolePieceId);
            passenger.ShipId = ship.ShipId; passenger.ConsolePieceId = console.PieceId; passenger.Supported = true;
        }
        public bool ExitConsole(Player player)
        {
            if (player == null || State == null) return false;
            if (SpaceNetworkBridge.SendIntent(player.ProfileName, new SpaceNetworkIntent { Kind = SpaceNetworkIntentKind.ExitConsole })) return true;
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
            if (!passenger.Supported || !IsInPresentationWorld(ship) || string.IsNullOrWhiteSpace(passenger.ConsolePieceId))
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
            Vector2 position = IsSpaceView ? ProjectPosition(new SpaceSectorPosition(assembly?.Sector ?? ship.Sector, center)) : WorldLocalPresentation.ProjectPosition(center.ToVector2());
            ShipFlightState flight = GetFlight(ship.ShipId);
            double height = flight != null && flight.Phase is ShipFlightPhase.Lifting or ShipFlightPhase.SurfaceDescending ? flight.HeightMeters : 0d;
            float rise = (float)ShipLandingGuidance.SurfaceVisualRise(height);
            drivingCameraTarget.position = new Vector3(position.x, position.y + rise, player.transform.position.z);
            drivingCameraTarget.rotation = Quaternion.identity;
            if (drivingCamera != camera)
            {
                drivingCamera?.ClearFollowOverride(drivingCameraTarget);
                drivingCamera = camera;
                camera.SetOrthographicSize(SpaceGameplaySettings.Current.DrivingCameraSize);
            }
            camera.SetFollowTargetRotation(false);
            SpaceGameplaySettings settings = SpaceGameplaySettings.Current;
            camera.SetFollowOverride(drivingCameraTarget,
                IsSpaceView ? settings.DrivingCameraMaxSize : 0f,
                IsSpaceView ? settings.DrivingCameraZoomMultiplier : 1f);
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
                Player player = FindNetworkPlayer(passenger.ProfileId);
                if (passenger.SurfaceDescending)
                {
                    BodyState body = GetBody(passenger.LandingBodyId);
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
                Mod_Mover mover = player != null && player.ProfileName == passenger.ProfileId
                    ? player.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover) : null;
                // 主动推力只修改自己的速度分量，松键保留全部漂移与碰撞冲量。
                if (mover != null)
                {
                    SpaceVector2 walking = SpaceVector2.FromVector2(passengerInputs.TryGetValue(passenger.ProfileId, out SpacePassengerInput input)
                        ? mover.SmoothSpaceVelocity(passenger.FreeWalkVelocity.ToVector2(), (float)seconds, new Vector2(input.MoveX, input.MoveY))
                        : mover.SmoothSpaceVelocity(passenger.FreeWalkVelocity.ToVector2(), (float)seconds));
                    passenger.UpdateFreeWalkVelocity(walking);
                }
                passenger.FreeMotion.RadiusMeters = .3d;
            }
        }
        public void RestorePassengerPose(Player player)
        {
            if (player == null || State == null) return;
            SpacePassengerState passenger = GetPassenger(player);
            ShipState ship = GetShip(passenger.ShipId);
            if (IsSpaceView)
                { SpaceSectorPosition position = GetPassengerPosition(passenger); ViewSector = position.Sector; ViewOrigin = position.LocalMeters; }
            if (passenger.Supported && IsInPresentationWorld(ship)) PlacePassenger(player, passenger, ship);
            else if (passenger.SurfaceDescending && !IsSpaceView && passenger.LandingWorldKey == UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)
                player.transform.position = WorldLocalPresentation.ProjectPosition(new Vector2((float)passenger.LandingX, (float)passenger.LandingY));
            else if (!passenger.Supported && IsSpaceView && passenger.IsInSpace)
            {
                player.transform.position = ProjectPosition(new SpaceSectorPosition(passenger.FreeMotion.Sector, passenger.FreeMotion.PositionMeters));
                player.transform.rotation = Quaternion.identity;
            }
            Mod_Mover mover = player.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover);
            mover?.SetSpaceUpright(IsSpaceView);
            if (mover?.rb != null) { mover.rb.position = player.transform.position; mover.rb.rotation = player.transform.eulerAngles.z; }
        }
        #endregion
    }
}
