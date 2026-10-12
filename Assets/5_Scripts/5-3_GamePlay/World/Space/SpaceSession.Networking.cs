using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using Newtonsoft.Json;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
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
            if (player == null || State == null || State.Passengers.Exists(value => value.ProfileId == player.ProfileName)) return;
            SpacePassengerState passenger = GetPassenger(player);
            passenger.IsInSpace = SpaceMgr.IsSpaceScene(player.Data.CurrentSceneName);
            passenger.LandingWorldKey = passenger.IsInSpace ? null : player.Data.CurrentSceneName;
            if (SpaceSectorCoordinates.TryParseSceneKey(player.Data.CurrentSceneName, out SpaceSectorId sector))
                passenger.FreeMotion.Sector = sector;
        }
        public void SetNetworkPlayerRealm(string profile, bool inSpace, SpaceSectorPosition position, string worldKey)
        {
            if (!GameNetwork.HasStateAuthority || State == null || !position.LocalMeters.IsFinite) return;
            SpacePassengerState passenger = State.Passengers.Find(value => value.ProfileId == profile);
            if (passenger == null) { passenger = new SpacePassengerState { ProfileId = profile }; State.Passengers.Add(passenger); }
            position = SpaceSectorCoordinates.Normalize(position.Sector, position.LocalMeters);
            passenger.IsInSpace = inSpace; passenger.FreeMotion.Sector = position.Sector; passenger.FreeMotion.PositionMeters = position.LocalMeters;
            passenger.LandingWorldKey = inSpace ? null : worldKey;
            if (inSpace) GetUniverse(position.Sector);
        }
        public void SubmitPassengerInput(string profile, SpacePassengerInput input)
        {
            if (!GameNetwork.HasStateAuthority || State?.Passengers.Exists(value => value.ProfileId == profile) != true) return;
            if (!float.IsFinite(input.MoveX) || !float.IsFinite(input.MoveY) || !float.IsFinite(input.Turn) || !float.IsFinite(input.Thrust)) return;
            Vector2 move = Vector2.ClampMagnitude(new Vector2(input.MoveX, input.MoveY), 1f);
            input.MoveX = move.x; input.MoveY = move.y; input.Turn = Mathf.Clamp(input.Turn, -1f, 1f);
            passengerInputs[profile] = input;
            SpacePassengerState passenger = State.Passengers.Find(value => value.ProfileId == profile);
            if (!string.IsNullOrEmpty(passenger.ConsolePieceId)) SetControl(passenger.ConsolePieceId, move, input.Turn);
        }
        public void RemoveNetworkPassengerInput(string profile)
        {
            passengerInputs.Remove(profile);
            SpacePassengerState passenger = State?.Passengers.Find(value => value.ProfileId == profile);
            if (passenger?.ConsolePieceId != null) ClearControl(passenger.ConsolePieceId);
        }
        public bool ExecuteNetworkIntent(string profile, SpaceNetworkIntent intent, out string reason)
        {
            reason = "操作距离或权限不满足";
            if (!GameNetwork.HasStateAuthority || !TryGetNetworkObserver(profile, out SpaceNetworkObserver observer)) return false;
            SpacePassengerState passenger = State.Passengers.Find(value => value.ProfileId == profile);
            if (intent.Kind == SpaceNetworkIntentKind.Respawn) return ExecuteNetworkRespawn(profile, out reason);
            if (intent.Kind is SpaceNetworkIntentKind.Attack or SpaceNetworkIntentKind.AttackEnd or SpaceNetworkIntentKind.UseSkill)
                return ExecuteNetworkAttack(profile, intent.Kind == SpaceNetworkIntentKind.Attack ? 0 : intent.Kind == SpaceNetworkIntentKind.AttackEnd ? 1 : 2,
                    new SpaceSectorPosition(intent.Sector, new SpaceVector2(intent.X, intent.Y)), out reason);
            if (intent.Kind == SpaceNetworkIntentKind.ExitConsole)
            {
                if (passenger.ConsolePieceId != null) ClearControl(passenger.ConsolePieceId);
                passenger.ConsolePieceId = null; passenger.DescentDriving = false; reason = null; return true;
            }
            if (!FindPiece(intent.PieceId, out ShipState ship, out ShipPieceState piece) ||
                !string.IsNullOrEmpty(intent.ShipId) && ship.ShipId != intent.ShipId) return false;
            ShipGeometry.LocalToWorld(ship, (piece.CellX + .5d) * ship.CellSizeMeters,
                (piece.CellY + .5d) * ship.CellSizeMeters, out double x, out double y);
            double distance = observer.InSpace
                ? SpaceSectorCoordinates.Difference(observer.Position, new SpaceSectorPosition(ship.Sector, new SpaceVector2(x, y))).Magnitude
                : WorldTopologyRuntime.Distance(observer.Position.LocalMeters.ToVector2(), new Vector2((float)x, (float)y));
            if (distance > ConsoleInteractionDistance || observer.InSpace != (GetFlight(ship.ShipId)?.Phase == ShipFlightPhase.Cruising)) return false;
            bool result;
            switch (intent.Kind)
            {
                case SpaceNetworkIntentKind.EnterConsole:
                    result = piece.Kind == ShipPieceKind.Console && IsPowered(ship, piece) &&
                        !State.Passengers.Exists(value => value.ProfileId != profile && value.ConsolePieceId == piece.PieceId);
                    if (result)
                    {
                        if (passenger.ConsolePieceId != null) ClearControl(passenger.ConsolePieceId);
                        passenger.ShipId = ship.ShipId; passenger.ConsolePieceId = piece.PieceId; passenger.Supported = true;
                        passenger.LocalX = (piece.CellX + .5d) * ship.CellSizeMeters; passenger.LocalY = (piece.CellY + .5d) * ship.CellSizeMeters;
                    }
                    break;
                case SpaceNetworkIntentKind.ToggleDoor: result = TryToggleDoor(piece.PieceId); break;
                case SpaceNetworkIntentKind.ToggleIgnition: result = ToggleIgnition(piece.PieceId); break;
                case SpaceNetworkIntentKind.RequestLanding:
                    BodyState body = GetLandingCandidate(ship.ShipId);
                    if (body != null && !CanTransferToNetworkSurface(body.SurfaceAddress.WorldKey, out reason)) return false;
                    result = RequestLanding(piece.PieceId); break;
                case SpaceNetworkIntentKind.StartNavigation:
                    result = StartNavigation(piece.PieceId, new SpaceSectorPosition(intent.Sector, new SpaceVector2(intent.X, intent.Y))); break;
                case SpaceNetworkIntentKind.CancelNavigation: CancelNavigation(ship.ShipId); result = true; break;
                case SpaceNetworkIntentKind.ConfirmDock: result = ConfirmDockForShip(ship); break;
                case SpaceNetworkIntentKind.DisconnectDock: result = DisconnectDock(ship.ShipId, intent.TargetId ?? piece.PieceId); break;
                default: result = false; break;
            }
            if (result) reason = null;
            return result;
        }
        private bool ConfirmDockForShip(ShipState ship)
        {
            ShipDockingService.SynchronizePorts(ship);
            foreach (ShipDockingPortState own in ship.DockingPorts)
                foreach (ShipState other in State.Ships)
                {
                    if (other == ship || GetFlight(other.ShipId)?.Phase != GetFlight(ship.ShipId)?.Phase) continue;
                    ShipDockingService.SynchronizePorts(other);
                    foreach (ShipDockingPortState target in other.DockingPorts)
                        if (ShipDockingService.TryCreateCandidate(State.Ships, State.Docking, ship.ShipId, own.PortId, other.ShipId, target.PortId, out ShipDockingPairState pair))
                            return ShipDockingService.Confirm(State.Ships, State.Docking, pair.PairId, ship.ShipId);
                }
            return false;
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
            CaptureContents(); CaptureLooseItems();
            var snapshot = new SpaceSessionState { SimulationSeconds = State.SimulationSeconds, NavigationSequence = State.NavigationSequence };
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
            return JsonConvert.SerializeObject(snapshot);
        }
        public void ApplyNetworkSnapshot(string json)
        {
            if (GameNetwork.HasStateAuthority || string.IsNullOrWhiteSpace(json)) return;
            SpaceSessionState received = SaveDataMgr.DeserializeSpaceState(json);
            if (State != null && received.SimulationSeconds < State.SimulationSeconds) return;
            State = received;
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
