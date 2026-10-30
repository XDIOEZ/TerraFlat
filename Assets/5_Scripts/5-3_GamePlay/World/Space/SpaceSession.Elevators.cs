using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using FlatWorld.Networking;
using FlatWorld.Structures;
using FlatWorld.Localization;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        private const string ElevatorGroundLoadingReason = "正在加载地面区块。";
        #region 电梯资格与真实端点
        public const string SpaceElevatorTerminalItemId = "SpaceElevatorTerminal";
        private readonly Dictionary<string, Inventory_Data> elevatorCargo = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Id, SpaceElevatorEndpoint Endpoint), Inventory_Data> elevatorWarehouses = new();
        private readonly Dictionary<Inventory_Data, Action<ItemSlot>> elevatorWarehouseListeners = new();
        private readonly HashSet<string> elevatorDeliveryDirty = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (ulong Revision, double CellSize, SpaceVector2 Min, SpaceVector2 Max)> elevatorStationBounds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Action<bool, string>> elevatorReceipts = new(StringComparer.Ordinal);

        public static bool HasSpaceElevatorAuthority(bool hasStateAuthority, bool isLocalPlayer) => hasStateAuthority && isLocalPlayer;

        private bool CanOperateElevator(Player actor, out string reason)
        {
            reason = null;
            if (!HasSpaceElevatorAuthority(GameNetwork.HasStateAuthority, actor != null && actor.IsLocalProfile && actor == ItemMgr.Instance?.User_Player))
                reason = "电梯原型目前仅支持本机房主操作，远程客户端暂未开放。";
            else if (State == null || GameManager.Instance?.IsGameplayReady != true || actor.Data == null ||
                actor.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp)?.Hp <= 0f)
                reason = "玩家或电梯会话尚未就绪。";
            else if (transitionRunning || DimensionManager.Instance.IsTransitioning)
                reason = "正在切换世界，请稍后操作。";
            return reason == null;
        }

        public bool TryGetSpaceElevatorEndpoint(Player actor, string terminalPieceId, out SpaceElevatorState elevator,
            out SpaceElevatorEndpoint endpoint, out string reason)
        {
            elevator = null; endpoint = default;
            if (!CanOperateElevator(actor, out reason)) return false;
            foreach (SpaceElevatorState candidate in State.Elevators)
            {
                if (candidate.GroundTerminalId == terminalPieceId) endpoint = SpaceElevatorEndpoint.Ground;
                else if (candidate.StationTerminalId == terminalPieceId) endpoint = SpaceElevatorEndpoint.Station;
                else continue;
                if (!IsAtElevatorEndpoint(actor, candidate, endpoint))
                { reason = "请靠近这一端的电梯终端。"; return false; }
                elevator = candidate; return true;
            }
            reason = "这个终端还没有绑定电梯线路。"; return false;
        }

        private bool IsAtElevatorEndpoint(Player actor, SpaceElevatorState elevator, SpaceElevatorEndpoint endpoint)
        {
            if (actor?.Data == null || actor.DestructionHandled ||
                actor.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp)?.Hp <= 0f) return false;
            SpacePassengerState passenger = FindPassenger(actor.ProfileName);
            if (endpoint == SpaceElevatorEndpoint.Station)
            {
                if (!IsSpaceView || passenger?.Supported != true || passenger.ShipId != elevator.StationShipId ||
                    !FindPiece(elevator.StationTerminalId, out ShipState station, out ShipPieceState terminal) || !terminal.IsOperational) return false;
                double dx = passenger.LocalX - (terminal.CellX + .5d) * station.CellSizeMeters;
                double dy = passenger.LocalY - (terminal.CellY + .5d) * station.CellSizeMeters;
                return dx * dx + dy * dy <= ConsoleInteractionDistance * ConsoleInteractionDistance;
            }
            if (IsSpaceView || actor.gameObject.scene.name != elevator.GroundWorldKey || passenger?.IsInSpace == true) return false;
            Item ground = FindElevatorGroundTerminal(elevator);
            if (ground == null) return false;
            return WorldTopologyRuntime.Distance(WorldLocalPresentation.ToLogical(actor.transform.position),
                new Vector2((float)elevator.GroundX, (float)elevator.GroundY)) <= ConsoleInteractionDistance;
        }

        private Item FindElevatorGroundTerminal(SpaceElevatorState elevator)
        {
            if (ItemMgr.Instance == null || !int.TryParse(elevator.GroundTerminalId, out int guid)) return null;
            if (ItemMgr.Instance.WorldRunTimeItems.TryGetValue(guid, out Item item) && item != null &&
                !item.DestructionHandled && item.itemData?.IDName == SpaceElevatorTerminalItemId && item.gameObject.scene.name == elevator.GroundWorldKey &&
                item.itemMods.GetMod_ByID<Mod_Building>(ModText.Building)?.IsInstalled() == true) return item;
            return null;
        }

        private bool TryGetElevatorForActor(Player actor, string id, out SpaceElevatorState elevator,
            out SpaceElevatorEndpoint endpoint, out string reason)
        {
            elevator = null; endpoint = default;
            if (!CanOperateElevator(actor, out reason)) return false;
            elevator = State.Elevators.Find(value => value.ElevatorId == id);
            if (elevator == null) { reason = "电梯线路不存在。"; return false; }
            endpoint = IsSpaceView ? SpaceElevatorEndpoint.Station : SpaceElevatorEndpoint.Ground;
            if (!IsAtElevatorEndpoint(actor, elevator, endpoint)) { reason = "请靠近电梯终端后操作。"; return false; }
            return true;
        }
        #endregion

        #region 固定空间站创建与锚定
        public static bool ValidateSpaceElevatorPlacement(string worldKey, Vector2 logicalPosition, out string reason)
        {
            reason = null;
            double normalizedX = 0d;
            if (string.IsNullOrWhiteSpace(worldKey) || !WorldAddress.FromWorldKey(worldKey).IsSurface ||
                SpaceMgr.IsSpaceScene(worldKey))
                reason = "太空电梯只能放在星球地表的赤道带。";
            else if (!SpaceSurfaceQuery.IsInsideEquatorBand(worldKey, logicalPosition, out normalizedX))
                reason = "太空电梯只能放在这个星球的赤道带。";
            if (reason == null && Current?.FindSurfaceBody(worldKey) is BodyState body)
            {
                SpaceVector2 offset = SpaceElevatorDomain.GetEquatorAnchorOffset(normalizedX, body.RadiusMeters, body.EquatorTiltRadians);
                foreach (SpaceElevatorState existing in Current.State.Elevators)
                {
                    if (existing.GroundWorldKey != worldKey ||
                        Vector2.Distance(logicalPosition, new Vector2((float)existing.GroundX, (float)existing.GroundY)) < .1f) continue;
                    ShipState station = Current.GetShip(existing.StationShipId);
                    if (station?.Anchor != null && !Current.HasElevatorStationClearance(offset, station))
                    { reason = "与已有电梯的太空站台过近，请沿赤道再移远一些。"; break; }
                }
            }
            return reason == null;
        }

        private bool HasElevatorStationClearance(SpaceVector2 offset, ShipState station)
        {
            var anchor = new SpaceVector2(station.Anchor.OffsetX, station.Anchor.OffsetY);
            if (!SpaceElevatorDomain.HasStationClearance(offset, anchor)) return false;
            if (!elevatorStationBounds.TryGetValue(station.ShipId, out var bounds) ||
                bounds.Revision != station.StructureRevision || bounds.CellSize != station.CellSizeMeters)
            {
                double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
                double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
                foreach (ShipPieceState piece in station.Pieces)
                {
                    if (piece?.IsAlive != true) continue;
                    int width = Math.Max(1, piece.Width), height = Math.Max(1, piece.Height);
                    if ((piece.QuarterTurns & 1) != 0) (width, height) = (height, width);
                    minX = Math.Min(minX, piece.CellX * station.CellSizeMeters);
                    minY = Math.Min(minY, piece.CellY * station.CellSizeMeters);
                    maxX = Math.Max(maxX, (piece.CellX + width) * station.CellSizeMeters);
                    maxY = Math.Max(maxY, (piece.CellY + height) * station.CellSizeMeters);
                }
                bounds = (station.StructureRevision, station.CellSizeMeters,
                    new SpaceVector2(minX, minY), new SpaceVector2(maxX, maxY));
                elevatorStationBounds[station.ShipId] = bounds;
            }
            return SpaceElevatorDomain.HasStationFootprintClearance(offset, anchor, bounds.Min, bounds.Max);
        }

        public bool TryCreateSpaceElevator(Player actor, Item terminal, out SpaceElevatorState elevator, out string reason)
        {
            elevator = null;
            if (!CanOperateElevator(actor, out reason)) return false;
            if (terminal?.itemData?.IDName != SpaceElevatorTerminalItemId || terminal.InHand || IsSpaceView ||
                terminal.itemMods.GetMod_ByID<Mod_Building>(ModText.Building)?.IsInstalled() != true ||
                terminal.gameObject.scene != actor.gameObject.scene ||
                WorldTopologyRuntime.Distance(actor.transform.position, terminal.transform.position) > ConsoleInteractionDistance)
            { reason = "请在星球地表靠近已安装的电梯终端。"; return false; }
            return TryCreateSpaceElevator(terminal, out elevator, out reason);
        }

        // 正式放置提交后立即建站，交互入口仅负责重试未完成的绑定。
        public bool TryCreateSpaceElevator(Item terminal, out SpaceElevatorState elevator, out string reason)
        {
            elevator = null;
            reason = null;
            if (!GameNetwork.HasStateAuthority || State == null || terminal?.itemData?.IDName != SpaceElevatorTerminalItemId ||
                terminal.InHand || terminal.itemMods.GetMod_ByID<Mod_Building>(ModText.Building)?.IsInstalled() != true)
            { reason = "电梯终端尚未完成正式安装。"; return false; }
            string world = terminal.gameObject.scene.name;
            BodyState body = FindSurfaceBody(world);
            if (body == null || !WorldAddress.FromWorldKey(world).IsSurface)
            { reason = "太空电梯只能建在已登记的星球地表。"; return false; }
            Vector2 ground = WorldLocalPresentation.ToLogical(terminal.transform.position);
            if (!ValidateSpaceElevatorPlacement(world, ground, out reason) ||
                !SpaceSurfaceQuery.IsInsideEquatorBand(world, ground, out double normalizedX)) return false;
            string terminalId = terminal.itemData.Guid.ToString(CultureInfo.InvariantCulture);
            foreach (SpaceElevatorState existing in State.Elevators)
            {
                if (existing.GroundWorldKey != world) continue;
                if (existing.GroundTerminalId == terminalId ||
                    Vector2.Distance(new Vector2((float)existing.GroundX, (float)existing.GroundY), ground) < .1f)
                {
                    existing.GroundTerminalId = terminalId;
                    elevator = existing; reason = "已连接现有空间站。"; return true;
                }
            }
            ShipState station = null;
            try
            {
                SpaceVector2 offset = SpaceElevatorDomain.GetEquatorAnchorOffset(normalizedX, body.RadiusMeters,
                    body.EquatorTiltRadians);
                station = new ShipState { Sector = body.Sector, PlatformKind = ShipPlatformKind.Space,
                    ReferenceBodyId = body.BodyId, WorldAddress = SpaceSectorCoordinates.SceneKey(body.Sector),
                    Anchor = new ShipAnchorState { BodyId = body.BodyId, OffsetX = offset.X, OffsetY = offset.Y } };
                SetInitialAnchoredPose(station);
                State.Ships.Add(station);
                State.Flights.Add(new ShipFlightState { ShipId = station.ShipId, BodyId = body.BodyId,
                    SurfaceWorldKey = world, Phase = ShipFlightPhase.Cruising,
                    Motion = new SpaceMotionState { Sector = station.Sector, PositionMeters = new SpaceVector2(station.PositionX, station.PositionY) } });
                for (int y = -2; y <= 2; y++)
                    for (int x = -2; x <= 2; x++)
                    {
                        ShipPieceState floor = AddStationPiece(station, "ShipFloor_Iron", x, y);
                        if (x == 0 && y == 1) { floor.ProtectedAnchor = true; station.IdentityAnchorPieceId = floor.PieceId; }
                    }
                for (int y = -2; y <= 2; y++)
                    for (int x = -2; x <= 2; x++)
                        if (x == -2 || x == 2 || y == -2 || y == 2)
                            AddStationPiece(station, x == 0 && y == -2 ? "ShipDoor" : "ShipWall_Iron", x, y);
                ShipPieceState top = AddStationPiece(station, SpaceElevatorTerminalItemId, 0, 1);
                top.ProtectedAnchor = true;
                SetInitialAnchoredPose(station);
                ShipAtmosphereService.RebuildCompartments(station, GetOutside(station));
                var gas = new FixedStructureGas { PressureKPa = 101.325d, TemperatureCelsius = 20d,
                    Components = new Dictionary<string, double> { ["core:oxygen"] = .21d, ["core:nitrogen"] = .79d } };
                foreach (ShipCompartmentState room in station.Compartments)
                    room.Gas = FixedStructureResourceInitializer.CreateFluidState(gas, null, room.VolumeLiters,
                        ShipAtmosphereService.MinimumCabinGasSpaceLiters, double.MaxValue);
                elevator = new SpaceElevatorState { GroundTerminalId = terminalId, StationTerminalId = top.PieceId,
                    GroundWorldKey = world, GroundX = ground.x, GroundY = ground.y, StationShipId = station.ShipId };
                State.Elevators.Add(elevator);
                GetSpaceElevatorCargo(elevator.ElevatorId);
                CommitPiece(station, top);
                reason = "地面电梯已连接可扩建的空间站。"; return true;
            }
            catch (Exception error)
            {
                if (elevator != null) { State.Elevators.Remove(elevator); elevatorCargo.Remove(elevator.ElevatorId); }
                if (station != null)
                {
                    for (int i = station.Pieces.Count - 1; i >= 0; i--) RollbackPiece(station, station.Pieces[i]);
                    if (State.Ships.Contains(station)) RemoveEmptyShip(station);
                }
                elevator = null; reason = FlatWorldLocalizationService.GetUiFormat("电梯空间站创建失败：{0}", error.Message); return false;
            }
        }

        private ShipPieceState AddStationPiece(ShipState station, string itemId, int x, int y)
        {
            ItemData data = GameRes.ExistingInstance.CreateItemData(itemId);
            do { data.Guid = ItemMgr.Instance.GenerateGuid(); }
            while (data.Guid == 0 || ItemMgr.Instance.GetItemByGuid(data.Guid) != null || Owns(data.Guid));
            data.Stack.Amount = 1f;
            Mod_Building.SetInstalledDataState(data);
            return AddPiece(data, new Vector2(x + .5f, y + .5f), 0, out _, targetShip: station);
        }

        private SpaceSectorPosition GetAnchoredShipPosition(ShipState ship, double seconds)
        {
            BodyState body = GetBody(ship.Anchor.BodyId);
            SpaceVector2 center = GetUniverse(body.Sector).GetPositionAtTime(body.BodyId, seconds);
            return SpaceSectorCoordinates.Normalize(body.Sector, center + new SpaceVector2(
                ship.Anchor.OffsetX + ship.CenterOfMassLocalX, ship.Anchor.OffsetY + ship.CenterOfMassLocalY));
        }

        private void SetInitialAnchoredPose(ShipState ship)
        {
            SpaceSectorPosition point = GetAnchoredShipPosition(ship, State.SimulationSeconds);
            BodyState body = GetBody(ship.Anchor.BodyId);
            SpaceVector2 speed = GetUniverse(body.Sector).GetVelocityAtTime(body.BodyId, State.SimulationSeconds);
            ship.Sector = point.Sector; ship.PositionX = point.LocalMeters.X; ship.PositionY = point.LocalMeters.Y;
            ship.VelocityX = speed.X; ship.VelocityY = speed.Y;
            ship.AngleRadians = ship.AngularVelocityRadiansPerSecond = 0d;
        }

        public bool CanDismantleSpaceElevatorTerminal(string pieceId, out string reason)
        {
            reason = null;
            if (State?.Elevators.Exists(value => value.GroundTerminalId == pieceId || value.StationTerminalId == pieceId) != true) return true;
            reason = "已配对的电梯终端是线路锚点，原型暂不支持拆回；周围空间站可以自由扩建。"; return false;
        }

        public bool CanDismantleShipPiece(string pieceId, out string reason)
        {
            reason = null;
            if (!FindPiece(pieceId, out _, out ShipPieceState piece) || !piece.ProtectedAnchor) return true;
            reason = "固定站台的核心锚点不能拆除。"; return false;
        }
        #endregion

        #region 货舱库存与保存
        public Inventory_Data GetSpaceElevatorCargo(string elevatorId)
        {
            if (State?.Elevators.Find(value => value.ElevatorId == elevatorId) is not SpaceElevatorState elevator) return null;
            if (elevatorCargo.TryGetValue(elevatorId, out Inventory_Data existing)) return existing;
            Inventory_Data inventory = CreateElevatorInventory(elevator.CargoSnapshots, "电梯运输货舱");
            elevatorCargo.Add(elevatorId, inventory); return inventory;
        }

        public Inventory_Data GetSpaceElevatorWarehouse(string elevatorId, SpaceElevatorEndpoint endpoint)
        {
            if (endpoint is not (SpaceElevatorEndpoint.Ground or SpaceElevatorEndpoint.Station) ||
                State?.Elevators.Find(value => value.ElevatorId == elevatorId) is not SpaceElevatorState elevator) return null;
            var key = (elevatorId, endpoint);
            if (elevatorWarehouses.TryGetValue(key, out Inventory_Data existing)) return existing;
            Inventory_Data inventory = CreateElevatorInventory(endpoint == SpaceElevatorEndpoint.Ground
                ? elevator.GroundWarehouseSnapshots : elevator.StationWarehouseSnapshots,
                endpoint == SpaceElevatorEndpoint.Ground ? "电梯地面仓库" : "电梯太空仓库");
            Action<ItemSlot> listener = _ =>
            {
                if (elevator.DeliveryPending && elevator.CargoDestination == endpoint) elevatorDeliveryDirty.Add(elevatorId);
            };
            inventory.Event_OnDataChanged += listener;
            elevatorWarehouseListeners.Add(inventory, listener);
            elevatorWarehouses.Add(key, inventory);
            return inventory;
        }

        private static Inventory_Data CreateElevatorInventory(List<string> snapshots, string name)
        {
            SpaceGameplaySettings settings = SpaceGameplaySettings.Current;
            var slots = new List<ItemSlot>();
            for (int i = 0; i < Math.Max(settings.ElevatorCargoSlots, snapshots.Count); i++)
                slots.Add(new ItemSlot(i) { itemData = i < snapshots.Count && !string.IsNullOrWhiteSpace(snapshots[i])
                    ? Decode(snapshots[i]) : null });
            var inventory = new Inventory_Data(slots, name);
            inventory.ConfigureStorageContainerCapacity(settings.ElevatorCargoMaxWeightKg, settings.ElevatorCargoMaxVolumeLiters);
            return inventory;
        }

        public bool TryTransferSpaceElevatorCargo(Player actor, string elevatorId, Inventory_Data playerInventory,
            int sourceIndex, bool loadCargo, out string reason)
        {
            if (!TryGetElevatorForActor(actor, elevatorId, out SpaceElevatorState elevator, out SpaceElevatorEndpoint endpoint, out reason)) return false;
            if (!OwnsElevatorInventory(actor, playerInventory)) { reason = "只能从玩家自己的真实库存装卸物资。"; return false; }
            Inventory_Data cargo = GetSpaceElevatorWarehouse(elevatorId, endpoint);
            return SpaceElevatorDomain.TryTransferCargo(loadCargo ? playerInventory : cargo, sourceIndex,
                loadCargo ? cargo : playerInventory, out reason);
        }

        private static bool OwnsElevatorInventory(Player player, Inventory_Data inventory)
        {
            if (inventory == null) return false;
            foreach (Module module in player.itemMods.Mods.Values)
            {
                if (module is Mod_HotBar hotbar && ReferenceEquals(hotbar.RuntimeInventory?.Data, inventory)) return true;
                if (module is Mod_Inventory bag && bag.InventoryInstances != null)
                    foreach (Inventory candidate in bag.InventoryInstances)
                        if (ReferenceEquals(candidate?.Data, inventory)) return true;
                if (module is IInventory provider && ReferenceEquals(provider.GetDefaultTargetInventory()?.Data, inventory)) return true;
            }
            return false;
        }

        private void CaptureElevatorCargo()
        {
            foreach (SpaceElevatorState elevator in State.Elevators)
            {
                CaptureInventory(elevatorCargo, elevator.ElevatorId, elevator.CargoSnapshots);
                CaptureInventory(elevatorWarehouses, (elevator.ElevatorId, SpaceElevatorEndpoint.Ground), elevator.GroundWarehouseSnapshots);
                CaptureInventory(elevatorWarehouses, (elevator.ElevatorId, SpaceElevatorEndpoint.Station), elevator.StationWarehouseSnapshots);
            }
        }

        private static void CaptureInventory<TKey>(Dictionary<TKey, Inventory_Data> inventories, TKey key, List<string> snapshots)
        {
            if (!inventories.TryGetValue(key, out Inventory_Data inventory)) return;
            var captured = new List<string>(inventory.itemSlots.Count);
            foreach (ItemSlot slot in inventory.itemSlots)
                captured.Add(slot?.itemData == null ? null : Encode(slot.itemData));
            snapshots.Clear();
            snapshots.AddRange(captured);
        }

        private void ResetElevatorRuntime()
        {
            foreach (var listener in elevatorWarehouseListeners) listener.Key.Event_OnDataChanged -= listener.Value;
            elevatorWarehouseListeners.Clear(); elevatorWarehouses.Clear(); elevatorDeliveryDirty.Clear(); elevatorStationBounds.Clear();
            elevatorCargo.Clear(); elevatorReceipts.Clear();
        }
        #endregion

        #region 运行调度与玩家乘坐预约
        public bool TryPackAndSendSpaceElevatorCargo(Player actor, string elevatorId,
            Action<bool, string> completed, out string reason)
        {
            if (!TryGetElevatorForActor(actor, elevatorId, out SpaceElevatorState elevator,
                    out SpaceElevatorEndpoint endpoint, out reason)) return false;
            SpaceElevatorEndpoint destination = endpoint == SpaceElevatorEndpoint.Ground
                ? SpaceElevatorEndpoint.Station : SpaceElevatorEndpoint.Ground;
            if (!SpaceElevatorDomain.CanAccessCargo(elevator, endpoint))
            { reason = "请先召回轿厢，停靠后再打包发送。"; return false; }
            if (elevator.DeliveryPending)
            { reason = "请先腾出目的地仓库空间，上一批物资入库后才能再次打包。"; return false; }
            SpaceElevatorEndpoint previousDestination = elevator.Destination;
            string previousStatus = elevator.Status;
            if (!SpaceElevatorDomain.TryBegin(elevator, destination, SpaceGameplaySettings.Current.ElevatorTravelSeconds, out reason)) return false;
            if (!SpaceElevatorDomain.TryPackCargo(GetSpaceElevatorWarehouse(elevatorId, endpoint), GetSpaceElevatorCargo(elevatorId), out reason))
            {
                elevator.Travelling = false; elevator.RemainingSeconds = 0d;
                elevator.Destination = previousDestination; elevator.Status = previousStatus;
                return false;
            }
            elevator.CargoDestination = destination;
            elevator.RiderProfileId = null;
            if (completed != null) elevatorReceipts[elevatorId] = completed;
            reason = "已打包发送，到站后会自动放入目的地仓库。";
            return true;
        }

        public bool TryCallSpaceElevator(Player actor, string elevatorId, out string reason)
        {
            if (!TryGetElevatorForActor(actor, elevatorId, out SpaceElevatorState elevator, out SpaceElevatorEndpoint endpoint, out reason)) return false;
            return SpaceElevatorDomain.TryBegin(elevator, endpoint, SpaceGameplaySettings.Current.ElevatorTravelSeconds, out reason);
        }

        public bool TryDispatchSpaceElevator(Player actor, string elevatorId, bool ride, Action<bool, string> completed, out string reason)
        {
            if (!ride) return TryPackAndSendSpaceElevatorCargo(actor, elevatorId, completed, out reason);
            if (!TryGetElevatorForActor(actor, elevatorId, out SpaceElevatorState elevator, out SpaceElevatorEndpoint endpoint, out reason)) return false;
            if (!SpaceElevatorDomain.CanAccessCargo(elevator, endpoint))
            { reason = "轿厢尚未停在这一端。"; return false; }
            if (ride && State.Elevators.Exists(value => value.RiderProfileId == actor.ProfileName))
            { reason = "你已有一个乘坐预约。"; return false; }
            SpaceElevatorEndpoint destination = endpoint == SpaceElevatorEndpoint.Ground ? SpaceElevatorEndpoint.Station : SpaceElevatorEndpoint.Ground;
            if (ride && destination == SpaceElevatorEndpoint.Station && !TryGetElevatorArrival(elevator, destination, out _, out reason)) return false;
            if (!SpaceElevatorDomain.TryBegin(elevator, destination, SpaceGameplaySettings.Current.ElevatorTravelSeconds, out reason)) return false;
            elevator.RiderProfileId = ride ? actor.ProfileName : null;
            if (completed != null) elevatorReceipts[elevatorId] = completed;
            reason = ride ? "已预约乘坐，请留在终端附近；走开会取消乘坐，货运继续。" : "轿厢已出发。";
            return true;
        }

        private void StepSpaceElevators(double seconds)
        {
            foreach (SpaceElevatorState elevator in State.Elevators)
            {
                if (elevator.DeliveryPending && (elevatorDeliveryDirty.Remove(elevator.ElevatorId) ||
                    !elevatorWarehouses.ContainsKey((elevator.ElevatorId, elevator.CargoDestination))))
                    DeliverElevatorCargo(elevator);
                if (!elevator.Travelling) continue;
                Player rider = string.IsNullOrWhiteSpace(elevator.RiderProfileId) ? null : ItemMgr.Instance?.User_Player;
                if (!string.IsNullOrWhiteSpace(elevator.RiderProfileId) && (rider?.ProfileName != elevator.RiderProfileId ||
                    !IsAtElevatorEndpoint(rider, elevator, elevator.CabinEndpoint)))
                {
                    elevator.RiderProfileId = null;
                    CompleteElevatorReceipt(elevator, false, "乘坐已取消，轿厢物资继续运输。");
                }
                if (!SpaceElevatorDomain.Step(elevator, seconds)) continue;
                DeliverElevatorCargo(elevator);
                if (rider != null && elevator.RiderProfileId == rider.ProfileName)
                    StartCoroutine(TransferElevatorPassenger(elevator, rider));
                else CompleteElevatorReceipt(elevator, true, elevator.DeliveryPending
                    ? "已抵达；目的地仓库空间不足，余货会在腾出空位后自动入库。"
                    : "物资已抵达，并已自动放入目的地仓库。");
            }
        }

        private void DeliverElevatorCargo(SpaceElevatorState elevator)
        {
            elevator.DeliveryPending = false;
            elevator.DeliveryPending = !SpaceElevatorDomain.DeliverCargo(GetSpaceElevatorCargo(elevator.ElevatorId),
                GetSpaceElevatorWarehouse(elevator.ElevatorId, elevator.CargoDestination));
            elevatorDeliveryDirty.Remove(elevator.ElevatorId);
            if (elevator.DeliveryPending) elevator.Status = "目的地仓库空间不足，余货等待自动入库";
            else elevator.Status = elevator.CabinEndpoint == SpaceElevatorEndpoint.Ground ? "轿厢停在地面" : "轿厢停在空间站";
        }

        private void CompleteElevatorReceipt(SpaceElevatorState elevator, bool accepted, string reason)
        {
            if (elevatorReceipts.Remove(elevator.ElevatorId, out Action<bool, string> callback)) callback?.Invoke(accepted, reason);
        }

        private bool TryGetElevatorArrival(SpaceElevatorState elevator, SpaceElevatorEndpoint endpoint, out Vector2 point, out string reason)
        {
            point = default; reason = null;
            if (endpoint == SpaceElevatorEndpoint.Station)
            {
                if (!FindPiece(elevator.StationTerminalId, out ShipState station, out ShipPieceState terminal) || station.IsWreck || station.Anchor == null)
                { reason = "空间站电梯终端已失效。"; return false; }
                for (int radius = 1; radius <= 3; radius++)
                    for (int y = -radius; y <= radius; y++)
                        for (int x = -radius; x <= radius; x++)
                        {
                            if (Math.Max(Math.Abs(x), Math.Abs(y)) != radius) continue;
                            int cx = terminal.CellX + x, cy = terminal.CellY + y;
                            if (!ShipAtmosphereService.HasFloor(station, cx, cy) || station.Pieces.Exists(piece =>
                                piece.IsAlive && piece.BlocksPassage && ContainsCell(piece, cx, cy))) continue;
                            point = new Vector2((float)((cx + .5d) * station.CellSizeMeters), (float)((cy + .5d) * station.CellSizeMeters));
                            return true;
                        }
                reason = "空间站终端周围没有安全落脚的地板，请先清出一格。"; return false;
            }
            if (!GameplayWorldRegistry.TryGet(elevator.GroundWorldKey, out _))
            { reason = "地面世界尚未加载，请先在地面呼叫电梯。"; return false; }
            using var worldScope = GameplayWorldRegistry.UseWorld(elevator.GroundWorldKey);
            if (FindElevatorGroundTerminal(elevator) == null)
            { reason = "地面终端已损毁，请在原位置重建终端后重新绑定。"; return false; }
            if (SpaceSurfaceQuery.EnsureSurfaceAreaReady(elevator.GroundWorldKey,
                    new Vector2((float)elevator.GroundX, (float)elevator.GroundY), 4f) == SurfaceQueryStatus.Pending)
            { reason = ElevatorGroundLoadingReason; return false; }
            for (int radius = 1; radius <= 3; radius++)
                for (int y = -radius; y <= radius; y++)
                    for (int x = -radius; x <= radius; x++)
                    {
                        if (Math.Max(Math.Abs(x), Math.Abs(y)) != radius) continue;
                        Vector2 candidate = new((float)Math.Floor(elevator.GroundX) + x + .5f, (float)Math.Floor(elevator.GroundY) + y + .5f);
                        if (!SpaceSurfaceQuery.TryGetLandingSurface(elevator.GroundWorldKey, candidate, out LandingSurfaceSample surface) ||
                            surface.BlockingTileId != 0 || surface.IsWater && surface.SupportTileId == 0 ||
                            !BuildingOccupancyRegistry.GetEffectiveWalkable(surface.Cell, true)) continue;
                        point = candidate; return true;
                    }
            reason = "地面终端周围没有安全落脚点，请先清空周围位置。"; return false;
        }

        private IEnumerator TransferElevatorPassenger(SpaceElevatorState elevator, Player actor)
        {
            elevator.RiderProfileId = null;
            if (!CanOperateElevator(actor, out string reason))
            { CompleteElevatorReceipt(elevator, false, WithElevatorCargoReceipt(reason)); yield break; }
            SpaceSessionState savedSession = State;
            SpacePassengerState currentPassenger = GetPassenger(actor);
            SpacePassengerState previous = Newtonsoft.Json.JsonConvert.DeserializeObject<SpacePassengerState>(
                Newtonsoft.Json.JsonConvert.SerializeObject(currentPassenger));
            string profile = actor.ProfileName;
            int index = State.Passengers.IndexOf(currentPassenger);
            uint revision = previous.RealmRevision;
            bool succeeded = false, committed = false;
            Mod_GameController controller = actor.GetComponentInChildren<Mod_GameController>(true);
            object inputLease = new();
            controller?.AcquireGameplayInputLock(inputLease);
            transitionRunning = true;
            try
            {
                if (elevator.CabinEndpoint == SpaceElevatorEndpoint.Ground)
                {
                    Exception preparationFailure = null;
                    yield return GameManager.RunWorldTransitionSteps(GameplayWorldRegistry.PrepareWorld(elevator.GroundWorldKey,
                        new Vector3((float)elevator.GroundX, (float)elevator.GroundY)), error => preparationFailure = error);
                    if (preparationFailure != null) { reason = preparationFailure.Message; yield break; }
                }
                SpaceElevatorEndpoint departure = elevator.CabinEndpoint == SpaceElevatorEndpoint.Ground ? SpaceElevatorEndpoint.Station : SpaceElevatorEndpoint.Ground;
                if (State != savedSession || actor != ItemMgr.Instance?.User_Player || !IsAtElevatorEndpoint(actor, elevator, departure))
                { reason = "乘坐准备期间玩家离开终端，已取消乘坐。"; yield break; }
                Vector2 point;
                while (!TryGetElevatorArrival(elevator, elevator.CabinEndpoint, out point, out reason))
                {
                    if (reason != ElevatorGroundLoadingReason || State != savedSession || actor != ItemMgr.Instance?.User_Player ||
                        !IsAtElevatorEndpoint(actor, elevator, departure)) yield break;
                    yield return null;
                }
                if (elevator.CabinEndpoint == SpaceElevatorEndpoint.Station)
                {
                    ShipState station = GetShip(elevator.StationShipId);
                    var target = new SpacePassengerState { ProfileId = profile, ShipId = station.ShipId,
                        LocalX = point.x, LocalY = point.y, Supported = true, IsInSpace = true,
                        UnknownOptionalFields = previous.UnknownOptionalFields,
                        RealmRevision = unchecked(revision + 1), SavedCameraSize = previous.SavedCameraSize,
                        FreeMotion = new SpaceMotionState { Sector = station.Sector, UnknownOptionalFields = previous.FreeMotion?.UnknownOptionalFields } };
                    State.Passengers[index] = target; committed = true;
                    if (GameNetwork.IsOnline)
                    {
                        if (TryGetNetworkObserver(profile, out SpaceNetworkObserver observer)) GameManager.Instance.ApplyNetworkLocalSpaceObserver(actor, observer);
                    }
                    else if (!DimensionManager.Instance.TryBeginPlanetLanding(actor,
                        WorldAddress.FromWorldKey(SpaceSectorCoordinates.SceneKey(station.Sector)), Vector2.zero)) yield break;
                }
                else if (GameNetwork.IsOnline)
                {
                    bool replied = false, accepted = false;
                    yield return DimensionTransitionService.ExecuteExact(actor, WorldAddress.FromWorldKey(elevator.GroundWorldKey), point,
                        (success, message) => { replied = true; accepted = success; reason = message; },
                        () => State == savedSession && actor == ItemMgr.Instance?.User_Player &&
                            actor.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp)?.Hp > 0f);
                    if (!replied || !accepted) yield break;
                    committed = true;
                }
                else
                {
                    State.Passengers[index] = new SpacePassengerState { ProfileId = profile,
                        LandingWorldKey = elevator.GroundWorldKey, LandingX = point.x, LandingY = point.y,
                        UnknownOptionalFields = previous.UnknownOptionalFields,
                        RealmRevision = unchecked(revision + 1), FreeMotion = new SpaceMotionState { PositionMeters = SpaceVector2.FromVector2(point),
                            UnknownOptionalFields = previous.FreeMotion?.UnknownOptionalFields } };
                    committed = true;
                    if (!DimensionManager.Instance.TryBeginPlanetLanding(actor, WorldAddress.FromWorldKey(elevator.GroundWorldKey), point)) yield break;
                }
                double deadline = Time.realtimeSinceStartupAsDouble + 30d;
                string targetWorld = elevator.CabinEndpoint == SpaceElevatorEndpoint.Station
                    ? SpaceSectorCoordinates.SceneKey(GetShip(elevator.StationShipId).Sector) : elevator.GroundWorldKey;
                while (State == savedSession && Time.realtimeSinceStartupAsDouble < deadline)
                {
                    Player current = ItemMgr.Instance?.User_Player;
                    if (current == null || current.ProfileName != profile || current.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp)?.Hp <= 0f) break;
                    if (current.gameObject.scene.name == targetWorld && SceneManager.GetActiveScene().name == targetWorld && !DimensionManager.Instance.IsTransitioning)
                    {
                        RestorePassengerPose(current);
                        SetTerrainSuppression(current, elevator.CabinEndpoint == SpaceElevatorEndpoint.Station);
                        Mod_Mover mover = current.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover);
                        if (mover?.rb != null) { mover.rb.velocity = Vector2.zero; mover.rb.angularVelocity = 0f; }
                        succeeded = true; break;
                    }
                    yield return null;
                }
            }
            finally
            {
                if (!succeeded && committed && State == savedSession && index < State.Passengers.Count &&
                    State.Passengers[index].ProfileId == profile && State.Passengers[index].RealmRevision == unchecked(revision + 1) &&
                    ItemMgr.Instance?.User_Player?.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp)?.Hp > 0f)
                {
                    State.Passengers[index] = previous;
                    unchecked { previous.RealmRevision += 2; }
                    Player current = ItemMgr.Instance?.User_Player;
                    if (current?.ProfileName == profile && GameNetwork.IsOnline && TryGetNetworkObserver(profile, out SpaceNetworkObserver observer))
                        GameManager.Instance.ApplyNetworkLocalSpaceObserver(current, observer);
                }
                controller?.ReleaseGameplayInputLock(inputLease);
                transitionRunning = false;
                CompleteElevatorReceipt(elevator, succeeded, succeeded ? "已抵达，可在本端装卸物资。" :
                    WithElevatorCargoReceipt(reason ?? "玩家迁移未完成，请重新呼叫轿厢后再试。"));
            }
        }

        private static string WithElevatorCargoReceipt(string reason)
            => FlatWorldLocalizationService.GetUiFormat("{0}已发送物资会继续送往目的地仓库。", FlatWorldLocalizationService.GetUiText(reason));
        #endregion
    }
}
