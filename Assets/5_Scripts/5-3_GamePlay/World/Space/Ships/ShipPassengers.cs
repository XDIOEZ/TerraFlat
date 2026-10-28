using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using Newtonsoft.Json;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 乘员支撑与惯性
        /// <summary>太空热路径按身份读取既有乘员，不为 List.Find 创建捕获委托。</summary>
        private SpacePassengerState FindPassenger(string profileId)
        {
            var passengers = State?.Passengers;
            if (passengers == null) return null;
            for (int i = 0; i < passengers.Count; i++)
            {
                SpacePassengerState passenger = passengers[i];
                if (passenger != null && passenger.ProfileId == profileId)
                    return passenger;
            }
            return null;
        }

        public SpacePassengerState GetPassenger(Player player)
        {
            SpacePassengerState passenger = FindPassenger(player.ProfileName);
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
            SpacePassengerState passenger = GetPassenger(player);
            if (IsPlayerInShipInteriorWorld(player, out ShipState interiorShip, out _))
                return HandleShipInteriorPassengerInput(player, passenger, mover, controller, screenInput, turn, interiorShip);

            mover.SetSpaceUpright(IsSpaceView);
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
            if (passenger.Supported && ship != null && TryEnterShipInteriorWorld(player, passenger,
                    ship, new Vector2((float)passenger.LocalX, (float)passenger.LocalY)))
                return HandleShipInteriorPassengerInput(player, passenger, mover, controller, screenInput, turn, ship);
            UpdateDescentControl(player, passenger, ship);
            if (passenger.SurfaceDescending)
            {
                PlaceDescendingPassenger(player, passenger);
                return true;
            }
            if (!string.IsNullOrWhiteSpace(passenger.ConsolePieceId))
            {
                if (!FindPiece(passenger.ConsolePieceId, out ship, out ShipPieceState console) || !console.IsOperational ||
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
            if (!floor) floor = TryGetSupport(GetPlayerAuthoritativeDisplayPosition(player), out floorShip, out _, out local, false);
            if (floor)
            {
                passenger.ShipId = floorShip.ShipId;
                passenger.Supported = true;
                passenger.LocalX = local.x;
                passenger.LocalY = local.y;
                passenger.IsInSpace = IsSpaceView;
                ship = floorShip;
                if (TryEnterShipInteriorWorld(player, passenger, ship, local))
                    return HandleShipInteriorPassengerInput(player, passenger, mover, controller, screenInput, turn, ship);
            }
            if (!IsSpaceView)
            {
                passenger.Supported = false; passenger.ShipId = null; passenger.ConsolePieceId = null;
                RestorePassengerSurface(player);
                SetTerrainSuppression(player, false); return false;
            }
            passenger.IsInSpace = true;
            if (passenger.Supported && ship != null) ReleasePassenger(passenger, ship);
            SetTerrainSuppression(player, floor);
            return true;
        }

        private bool HandleShipInteriorPassengerInput(Player player, SpacePassengerState passenger, Mod_Mover mover,
            Mod_GameController controller, Vector2 screenInput, float turn, ShipState ship)
        {
            mover.SetSpaceUpright(false);
            if (ship == null || mover.rb == null) return false;
            passenger.LocalX = mover.rb.position.x;
            passenger.LocalY = mover.rb.position.y;
            Vector2 local = mover.rb.position;
            bool floor = ShipAtmosphereService.HasFloor(ship,
                (int)Math.Floor(local.x / ship.CellSizeMeters), (int)Math.Floor(local.y / ship.CellSizeMeters));
            // 客户端发送显示坐标输入，同时让普通刚体保留本地预测。
            if (!FlatWorld.Networking.GameNetwork.HasStateAuthority && player.IsLocalProfile)
            {
                Vector2 command = controller?._mainCamera != null
                    ? (Vector2)controller._mainCamera.transform.TransformDirection(screenInput)
                    : screenInput;
                SpaceNetworkBridge.SubmitInput(player.ProfileName,
                    new SpacePassengerInput { MoveX = command.x, MoveY = command.y, Turn = turn });
            }
            if (!floor)
            {
                if (!FlatWorld.Networking.GameNetwork.HasStateAuthority) return false;
                ReleasePassenger(passenger, ship);
                SetTerrainSuppression(player, false);
                return IsSpaceView;
            }

            UpdateDescentControl(player, passenger, ship);
            if (!string.IsNullOrWhiteSpace(passenger.ConsolePieceId))
            {
                if (!FindPiece(passenger.ConsolePieceId, out ShipState consoleShip, out ShipPieceState console) ||
                    consoleShip != ship || !console.IsOperational || console.Kind != ShipPieceKind.Console ||
                    !passenger.DescentDriving && !IsPowered(ship, console))
                    ExitConsole(player);
                else
                {
                    SetControl(console.PieceId, screenInput, turn);
                    passenger.Supported = floor;
                    SynchronizePassengerInteriorProjection(player, ship);
                    UpdateDrivingCamera(player, passenger, ship);
                    return true;
                }
            }

            passenger.Supported = true;
            passenger.ShipId = ship.ShipId;
            passenger.IsInSpace = IsSpaceView;
            SetTerrainSuppression(player, true);
            SynchronizePassengerInteriorProjection(player, ship);
            UpdateDrivingCamera(player, passenger, ship);
            return false;
        }

        public bool IsPlayerInShipInteriorWorld(Player player)
            => IsPlayerInShipInteriorWorld(player, out _, out _);

        public bool IsPlayerInShipInteriorWorld(Player player, out ShipState ship, out ShipInteriorWorldRuntime world)
        {
            ship = null;
            world = null;
            if (player == null || State == null) return false;
            string key = GameplayWorldRegistry.GetItemWorldKey(player);
            if (string.IsNullOrEmpty(key)) return false;
            SpacePassengerState passenger = FindPassenger(player.ProfileName);
            if (passenger == null || string.IsNullOrWhiteSpace(passenger.ShipId)) return false;
            ShipState candidate = GetShip(passenger.ShipId);
            if (candidate == null || candidate.IsWreck || MachineWorld.GetShipScopeKey(candidate.ShipId) != key ||
                !TryGetShipInteriorWorld(candidate.ShipId, out world)) return false;
            ship = candidate;
            return true;
        }

        public Vector2 TransformPassengerInputToShipLocal(Player player, Vector2 displayedWorldDirection)
        {
            if (!IsPlayerInShipInteriorWorld(player, out ShipState ship, out _)) return displayedWorldDirection;
            ShipGeometry.Rotate(displayedWorldDirection.x, displayedWorldDirection.y, -ship.AngleRadians,
                out double localX, out double localY);
            return new Vector2((float)localX, (float)localY);
        }

        public bool TryGetShipInteriorPresentationPosition(Player player, out Vector2 position)
        {
            position = default;
            if (!IsPlayerInShipInteriorWorld(player, out ShipState ship, out _)) return false;
            Mod_Mover mover = player.itemMods?.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover);
            Vector2 local = mover?.rb != null ? mover.rb.position : (Vector2)player.transform.position;
            position = ProjectShipInteriorPosition(ship, local);
            return true;
        }

        public double GetShipInteriorWalkingGravity(ShipState ship, Vector2 localPosition)
        {
            return ship != null && HasArtificialGravity(ship, localPosition.x, localPosition.y)
                ? SpaceGameplaySettings.Current.EarthGravity
                : 0d;
        }

        private bool TryEnterShipInteriorWorld(Player player, SpacePassengerState passenger, ShipState ship, Vector2 local)
        {
            if (player == null || passenger == null || ship == null) return false;
            ShipInteriorWorldRuntime world = EnsureShipInteriorWorld(ship);
            if (GameplayWorldRegistry.GetItemWorldKey(player) == world.WorldKey)
            {
                passenger.ShipId = ship.ShipId;
                passenger.LocalX = local.x;
                passenger.LocalY = local.y;
                SynchronizePassengerInteriorProjection(player, ship);
                return true;
            }
            Mod_Mover mover = player.itemMods?.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover);
            RestorePassengerSurface(player);
            Vector3 position = new(local.x, local.y, 0f);
            player.transform.SetPositionAndRotation(position, Quaternion.identity);
            if (mover?.rb != null)
            {
                mover.rb.position = local;
                mover.rb.rotation = 0f;
                mover.rb.velocity = Vector2.zero;
                mover.rb.angularVelocity = 0f;
            }
            player.itemData.transform.position = position;
            player.itemData.transform.rotation = Quaternion.identity;
            if (GameplayWorldRegistry.GetItemWorldKey(player) != world.WorldKey)
                GameManager.AssignRegisteredPlayerAndChildItemsToWorld(player, world.WorldKey);
            passenger.ShipId = ship.ShipId;
            passenger.LocalX = local.x;
            passenger.LocalY = local.y;
            passenger.Supported = true;
            passenger.IsInSpace = IsSpaceView;
            SynchronizePassengerInteriorProjection(player, ship);
            return true;
        }

        internal bool TryTransferPassengerToShipInteriorWorld(Player player, SpacePassengerState passenger, ShipState ship)
            => player != null && passenger != null && ship != null &&
                TryEnterShipInteriorWorld(player, passenger, ship,
                    new Vector2((float)passenger.LocalX, (float)passenger.LocalY));

        private void SynchronizePassengerInteriorProjection(Player player, ShipState ship)
        {
            if (player == null || ship == null || !IsPlayerInShipInteriorWorld(player, out ShipState owner, out _) || owner != ship)
                return;
            SynchronizePassengerSortingProjection(player, ship, ResolveShipInteriorPresentationMatrix(ship));
        }

        // 局部世界乘员投影船姿；已定位的外部乘员使用单位矩阵。
        private void SynchronizePassengerSortingProjection(Player player, ShipState ship, Matrix4x4 worldToDisplay)
        {
            if (player == null || ship == null || !TryGetShipInteriorWorld(ship.ShipId, out ShipInteriorWorldRuntime world))
            { ReleasePassengerInteriorProjection(player); return; }
            bool visible = IsVisible(ship);
            Matrix4x4 shipProjection = ResolveShipInteriorPresentationMatrix(ship);
            WorldPresentationSortingDomain domain = SynchronizeShipInteriorSortingDomain(world, shipProjection, visible);
            passengerProjections.TryGetValue(player, out WorldPresentationProjection projection);
            if (projection == null || projection.IsReleased)
                passengerProjections[player] = projection = WorldPresentationProjection.Create(player.transform, transform);
            projection.SetSortingDomain(domain);
            projection.Synchronize(worldToDisplay, visible);
            visibleInteriorPassengers.Add(player);
        }

        private readonly System.Collections.Generic.Dictionary<Player, WorldPresentationProjection> passengerProjections = new();
        private readonly HashSet<Player> visibleInteriorPassengers = new();
        private readonly List<Player> expiredInteriorPassengers = new();

        internal bool TryGetShipPassengerProjection(Player player, out WorldPresentationProjection projection)
            => passengerProjections.TryGetValue(player, out projection) && projection != null && !projection.IsReleased;

        private void ReleasePassengerInteriorProjection(Player player)
        {
            if (ReferenceEquals(player, null) || !passengerProjections.Remove(player, out WorldPresentationProjection projection) || projection == null)
                return;
            if (player != null) player.itemMods?.GetMod_ByID<Mod_Cam>(ModText.Camera)?.ClearFollowOverride(projection.transform);
            projection.Release();
        }

        private void ReleaseExpiredPassengerInteriorProjections(bool releaseAll = false)
        {
            expiredInteriorPassengers.Clear();
            foreach (Player player in passengerProjections.Keys)
                if (releaseAll || player == null || !visibleInteriorPassengers.Contains(player)) expiredInteriorPassengers.Add(player);
            foreach (Player player in expiredInteriorPassengers) ReleasePassengerInteriorProjection(player);
            expiredInteriorPassengers.Clear();
        }

        public void MoveAllPassengersOutOfShipInteriorWorlds()
        {
            ItemMgr manager = ItemMgr.GetExistingInstance();
            if (manager == null) return;
            foreach (Player player in new List<Player>(manager.Player_DIC.Values))
            {
                string worldKey = GameplayWorldRegistry.GetItemWorldKey(player);
                if (string.IsNullOrEmpty(worldKey) || !worldKey.StartsWith("ship:", StringComparison.Ordinal)) continue;
                SpacePassengerState passenger = State != null ? FindPassenger(player.ProfileName) : null;
                ShipState ship = passenger != null ? GetShip(passenger.ShipId) : GetShip(worldKey.Substring(5));
                if (passenger?.Supported == true && ship != null) ReleasePassenger(passenger, ship);
                else TransferPassengerOutOfShipInterior(player, passenger ?? CreatePassengerExitFallback(player), ship,
                    passenger?.IsInSpace == true ? null : player.Data?.CurrentSceneName);
            }
        }

        public void MovePassengersOutOfRemovedShipInteriorWorlds(ISet<string> retainedShipIds)
        {
            if (State == null) return;
            foreach (SpacePassengerState passenger in State.Passengers)
            {
                Player player = FindNetworkPlayer(passenger.ProfileId);
                if (player == null) continue;
                string worldKey = GameplayWorldRegistry.GetItemWorldKey(player);
                if (string.IsNullOrEmpty(worldKey) || !worldKey.StartsWith("ship:", StringComparison.Ordinal)) continue;
                string shipId = worldKey.Substring(5);
                if (retainedShipIds != null && retainedShipIds.Contains(shipId)) continue;
                ShipState ship = GetShip(shipId);
                if (passenger.Supported && ship != null) ReleasePassenger(passenger, ship);
                else TransferPassengerOutOfShipInterior(player, passenger, ship,
                    passenger.IsInSpace ? null : player.Data?.CurrentSceneName);
            }
        }

        public void RebindPassengersToShipInteriorWorlds()
        {
            if (State == null) return;
            foreach (SpacePassengerState passenger in State.Passengers)
            {
                Player player = FindNetworkPlayer(passenger.ProfileId);
                if (player == null) continue;
                ShipState ship = passenger.Supported ? GetShip(passenger.ShipId) : null;
                if (ship != null && ShipAtmosphereService.HasFloor(ship,
                        (int)Math.Floor(passenger.LocalX / ship.CellSizeMeters), (int)Math.Floor(passenger.LocalY / ship.CellSizeMeters)))
                {
                    ShipInteriorWorldRuntime world = EnsureShipInteriorWorld(ship);
                    Vector2 local = new((float)passenger.LocalX, (float)passenger.LocalY);
                    SetPassengerItemLocalPose(player, local);
                    if (GameplayWorldRegistry.GetItemWorldKey(player) != world.WorldKey)
                        GameManager.AssignRegisteredPlayerAndChildItemsToWorld(player, world.WorldKey);
                    SynchronizePassengerInteriorProjection(player, ship);
                }
                else if (GameplayWorldRegistry.GetItemWorldKey(player) is string worldKey &&
                    worldKey.StartsWith("ship:", StringComparison.Ordinal))
                    TransferPassengerOutOfShipInterior(player, passenger, GetShip(worldKey.Substring(5)), GetPassengerWorld(passenger));
            }
        }

        public bool HasArtificialGravity(ShipState ship, double x, double y)
        {
            // 重力只需要确认地板支撑，不读取昂贵的船外地表与舱气状态。
            if (!ShipAtmosphereService.HasFloor(ship, (int)Math.Floor(x / ship.CellSizeMeters),
                    (int)Math.Floor(y / ship.CellSizeMeters))) return false;
            if (ship.Anchor != null) return true;
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
            if (!TryReleasePassenger(passenger, ship))
                throw new InvalidOperationException("船内乘员移交目标世界未就绪，保留船内权威状态。");
        }

        // 先验证目标作用域再提交乘员状态，失败时恢复船内物理归属。
        public bool TryReleasePassenger(SpacePassengerState passenger, ShipState ship)
        {
            if (passenger == null || ship == null) return false;
            Player passengerPlayer = FindNetworkPlayer(passenger.ProfileId);
            string originalState = JsonConvert.SerializeObject(passenger);
            SpacePassengerState released = JsonConvert.DeserializeObject<SpacePassengerState>(originalState);
            if (released == null) return false;
            if (passengerPlayer != null && GameplayWorldRegistry.GetItemWorldKey(passengerPlayer) ==
                MachineWorld.GetShipScopeKey(ship.ShipId))
            {
                Mod_Mover mover = passengerPlayer.itemMods?.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover);
                if (mover?.rb != null)
                {
                    released.LocalX = mover.rb.position.x;
                    released.LocalY = mover.rb.position.y;
                    released.WalkVelocityX = mover.rb.velocity.x;
                    released.WalkVelocityY = mover.rb.velocity.y;
                }
            }

            ShipGeometry.LocalToWorld(ship, released.LocalX, released.LocalY, out double x, out double y);
            ShipGeometry.PointVelocity(ship, x, y, out double vx, out double vy);
            released.FreeMotion.Sector = ship.Sector;
            released.FreeMotion.PositionMeters = new SpaceVector2(x, y);
            ShipGeometry.Rotate(released.WalkVelocityX, released.WalkVelocityY, ship.AngleRadians, out double wx, out double wy);
            released.FreeMotion.VelocityMetersPerSecond = new SpaceVector2(vx + wx, vy + wy);
            released.FreeWalkVelocity = new SpaceVector2(wx, wy);
            released.WalkVelocityX = released.WalkVelocityY = 0d;
            ShipFlightState flight = GetFlight(ship.ShipId);
            if (flight is { HeightMeters: > 0d } && flight.Phase is ShipFlightPhase.Lifting or ShipFlightPhase.SurfaceDescending)
            {
                released.SurfaceDescending = true; released.IsInSpace = false; released.LandingResolved = false;
                released.LandingBodyId = flight.BodyId; released.LandingWorldKey = flight.SurfaceWorldKey;
                released.SurfaceHeight = flight.HeightMeters; released.SurfaceVerticalSpeed = flight.VerticalSpeed;
                released.SurfaceHorizontalSpeed = flight.EntrySurfaceVelocity.X + wx;
                released.LandingOriginX = released.LandingX = x; released.LandingOriginY = released.LandingY = y;
                released.LandingSelected = false;
            }
            else if (flight?.Phase == ShipFlightPhase.Cruising)
                released.IsInSpace = true;
            else if (flight != null)
            {
                released.IsInSpace = false;
                released.LandingWorldKey = flight.SurfaceWorldKey;
            }
            released.Supported = false; released.ShipId = null; released.ConsolePieceId = null;

            try
            {
                if (passengerPlayer != null && !TransferPassengerOutOfShipInterior(passengerPlayer, released, ship, null))
                    return false;
                Player localPlayer = ItemMgr.Instance?.User_Player;
                if (localPlayer != null && passenger.ProfileId == localPlayer.ProfileName) ExitConsole(localPlayer);
            }
            catch (Exception error)
            {
                Debug.LogWarning("船内乘员移交失败，保留原船内状态：" + error.Message);
                try
                {
                    JsonConvert.PopulateObject(originalState, passenger);
                    if (passenger.Supported && passenger.ShipId == ship.ShipId)
                    {
                        if (passengerPlayer != null)
                            GameManager.AssignRegisteredPlayerAndChildItemsToWorld(passengerPlayer,
                                MachineWorld.GetShipScopeKey(ship.ShipId));
                        RebindPassengersToShipInteriorWorlds();
                    }
                }
                catch (Exception rollbackError) { Debug.LogException(rollbackError); }
                return false;
            }
            JsonConvert.PopulateObject(JsonConvert.SerializeObject(released), passenger);
            return true;
        }

        private bool TransferPassengerOutOfShipInterior(Player player, SpacePassengerState passenger, ShipState ship,
            string preferredWorld)
        {
            if (player == null) return true;
            string sourceWorld = GameplayWorldRegistry.GetItemWorldKey(player);
            if (string.IsNullOrEmpty(sourceWorld) || !sourceWorld.StartsWith("ship:", StringComparison.Ordinal)) return true;
            passenger ??= CreatePassengerExitFallback(player);
            string targetWorld = preferredWorld;
            if (string.IsNullOrWhiteSpace(targetWorld))
            {
                if (passenger.SurfaceDescending) targetWorld = passenger.LandingWorldKey;
                else if (passenger.IsInSpace)
                    targetWorld = SpaceSectorCoordinates.SceneKey(passenger.FreeMotion.Sector);
                else targetWorld = GetFlight(ship?.ShipId)?.SurfaceWorldKey ?? passenger.LandingWorldKey;
            }
            if (string.IsNullOrWhiteSpace(targetWorld)) targetWorld = player.Data?.CurrentSceneName;
            if (string.IsNullOrWhiteSpace(targetWorld))
                throw new InvalidOperationException("船内乘员没有可移交的地表或星区世界。");
            Vector2 displayPosition, itemPosition;
            Vector2 velocity = passenger.FreeMotion.VelocityMetersPerSecond.ToVector2();

            if (SpaceMgr.IsSpaceScene(targetWorld))
            {
                bool boundForTransfer = false;
                if (!GameplayWorldRegistry.TryGet(targetWorld, out _))
                {
                    if (!SpaceSectorCoordinates.TryParseSceneKey(targetWorld, out SpaceSectorId sector) || SpaceMgr.ExistingInstance == null)
                        throw new InvalidOperationException("目标星区尚未建立正式物理场景：" + targetWorld);
                    UnityEngine.SceneManagement.Scene scene = SpaceMgr.ExistingInstance.BindAuthoritySector(player.ProfileName, sector);
                    boundForTransfer = true;
                    if (!scene.IsValid() || !scene.isLoaded)
                    {
                        SpaceMgr.ExistingInstance.ReleaseAuthoritySector(player.ProfileName);
                        throw new InvalidOperationException("目标星区尚未加载：" + targetWorld);
                    }
                }
                try
                {
                    using (GameplayWorldRegistry.UseSpaceScene(targetWorld))
                    {
                        displayPosition = ProjectPosition(new SpaceSectorPosition(passenger.FreeMotion.Sector,
                            passenger.FreeMotion.PositionMeters));
                        itemPosition = displayPosition;
                    }
                }
                catch
                {
                    if (boundForTransfer) SpaceMgr.ExistingInstance?.ReleaseAuthoritySector(player.ProfileName);
                    throw;
                }
            }
            else
            {
                using (GameplayWorldRegistry.UseWorld(targetWorld))
                {
                    double x = passenger.SurfaceDescending ? passenger.LandingX : passenger.FreeMotion.PositionMeters.X;
                    double y = passenger.SurfaceDescending ? passenger.LandingY : passenger.FreeMotion.PositionMeters.Y;
                    displayPosition = WorldLocalPresentation.ProjectPosition(new Vector2((float)x, (float)y));
                    itemPosition = WorldLocalPresentation.ToLogical(displayPosition);
                }
            }
            RestorePassengerSurface(player);
            ReleasePassengerInteriorProjection(player);
            player.itemData.transform ??= new ItemTransform();
            player.itemData.transform.position = itemPosition;
            player.transform.SetPositionAndRotation(displayPosition, Quaternion.identity);
            Mod_Mover mover = player.itemMods?.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover);
            if (mover?.rb != null)
            {
                mover.rb.position = displayPosition;
                mover.rb.rotation = 0f;
                mover.rb.velocity = velocity;
                mover.rb.angularVelocity = 0f;
            }
            GameManager.AssignRegisteredPlayerAndChildItemsToWorld(player, targetWorld);
            if (SpaceMgr.IsSpaceScene(targetWorld)) SpaceMgr.ExistingInstance?.ReleaseAuthoritySector(player.ProfileName);
            return true;
        }

        private static SpacePassengerState CreatePassengerExitFallback(Player player)
        {
            Vector2 position = player != null ? (Vector2)player.transform.position : Vector2.zero;
            return new SpacePassengerState
            {
                ProfileId = player?.ProfileName,
                IsInSpace = SpaceMgr.IsSpaceScene(player?.Data?.CurrentSceneName),
                LandingWorldKey = player?.Data?.CurrentSceneName,
                FreeMotion = new SpaceMotionState { PositionMeters = SpaceVector2.FromVector2(position), RadiusMeters = .3d }
            };
        }

        private static void SetPassengerItemLocalPose(Player player, Vector2 local)
        {
            Mod_Mover mover = player.itemMods?.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover);
            Vector3 position = new(local.x, local.y, 0f);
            player.transform.position = position;
            if (mover?.rb != null)
            {
                mover.rb.position = local;
                mover.rb.rotation = 0f;
            }
            player.itemData.transform ??= new ItemTransform();
            player.itemData.transform.position = position;
            player.itemData.transform.rotation = Quaternion.identity;
        }
        private void PlacePassenger(Player player, SpacePassengerState passenger, ShipState ship, bool presentation = false)
        {
            if (IsPlayerInShipInteriorWorld(player, out ShipState interiorShip, out _) && interiorShip == ship)
            {
                SynchronizePassengerInteriorProjection(player, ship);
                return;
            }
            Vector2 authoritativePosition = DisplayPoint(ship, passenger.LocalX, passenger.LocalY);
            Vector2 position = presentation ? GetPassengerPresentationPosition(passenger, ship) : authoritativePosition;
            const float heading = 0f;
            Mod_Mover mover = player.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover);
            float rise = (float)ShipSurfaceState.VisualRise(GetFlight(ship.ShipId));
            player.transform.rotation = Quaternion.Euler(0f, 0f, heading);
            if (mover?.rb != null) mover.rb.rotation = heading;
            bool projected = TryGetShipInteriorWorld(ship.ShipId, out _);
            SetPassengerSurfacePose(player, position, authoritativePosition, rise,
                ShipSurfaceState.IsElevated(GetFlight(ship.ShipId)), projected);
            if (projected) SynchronizePassengerSortingProjection(player, ship, Matrix4x4.identity);
            else ReleasePassengerInteriorProjection(player);
        }
        private void SetTerrainSuppression(Player player, bool value)
            => player.itemMods.GetMod_ByID<Mod_TileEffectReceiver>(ModText.Mod_TileEffectReceiver)?.SetEffectsSuppressed(this, value);
        public bool EnterConsole(Player player, string pieceId) => EnterConsole(player, pieceId, out _);
        public bool EnterConsole(Player player, string pieceId, out string reason)
        {
            reason = null;
            if (player == null) return RejectShipOperation("玩家尚未就绪。", out reason);
            if (SpaceNetworkBridge.SendIntent(player.ProfileName, new SpaceNetworkIntent { Kind = SpaceNetworkIntentKind.EnterConsole, PieceId = pieceId }))
            {
                NotifyLocalManualConsoleEntry(player, pieceId);
                return true;
            }
            if (!TryGetPoweredConsole(pieceId, out ShipState ship, out ShipPieceState piece, out reason)) return false;
            if (GetFlight(ship.ShipId)?.Phase == ShipFlightPhase.Floating)
                return RejectShipOperation("海面浮船无法驾驶。", out reason);
            if (Vector2.Distance(GetPlayerAuthoritativeDisplayPosition(player), DisplayPoint(ship, piece.CellX + .5d, piece.CellY + .5d)) > 3f)
                return RejectShipOperation("距离控制台太远，请靠近后再驾驶。", out reason);
            if (State.Passengers.Exists(value => value.ProfileId != player.ProfileName && value.ConsolePieceId == pieceId))
                return RejectShipOperation("控制台正由其他玩家驾驶。", out reason);
            SpacePassengerState passenger = GetPassenger(player);
            bool alreadyDriving = !string.IsNullOrWhiteSpace(passenger.ConsolePieceId);
            SpaceSectorPosition position = IsSpaceView ? GetPassengerPosition(passenger)
                : new SpaceSectorPosition(default, SpaceVector2.FromVector2(WorldLocalPresentation.ToLogical(GetPlayerAuthoritativeDisplayPosition(player))));
            BindConsoleControl(passenger, ship, piece, position, IsSpaceView);
            TryEnterShipInteriorWorld(player, passenger, ship, new Vector2((float)passenger.LocalX, (float)passenger.LocalY));
            ShipFlightState flight = GetFlight(ship.ShipId);
            if (flight.Phase == ShipFlightPhase.SurfaceDescending || flight.Phase == ShipFlightPhase.Lifting && flight.VerticalSpeed < 0d)
                passenger.DescentControlEntered = passenger.DescentDriving = true;
            Mod_Cam camera = player.itemMods.GetMod_ByID<Mod_Cam>(ModText.Camera);
            if (!alreadyDriving) passenger.SavedCameraSize = camera?.CurrentOrthographicSize ?? 10f;
            camera?.SetOrthographicSize(SpaceGameplaySettings.Current.DrivingCameraSize);
            NotifyLocalManualConsoleEntry(player, pieceId);
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
            ShipPieceState console = ship.Pieces.Find(piece => piece.IsOperational && piece.Kind == ShipPieceKind.Console &&
                piece.PieceId == passenger.ConsolePieceId);
            console ??= ship.Pieces.Find(piece => piece.IsOperational && piece.Kind == ShipPieceKind.Console && IsPowered(ship, piece));
            console ??= ship.Pieces.Find(piece => piece.IsOperational && piece.Kind == ShipPieceKind.Console);
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
            if (passenger.Supported && ship != null && IsPlayerInShipInteriorWorld(player, out ShipState interiorShip, out _) && interiorShip == ship)
            {
                SynchronizePassengerInteriorProjection(player, ship);
                Mod_Cam interiorCamera = player.itemMods.GetMod_ByID<Mod_Cam>(ModText.Camera);
                if (string.IsNullOrWhiteSpace(passenger.ConsolePieceId))
                {
                    ReleaseDrivingCamera();
                    if (interiorCamera != null && passengerProjections.TryGetValue(player, out WorldPresentationProjection projection) && projection != null && !projection.IsReleased)
                        interiorCamera.SetFollowOverride(projection.transform);
                    return;
                }
            }
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
            if (IsSpaceView)
            {
                SpaceVector2 relative = SpaceSectorCoordinates.Relative(assembly?.Sector ?? ship.Sector, center, ship.Sector);
                ShipGeometry.WorldToLocal(ship, relative.X, relative.Y, out double x, out double y);
                position = DisplayPresentationPoint(ship, x, y);
            }
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
                IsSpaceView ? settings.DrivingCameraZoomMultiplier : 1f,
                IsSpaceView ? Mod_Cam.FollowMode.PresentationLocked : Mod_Cam.FollowMode.PlayerSettings);
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
                        RestorePassengerSurface(player);
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
            ResetPresentationHistory();
            SpacePassengerState passenger = GetPassenger(player);
            ShipState ship = GetShip(passenger.ShipId);
            if (IsSpaceView)
                { SpaceSectorPosition position = GetPassengerPosition(passenger); ViewSector = position.Sector; ViewOrigin = position.LocalMeters; }
            if (passenger.Supported && IsInPresentationWorld(ship)) PlacePassenger(player, passenger, ship);
            else if (IsPassengerSurfaceDescendingHere(passenger))
                PlaceDescendingPassenger(player, passenger);
            else if (!passenger.Supported && IsSpaceView && passenger.IsInSpace)
            {
                RestorePassengerSurface(player);
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
