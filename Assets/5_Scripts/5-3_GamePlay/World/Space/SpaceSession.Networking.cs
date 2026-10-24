using System;
using System.Collections.Generic;
using FlatWorld.Diagnostics;
using FlatWorld.Networking;
using Newtonsoft.Json;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 观察者快照性能诊断
        private static readonly PerformanceDebugPoint NetworkCapturePoint = new("FlatWorld.Space.Network.CaptureContents");
        private static readonly PerformanceDebugPoint NetworkFilterPoint = new("FlatWorld.Space.Network.Filter");
        private static readonly PerformanceDebugPoint NetworkJsonPoint = new("FlatWorld.Space.Network.Json");
        #endregion

        #region 连接身份与权威输入
        private readonly Dictionary<string, SpacePassengerInput> passengerInputs = new(StringComparer.Ordinal);
        private static Player FindNetworkPlayer(string profile)
        {
            ItemMgr manager = ItemMgr.GetInstance();
            return manager != null && manager.Player_DIC.TryGetValue(profile, out Player player) ? player : null;
        }
        private void StepRemotePassengerInput(double seconds)
        {
            Player local = ItemMgr.Instance?.User_Player;
            foreach (SpacePassengerState passenger in State.Passengers)
            {
                if (local?.ProfileName == passenger.ProfileId || !passenger.Supported || passenger.ConsolePieceId != null ||
                    !passengerInputs.TryGetValue(passenger.ProfileId, out SpacePassengerInput input)) continue;
                ShipState ship = GetShip(passenger.ShipId);
                if (ship == null) continue;
                Mod_Mover mover = FindNetworkPlayer(passenger.ProfileId)?.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover);
                if (mover == null) continue;
                if (GetFlight(ship.ShipId)?.Phase == ShipFlightPhase.Cruising && !HasArtificialGravity(ship, passenger.LocalX, passenger.LocalY))
                { ReleasePassenger(passenger, ship); continue; }
                ShipGeometry.Rotate(input.MoveX, input.MoveY, -ship.AngleRadians, out double dx, out double dy);
                double speed = mover.Speed.Value;
                passenger.WalkVelocityX = dx * speed; passenger.WalkVelocityY = dy * speed;
                double x = passenger.LocalX + dx * speed * seconds, y = passenger.LocalY + dy * speed * seconds;
                int cellX = (int)Math.Floor(x / ship.CellSizeMeters), cellY = (int)Math.Floor(y / ship.CellSizeMeters);
                bool blocked = ship.Pieces.Exists(piece => piece.IsAlive && piece.BlocksPassage &&
                    System.Linq.Enumerable.Contains(ShipGeometry.Footprint(piece), new ShipCell(cellX, cellY)));
                if (!blocked) { passenger.LocalX = x; passenger.LocalY = y; }
                if (!ShipAtmosphereService.HasFloor(ship, cellX, cellY)) ReleasePassenger(passenger, ship);
            }
        }
        public void EnsureNetworkPassenger(Player player)
        {
            if (player == null || State == null || FindPassenger(player.ProfileName) != null) return;
            SpacePassengerState passenger = GetPassenger(player);
            passenger.IsInSpace = SpaceMgr.IsSpaceScene(player.Data.CurrentSceneName);
            passenger.LandingWorldKey = passenger.IsInSpace ? null : player.Data.CurrentSceneName;
            if (SpaceSectorCoordinates.TryParseSceneKey(player.Data.CurrentSceneName, out SpaceSectorId sector))
                passenger.FreeMotion.Sector = sector;
        }
        public void SetNetworkPlayerRealm(string profile, bool inSpace, SpaceSectorPosition position, string worldKey)
        {
            if (!GameNetwork.HasStateAuthority || State == null || !position.LocalMeters.IsFinite) return;
            SpacePassengerState passenger = FindPassenger(profile);
            if (passenger == null) { passenger = new SpacePassengerState { ProfileId = profile }; State.Passengers.Add(passenger); }
            position = SpaceSectorCoordinates.Normalize(position.Sector, position.LocalMeters);
            passenger.IsInSpace = inSpace; passenger.FreeMotion.Sector = position.Sector; passenger.FreeMotion.PositionMeters = position.LocalMeters;
            passenger.LandingWorldKey = inSpace ? null : worldKey;
            if (inSpace) GetUniverse(position.Sector);
        }
        public void SubmitPassengerInput(string profile, SpacePassengerInput input)
        {
            if (!GameNetwork.HasStateAuthority || State == null) return;
            SpacePassengerState passenger = FindPassenger(profile);
            if (passenger == null) return;
            if (!float.IsFinite(input.MoveX) || !float.IsFinite(input.MoveY) || !float.IsFinite(input.Turn) || !float.IsFinite(input.Thrust)) return;
            Vector2 move = Vector2.ClampMagnitude(new Vector2(input.MoveX, input.MoveY), 1f);
            input.MoveX = move.x; input.MoveY = move.y; input.Turn = Mathf.Clamp(input.Turn, -1f, 1f);
            passengerInputs[profile] = input;
            if (!string.IsNullOrEmpty(passenger.ConsolePieceId)) SetControl(passenger.ConsolePieceId, move, input.Turn);
        }
        public void RemoveNetworkPassengerInput(string profile)
        {
            passengerInputs.Remove(profile);
            SpacePassengerState passenger = FindPassenger(profile);
            if (passenger?.ConsolePieceId != null) ClearControl(passenger.ConsolePieceId);
        }
        /// <summary>只读返回服务端最后接受的输入，便于联机诊断和隔离测试。</summary>
        public bool TryGetNetworkPassengerInput(string profile, out SpacePassengerInput input)
        {
            input = default;
            return !string.IsNullOrEmpty(profile) && passengerInputs.TryGetValue(profile, out input);
        }
        public bool ExecuteNetworkIntent(string profile, SpaceNetworkIntent intent, out string reason)
        {
            reason = null;
            if (!GameNetwork.HasStateAuthority) return RejectShipOperation("当前端没有世界操作权限。", out reason);
            if (!TryGetNetworkObserver(profile, out SpaceNetworkObserver observer))
                return RejectShipOperation("玩家所在世界尚未就绪。", out reason);
            SpacePassengerState passenger = FindPassenger(profile);
            if (intent.Kind == SpaceNetworkIntentKind.Respawn) return ExecuteNetworkRespawn(profile, out reason);
            if (intent.Kind == SpaceNetworkIntentKind.LaunchGroundShip)
            {
                if (observer.InSpace) return RejectShipOperation("地面点火需要玩家和控制台位于同一地表。", out reason);
                if ((intent.Action != 0 && intent.Action != 1) ||
                    !int.TryParse(intent.PieceId, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out int consoleGuid))
                    return RejectShipOperation("地面点火请求的控制台身份或操作参数无效。", out reason);
                Player actor = FindNetworkPlayer(profile);
                if (!ShipGroundAssembly.TryLaunch(actor, consoleGuid, out ShipState launched, out reason)) return false;
                if (intent.Action == 1 && FindPiece(intent.PieceId, out _, out ShipPieceState console))
                    BindConsoleControl(passenger, launched, console, observer.Position, false);
                return true;
            }

            if (intent.Kind is SpaceNetworkIntentKind.Attack or SpaceNetworkIntentKind.AttackEnd or SpaceNetworkIntentKind.UseSkill or SpaceNetworkIntentKind.UsePlayerItem or SpaceNetworkIntentKind.OffhandParry)
            {
                if (!observer.InSpace && intent.Kind != SpaceNetworkIntentKind.AttackEnd && intent.TargetId != observer.WorldKey)
                    return RejectShipOperation("动作来自其他世界，当前玩家无法执行。", out reason);
                Mod_HotBar combatHotbar = FindNetworkPlayer(profile)?.itemMods.GetMod_ByID<Mod_HotBar>(ModText.Hotbar);
                if (intent.Kind != SpaceNetworkIntentKind.AttackEnd && intent.Kind != SpaceNetworkIntentKind.OffhandParry)
                {
                    if (combatHotbar == null || intent.SelectedSlot < 0 || intent.SelectedSlot >= combatHotbar.MaxIndex)
                        return RejectShipOperation("当前快捷栏或所选槽位无效。", out reason);
                    combatHotbar.ApplyNetworkSelectedIndex(intent.SelectedSlot);
                }
                return ExecuteNetworkAttack(profile, intent.Kind == SpaceNetworkIntentKind.Attack ? 0 : intent.Kind == SpaceNetworkIntentKind.AttackEnd ? (intent.Action == 4 ? 4 : 1) : intent.Kind == SpaceNetworkIntentKind.OffhandParry ? 3 : 2,
                    new SpaceSectorPosition(intent.Sector, new SpaceVector2(intent.X, intent.Y)), out reason);
            }
            if (intent.Kind == SpaceNetworkIntentKind.HarvestNatural)
            {
                if (observer.InSpace || intent.TargetId != observer.WorldKey)
                    return RejectShipOperation("采集目标不在玩家当前地表世界。", out reason);
                if (!int.TryParse(intent.PieceId, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out int guid))
                    return RejectShipOperation("采集目标身份无效。", out reason);
                bool harvested = FlatWorld.NaturalEntities.NaturalEntityEcsService.TryHarvestNetwork(
                    FindNetworkPlayer(profile), observer.WorldKey, guid, new Vector2((float)intent.X, (float)intent.Y));
                if (!harvested) return RejectShipOperation("采集目标已失效或当前无法采集。", out reason);
                reason = null;
                return true;
            }
            if (intent.Kind == SpaceNetworkIntentKind.ExitConsole)
            {
                if (string.IsNullOrWhiteSpace(passenger.ConsolePieceId))
                    return RejectShipOperation("当前未处于驾驶状态。", out reason);
                ClearControl(passenger.ConsolePieceId);
                passenger.ConsolePieceId = null; passenger.DescentDriving = false; reason = null; return true;
            }
            if (!FindPiece(intent.PieceId, out ShipState ship, out ShipPieceState piece) ||
                !string.IsNullOrEmpty(intent.ShipId) && ship.ShipId != intent.ShipId)
                return RejectShipOperation("操作目标已失效或不属于这艘飞船。", out reason);
            ShipGeometry.LocalToWorld(ship, (piece.CellX + .5d) * ship.CellSizeMeters,
                (piece.CellY + .5d) * ship.CellSizeMeters, out double x, out double y);
            double distance = observer.InSpace
                ? SpaceSectorCoordinates.Difference(observer.Position, new SpaceSectorPosition(ship.Sector, new SpaceVector2(x, y))).Magnitude
                : WorldTopologyRuntime.Distance(observer.Position.LocalMeters.ToVector2(), new Vector2((float)x, (float)y));
            ShipFlightState targetFlight = GetFlight(ship.ShipId);
            if (observer.InSpace != (targetFlight?.Phase == ShipFlightPhase.Cruising) ||
                !observer.InSpace && !string.Equals(observer.WorldKey, targetFlight?.SurfaceWorldKey, StringComparison.Ordinal))
                return RejectShipOperation("玩家与操作目标不在同一世界。", out reason);
            if (distance > ConsoleInteractionDistance)
                return RejectShipOperation("距离操作目标太远，请靠近后重试。", out reason);
            bool result;
            switch (intent.Kind)
            {
                case SpaceNetworkIntentKind.EnterConsole:
                    if (!TryGetPoweredConsole(piece.PieceId, out _, out _, out reason)) return false;
                    if (targetFlight?.Phase == ShipFlightPhase.Floating)
                        return RejectShipOperation("海面浮船无法驾驶。", out reason);
                    if (State.Passengers.Exists(value => value.ProfileId != profile && value.ConsolePieceId == piece.PieceId))
                        return RejectShipOperation("控制台正由其他玩家驾驶。", out reason);
                    BindConsoleControl(passenger, ship, piece, observer.Position, observer.InSpace);
                    result = true;
                    break;
                case SpaceNetworkIntentKind.ToggleDoor: return TryToggleDoor(piece.PieceId, out reason);
                case SpaceNetworkIntentKind.ToggleIgnition: return ToggleIgnition(piece.PieceId, out reason);
                case SpaceNetworkIntentKind.RequestLanding: return RequestLanding(piece.PieceId, out reason);
                case SpaceNetworkIntentKind.StartNavigation:
                    return StartNavigation(piece.PieceId, new SpaceSectorPosition(intent.Sector, new SpaceVector2(intent.X, intent.Y)), out reason);
                case SpaceNetworkIntentKind.CancelNavigation:
                    if (GetNavigation(GetConnectedAssembly(ship.ShipId)) == null)
                        return RejectShipOperation("当前没有正在执行的导航任务。", out reason);
                    CancelNavigation(ship.ShipId); result = true; break;
                case SpaceNetworkIntentKind.ConfirmDock:
                    return ConfirmDockForShip(ship, out reason);
                case SpaceNetworkIntentKind.DisconnectDock:
                    return DisconnectDock(ship.ShipId, intent.TargetId ?? piece.PieceId, out reason);
                case SpaceNetworkIntentKind.SetTargetPressure:
                    ShipDeviceState device = GetDevice(piece.PieceId);
                    if (device == null || device.Configuration.SupplyMolesPerSecond <= 0f)
                        return RejectShipOperation("请在供气器上设置目标气压。", out reason);
                    if (!IsValidTargetPressure(intent.X))
                        return RejectShipOperation("请输入 0 至 200000 范围内的有效气压（kPa）。", out reason);
                    device.TargetPressureKPa = intent.X;
                    result = true;
                    break;
                default: return RejectShipOperation("当前操作不受支持。", out reason);
            }
            if (result) reason = null;
            return result;
        }
        private bool ConfirmDockForShip(ShipState ship) => ConfirmDockForShip(ship, out _);
        private bool ConfirmDockForShip(ShipState ship, out string reason)
        {
            reason = null;
            ShipDockingService.SynchronizePorts(ship);
            if (ship.DockingPorts.Count == 0) return RejectShipOperation("本船没有可用的对接接口。", out reason);
            foreach (ShipDockingPortState own in ship.DockingPorts)
                foreach (ShipState other in State.Ships)
                {
                    ShipFlightState ownFlight = GetFlight(ship.ShipId), otherFlight = GetFlight(other.ShipId);
                    if (other == ship || ownFlight == null || otherFlight?.Phase != ownFlight.Phase ||
                        ownFlight.Phase != ShipFlightPhase.Cruising &&
                        (ownFlight.SurfaceWorldKey != otherFlight.SurfaceWorldKey || Math.Abs(ownFlight.HeightMeters - otherFlight.HeightMeters) > 1d)) continue;
                    ShipDockingService.SynchronizePorts(other);
                    foreach (ShipDockingPortState target in other.DockingPorts)
                        if (ShipDockingService.TryCreateCandidate(State.Ships, State.Docking, ship.ShipId, own.PortId, other.ShipId, target.PortId, out ShipDockingPairState pair))
                        {
                            if (!ShipDockingService.Confirm(State.Ships, State.Docking, pair.PairId, ship.ShipId, out string dockReason))
                                return RejectShipOperation(dockReason, out reason);
                            reason = FlatWorld.Localization.FlatWorldLocalizationService.GetUiText(GetDockConfirmationFeedback(pair.Connected));
                            return true;
                        }
                }
            return RejectShipOperation("附近没有符合对接条件且未被占用的接口。", out reason);
        }
        private bool SubmitLocalIntent(SpaceNetworkIntentKind kind, string piece = null, string ship = null, string target = null, SpaceSectorPosition position = default)
        {
            Player player = ItemMgr.Instance?.User_Player;
            return player != null && SpaceNetworkBridge.SendIntent(player.ProfileName, new SpaceNetworkIntent
            { Kind = kind, PieceId = piece, ShipId = ship, TargetId = target, Sector = position.Sector, X = position.LocalMeters.X, Y = position.LocalMeters.Y });
        }
        #endregion

        #region 按真实距离复制唯一数据
        public string CaptureNetworkSnapshot(string profile, double rangeMeters)
        {
            if (!TryGetNetworkObserver(profile, out SpaceNetworkObserver observer)) return null;
            using (NetworkCapturePoint.Measure())
            {
                CaptureContents(); CaptureLooseItems();
                NetworkCapturePoint.SetGauge(contents.Count + looseData.Count);
            }
            SpaceSessionState snapshot;
            using (NetworkFilterPoint.Measure(State.Ships.Count + State.Devices.Count + State.Passengers.Count +
                       State.LooseItems.Count + State.Navigation.Count + State.Docking.Count))
            {
                snapshot = new SpaceSessionState { SimulationSeconds = State.SimulationSeconds, NavigationSequence = State.NavigationSequence };
                var includedShips = new HashSet<string>(StringComparer.Ordinal);
                var includedSectors = new HashSet<SpaceSectorId> { observer.Position.Sector };
                var includedPieces = new HashSet<string>(StringComparer.Ordinal);
                foreach (ShipState ship in State.Ships)
                {
                    ShipFlightState flight = GetFlight(ship.ShipId);
                    bool visible = observer.InSpace ? flight?.Phase == ShipFlightPhase.Cruising &&
                        SpaceSectorCoordinates.Difference(new SpaceSectorPosition(ship.Sector, new SpaceVector2(ship.PositionX, ship.PositionY)), observer.Position).Magnitude <= rangeMeters + CalculateShipContactRadius(ship)
                        : flight?.SurfaceWorldKey == observer.WorldKey;
                    if (visible || ship.ShipId == observer.ShipId) includedShips.Add(ship.ShipId);
                }
                // 一条已对接的船组必须整组复制，客户端不重建缺成员的组。
                foreach (ShipAssemblyState assembly in queryAssemblies.Refresh(State.Ships, State.Docking))
                {
                    bool included = assembly.MemberShipIds.Exists(id => includedShips.Contains(id));
                    if (included) foreach (string id in assembly.MemberShipIds) includedShips.Add(id);
                }
                foreach (ShipState ship in State.Ships)
                    if (includedShips.Contains(ship.ShipId))
                    {
                        snapshot.Ships.Add(ship); snapshot.Flights.Add(GetFlight(ship.ShipId)); includedSectors.Add(ship.Sector);
                        foreach (ShipPieceState piece in ship.Pieces) includedPieces.Add(piece.PieceId);
                    }
                foreach (ShipDeviceState device in State.Devices) if (includedPieces.Contains(device.PieceId)) snapshot.Devices.Add(device);
                foreach (SpacePassengerState passenger in State.Passengers)
                    if (passenger.ProfileId == profile || TryGetNetworkObserver(passenger.ProfileId, out SpaceNetworkObserver remote) &&
                        remote.InSpace == observer.InSpace && (observer.InSpace
                            ? SpaceSectorCoordinates.Difference(remote.Position, observer.Position).Magnitude <= rangeMeters
                            : remote.WorldKey == observer.WorldKey))
                    { snapshot.Passengers.Add(passenger); includedSectors.Add(GetPassengerPosition(passenger).Sector); }
                foreach (SpaceLooseItemState loose in State.LooseItems)
                    if (loose.InSpace == observer.InSpace && (observer.InSpace
                        ? SpaceSectorCoordinates.Difference(new SpaceSectorPosition(loose.Motion.Sector, loose.Motion.PositionMeters), observer.Position).Magnitude <= rangeMeters
                        : loose.WorldKey == observer.WorldKey)) snapshot.LooseItems.Add(loose);
                foreach (ShipNavigationState task in State.Navigation) if (includedShips.Contains(task.ShipId)) snapshot.Navigation.Add(task);
                foreach (ShipDockingPairState pair in State.Docking) if (includedShips.Contains(pair.ShipAId) && includedShips.Contains(pair.ShipBId)) snapshot.Docking.Add(pair);
                foreach (SpaceSectorId sector in includedSectors) snapshot.Sectors.Add(GetUniverse(sector).State);
                foreach (GroundAtmosphereState ground in State.GroundAtmospheres)
                    if (ground.WorldKey == observer.WorldKey) snapshot.GroundAtmospheres.Add(ground);
                NetworkFilterPoint.SetGauge(includedShips.Count);
            }
            using (NetworkJsonPoint.Measure(snapshot.Ships.Count + snapshot.Devices.Count +
                       snapshot.Passengers.Count + snapshot.LooseItems.Count))
            {
                string json = JsonConvert.SerializeObject(snapshot);
                NetworkJsonPoint.SetGauge(json.Length);
                return json;
            }
        }
        public void ApplyNetworkSnapshot(string json)
        {
            if (GameNetwork.HasStateAuthority || string.IsNullOrWhiteSpace(json)) return;
            SpaceSessionState received = SaveDataMgr.DeserializeSpaceState(json);
            if (State != null && received.SimulationSeconds < State.SimulationSeconds) return;
            double presentationSeconds = State == null ? Time.fixedDeltaTime : received.SimulationSeconds - State.SimulationSeconds;
            CapturePresentationState(false);
            State = received;
            groundAtmosphereIndex.Clear(); groundGeometryWorld = null; groundMachinesDirty = true;
            RebuildSectorDirectory();
            foreach (UniverseState sector in State.Sectors) RegisterSectorPlanets(sector, null);
            contents.Clear(); ClearQueryIndexes();
            foreach (ShipState ship in State.Ships)
                foreach (ShipPieceState piece in ship.Pieces)
                    if (piece.IsAlive && !string.IsNullOrWhiteSpace(piece.SnapshotJson)) contents[piece.PieceId] = Decode(piece.SnapshotJson);
            destroyed.Clear(); foreach (string id in views.Keys) if (!contents.ContainsKey(id)) destroyed.Add(id);
            foreach (string id in destroyed) RemovePieceView(id);
            destroyed.Clear(); foreach (string id in wreckViews.Keys) destroyed.Add(id);
            foreach (string id in destroyed) RemoveWreckView(id);
            var looseIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (SpaceLooseItemState loose in State.LooseItems)
            {
                looseIds.Add(loose.Id);
                looseData[loose.Id] = Decode(loose.Snapshot);
                if (looseViews.TryGetValue(loose.Id, out Item view) && view != null)
                { view.itemData.Stack.Amount = looseData[loose.Id].Stack.Amount; view.itemData.Stack.CanBePickedUp = looseData[loose.Id].Stack.CanBePickedUp; }
            }
            destroyed.Clear(); foreach (string id in looseViews.Keys) if (!looseIds.Contains(id)) destroyed.Add(id);
            foreach (string id in destroyed) { DespawnLooseView(looseViews[id]); looseViews.Remove(id); looseData.Remove(id); }
            RebuildPieceIndex(); viewBindingsDirty = true;
            queryAssemblies.Clear(); simulationAssemblies.Clear(); freeContactAssemblies.Clear();
            Player player = ItemMgr.Instance?.User_Player;
            if (player != null && TryGetNetworkObserver(player.ProfileName, out SpaceNetworkObserver observer) && !string.IsNullOrEmpty(observer.WorldKey))
                GameManager.Instance.ApplyNetworkLocalSpaceObserver(player, observer);
            running = true;
            PrimeInitialNetworkFlightAudio();
            if (presentationSeconds > 0d) CompletePresentationSample(presentationSeconds, Time.timeAsDouble);
            else ResetPresentationHistory();
        }
        public bool TryGetNetworkLooseItem(int guid, out ItemData data, out SpaceSectorPosition position)
        {
            data = null; position = default;
            foreach (SpaceLooseItemState loose in State.LooseItems)
            {
                if (!looseData.TryGetValue(loose.Id, out ItemData saved)) looseData[loose.Id] = saved = Decode(loose.Snapshot);
                if (saved.Guid != guid) continue;
                data = FastCloner.FastCloner.DeepClone(saved);
                position = new SpaceSectorPosition(loose.Motion.Sector, loose.Motion.PositionMeters); return true;
            }
            return false;
        }
        public bool RemoveNetworkLooseItem(int guid)
        {
            foreach (SpaceLooseItemState loose in State.LooseItems.ToArray())
            {
                ItemData saved = looseData.TryGetValue(loose.Id, out ItemData cached) ? cached : Decode(loose.Snapshot);
                if (saved.Guid != guid) continue;
                if (looseViews.Remove(loose.Id, out Item view)) DespawnLooseView(view);
                looseData.Remove(loose.Id); State.LooseItems.Remove(loose); return true;
            }
            return false;
        }
        public void UpdateNetworkLooseItem(ItemData data)
        {
            foreach (SpaceLooseItemState loose in State.LooseItems)
            {
                ItemData saved = looseData.TryGetValue(loose.Id, out ItemData cached) ? cached : Decode(loose.Snapshot);
                if (saved.Guid != data.Guid) continue;
                looseData[loose.Id] = data; loose.Snapshot = Encode(data);
                if (looseViews.TryGetValue(loose.Id, out Item view) && view != null)
                { view.itemData.Stack.Amount = data.Stack.Amount; view.itemData.Stack.CanBePickedUp = data.Stack.CanBePickedUp; }
                return;
            }
        }
        #endregion
    }
}
