using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FlatWorld.Gameplay.Progress;
using FlatWorld.Networking;
using FlatWorld.Structures;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.Spaceflight
{
    public static class ShipGroundAssembly
    {
        #region 普通地面控制台与离散落点
        public static bool TryGetGroundConsole(Player actor, int consoleGuid, out MachineEntity console)
        {
            console = null;
            if (actor == null || SpaceMgr.IsSpaceScene(actor.gameObject.scene.name)) return false;
            using (MachineWorld.UseScope(actor.gameObject.scene.name))
            {
                MachineEntity candidate = MachineWorld.GetById(consoleGuid);
                if (candidate == null || candidate.ScopeKey != actor.gameObject.scene.name ||
                    SpaceSession.ResolvePartConfiguration(candidate.Snapshot)?.Kind != ShipPieceKind.Console ||
                    WorldTopologyRuntime.Distance(actor.transform.position,
                        WorldLocalPresentation.ProjectPosition(candidate.Snapshot.transform.position)) > SpaceSession.ConsoleInteractionDistance)
                    return false;
                console = candidate; return true;
            }
        }

        public static bool TryLaunch(Player actor, int consoleGuid, out ShipState ship, out string reason)
        {
            ship = null;
            if (!GameNetwork.HasStateAuthority || !TryGetGroundConsole(actor, consoleGuid, out MachineEntity console))
            { reason = "请靠近有效的地面控制台后点火。"; return false; }
            return SpaceSession.EnsureLoaded().TryLaunchGroundShip(actor, console, out ship, out reason);
        }

        // 整船只吸附一次，旋转后的矩形左下格由同一整数变换求出。
        public static int SnapQuarterTurns(double radians) =>
            (int)Math.Round(radians / (Math.PI * .5d), MidpointRounding.AwayFromZero) & 3;

        public static Vector2Int RotateAnchor(int x, int y, int width, int height, int quarterTurns) =>
            (quarterTurns & 3) switch
            {
                1 => new Vector2Int(-y - height, x),
                2 => new Vector2Int(-x - width, -y - height),
                3 => new Vector2Int(y, -x - width),
                _ => new Vector2Int(x, y)
            };
        #endregion
    }

    public sealed partial class SpaceSession
    {
        #region 起飞时从普通建筑装配
        private const string GroundPartStateKey = "flatworld.ship-part-state";

        internal bool TryLaunchGroundShip(Player actor, MachineEntity console, out ShipState ship, out string reason)
        {
            ship = null; reason = null;
            string world = console.ScopeKey;
            var selected = new List<ShipGroundBuildingStore.Entry>();
            var pieces = new List<ShipPieceState>();
            var removedItems = new List<ShipGroundBuildingStore.Entry>();
            var originalData = new Dictionary<int, ItemData>();
            var originalPlacement = new Dictionary<int, MachineScopePlacement>();
            var machineIds = new List<int>();
            var existingShips = new HashSet<string>();
            foreach (ShipState existing in State.Ships) existingShips.Add(existing.ShipId);
            bool transferred = false;
            try
            {
                selected = CollectGroundAssembly(world, console.Id);
                foreach (ShipGroundBuildingStore.Entry entry in selected)
                {
                    ItemData snapshot = entry.Capture();
                    originalData.Add(entry.Guid, FastCloner.FastCloner.DeepClone(snapshot));
                    originalPlacement.Add(entry.Guid, new MachineScopePlacement(entry.Cell,
                        new Vector3(entry.Cell.x + .5f, entry.Cell.y + .5f), entry.QuarterTurns));
                    Vector2 point = WorldLocalPresentation.ProjectPosition(new Vector2(entry.Cell.x + .5f, entry.Cell.y + .5f));
                    ShipPieceState piece = AddPiece(snapshot, point, entry.QuarterTurns, out ShipState ownerShip, registerMachine: false);
                    if (existingShips.Contains(ownerShip.ShipId))
                    {
                        RollbackPiece(ownerShip, piece);
                        throw new InvalidOperationException("地面建筑与正在飞行的船体重叠，请移开后再起飞。");
                    }
                    ship ??= ownerShip;
                    if (ownerShip != ship)
                    {
                        RollbackPiece(ownerShip, piece);
                        throw new InvalidOperationException("控制台脚下的地板没有连接成完整船体。");
                    }
                    pieces.Add(piece);
                    RestoreGroundPartState(snapshot, piece);
                    if (entry.Machine != null) machineIds.Add(entry.Guid);
                }
                if (ship == null) throw new InvalidOperationException("没有找到可以装配的船体。");
                ShipState candidate = ship;
                var destinations = new Dictionary<int, MachineScopePlacement>();
                foreach (ShipPieceState piece in pieces)
                    destinations.Add(int.Parse(piece.PieceId, CultureInfo.InvariantCulture), new MachineScopePlacement(
                        new Vector2Int(piece.CellX, piece.CellY),
                        new Vector3((float)((piece.CellX + .5d) * ship.CellSizeMeters),
                            (float)((piece.CellY + .5d) * ship.CellSizeMeters)), piece.QuarterTurns));
                MachineWorld.TransferScopeNodes(world, MachineWorld.GetShipScopeKey(ship.ShipId), machineIds,
                    node => destinations[node.Id]);
                transferred = true;
                ShipAtmosphereService.RebuildCompartments(ship, GetOutside(ship));
                if (!ToggleIgnition(console.Id.ToString(CultureInfo.InvariantCulture)))
                    throw new InvalidOperationException("点火失败：请检查控制台供电、引擎方向、管路、燃料和载重。");
                foreach (ShipGroundBuildingStore.Entry entry in selected)
                    if (entry.Item != null)
                    { removedItems.Add(entry); ShipGroundBuildingStore.RemoveItem(entry); }
                InitializeTemplateCabin(ship, console.Id);
                foreach (ShipPieceState piece in pieces)
                    if (MachineCatalog.Get(piece.ItemId) != null)
                        using (MachineWorld.UseScope(MachineWorld.GetShipScopeKey(candidate.ShipId)))
                            CaptureMachinePieceSnapshot(piece, MachineWorld.GetById(int.Parse(piece.PieceId, CultureInfo.InvariantCulture)));
                CommitPiece(ship, pieces[pieces.Count - 1]);
                reason = "地面建筑已装配，飞船开始起飞。"; return true;
            }
            catch (Exception error)
            {
                if (ship != null)
                {
                    if (transferred)
                    {
                        try { MachineWorld.TransferScopeNodes(MachineWorld.GetShipScopeKey(ship.ShipId), world, machineIds, node =>
                        {
                            node.Snapshot.ItemSpecialData = originalData[node.Id].ItemSpecialData;
                            return originalPlacement[node.Id];
                        }); }
                        catch (Exception rollback)
                        {
                            reason = "起飞移交中断，已保留船体数据等待恢复：" + error.Message + "；" + rollback.Message;
                            Debug.LogException(rollback); return false;
                        }
                    }
                    for (int i = pieces.Count - 1; i >= 0; i--) RollbackPiece(ship, pieces[i]);
                    if (shipColliderPresenters.Remove(ship.ShipId, out ShipColliderPresenter presenter)) CompleteGroundCleanup(presenter.Dispose);
                }
                foreach (ShipGroundBuildingStore.Entry entry in removedItems)
                    if (entry.Item == null || entry.Item.DestructionHandled)
                        ShipGroundBuildingStore.Place(originalData[entry.Guid], entry.Cell, entry.QuarterTurns, world);
                ship = null; reason = "无法起飞：" + error.Message; return false;
            }
        }

        private static List<ShipGroundBuildingStore.Entry> CollectGroundAssembly(string world, int consoleGuid)
        {
            List<ShipGroundBuildingStore.Entry> available = ShipGroundBuildingStore.Collect(world);
            ShipGroundBuildingStore.Entry console = available.Find(entry => entry.Guid == consoleGuid);
            if (console == null) throw new InvalidOperationException("地面控制台已失效。");
            var floors = new Dictionary<Vector2Int, ShipGroundBuildingStore.Entry>();
            foreach (ShipGroundBuildingStore.Entry entry in available)
                if (ResolvePartConfiguration(entry.Data)?.Kind == ShipPieceKind.Floor)
                    foreach (Vector2Int cell in GroundFootprint(entry)) floors[cell] = entry;
            var cells = new HashSet<Vector2Int>();
            var floorIds = new HashSet<int>();
            var queue = new Queue<Vector2Int>();
            var selected = new List<ShipGroundBuildingStore.Entry>();
            queue.Enqueue(console.Cell);
            while (queue.Count > 0)
            {
                Vector2Int cell = WorldTopologyRuntime.NormalizeCell(queue.Dequeue());
                if (cells.Contains(cell) || !floors.TryGetValue(cell, out ShipGroundBuildingStore.Entry floor)) continue;
                cells.Add(cell);
                if (floorIds.Add(floor.Guid)) selected.Add(floor);
                queue.Enqueue(cell + Vector2Int.left); queue.Enqueue(cell + Vector2Int.right);
                queue.Enqueue(cell + Vector2Int.up); queue.Enqueue(cell + Vector2Int.down);
            }
            if (selected.Count == 0) throw new InvalidOperationException("控制台需要放在四邻连通的飞船地板上。");
            foreach (ShipGroundBuildingStore.Entry entry in available)
            {
                if (floorIds.Contains(entry.Guid)) continue;
                bool touches = false, supported = true;
                foreach (Vector2Int cell in GroundFootprint(entry))
                { bool inside = cells.Contains(cell); touches |= inside; supported &= inside; }
                if (!touches) continue;
                if (!supported) throw new InvalidOperationException("建筑跨出了船体地板，请先完整承载或移开：" + entry.Data.IDName);
                selected.Add(entry);
            }
            return selected;
        }

        private static IEnumerable<Vector2Int> GroundFootprint(ShipGroundBuildingStore.Entry entry)
        {
            ShipPartConfiguration config = ResolvePartConfiguration(entry.Data);
            Mod_Building.TryReadBuildingData(entry.Data, out _, out Mod_Building.Building_Data building);
            int width = Math.Clamp(Math.Max(config.FootprintWidth, building?.FootprintWidth ?? 1), 1, 8);
            int height = Math.Clamp(Math.Max(config.FootprintHeight, building?.FootprintHeight ?? 1), 1, 8);
            if ((entry.QuarterTurns & 1) != 0) (width, height) = (height, width);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++) yield return WorldTopologyRuntime.NormalizeCell(entry.Cell + new Vector2Int(x, y));
        }

        private static void InitializeTemplateCabin(ShipState ship, int consoleGuid)
        {
            using var scope = MachineWorld.UseScope(MachineWorld.GetShipScopeKey(ship.ShipId));
            MachineEntity console = MachineWorld.GetById(consoleGuid);
            JObject root = ItemSpecialDataJsonStore.ReadRoot(console.Snapshot.ItemSpecialData);
            JObject marker = root[ShipFixedStructureBuilder.TemplateMarker] as JObject;
            if (marker?["initialCabinGas"] is not JObject payload) return;
            if (ship.Compartments.Count == 0) throw new InvalidOperationException("模板配置了舱气，但当前船体没有密闭舱。");
            FixedStructureGas gas = payload.ToObject<FixedStructureGas>();
            foreach (ShipCompartmentState room in ship.Compartments)
                room.Gas = FixedStructureResourceInitializer.CreateFluidState(gas, null, room.VolumeLiters,
                    ShipAtmosphereService.MinimumCabinGasSpaceLiters, double.MaxValue);
            marker.Remove("initialCabinGas");
            console.Snapshot.ItemSpecialData = root.ToString(Newtonsoft.Json.Formatting.None);
        }
        #endregion

        #region 落地后交还普通建筑与地表网络
        private string lastGroundRestoreError;

        private void RestoreLandedBuildings()
        {
            if (IsSpaceView || !GameNetwork.HasStateAuthority || GameManager.Instance?.IsGameplayReady != true ||
                DimensionManager.ExistingInstance?.IsTransitioning == true) return;
            string world = SceneManager.GetActiveScene().name;
            foreach (ShipState ship in State.Ships.ToArray())
            {
                ShipFlightState flight = GetFlight(ship.ShipId);
                if (ship.IsWreck || flight == null || flight.SurfaceWorldKey != world ||
                    flight.Phase is not (ShipFlightPhase.Landed or ShipFlightPhase.Floating)) continue;
                try { RestoreGroundShip(ship, world); lastGroundRestoreError = null; }
                catch (Exception error)
                {
                    if (lastGroundRestoreError == error.Message) continue;
                    lastGroundRestoreError = error.Message;
                    Debug.LogError("[飞船落地] 普通建筑移交未完成：" + error.Message, this);
                }
            }
        }

        private void RestoreGroundShip(ShipState ship, string world)
        {
            using var worldScope = MachineWorld.UseScope(world);
            if (Math.Abs(ship.CellSizeMeters - 1d) > .000001d) throw new InvalidOperationException("船格必须与普通建筑的一米格对齐。");
            int turns = ShipGroundAssembly.SnapQuarterTurns(ship.AngleRadians);
            Vector2Int origin = new((int)Math.Round(ship.PositionX, MidpointRounding.AwayFromZero),
                (int)Math.Round(ship.PositionY, MidpointRounding.AwayFromZero));
            var placements = new Dictionary<int, MachineScopePlacement>();
            var snapshots = new Dictionary<int, ItemData>();
            var machineIds = new List<int>();
            var created = new List<ShipGroundBuildingStore.Entry>();
            foreach (ShipPieceState piece in ship.Pieces)
            {
                if (!piece.IsAlive) continue;
                int id = int.Parse(piece.PieceId, CultureInfo.InvariantCulture);
                int width = (piece.QuarterTurns & 1) == 0 ? piece.Width : piece.Height;
                int height = (piece.QuarterTurns & 1) == 0 ? piece.Height : piece.Width;
                Vector2Int cell = WorldTopologyRuntime.NormalizeCell(origin + ShipGroundAssembly.RotateAnchor(
                    piece.CellX, piece.CellY, width, height, turns));
                int q = (piece.QuarterTurns + turns) & 3;
                placements.Add(id, new MachineScopePlacement(cell, new Vector3(cell.x + .5f, cell.y + .5f), q));
                MachineDefinition definition = MachineCatalog.Get(piece.ItemId);
                int layer = piece.Kind == ShipPieceKind.Floor ? Mod_ShipPart.GroundFloorLayer : definition?.Layer ??
                    ReadBuildingPlacementLayer(GameRes.ExistingInstance.TryGetItemDefinition(piece.ItemId, out RuntimeItemDefinition itemDefinition) ? itemDefinition : null);
                int worldWidth = (turns & 1) == 0 ? width : height, worldHeight = (turns & 1) == 0 ? height : width;
                for (int y = 0; y < worldHeight; y++)
                    for (int x = 0; x < worldWidth; x++)
                        if (BuildingOccupancyRegistry.IsOccupied(cell + new Vector2Int(x, y), layer: layer))
                            throw new InvalidOperationException("落点的建筑层已被占用，请清理落点。" );
                ItemData snapshot;
                if (definition != null)
                {
                    using (MachineWorld.UseScope(MachineWorld.GetShipScopeKey(ship.ShipId)))
                        snapshot = MachineWorld.CaptureSnapshot(MachineWorld.GetById(id));
                    machineIds.Add(id);
                }
                else snapshot = FastCloner.FastCloner.DeepClone(GetPieceData(piece));
                if (snapshot == null) throw new InvalidOperationException("船部件快照缺失：" + piece.ItemId);
                CaptureRecoveredPieceHealth(snapshot, piece);
                SaveGroundPartState(snapshot, piece);
                snapshots.Add(id, snapshot);
            }
            bool transferred = false;
            try
            {
                // 先创建独立的非机器建筑，再将真实机器整批接回地表管网。
                foreach (ShipPieceState piece in ship.Pieces)
                {
                    if (!piece.IsAlive || MachineCatalog.Get(piece.ItemId) != null) continue;
                    int id = int.Parse(piece.PieceId, CultureInfo.InvariantCulture);
                    MachineScopePlacement target = placements[id];
                    created.Add(ShipGroundBuildingStore.Place(snapshots[id], target.Cell, target.QuarterTurns, world));
                }
                MachineWorld.TransferScopeNodes(MachineWorld.GetShipScopeKey(ship.ShipId), world, machineIds, node =>
                {
                    ShipPieceState piece = ship.Pieces.Find(value => value.PieceId == node.Id.ToString(CultureInfo.InvariantCulture));
                    if (node.State != null) node.State.Hp = (float)piece.Health;
                    CaptureRecoveredPieceHealth(node.Snapshot, piece);
                    node.Snapshot.ItemSpecialData = snapshots[node.Id].ItemSpecialData;
                    return placements[node.Id];
                });
                transferred = true;
                ship.PositionX = origin.x; ship.PositionY = origin.y; ship.AngleRadians = turns * Math.PI * .5d;
                // 数据交接成功先撤销船体所有权，后续表现通知异常不能再次创建落地建筑。
                foreach (ShipPieceState piece in ship.Pieces)
                {
                    contents.Remove(piece.PieceId); pieceIndex.Remove(piece.PieceId);
                    State.Devices.RemoveAll(device => device.PieceId == piece.PieceId);
                }
                State.Ships.Remove(ship);
                State.Flights.RemoveAll(flight => flight.ShipId == ship.ShipId);
                State.Navigation.RemoveAll(task => task.ShipId == ship.ShipId);
                State.Docking.RemoveAll(pair => pair.ShipAId == ship.ShipId || pair.ShipBId == ship.ShipId);
                pieceIndexDirty = true; viewBindingsDirty = true;
                foreach (SpacePassengerState passenger in State.Passengers)
                {
                    if (passenger.ShipId != ship.ShipId) continue;
                    Vector2 display = DisplayPoint(ship, passenger.LocalX, passenger.LocalY);
                    CompleteGroundCleanup(() => ReleasePassenger(passenger, ship));
                    passenger.Supported = false; passenger.ShipId = null; passenger.ConsolePieceId = null;
                    passenger.IsInSpace = false; passenger.SurfaceDescending = false; passenger.LandingResolved = true;
                    passenger.LandingWorldKey = world; passenger.LandingX = display.x; passenger.LandingY = display.y;
                    if (ItemMgr.Instance.Player_DIC.TryGetValue(passenger.ProfileId, out Player player) && player != null)
                    {
                        RestorePassengerSurface(player);
                        player.transform.position = display;
                        Mod_Mover mover = player.itemMods?.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover);
                        if (mover?.rb != null) { mover.rb.position = display; mover.rb.velocity = Vector2.zero; }
                    }
                }
                foreach (ShipPieceState piece in ship.Pieces)
                    CompleteGroundCleanup(() => RemovePieceView(piece.PieceId));
                if (shipColliderPresenters.Remove(ship.ShipId, out ShipColliderPresenter presenter)) CompleteGroundCleanup(presenter.Dispose);
                CompleteGroundCleanup(() => RemovePooledShipPresenter(ship.ShipId));
                if (surfaceShadows.Remove(ship.ShipId, out ShipSurfaceShadowPresenter shadow)) CompleteGroundCleanup(shadow.Dispose);
                CompleteGroundCleanup(() => RemoveEmptyShip(ship));
            }
            catch
            {
                if (!transferred)
                    for (int i = created.Count - 1; i >= 0; i--) ShipGroundBuildingStore.RollbackCreated(created[i]);
                throw;
            }
        }

        private static void CompleteGroundCleanup(Action cleanup)
        {
            try { cleanup(); }
            catch (Exception error) { Debug.LogException(error); }
        }

        private static void RestoreGroundPartState(ItemData data, ShipPieceState piece)
        {
            JToken state = ItemSpecialDataJsonStore.ReadRoot(data.ItemSpecialData)[GroundPartStateKey];
            if (state != null) piece.DisableHealth = Math.Clamp((double?)state["disableHealth"] ?? piece.DisableHealth, 0d, piece.MaxDisableHealth);
            if (piece.Kind == ShipPieceKind.Door && data.ModuleDataDic != null)
                foreach (ModuleData module in data.ModuleDataDic.Values)
                    if (module.ID == "Door" && module is Ex_ModData saved)
                        piece.DoorOpen = saved.GetData<Mod_Door.DoorStateData>()?.IsOpen == true;
        }

        private static void SaveGroundPartState(ItemData data, ShipPieceState piece)
        {
            JObject root = ItemSpecialDataJsonStore.ReadRoot(data.ItemSpecialData);
            root[GroundPartStateKey] = new JObject { ["disableHealth"] = piece.DisableHealth };
            data.ItemSpecialData = root.ToString(Newtonsoft.Json.Formatting.None);
            if (piece.Kind == ShipPieceKind.Door && data.ModuleDataDic != null)
                foreach (ModuleData module in data.ModuleDataDic.Values)
                    if (module.ID == "Door" && module is Ex_ModData saved)
                        saved.WriteData(new Mod_Door.DoorStateData { IsOpen = piece.DoorOpen });
        }
        #endregion
    }
}
