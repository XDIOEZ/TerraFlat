using System;
using System.Collections.Generic;
using System.Globalization;
using FlatWorld.Gameplay.Progress;
using FlatWorld.Networking;
using FlatWorld.Structures;
using FlatWorld.WorldModel;
using Newtonsoft.Json;
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
            if (actor == null) return false;
            string world = actor.Data?.CurrentSceneName ?? actor.gameObject.scene.name;
            Vector2 position = WorldLocalPresentation.ToLogical(actor.transform.position);
            if (GameNetwork.IsOnline && GameNetwork.HasStateAuthority &&
                SpaceSession.Current?.TryGetNetworkObserver(actor.ProfileName, out SpaceNetworkObserver observer) == true)
            {
                if (observer.InSpace) return false;
                world = observer.WorldKey; position = observer.Position.LocalMeters.ToVector2();
            }
            if (string.IsNullOrWhiteSpace(world) || SpaceMgr.IsSpaceScene(world)) return false;
            using var worldScope = GameplayWorldRegistry.TryGet(world, out _) ? GameplayWorldRegistry.UseWorld(world) : null;
            using (MachineWorld.UseScope(world))
            {
                MachineEntity candidate = MachineWorld.GetById(consoleGuid);
                if (candidate == null || candidate.ScopeKey != world ||
                    SpaceSession.ResolvePartConfiguration(candidate.Snapshot)?.Kind != ShipPieceKind.Console ||
                    WorldTopologyRuntime.Distance(position, candidate.Snapshot.transform.position) > SpaceSession.ConsoleInteractionDistance)
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

        // 地面点火统一由权威端装配，客户端只申请控制台操作并等待真实结果。
        public static bool RequestLaunch(Player actor, int consoleGuid, bool driveAfterLaunch, Action<bool, string> completed)
        {
            if (actor == null) { completed?.Invoke(false, "当前玩家尚未就绪。"); return false; }
            var intent = new SpaceNetworkIntent { Kind = SpaceNetworkIntentKind.LaunchGroundShip,
                PieceId = consoleGuid.ToString(CultureInfo.InvariantCulture), Action = driveAfterLaunch ? 1 : 0 };
            if (!GameNetwork.HasStateAuthority)
            {
                bool submitted = SpaceNetworkBridge.SendIntent(actor.ProfileName, intent, completed);
                if (!submitted) completed?.Invoke(false, "联机连接尚未就绪，无法申请点火。");
                return submitted;
            }
            SpaceSession session = SpaceSession.EnsureLoaded();
            session.EnsureNetworkPassenger(actor);
            bool accepted = session.ExecuteNetworkIntent(actor.ProfileName, intent, out string reason);
            completed?.Invoke(accepted, reason);
            return true;
        }

        // 整船只吸附一次，旋转后的矩形左下格由同一整数变换求出。
        public static int SnapQuarterTurns(double radians) =>
            (int)Math.Round(radians / (Math.PI * .5d), MidpointRounding.AwayFromZero) & 3;

        // 船体位置记录重心，落地格子必须从本地原点换算，避免载荷改变后整船错位。
        public static Vector2Int SnapOrigin(ShipState ship)
        {
            ShipGeometry.LocalToWorld(ship, 0d, 0d, out double x, out double y);
            return new Vector2Int((int)Math.Round(x, MidpointRounding.AwayFromZero),
                (int)Math.Round(y, MidpointRounding.AwayFromZero));
        }

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
            using var worldScope = GameplayWorldRegistry.UseWorld(world);
            var selected = new List<ShipGroundBuildingStore.Entry>();
            var pieces = new List<ShipPieceState>();
            var movedItems = new List<(ShipGroundBuildingStore.Entry Entry, ShipPieceState Piece)>();
            var removedTiles = new List<ShipGroundBuildingStore.Entry>();
            var originalData = new Dictionary<int, ItemData>();
            var originalPlacement = new Dictionary<int, MachineScopePlacement>();
            var machineIds = new List<int>();
            var existingShips = new HashSet<string>();
            foreach (ShipState existing in State.Ships) existingShips.Add(existing.ShipId);
            bool transferred = false;
            bool atmosphereTransferred = false;
            DroppedItemService.WorldTransfer movedDrops = null;
            InteriorEntityTransfer movedLooseItems = null;
            SaveDataMgr.WorldTerrainTransfer movedTerrain = null;
            try
            {
                selected = CollectGroundAssembly(world, console.Id);
                foreach (ShipGroundBuildingStore.Entry entry in selected)
                    if (entry.Data.IDName == SpaceElevatorTerminalItemId)
                        throw new InvalidOperationException("太空电梯终端固定在地面，不能随飞船起飞；请将飞船与电梯地板分开。");
                GetGroundAtmosphere(world);
                foreach (ShipGroundBuildingStore.Entry entry in selected)
                {
                    ItemData snapshot = entry.Capture();
                    originalData.Add(entry.Guid, FastCloner.FastCloner.DeepClone(snapshot));
                    originalPlacement.Add(entry.Guid, new MachineScopePlacement(entry.Cell,
                        new Vector3(entry.Cell.x + .5f, entry.Cell.y + .5f), entry.QuarterTurns));
                    Vector2 point = new(entry.Cell.x + .5f, entry.Cell.y + .5f);
                    ShipPieceState piece = AddPiece(snapshot, point, entry.QuarterTurns, out ShipState ownerShip,
                        registerMachine: false, surfaceWorldKey: world);
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
                TakeGroundAtmosphere(ship, world);
                atmosphereTransferred = true;
                MachineWorld.TransferScopeNodes(world, MachineWorld.GetShipScopeKey(ship.ShipId), machineIds,
                    node => destinations[node.Id]);
                transferred = true;
                ShipAtmosphereService.RebuildCompartments(ship, GetOutside(ship));
                if (!ToggleIgnition(console.Id.ToString(CultureInfo.InvariantCulture), out string ignitionReason))
                    throw new InvalidOperationException(ignitionReason);
                for (int i = 0; i < selected.Count; i++)
                    if (selected[i].Tile.HasValue && !selected[i].Tile.Value.ReplacedGroundCell.HasValue)
                    {
                        removedTiles.Add(selected[i]);
                        ShipGroundBuildingStore.RemoveItem(selected[i]);
                    }
                    else if (selected[i].Item != null)
                    {
                        TransferExistingItemToShipInterior(ship, pieces[i], selected[i].Item);
                        movedItems.Add((selected[i], pieces[i]));
                    }
                for (int i = 0; i < selected.Count; i++)
                    if (selected[i].Tile.HasValue && selected[i].Tile.Value.ReplacedGroundCell.HasValue)
                    {
                        removedTiles.Add(selected[i]);
                        ShipGroundBuildingStore.RemoveItem(selected[i]);
                    }
                Vector2 ToLocal(Vector2 point)
                {
                    point = WorldLocalPresentation.ProjectPosition(point, new Vector2((float)candidate.PositionX, (float)candidate.PositionY));
                    ShipGeometry.WorldToLocal(candidate, point.x, point.y, out double x, out double y);
                    return new Vector2((float)x, (float)y);
                }
                Vector2 ToGround(Vector2 point)
                {
                    ShipGeometry.LocalToWorld(candidate, point.x, point.y, out double x, out double y);
                    return new Vector2((float)x, (float)y);
                }
                movedDrops = DroppedItemService.TransferWorldItems(world, MachineWorld.GetShipScopeKey(ship.ShipId),
                    point => { Vector2 local = ToLocal(point); return ShipGeometry.HasFloor(candidate,
                        Mathf.FloorToInt(local.x / (float)candidate.CellSizeMeters), Mathf.FloorToInt(local.y / (float)candidate.CellSizeMeters)); },
                    ToLocal, ToGround, -(float)(candidate.AngleRadians * 180d / Math.PI));
                movedLooseItems = new InteriorEntityTransfer(world, MachineWorld.GetShipScopeKey(ship.ShipId),
                    point => { Vector2 local = ToLocal(point); return ShipGeometry.HasFloor(candidate,
                        Mathf.FloorToInt(local.x / (float)candidate.CellSizeMeters), Mathf.FloorToInt(local.y / (float)candidate.CellSizeMeters)); },
                    ToLocal, -(float)(candidate.AngleRadians * 180d / Math.PI));
                movedTerrain = SaveDataMgr.Instance.TransferWorldTerrain(world, MachineWorld.GetShipScopeKey(ship.ShipId),
                    point => { Vector2 local = ToLocal(point); return ShipGeometry.HasFloor(candidate,
                        Mathf.FloorToInt(local.x / (float)candidate.CellSizeMeters), Mathf.FloorToInt(local.y / (float)candidate.CellSizeMeters)); }, ToLocal);
                InitializeTemplateCabin(ship, console.Id);
                foreach (ShipPieceState piece in pieces)
                    if (MachineCatalog.Get(piece.ItemId) != null)
                        using (MachineWorld.UseScope(MachineWorld.GetShipScopeKey(candidate.ShipId)))
                            CaptureMachinePieceSnapshot(piece, MachineWorld.GetById(int.Parse(piece.PieceId, CultureInfo.InvariantCulture)));
                CommitPiece(ship, pieces[pieces.Count - 1]);
                movedDrops.Commit();
                movedLooseItems.Commit();
                movedTerrain.Commit();
                reason = "地面建筑已装配，飞船开始起飞。"; return true;
            }
            catch (Exception error)
            {
                if (ship != null)
                {
                    try { movedTerrain?.Dispose(); }
                    catch (Exception rollback)
                    {
                        reason = "起飞移交中断，已保留船体数据等待恢复：" + error.Message + "；" + rollback.Message;
                        Debug.LogException(rollback); return false;
                    }
                    try { movedLooseItems?.Dispose(); }
                    catch (Exception rollback)
                    {
                        reason = "起飞移交中断，已保留船体数据等待恢复：" + error.Message + "；" + rollback.Message;
                        Debug.LogException(rollback); return false;
                    }
                    try { movedDrops?.Dispose(); }
                    catch (Exception rollback)
                    {
                        reason = "起飞移交中断，已保留船体数据等待恢复：" + error.Message + "；" + rollback.Message;
                        Debug.LogException(rollback); return false;
                    }
                    for (int i = removedTiles.Count - 1; i >= 0; i--)
                    {
                        ShipGroundBuildingStore.Entry entry = removedTiles[i];
                        TileBuildingCell tile = entry.Tile.Value;
                        try
                        {
                            if (tile.RuntimeChunk?.Terrain == null) throw new InvalidOperationException("地面区块已失效。");
                            int present = tile.ReplacedGroundCell.HasValue
                                ? TerrainSupportLayer.GetTileId(tile.RuntimeChunk.Terrain, tile.LocalPosition.x, tile.LocalPosition.y)
                                : tile.RuntimeChunk.Terrain.GetCell(tile.LocalPosition.x, tile.LocalPosition.y).BlockingTileId;
                            if (present == tile.RuntimeTileId) continue;
                            ShipGroundBuildingStore.Place(originalData[entry.Guid], entry.Cell, entry.QuarterTurns, world);
                        }
                        catch (Exception rollback)
                        {
                            reason = "起飞移交中断，已保留船体数据等待恢复：" + error.Message + "；" + rollback.Message;
                            Debug.LogException(rollback); return false;
                        }
                    }
                    for (int i = movedItems.Count - 1; i >= 0; i--)
                    {
                        var moved = movedItems[i];
                        try { TransferShipInteriorItemToWorld(ship, moved.Piece, world, moved.Entry.Cell, moved.Entry.QuarterTurns); }
                        catch (Exception rollback)
                        {
                            reason = "起飞移交中断，已保留船体数据等待恢复：" + error.Message + "；" + rollback.Message;
                            Debug.LogException(rollback); return false;
                        }
                    }
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
                    if (atmosphereTransferred) ReturnGroundAtmosphere(ship, world);
                    for (int i = pieces.Count - 1; i >= 0; i--) RollbackPiece(ship, pieces[i]);
                    if (shipColliderPresenters.Remove(ship.ShipId, out ShipColliderPresenter presenter)) CompleteGroundCleanup(presenter.Dispose);
                }
                ship = null; reason = "无法起飞：" + error.Message; return false;
            }
        }

        private static List<ShipGroundBuildingStore.Entry> CollectGroundAssembly(string world, int consoleGuid)
        {
            using var scope = GameplayWorldRegistry.UseWorld(world);
            List<ShipGroundBuildingStore.Entry> available = ShipGroundBuildingStore.Collect(world);
            ShipGroundBuildingStore.Entry console = available.Find(entry => entry.Guid == consoleGuid);
            if (console == null) throw new InvalidOperationException("地面控制台已失效。");
            var floors = new Dictionary<Vector2Int, ShipGroundBuildingStore.Entry>();
            var capturedIds = new HashSet<int>();
            foreach (ShipGroundBuildingStore.Entry entry in available)
            {
                capturedIds.Add(entry.Guid);
                if (ResolvePartConfiguration(entry.Data)?.Kind == ShipPieceKind.Floor)
                    foreach (Vector2Int cell in GroundFootprint(entry)) floors[cell] = entry;
            }
            var cells = new HashSet<Vector2Int>();
            var floorIds = new HashSet<int>();
            var queue = new Queue<Vector2Int>();
            var selected = new List<ShipGroundBuildingStore.Entry>();
            queue.Enqueue(console.Cell);
            while (queue.Count > 0)
            {
                Vector2Int cell = WorldTopologyRuntime.NormalizeCell(queue.Dequeue());
                if (cells.Contains(cell)) continue;
                if (!floors.TryGetValue(cell, out ShipGroundBuildingStore.Entry floor) &&
                    !ShipGroundBuildingStore.TryGetGroundSupportTileBuilding(world, cell, capturedIds, out floor)) continue;
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
            selected.AddRange(ShipGroundBuildingStore.CollectGroundTileBuildings(world, cells, capturedIds));
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

        private static void InitializeTemplateCabin(ShipState ship, int consoleGuid, string scopeKey = null, HashSet<ShipCell> selected = null)
        {
            using var scope = MachineWorld.UseScope(scopeKey ?? MachineWorld.GetShipScopeKey(ship.ShipId));
            MachineEntity console = MachineWorld.GetById(consoleGuid);
            JObject root = ItemSpecialDataJsonStore.ReadRoot(console.Snapshot.ItemSpecialData);
            JObject marker = root[ShipFixedStructureBuilder.TemplateMarker] as JObject;
            if (marker?["initialCabinGas"] is not JObject payload) return;
            if (ship.Compartments.Count == 0) throw new InvalidOperationException("模板配置了舱气，但当前船体没有密闭舱。");
            FixedStructureGas gas = payload.ToObject<FixedStructureGas>();
            bool initialized = false;
            foreach (ShipCompartmentState room in ship.Compartments)
            {
                if (selected != null && !room.Cells.Exists(selected.Contains)) continue;
                room.Gas = FixedStructureResourceInitializer.CreateFluidState(gas, null, room.VolumeLiters,
                    ShipAtmosphereService.MinimumCabinGasSpaceLiters, double.MaxValue);
                initialized = true;
            }
            if (!initialized) return;
            marker.Remove("initialCabinGas");
            console.Snapshot.ItemSpecialData = root.ToString(Newtonsoft.Json.Formatting.None);
        }
        #endregion

        #region 落地后交还普通建筑与地表网络
        private string lastGroundRestoreError;
        private readonly List<ShipState> groundRestoreShips = new();
        private readonly List<ShipState> groundRestoreMembers = new();
        private readonly HashSet<string> groundRestoreHandled = new(StringComparer.Ordinal);

        private sealed class GroundRestorePlan
        {
            public ShipState Ship;
            public GroundShipPreparation Preparation;
            public Vector2Int Origin;
            public int Turns;
            public bool MachinesTransferred;
            public readonly Dictionary<int, MachineScopePlacement> Placements = new(), OriginalMachines = new();
            public readonly Dictionary<int, ItemData> Snapshots = new();
            public readonly Dictionary<int, ShipPieceState> Pieces = new();
            public readonly List<int> MachineIds = new();
            public readonly List<ShipGroundBuildingStore.Entry> Created = new();
            public readonly List<(ShipPieceState Piece, Item Item)> TransferredItems = new();
            public DroppedItemService.WorldTransfer TransferredDrops;
            public InteriorEntityTransfer TransferredLooseItems;
            public SaveDataMgr.WorldTerrainTransfer TransferredTerrain;
        }

        private void RestoreLandedBuildings()
        {
            if (!GameNetwork.HasStateAuthority) return;
            groundRestoreHandled.Clear(); groundRestoreShips.Clear(); groundRestoreShips.AddRange(State.Ships);
            foreach (ShipState ship in groundRestoreShips)
            {
                if (groundRestoreHandled.Contains(ship.ShipId)) continue;
                ShipFlightState flight = GetFlight(ship.ShipId);
                if (ship.IsWreck || ship.RetainsWorldWithoutStructure || flight == null || !flight.LandingResolved ||
                    !GameplayWorldRegistry.TryGet(flight.SurfaceWorldKey, out _) ||
                    flight.Phase is not (ShipFlightPhase.Landed or ShipFlightPhase.Floating)) continue;
                string world = flight.SurfaceWorldKey;
                using var worldScope = GameplayWorldRegistry.UseWorld(world);
                List<ShipState> members = groundRestoreMembers;
                members.Clear();
                ShipAssemblyState assembly = GetConnectedAssembly(ship.ShipId);
                if (assembly == null) members.Add(ship);
                else foreach (string id in assembly.MemberShipIds)
                    if (GetShip(id) is ShipState member && !member.IsWreck) members.Add(member);
                bool ready = true;
                foreach (ShipState member in members)
                {
                    groundRestoreHandled.Add(member.ShipId);
                    ShipFlightState memberFlight = GetFlight(member.ShipId);
                    if (memberFlight == null || !memberFlight.LandingResolved || memberFlight.SurfaceWorldKey != world ||
                        memberFlight.Phase is not (ShipFlightPhase.Landed or ShipFlightPhase.Floating)) { ready = false; continue; }
                    GroundShipPreparation preparation = EnsureGroundPreparation(member, world);
                    if (!preparation.Failed && !preparation.Ready) ready = false;
                }
                if (!ready) continue;
                try { RestoreGroundAssembly(members, world); lastGroundRestoreError = null; }
                catch (Exception error)
                {
                    if (lastGroundRestoreError == error.Message) continue;
                    lastGroundRestoreError = error.Message;
                    Debug.LogError("[飞船落地] 普通建筑移交未完成：" + error.Message, this);
                }
            }
        }

        private GroundRestorePlan PlanGroundShip(ShipState ship, string world, Dictionary<Vector3Int, ShipPieceState> occupied)
        {
            using var worldScope = MachineWorld.UseScope(world);
            if (Math.Abs(ship.CellSizeMeters - 1d) > .000001d) throw new InvalidOperationException("船格必须与普通建筑的一米格对齐。");
            int turns = ShipGroundAssembly.SnapQuarterTurns(ship.AngleRadians);
            Vector2Int origin = ShipGroundAssembly.SnapOrigin(ship);
            var plan = new GroundRestorePlan { Ship = ship, Origin = origin, Turns = turns,
                Preparation = EnsureGroundPreparation(ship, world) };
            plan.Preparation.ReadyAtCommit = plan.Preparation.Ready;
            foreach (ShipPieceState piece in ship.Pieces)
            {
                if (!piece.IsAlive) continue;
                int id = int.Parse(piece.PieceId, CultureInfo.InvariantCulture);
                int width = (piece.QuarterTurns & 1) == 0 ? piece.Width : piece.Height;
                int height = (piece.QuarterTurns & 1) == 0 ? piece.Height : piece.Width;
                Vector2Int cell = WorldTopologyRuntime.NormalizeCell(origin + ShipGroundAssembly.RotateAnchor(
                    piece.CellX, piece.CellY, width, height, turns));
                int q = (piece.QuarterTurns + turns) & 3;
                plan.Placements.Add(id, new MachineScopePlacement(cell, new Vector3(cell.x + .5f, cell.y + .5f), q));
                plan.Pieces.Add(id, piece);
                MachineDefinition definition = MachineCatalog.Get(piece.ItemId);
                int layer = Mod_ShipPart.ResolveGroundOccupancyLayer(piece.Kind, piece.Layer, definition?.Layer);
                int worldWidth = (turns & 1) == 0 ? width : height, worldHeight = (turns & 1) == 0 ? height : width;
                for (int y = 0; y < worldHeight; y++)
                    for (int x = 0; x < worldWidth; x++)
                    {
                        Vector2Int at = WorldTopologyRuntime.NormalizeCell(cell + new Vector2Int(x, y));
                        var key = new Vector3Int(at.x, at.y, layer);
                        if (occupied.TryGetValue(key, out ShipPieceState other))
                            throw new InvalidOperationException($"船体部件落地占地重叠：格({at.x},{at.y})，层{layer}，" +
                                $"{other.ItemId}[{other.PieceId}]与{piece.ItemId}[{piece.PieceId}]。");
                        occupied.Add(key, piece);
                        if (BuildingOccupancyRegistry.IsOccupiedNormalized(at, layer: layer))
                            throw new InvalidOperationException($"落点的地面建筑层已被占用：格({at.x},{at.y})，层{layer}，" +
                                $"待落地部件{piece.ItemId}[{piece.PieceId}]，请清理落点。");
                        if (ChunkMgr.ExistingInstance == null ||
                            !ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(new Vector2(at.x + .5f, at.y + .5f), out var terrain))
                            throw new InvalidOperationException($"落点地形尚未准备：格({at.x},{at.y})。");
                        if (terrain.Cell.BlockingTileId != 0)
                            throw new InvalidOperationException($"落点已有阻挡地块：格({at.x},{at.y})。");
                    }
            }
            return plan;
        }

        private void CaptureGroundShipContents(GroundRestorePlan plan)
        {
            foreach (var pair in plan.Pieces)
            {
                int id = pair.Key;
                ShipPieceState piece = pair.Value;
                MachineDefinition definition = MachineCatalog.Get(piece.ItemId);
                if (definition != null)
                {
                    using (MachineWorld.UseScope(MachineWorld.GetShipScopeKey(plan.Ship.ShipId)))
                    {
                        MachineEntity node = MachineWorld.GetById(id) ?? throw new InvalidOperationException("待落地机器身份缺失：" + piece.ItemId);
                        plan.OriginalMachines.Add(id, new MachineScopePlacement(node.Cell, node.Snapshot.transform.position, node.RotationQuarterTurns));
                    }
                    plan.MachineIds.Add(id);
                    continue;
                }
                ItemData latest = GetPieceData(piece);
                if (TryGetShipInteriorItem(piece.PieceId, out Item item))
                { item.Save(); latest = item.itemData; }
                ItemData snapshot = FastCloner.FastCloner.DeepClone(latest);
                if (snapshot == null) throw new InvalidOperationException("船部件快照缺失：" + piece.ItemId);
                CaptureRecoveredPieceHealth(snapshot, piece);
                SaveGroundPartState(snapshot, piece);
                plan.Snapshots.Add(id, snapshot);
            }
        }

        private void RestoreGroundAssembly(List<ShipState> members, string world)
        {
            using var sample = GroundCommitMarker.Auto();
            GroundShipPreparation preparation = EnsureGroundPreparation(members[0], world);
            if (!preparation.RestoreGate.ShouldAttempt(world, BuildingOccupancyRegistry.Revision, members)) return;
            var plans = new List<GroundRestorePlan>(members.Count);
            var occupied = new Dictionary<Vector3Int, ShipPieceState>();
            // 整组先检查最终落点，再创建正式建筑，不能只交还对接组合的一半。
            try
            {
                foreach (ShipState ship in members) plans.Add(PlanGroundShip(ship, world, occupied));
            }
            catch (InvalidOperationException)
            {
                preparation.RestoreGate.Block(world, BuildingOccupancyRegistry.Revision, members);
                throw;
            }
            // 整组占地通过后才复制最新数据，失败落点不再逐帧制造快照垃圾。
            using (GroundCaptureMarker.Auto())
                foreach (GroundRestorePlan plan in plans) CaptureGroundShipContents(plan);
            bool handedOff = false;
            try
            {
                using (MachineWorld.BeginSurfaceChangeBatch())
                {
                    var presentationLeases = new List<IDisposable>(plans.Count);
                    try
                    {
                        foreach (GroundRestorePlan plan in plans)
                            if (!plan.Preparation.Failed && plan.Preparation.MachinePresentation != null)
                                presentationLeases.Add(plan.Preparation.MachinePresentation.BeginCommit());
                        foreach (GroundRestorePlan plan in plans)
                            for (int phase = 0; phase < 2; phase++)
                            foreach (var pair in plan.Snapshots)
                            {
                                MachineScopePlacement target = plan.Placements[pair.Key];
                                ShipPieceState piece = plan.Pieces[pair.Key];
                                if ((piece.Kind == ShipPieceKind.Floor) != (phase == 0)) continue;
                                if (TryGetShipInteriorItem(piece.PieceId, out _))
                                {
                                    Item moved = TransferShipInteriorItemToWorld(plan.Ship, piece, world, target.Cell, target.QuarterTurns);
                                    plan.TransferredItems.Add((piece, moved));
                                    plan.Created.Add(new ShipGroundBuildingStore.Entry { Item = moved, Data = moved.itemData,
                                        Cell = target.Cell, QuarterTurns = target.QuarterTurns });
                                    continue;
                                }
                                if (!Mod_Building.TryReadBuildingData(pair.Value, out _, out Mod_Building.Building_Data tile) ||
                                    string.IsNullOrWhiteSpace(tile.TileBlockId))
                                    throw new InvalidOperationException("待落地建筑缺少船内真实实例：" + piece.ItemId);
                                plan.Created.Add(ShipGroundBuildingStore.PlacePrepared(pair.Value, target.Cell, target.QuarterTurns, world, null));
                            }
                        foreach (GroundRestorePlan plan in plans)
                        {
                            MachineWorld.TransferScopeNodes(MachineWorld.GetShipScopeKey(plan.Ship.ShipId), world, plan.MachineIds, node =>
                            {
                                ShipPieceState piece = plan.Pieces[node.Id];
                                node.State ??= MachineWorld.ReadMachineState(node.Snapshot);
                                node.State.Hp = (float)piece.Health;
                                return plan.Placements[node.Id];
                            });
                            plan.MachinesTransferred = true;
                            using (MachineWorld.UseScope(world))
                                foreach (int id in plan.MachineIds)
                                {
                                    MachineEntity node = MachineWorld.GetById(id);
                                    // 移交先捕获真实加工与流体，再补最新耐久，避免被旧外壳覆盖。
                                    CaptureRecoveredPieceHealth(node.Snapshot, plan.Pieces[id]);
                                    SaveGroundPartState(node.Snapshot, plan.Pieces[id]);
                                }
                        }
                        foreach (GroundRestorePlan plan in plans)
                        {
                            Vector2 ToGround(Vector2 local)
                            {
                                ShipGeometry.Rotate(local.x, local.y, plan.Turns * Math.PI * .5d, out double x, out double y);
                                return new Vector2(plan.Origin.x + (float)x, plan.Origin.y + (float)y);
                            }
                            Vector2 ToLocal(Vector2 point)
                            {
                                ShipGeometry.Rotate(point.x - plan.Origin.x, point.y - plan.Origin.y,
                                    -plan.Turns * Math.PI * .5d, out double x, out double y);
                                return new Vector2((float)x, (float)y);
                            }
                            plan.TransferredDrops = DroppedItemService.TransferWorldItems(
                                MachineWorld.GetShipScopeKey(plan.Ship.ShipId), world, _ => true, ToGround, ToLocal,
                                plan.Turns * 90f);
                            plan.TransferredLooseItems = new InteriorEntityTransfer(
                                MachineWorld.GetShipScopeKey(plan.Ship.ShipId), world, _ => true, ToGround, plan.Turns * 90f);
                            plan.TransferredTerrain = SaveDataMgr.Instance.TransferWorldTerrain(
                                MachineWorld.GetShipScopeKey(plan.Ship.ShipId), world, _ => true, ToGround);
                        }
                        foreach (GroundRestorePlan plan in plans)
                            if (HasUntransferredGroundWorldContents(plan.Ship))
                                throw new InvalidOperationException("船内仍有未能安全移交的居民或作物，不能完成落地。");
                        TransferGroundPassengersBeforeRetiringShipWorlds(plans);
                        handedOff = true;
                        foreach (GroundRestorePlan plan in plans)
                        { plan.TransferredDrops.Commit(); plan.TransferredLooseItems.Commit(); plan.TransferredTerrain.Commit(); }
                        try
                        {
                            foreach (GroundRestorePlan plan in plans) DetachGroundShipOwnership(plan, world);
                        }
                        finally
                        {
                            foreach (GroundRestorePlan plan in plans)
                                CompleteGroundCleanup(() => FinishGroundShipPresentation(plan.Ship));
                            foreach (GroundRestorePlan plan in plans)
                                foreach (ShipGroundBuildingStore.Entry entry in plan.Created)
                                    CompleteGroundCleanup(() => ShipGroundBuildingStore.RevealPrepared(entry));
                            var restoredBuildings = new List<ShipGroundBuildingStore.Entry>();
                            foreach (GroundRestorePlan plan in plans) restoredBuildings.AddRange(plan.Created);
                            CompleteGroundCleanup(() => ShipGroundBuildingStore.PublishCreated(restoredBuildings));
                        }
                    }
                    finally
                    {
                        for (int i = presentationLeases.Count - 1; i >= 0; i--) presentationLeases[i].Dispose();
                    }
                }
                foreach (GroundRestorePlan plan in plans)
                {
                    plan.Preparation.Dispose(); RememberGroundPreparation(plan.Preparation, "committed");
                    groundPreparations.Remove(plan.Ship.ShipId);
                }
            }
            catch (Exception error)
            {
                if (!handedOff)
                {
                    var failures = new List<Exception> { error };
                    using (MachineWorld.BeginSurfaceChangeBatch())
                        for (int i = plans.Count - 1; i >= 0; i--)
                        {
                            GroundRestorePlan plan = plans[i];
                            try { plan.TransferredTerrain?.Dispose(); }
                            catch (Exception rollback) { failures.Add(rollback); }
                            try { plan.TransferredLooseItems?.Dispose(); }
                            catch (Exception rollback) { failures.Add(rollback); }
                            try { plan.TransferredDrops?.Dispose(); }
                            catch (Exception rollback) { failures.Add(rollback); }
                            if (plan.MachinesTransferred)
                                try { MachineWorld.TransferScopeNodes(world, MachineWorld.GetShipScopeKey(plan.Ship.ShipId),
                                    plan.MachineIds, node => plan.OriginalMachines[node.Id]); }
                                catch (Exception rollback) { failures.Add(rollback); }
                            for (int item = plan.TransferredItems.Count - 1; item >= 0; item--)
                                try { TransferExistingItemToShipInterior(plan.Ship, plan.TransferredItems[item].Piece,
                                    plan.TransferredItems[item].Item); }
                                catch (Exception rollback) { failures.Add(rollback); }
                            for (int item = plan.Created.Count - 1; item >= 0; item--)
                            {
                                if (plan.TransferredItems.Exists(value => ReferenceEquals(value.Item, plan.Created[item].Item))) continue;
                                try { ShipGroundBuildingStore.RollbackCreated(plan.Created[item]); }
                                catch (Exception rollback) { failures.Add(rollback); }
                            }
                            FailGroundPreparation(plan.Preparation, error);
                        }
                    if (failures.Count > 1) throw new AggregateException("飞船落地回滚未完全恢复，已保留原船记录。", failures);
                }
                else foreach (GroundRestorePlan plan in plans)
                {
                    plan.Preparation.Dispose(); RememberGroundPreparation(plan.Preparation, "committed");
                    groundPreparations.Remove(plan.Ship.ShipId);
                }
                throw;
            }
        }

        private bool HasUntransferredGroundWorldContents(ShipState ship)
        {
            string worldKey = MachineWorld.GetShipScopeKey(ship.ShipId);
            var items = new List<Item>();
            GameplayWorldRegistry.CopyWorldItems(worldKey, items);
            foreach (Item item in items)
            {
                if (item == null || item is Player || item.GetComponentInParent<Player>(true) != null) continue;
                Item ownerItem = item.Owner;
                int depth = 0;
                while (ownerItem != null && ownerItem is not Player && depth++ < 64) ownerItem = ownerItem.Owner;
                if (ownerItem is not Player) return true;
            }
            if (!TryGetShipInteriorWorld(ship.ShipId, out var world)) return false;
            using var scope = GameplayWorldRegistry.UseWorld(worldKey);
            foreach (var address in world.ChunkAddresses)
                foreach (AgricultureCellSaveData cell in SaveDataMgr.Instance.GetAgricultureCells(address))
                    if (cell?.Crop != null) return true;
            return false;
        }

        private void TransferGroundPassengersBeforeRetiringShipWorlds(IReadOnlyList<GroundRestorePlan> plans)
        {
            var saved = new List<(string Profile, string Snapshot)>();
            try
            {
                foreach (GroundRestorePlan plan in plans)
                {
                    ShipState ship = plan.Ship;
                    double oldX = ship.PositionX, oldY = ship.PositionY, oldAngle = ship.AngleRadians;
                    ship.AngleRadians = plan.Turns * Math.PI * .5d;
                    ShipGeometry.Rotate(ship.CenterOfMassLocalX, ship.CenterOfMassLocalY, ship.AngleRadians,
                        out double centerX, out double centerY);
                    ship.PositionX = plan.Origin.x + centerX;
                    ship.PositionY = plan.Origin.y + centerY;
                    try
                    {
                        foreach (SpacePassengerState passenger in State.Passengers)
                        {
                            if (passenger.ShipId != ship.ShipId) continue;
                            Player player = FindNetworkPlayer(passenger.ProfileId);
                            Mod_Mover mover = player?.itemMods?.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover);
                            if (player != null && GameplayWorldRegistry.GetItemWorldKey(player) == MachineWorld.GetShipScopeKey(ship.ShipId) && mover?.rb != null)
                            { passenger.LocalX = mover.rb.position.x; passenger.LocalY = mover.rb.position.y; }
                            saved.Add((passenger.ProfileId, JsonConvert.SerializeObject(passenger)));
                            if (!TryReleasePassenger(passenger, ship))
                                throw new InvalidOperationException("落地乘员未能安全移交目标世界：" + passenger.ProfileId);
                            if (player != null && GameplayWorldRegistry.GetItemWorldKey(player) == MachineWorld.GetShipScopeKey(ship.ShipId))
                                throw new InvalidOperationException("落地乘员仍属于船内世界：" + passenger.ProfileId);
                            passenger.SurfaceDescending = false;
                            passenger.LandingResolved = true;
                            passenger.LandingWorldKey = plan.Preparation.World;
                            passenger.LandingX = passenger.FreeMotion.PositionMeters.X;
                            passenger.LandingY = passenger.FreeMotion.PositionMeters.Y;
                        }
                    }
                    finally { ship.PositionX = oldX; ship.PositionY = oldY; ship.AngleRadians = oldAngle; }
                }
            }
            catch (Exception failure)
            {
                try
                {
                    foreach (var entry in saved)
                    {
                        int index = State.Passengers.FindIndex(value => value.ProfileId == entry.Profile);
                        if (index >= 0) State.Passengers[index] = JsonConvert.DeserializeObject<SpacePassengerState>(entry.Snapshot);
                    }
                    RebindPassengersToShipInteriorWorlds();
                }
                catch (Exception rollback) { throw new AggregateException("落地乘员移交及回滚失败。", failure, rollback); }
                throw;
            }
        }

        private void DetachGroundShipOwnership(GroundRestorePlan plan, string world)
        {
            ShipState ship = plan.Ship;
            ship.AngleRadians = plan.Turns * Math.PI * .5d;
            ShipGeometry.Rotate(ship.CenterOfMassLocalX, ship.CenterOfMassLocalY, ship.AngleRadians, out double centerX, out double centerY);
            ship.PositionX = plan.Origin.x + centerX; ship.PositionY = plan.Origin.y + centerY;
            ReturnGroundAtmosphere(ship, world);
            ShipFlightState completedFlight = GetFlight(ship.ShipId);
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
            CompleteGroundCleanup(() => RecordShipLandingCompletion(ship, completedFlight, world));
        }

        private void FinishGroundShipPresentation(ShipState ship)
        {
            foreach (ShipPieceState piece in ship.Pieces)
                CompleteGroundCleanup(() => RemovePieceView(piece.PieceId));
            if (shipColliderPresenters.Remove(ship.ShipId, out ShipColliderPresenter presenter)) CompleteGroundCleanup(presenter.Dispose);
            CompleteGroundCleanup(() => RemovePooledShipPresenter(ship.ShipId));
            if (surfaceShadows.Remove(ship.ShipId, out ShipSurfaceShadowPresenter shadow)) CompleteGroundCleanup(shadow.Dispose);
            CompleteGroundCleanup(() => RemoveEmptyShip(ship));
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
