using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FlatWorld.Gameplay.Building;
using UnityEngine;
using UnityEngine.SceneManagement;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlatWorld.Spaceflight
{
    public sealed class ShipBuildingPlacementBackend : IBuildingPlacementBackend
    {
        #region 本地格放置事务
        public bool CanHandle(BuildingPlacementRequest request)
        {
            SpaceSession session = SpaceSession.Current;
            ShipPartConfiguration config = Mod_ShipPart.Read(request.SourceData);
            return config?.Kind is ShipPieceKind.Floor or ShipPieceKind.Engine ||
                session != null && session.TryGetSupport(request.Position, out _, out _, out _);
        }
        public BuildingPlacementResult Place(BuildingPlacementRequest request)
        {
            SpaceSession session = SpaceSession.EnsureLoaded();
            ItemData data;
            if (Mod_Building.TryReadBuildingData(request.SourceData, out _, out Mod_Building.Building_Data state) &&
                !string.IsNullOrWhiteSpace(state.TileBlockId))
            {
                // 船上的普通格子墙保持真实召唤器定义，不要求不存在的动态建筑本体。
                data = string.IsNullOrWhiteSpace(state.SnapshotBase64)
                    ? GameRes.ExistingInstance.CreateItemData(request.SourceData.IDName) : SpaceSession.Decode(state.SnapshotBase64);
                data.Stack.Amount = 1f;
                data.transform.position = request.Position;
                request.Extension?.PreparePlacedData(data);
                request.CandidatePrepared?.Invoke(data);
            }
            else data = BuildingPlacementCandidate.Create(request, out _);
            Mod_Building.SetInstalledDataState(data);
            ShipPieceState piece = session.AddPiece(data, request.Position, request.RotationQuarterTurns ?? 0, out ShipState ship);
            return new BuildingPlacementResult(data.IDName,
                () => session.RollbackPiece(ship, piece),
                () => session.CommitPiece(ship, piece), placedData: data);
        }
        #endregion
    }

    public sealed partial class SpaceSession
    {
        #region 船体注册与结构变更
        public bool TryGetSupport(Vector2 displayPosition, out ShipState ship, out ShipPieceState floor, out Vector2 local)
        {
            SpaceVector2 position = IsSpaceView ? UnprojectPosition(displayPosition) : new SpaceVector2(displayPosition.x, displayPosition.y);
            if (State != null) foreach (ShipState value in State.Ships)
            {
                if (value.IsWreck || !IsVisible(value)) continue;
                Vector2 ground = IsSpaceView ? default : WorldLocalPresentation.ProjectPosition(displayPosition, new Vector2((float)value.PositionX, (float)value.PositionY));
                ShipGeometry.WorldToLocal(value, IsSpaceView ? position.X : ground.x, IsSpaceView ? position.Y : ground.y, out double x, out double y);
                int cellX = (int)Math.Floor(x / value.CellSizeMeters), cellY = (int)Math.Floor(y / value.CellSizeMeters);
                ShipPieceState found = value.Pieces.Find(piece => piece.IsAlive && piece.Kind == ShipPieceKind.Floor &&
                    ContainsCell(piece, cellX, cellY));
                if (found == null) continue;
                ship = value; floor = found; local = new Vector2((float)x, (float)y); return true;
            }
            ship = null; floor = null; local = default; return false;
        }

        public ShipPieceState AddPiece(ItemData data, Vector2 displayPosition, int quarterTurns, out ShipState ship)
        {
            ShipPartConfiguration config = Mod_ShipPart.Read(data);
            config ??= InferPart(data);
            Mod_Building.TryReadBuildingData(data, out _, out Mod_Building.Building_Data building);
            MachineDefinition machine = MachineCatalog.Get(data.IDName);
            ResolvePlacedPieceHealth(data, config, machine, building, out double health, out double maximumHealth, out bool rebuilt);
            int width = Math.Max(1, Math.Max(config.FootprintWidth, building?.FootprintWidth ?? 1));
            int height = Math.Max(1, Math.Max(config.FootprintHeight, building?.FootprintHeight ?? 1));
            TryGetSupport(displayPosition, out ship, out _, out Vector2 local);
            bool created = false;
            if (ship == null && config.Kind == ShipPieceKind.Floor)
            {
                ship = FindAdjacentShip(displayPosition, width, height, quarterTurns, out local);
                if (ship == null) { ship = CreateShip(displayPosition, out local); created = true; }
            }
            if (ship == null) throw new InvalidOperationException("设备需要完整的飞船地板支撑。");
            int cellX = Mathf.FloorToInt(local.x / (float)ship.CellSizeMeters), cellY = Mathf.FloorToInt(local.y / (float)ship.CellSizeMeters);
            bool blocksMovement = config.BlocksMovement ?? (config.Kind is ShipPieceKind.Wall or ShipPieceKind.Door or ShipPieceKind.DockingPort ||
                machine?.BlocksMovement == true);
            var piece = new ShipPieceState
            {
                PieceId = data.Guid.ToString(System.Globalization.CultureInfo.InvariantCulture), ItemId = data.IDName,
                Kind = config.Kind, Layer = config.Layer, CellX = cellX, CellY = cellY,
                Width = width, Height = height,
                QuarterTurns = quarterTurns & 3, MassKg = Math.Max(.001d, data.Stack?.CurrentWeight ?? config.StructuralMassKg),
                Health = health, MaxHealth = maximumHealth, SealsAtmosphere = config.SealsAtmosphere,
                BlocksMovement = blocksMovement,
                MaterialId = config.MaterialId,
                MinimumPressureKPa = config.MinimumPressureKPa, MaximumPressureKPa = config.MaximumPressureKPa,
                PressureDamagePerKPaSecond = config.PressureDamagePerKPaSecond,
                MinimumTemperatureCelsius = config.MinimumTemperatureCelsius, MaximumTemperatureCelsius = config.MaximumTemperatureCelsius,
                TemperatureDamagePerCelsiusSecond = config.TemperatureDamagePerCelsiusSecond
            };
            try
            {
                if (!ShipEnginePlacement.TryValidateAddition(ship, piece, out string engineReason))
                    throw new InvalidOperationException(engineReason);
                var footprint = new HashSet<ShipCell>(ShipGeometry.Footprint(piece));
                foreach (ShipCell cell in footprint)
                {
                    if (piece.Kind != ShipPieceKind.Floor && !ShipAtmosphereService.HasFloor(ship, cell.X, cell.Y))
                        throw new InvalidOperationException("设备全部占地都需要飞船地板支撑。");
                    foreach (ShipPieceState existing in ship.Pieces)
                        if (existing.IsAlive && SameOccupancyLayer(existing, piece) && ContainsCell(existing, cell.X, cell.Y))
                            throw new InvalidOperationException("船上这一层格子已经被占用。");
                }
                if (contents.ContainsKey(piece.PieceId)) throw new InvalidOperationException("部件真实身份已登记到另一艘飞船。");
                data.transform.position = new Vector3((float)((cellX + .5d) * ship.CellSizeMeters),
                    (float)((cellY + .5d) * ship.CellSizeMeters), 0f);
                if (machine != null)
                {
                    var state = MachineWorld.ReadMachineState(data); state.RotationQuarterTurns = quarterTurns & 3;
                    state.Hp = (float)piece.Health;
                    MachineWorld.WriteMachineState(data, state);
                    MachineEntity placed = MachineWorld.PlaceOnShip(ship.ShipId, data);
                    if (placed.State != null) placed.State.Hp = (float)piece.Health;
                    if (rebuilt && placed.Definition.Fluid != null)
                    {
                        using (MachineWorld.UseScope("ship:" + ship.ShipId))
                        {
                            FluidMachineState fluid = MachineWorld.GetFluidState(placed);
                            fluid.Ruptured = false; fluid.RuptureBudgetId = ""; fluid.OverpressureVictimId = 0;
                            MachineWorld.CaptureFluidState(placed);
                        }
                    }
                    data = placed.Snapshot;
                }
                piece.SnapshotJson = Encode(data);
                contents.Add(piece.PieceId, data);
                ship.Pieces.Add(piece);
                RegisterPieceIndex(ship, piece);
                State.Devices.Add(new ShipDeviceState { PieceId = piece.PieceId, Configuration = config, TargetPressureKPa = config.TargetPressureKPa });
                ShipStructureService.RecalculateMass(ship);
                return piece;
            }
            catch
            {
                RollbackPiece(ship, piece);
                if (created && ship.Pieces.Count == 0) RemoveEmptyShip(ship);
                throw;
            }
        }

        private static void ResolvePlacedPieceHealth(ItemData data, ShipPartConfiguration config, MachineDefinition machine,
            Mod_Building.Building_Data building, out double health, out double maximum, out bool rebuilt)
        {
            Ex_ModData storage = null;
            Mod_DamageReceiver.DamageReceiver_SaveData saved = null;
            if (data.ModuleDataDic != null)
                foreach (ModuleData module in data.ModuleDataDic.Values)
                    if (module.ID == ModText.Hp && module is Ex_ModData candidate)
                    { storage = candidate; saved = candidate.GetData<Mod_DamageReceiver.DamageReceiver_SaveData>(); break; }
            maximum = config.Health;
            if (machine != null) maximum = Math.Min(maximum, MachineWorld.ResolveMaximumHp(data));
            else if (saved?.MaxHp > 0f && string.IsNullOrWhiteSpace(building?.TileBlockId)) maximum = Math.Min(maximum, saved.MaxHp);
            double previous = machine != null ? MachineWorld.ReadMachineState(data).Hp : saved?.Hp ?? -1d;
            rebuilt = previous <= 0d;
            // 格子墙的召唤器血量只提供受损比例，真实墙壁上限始终取地块材质配置。
            if (machine == null && !string.IsNullOrWhiteSpace(building?.TileBlockId) && saved?.MaxHp > 0f && previous > 0d)
                previous = maximum * Math.Clamp(previous / saved.MaxHp, 0d, 1d);
            health = previous > 0d ? Math.Min(previous, maximum) : maximum;
            if (saved != null)
            { saved.Hp = (float)health; saved.MaxHp = (float)maximum; storage.WriteData(saved); }
        }

        private ShipState FindAdjacentShip(Vector2 displayPosition, int width, int height, int quarterTurns, out Vector2 local)
        {
            SpaceVector2 absolute = IsSpaceView ? UnprojectPosition(displayPosition) : new SpaceVector2(displayPosition.x, displayPosition.y);
            foreach (ShipState candidate in State.Ships)
            {
                if (!IsVisible(candidate)) continue;
                Vector2 ground = IsSpaceView ? default : WorldLocalPresentation.ProjectPosition(displayPosition, new Vector2((float)candidate.PositionX, (float)candidate.PositionY));
                ShipGeometry.WorldToLocal(candidate, IsSpaceView ? absolute.X : ground.x, IsSpaceView ? absolute.Y : ground.y, out double x, out double y);
                var proposal = new ShipPieceState { CellX = (int)Math.Floor(x / candidate.CellSizeMeters),
                    CellY = (int)Math.Floor(y / candidate.CellSizeMeters), Width = width,
                    Height = height, QuarterTurns = quarterTurns };
                foreach (ShipCell cell in ShipGeometry.Footprint(proposal))
                    foreach (ShipCell adjacent in ShipGeometry.Neighbors(cell))
                        if (ShipAtmosphereService.HasFloor(candidate, adjacent.X, adjacent.Y))
                        { local = new Vector2((float)x, (float)y); return candidate; }
            }
            local = default; return null;
        }
        private static bool ContainsCell(ShipPieceState piece, int x, int y)
        {
            foreach (ShipCell cell in ShipGeometry.Footprint(piece)) if (cell.X == x && cell.Y == y) return true;
            return false;
        }
        private static bool SameOccupancyLayer(ShipPieceState left, ShipPieceState right) =>
            left.Kind == ShipPieceKind.Floor || right.Kind == ShipPieceKind.Floor
                ? left.Kind == right.Kind : left.Layer == right.Layer;
        private ShipState CreateShip(Vector2 position, out Vector2 local)
        {
            string world = SceneManager.GetActiveScene().name;
            var ship = new ShipState { PositionX = Math.Floor(position.x), PositionY = Math.Floor(position.y), WorldAddress = world, PlatformKind = ShipPlatformKind.Land };
            if (IsSpaceView)
            {
                SpaceVector2 absolute = UnprojectPosition(position);
                ship.PositionX = Math.Floor(absolute.X); ship.PositionY = Math.Floor(absolute.Y); ship.PlatformKind = ShipPlatformKind.Space;
            }
            State.Ships.Add(ship);
            string bodyId = FindSurfaceBody(world)?.BodyId ?? "earth";
            State.Flights.Add(new ShipFlightState
            {
                ShipId = ship.ShipId, BodyId = bodyId, SurfaceWorldKey = world,
                SurfaceX = position.x, SurfaceY = position.y,
                Phase = IsSpaceView ? ShipFlightPhase.Cruising : ShipFlightPhase.Landed,
                Motion = new SpaceMotionState { PositionMeters = new SpaceVector2(ship.PositionX, ship.PositionY) },
                LandingRandomState = unchecked((ulong)(uint)owner.Seed * 6364136223846793005UL + (uint)State.Ships.Count) | 1UL
            });
            local = new Vector2(.5f, .5f); return ship;
        }
        private static ShipPartConfiguration InferPart(ItemData data)
        {
            var machine = MachineCatalog.Get(data.IDName);
            GameRes.ExistingInstance.TryGetItemDefinition(data.IDName, out RuntimeItemDefinition definition);
            Mod_Building.TryReadBuildingData(data, out _, out Mod_Building.Building_Data building);
            RuntimeTileDefinition tile = !string.IsNullOrWhiteSpace(building?.TileBlockId)
                ? GameRes.ExistingInstance.GetTileBlock(building.TileBlockId) : null;
            bool door = definition?.HasModule("门模块") == true;
            bool wall = tile != null && BlockingTilemapLayer.IsBlockingTile(tile.TileDataTemplate);
            int layer = machine?.Layer ?? ReadBuildingPlacementLayer(definition);
            double health = tile?.DamageProfile?.MaxHealth ?? definition?.Health?.MaxHp ?? 250f;
            return new ShipPartConfiguration
            {
                // 线路按真实能力归类，避免管道和电线被当成阻挡喷口的设备。
                Kind = door ? ShipPieceKind.Door : wall ? ShipPieceKind.Wall :
                    machine?.Electrical?.IsWire == true ? ShipPieceKind.Wire :
                    machine?.Fluid?.Kind == "pipe" ? ShipPieceKind.Pipe : ShipPieceKind.Equipment,
                Layer = layer,
                SealsAtmosphere = door || wall, StructuralMassKg = data.Stack?.CurrentWeight ?? 20f,
                BlocksMovement = door || wall || (machine?.BlocksMovement ?? (layer == (int)BuildingPlacementLayer.Structure && tile == null)),
                FootprintWidth = Math.Max(1, building?.FootprintWidth ?? 1), FootprintHeight = Math.Max(1, building?.FootprintHeight ?? 1),
                Health = (float)Math.Max(1d, health * data.CraftedDurabilityMultiplier), PowerWatts = 0f,
                RecoveryItemId = !string.IsNullOrWhiteSpace(building?.TileBlockId) ? data.IDName : Mod_Building.GetSummonerPrefabId(data.IDName)
            };
        }
        private static int ReadBuildingPlacementLayer(RuntimeItemDefinition definition)
        {
            if (definition == null) return (int)BuildingPlacementLayer.Structure;
            foreach (RuntimeItemModuleDefinition module in definition.ModuleDefinitions)
            {
                if (!module.Enabled || module.ModuleId != ModText.Building) continue;
                Mod_Building authoring = GameRes.ExistingInstance.GetPrefab(module.PrefabId, false)?.GetComponentInChildren<Mod_Building>(true);
                int result = (int)(authoring?.PlacementLayer ?? BuildingPlacementLayer.Structure);
                if (string.IsNullOrWhiteSpace(module.ParametersJson)) return result;
                JToken value = JObject.Parse(module.ParametersJson)["PlacementLayer"];
                if (value == null) return result;
                if (value.Type == JTokenType.Integer) return (int)value;
                return Enum.TryParse((string)value, out BuildingPlacementLayer parsed) ? (int)parsed : result;
            }
            return (int)BuildingPlacementLayer.Structure;
        }
        public bool RollbackPiece(ShipState ship, ShipPieceState piece)
        {
            if (int.TryParse(piece.PieceId, out int id) && MachineCatalog.Get(piece.ItemId) != null)
                using (MachineWorld.UseScope("ship:" + ship.ShipId)) MachineWorld.DiscardPlacementCandidate(id);
            ship.Pieces.Remove(piece); contents.Remove(piece.PieceId);
            pieceIndex.Remove(piece.PieceId);
            State.Devices.RemoveAll(value => value.PieceId == piece.PieceId);
            RemovePieceView(piece.PieceId);
            viewBindingsDirty = true;
            if (ship.Pieces.Count == 0) RemoveEmptyShip(ship);
            else ShipStructureService.RecalculateMass(ship);
            return true;
        }
        public void CommitPiece(ShipState ship, ShipPieceState piece)
        {
            ShipAtmosphereService.RebuildCompartments(ship, GetOutside(ship));
            SynchronizeViews(refreshBindings: true);
        }
        public void DestroyPiece(ShipState ship, ShipPieceState piece, string reason)
        {
            if (destroyed.Contains(piece.PieceId)) return;
            destroyed.Add(piece.PieceId);
            HandleDestroyedInventory(ship, piece, reason);
            RemovePieceView(piece.PieceId);
        }
        private void ProcessStructureChanges()
        {
            if (destroyed.Count == 0) return;
            // 分裂会把旧载荷分给新船，丢弃样本池以免复用这些已移交的对象。
            massContributionPools = new();
            viewBindingsDirty = true;
            pieceIndexDirty = true;
            foreach (ShipState ship in State.Ships.ToArray())
            {
                if (!ship.Pieces.Exists(piece => destroyed.Contains(piece.PieceId))) continue;
                // 先保留原船的实际姿态，失去支撑的人从原来所在点继承速度。
                var oldPose = new ShipState { ShipId = ship.ShipId, PositionX = ship.PositionX, PositionY = ship.PositionY,
                    VelocityX = ship.VelocityX, VelocityY = ship.VelocityY, AngleRadians = ship.AngleRadians,
                    AngularVelocityRadiansPerSecond = ship.AngularVelocityRadiansPerSecond,
                    CenterOfMassLocalX = ship.CenterOfMassLocalX, CenterOfMassLocalY = ship.CenterOfMassLocalY };
                ShipFlightState previous = GetFlight(ship.ShipId);
                List<ShipState> fragments = ShipStructureService.RebuildAndSplit(ship, GetOutside(ship), RecoverUnsupportedPiece);
                foreach (ShipState fragment in fragments)
                {
                    ShipFlightState flight = previous;
                    if (fragment.ShipId != ship.ShipId)
                    {
                        State.Ships.Add(fragment);
                        flight = JsonConvert.DeserializeObject<ShipFlightState>(JsonConvert.SerializeObject(previous));
                        flight.ShipId = fragment.ShipId;
                        State.Flights.Add(flight);
                        foreach (ShipPieceState member in fragment.Pieces)
                            if (int.TryParse(member.PieceId, out int id)) MachineWorld.TransferShipNode(ship.ShipId, fragment.ShipId, id);
                    }
                    flight.Motion.PositionMeters = new SpaceVector2(fragment.PositionX, fragment.PositionY);
                    flight.Motion.VelocityMetersPerSecond = new SpaceVector2(fragment.VelocityX, fragment.VelocityY);
                    if (flight.Phase is ShipFlightPhase.Landed or ShipFlightPhase.Floating or ShipFlightPhase.Lifting or ShipFlightPhase.SurfaceDescending)
                    { flight.SurfaceX = fragment.PositionX; flight.SurfaceY = fragment.PositionY; }
                    if (fragments.Count > 1 || fragment.IsWreck) PrepareWreck(fragment);
                }
                foreach (SpacePassengerState passenger in State.Passengers)
                {
                    if (passenger.ShipId != ship.ShipId) continue;
                    ShipState support = fragments.Find(fragment => !fragment.IsWreck && ShipAtmosphereService.HasFloor(fragment,
                        (int)Math.Floor(passenger.LocalX / fragment.CellSizeMeters), (int)Math.Floor(passenger.LocalY / fragment.CellSizeMeters)));
                    if (!string.IsNullOrWhiteSpace(passenger.ConsolePieceId) &&
                        (support == null || !support.Pieces.Exists(piece => piece.PieceId == passenger.ConsolePieceId && piece.IsAlive)))
                    {
                        if (ItemMgr.Instance != null && ItemMgr.Instance.Player_DIC.TryGetValue(passenger.ProfileId, out Player player)) ExitConsole(player);
                        else { ClearControl(passenger.ConsolePieceId); passenger.ConsolePieceId = null; }
                    }
                    if (support == null) ReleasePassenger(passenger, oldPose);
                    else passenger.ShipId = support.ShipId;
                }
                foreach (ShipNavigationState navigation in State.Navigation)
                {
                    if (navigation.ShipId != ship.ShipId) continue;
                    ShipState target = fragments.Find(fragment => fragment.Pieces.Exists(piece => piece.PieceId == navigation.NavigationPieceId));
                    if (target != null) navigation.ShipId = target.ShipId;
                    if (target == null || !target.Pieces.Exists(piece => piece.PieceId == navigation.ConsolePieceId))
                    { navigation.Active = false; navigation.NeedsManualRestart = true; navigation.Status = "船体分裂，导航需要重新启动"; }
                }
                FlushDetachedWrecks();
                if (fragments.Count == 0) RemoveEmptyShip(ship);
            }
            State.Docking.RemoveAll(pair => !FindPiece(pair.PortAId, out _, out _) || !FindPiece(pair.PortBId, out _, out _));
            State.Navigation.RemoveAll(value => !FindPiece(value.NavigationPieceId, out _, out _));
            destroyed.Clear();
            RebuildPieceIndex();
        }

        private void RemoveEmptyShip(ShipState ship)
        {
            RemoveWreckView(ship.ShipId);
            MachineWorld.ReleaseShipScope(ship.ShipId);
            State.Ships.Remove(ship);
            State.Flights.RemoveAll(value => value.ShipId == ship.ShipId);
            State.Navigation.RemoveAll(value => value.ShipId == ship.ShipId);
            State.Docking.RemoveAll(value => value.ShipAId == ship.ShipId || value.ShipBId == ship.ShipId);
        }

        #region 飞船质量计算工作区
        private readonly HashSet<string> assignedMassPassengers = new(StringComparer.Ordinal);
        private readonly Dictionary<int, ShipPieceState> massPiecesById = new();
        private readonly List<MachineWorld.FluidTankGroup> massFluidGroups = new();
        private readonly ShipMassReader massReader = new();
        private readonly ShipMassReader contactMassReader = new();
        private readonly ShipStructureService.MassWorkspace massWorkspace = new();
        private ConditionalWeakTable<ShipState, ShipMassContributionPool> massContributionPools = new();

        private void ResetMassWorkspace()
        {
            assignedMassPassengers.Clear();
            massPiecesById.Clear();
            massFluidGroups.Clear();
            massReader.ClearCache(); contactMassReader.ClearCache();
            massContributionPools = new();
            massWorkspace.Masses.Clear();
            massWorkspace.Sources.Clear();
        }
        #endregion

        private void RefreshMass()
        {
            assignedMassPassengers.Clear();
            foreach (ShipState ship in State.Ships)
            {
                massContributionPools.GetValue(ship, _ => new ShipMassContributionPool()).Begin(ship);
                massReader.Reset();
                massPiecesById.Clear();
                foreach (ShipPieceState piece in ship.Pieces)
                    if (piece.IsAlive && int.TryParse(piece.PieceId, out int id))
                    {
                        massPiecesById.Add(id, piece);
                        massReader.RegisterStructuralItem(GetMassPieceData(piece, out _));
                    }
                using (MachineWorld.UseScope("ship:" + ship.ShipId))
                {
                    massFluidGroups.Clear();
                    MachineWorld.CollectFluidTankGroups(massFluidGroups);
                    foreach (MachineWorld.FluidTankGroup group in massFluidGroups)
                    {
                        double mass = massReader.ReadFluidMass(group.Inventory?.State);
                        if (mass <= 0d) continue;
                        // 共享罐只有一份流体库存，重量按各罐真实容积放回各自的位置。
                        double remaining = mass;
                        for (int i = 0; i < group.Members.Count; i++)
                        {
                            MachineEntity member = group.Members[i];
                            if (!massPiecesById.TryGetValue(member.Id, out ShipPieceState piece))
                                throw new InvalidOperationException("飞船流体网含有不属于船体的储罐。");
                            double portion = i == group.Members.Count - 1 ? remaining :
                                mass * member.Definition.Fluid.VolumeLiters / group.TotalVolume;
                            remaining -= portion;
                            AddCargoMass(ship, piece, "fluid-group:", portion, group.OwnerId);
                        }
                    }
                    foreach (var entry in massPiecesById)
                    {
                        ShipPieceState piece = entry.Value;
                        ItemData data = GetMassPieceData(piece, out Item live);
                        if (data?.Stack != null) piece.MassKg = Math.Max(0d, data.Stack.CurrentWeight);
                        MachineEntity node = MachineWorld.GetById(entry.Key);
                        double cargo = massReader.ReadItemContents(data, live, node, 0);
                        if (node?.Definition.Fluid != null)
                        {
                            FluidMachineState fluids = MachineWorld.GetFluidState(node);
                            foreach (string chamber in fluids.Chambers.Keys)
                                cargo += massReader.ReadFluidMass(MachineWorld.GetFluidInventory(node, chamber)?.State);
                            cargo += massReader.ReadFluidMass(MachineWorld.GetFluidInventory(node, "main")?.State);
                            cargo += massReader.ReadInventory(fluids.PortableTank, 0) + massReader.ReadInventory(fluids.FilterMaterial, 0) + massReader.ReadInventory(fluids.Residue, 0);
                        }
                        AddCargoMass(ship, piece, "cargo:", cargo);
                    }
                }
                foreach (SpacePassengerState passenger in State.Passengers)
                {
                    if (assignedMassPassengers.Contains(passenger.ProfileId)) continue;
                    Player player = null;
                    if (ItemMgr.Instance != null) ItemMgr.Instance.Player_DIC.TryGetValue(passenger.ProfileId, out player);
                    Data_Player playerData = player?.Data;
                    if (playerData == null) owner.PlayerData_Dict.TryGetValue(passenger.ProfileId, out playerData);
                    if (playerData == null || !TryGetPassengerMassPosition(passenger, ship, player, playerData, out double x, out double y)) continue;
                    assignedMassPassengers.Add(passenger.ProfileId);
                    double mass = massReader.ReadItem(playerData, player, 0);
                    if (mass > 0d)
                    {
                        ShipMassContribution value = AddMassContribution(ship, "person:", passenger.ProfileId);
                        value.Kind = ShipMassKind.Occupant; value.LocalX = x; value.LocalY = y; value.MassKg = mass;
                    }
                }
                AddLooseCargoMass(ship, massReader);
                ShipStructureService.RecalculateMass(ship, massWorkspace);
            }
        }
        private ItemData GetMassPieceData(ShipPieceState piece, out Item live)
        {
            live = views.TryGetValue(piece.PieceId, out ShipPieceView view) && view != null ? view.Item : null;
            return live != null ? live.itemData : GetPieceData(piece);
        }
        private ShipMassContribution AddMassContribution(ShipState ship, string prefix, string identity, int ownerId = 0)
            => massContributionPools.GetValue(ship, _ => new ShipMassContributionPool()).Add(ship, prefix, identity, ownerId);
        private void AddCargoMass(ShipState ship, ShipPieceState piece, string prefix, double mass, int ownerId = 0)
        {
            if (mass <= 0d) return;
            int width = (piece.QuarterTurns & 1) == 0 ? piece.Width : piece.Height;
            int height = (piece.QuarterTurns & 1) == 0 ? piece.Height : piece.Width;
            double w = width * ship.CellSizeMeters, h = height * ship.CellSizeMeters;
            ShipMassContribution value = AddMassContribution(ship, prefix, piece.PieceId, ownerId);
            value.SupportPieceId = piece.PieceId;
            value.LocalX = (piece.CellX + width * .5d) * ship.CellSizeMeters;
            value.LocalY = (piece.CellY + height * .5d) * ship.CellSizeMeters; value.MassKg = mass;
            value.IntrinsicInertiaKgM2 = mass * (w * w + h * h) / 12d;
        }
        private bool TryGetPassengerMassPosition(SpacePassengerState passenger, ShipState ship, Player player,
            Data_Player data, out double localX, out double localY)
        {
            localX = passenger.LocalX; localY = passenger.LocalY;
            if (!string.IsNullOrWhiteSpace(passenger.ShipId)) return passenger.ShipId == ship.ShipId;
            if (passenger.IsInSpace != (ship.PlatformKind == ShipPlatformKind.Space)) return false;
            SpaceVector2 position;
            if (passenger.IsInSpace) position = passenger.FreeMotion.PositionMeters;
            else
            {
                string world = player != null ? player.gameObject.scene.name : data.CurrentSceneName;
                if (world != ship.WorldAddress) return false;
                Vector3 savedPosition = player != null ? player.transform.position : data.transform.position;
                position = new SpaceVector2(savedPosition.x, savedPosition.y);
            }
            ShipGeometry.WorldToLocal(ship, position.X, position.Y, out localX, out localY);
            return ShipAtmosphereService.HasFloor(ship, (int)Math.Floor(localX / ship.CellSizeMeters), (int)Math.Floor(localY / ship.CellSizeMeters));
        }
        #endregion


    }
}
