using System;
using System.Collections.Generic;
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
            return config?.Kind == ShipPieceKind.Floor || session != null && session.TryGetSupport(request.Position, out _, out _, out _);
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
                if (!IsVisible(value)) continue;
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
                Phase = IsSpaceView ? ShipFlightPhase.Orbit : ShipFlightPhase.Landed,
                Orbit = new OrbitState { PositionMeters = new SpaceVector2(ship.PositionX, ship.PositionY) },
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
                Kind = door ? ShipPieceKind.Door : wall ? ShipPieceKind.Wall : ShipPieceKind.Equipment,
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
            State.Devices.RemoveAll(value => value.PieceId == piece.PieceId);
            RemovePieceView(piece.PieceId);
            if (ship.Pieces.Count == 0) RemoveEmptyShip(ship);
            else ShipStructureService.RecalculateMass(ship);
            return true;
        }
        public void CommitPiece(ShipState ship, ShipPieceState piece)
        {
            ShipAtmosphereService.RebuildCompartments(ship, GetOutside(ship));
            SynchronizeViews();
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
                    flight.Orbit.PositionMeters = new SpaceVector2(fragment.PositionX, fragment.PositionY);
                    flight.Orbit.VelocityMetersPerSecond = new SpaceVector2(fragment.VelocityX, fragment.VelocityY);
                    if (flight.Phase is ShipFlightPhase.Landed or ShipFlightPhase.Floating or ShipFlightPhase.Lifting or ShipFlightPhase.SurfaceDescending)
                    { flight.SurfaceX = fragment.PositionX; flight.SurfaceY = fragment.PositionY; }
                }
                foreach (SpacePassengerState passenger in State.Passengers)
                {
                    if (passenger.ShipId != ship.ShipId) continue;
                    ShipState support = fragments.Find(fragment => ShipAtmosphereService.HasFloor(fragment,
                        (int)Math.Floor(passenger.LocalX / fragment.CellSizeMeters), (int)Math.Floor(passenger.LocalY / fragment.CellSizeMeters)));
                    if (!string.IsNullOrWhiteSpace(passenger.GripPieceId))
                        support = fragments.Find(fragment => fragment.Pieces.Exists(piece => piece.PieceId == passenger.GripPieceId && piece.IsAlive));
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
                if (fragments.Count == 0) RemoveEmptyShip(ship);
            }
            State.Docking.RemoveAll(pair => !FindPiece(pair.PortAId, out _, out _) || !FindPiece(pair.PortBId, out _, out _));
            State.Navigation.RemoveAll(value => !FindPiece(value.NavigationPieceId, out _, out _));
            destroyed.Clear();
        }

        private void RemoveEmptyShip(ShipState ship)
        {
            MachineWorld.ReleaseShipScope(ship.ShipId);
            State.Ships.Remove(ship);
            State.Flights.RemoveAll(value => value.ShipId == ship.ShipId);
            State.Navigation.RemoveAll(value => value.ShipId == ship.ShipId);
            State.Docking.RemoveAll(value => value.ShipAId == ship.ShipId || value.ShipBId == ship.ShipId);
        }
        private void RefreshMass()
        {
            var assignedPassengers = new HashSet<string>(StringComparer.Ordinal);
            foreach (ShipState ship in State.Ships)
            {
                ship.MassContributions.Clear();
                var reader = new ShipMassReader();
                var byId = new Dictionary<int, ShipPieceState>();
                foreach (ShipPieceState piece in ship.Pieces)
                    if (piece.IsAlive && int.TryParse(piece.PieceId, out int id))
                    {
                        byId.Add(id, piece);
                        reader.RegisterStructuralItem(GetPieceData(piece));
                    }
                using (MachineWorld.UseScope("ship:" + ship.ShipId))
                {
                    var groups = new List<MachineWorld.FluidTankGroup>();
                    MachineWorld.CollectFluidTankGroups(groups);
                    foreach (MachineWorld.FluidTankGroup group in groups)
                    {
                        double mass = reader.ReadFluidMass(group.Inventory?.State);
                        if (mass <= 0d) continue;
                        // 共享罐只有一份流体库存，重量按各罐真实容积放回各自的位置。
                        double remaining = mass;
                        for (int i = 0; i < group.Members.Count; i++)
                        {
                            MachineEntity member = group.Members[i];
                            if (!byId.TryGetValue(member.Id, out ShipPieceState piece))
                                throw new InvalidOperationException("飞船流体网含有不属于船体的储罐。");
                            double portion = i == group.Members.Count - 1 ? remaining :
                                mass * member.Definition.Fluid.VolumeLiters / group.TotalVolume;
                            remaining -= portion;
                            AddCargoMass(ship, piece, "fluid-group:" + group.OwnerId + ":" + member.Id, portion);
                        }
                    }
                    foreach (var entry in byId)
                    {
                        ShipPieceState piece = entry.Value;
                        ItemData data = GetPieceData(piece);
                        if (data?.Stack != null) piece.MassKg = Math.Max(0d, data.Stack.CurrentWeight);
                        MachineEntity node = MachineWorld.GetById(entry.Key);
                        double cargo = reader.ReadItemContents(data, null, node, 0);
                        if (node?.Definition.Fluid != null)
                        {
                            FluidMachineState fluids = MachineWorld.GetFluidState(node);
                            var chambers = new HashSet<string>(fluids.Chambers.Keys, StringComparer.Ordinal) { "main" };
                            foreach (string chamber in chambers) cargo += reader.ReadFluidMass(MachineWorld.GetFluidInventory(node, chamber)?.State);
                            cargo += reader.ReadInventory(fluids.PortableTank, 0) + reader.ReadInventory(fluids.FilterMaterial, 0) + reader.ReadInventory(fluids.Residue, 0);
                        }
                        AddCargoMass(ship, piece, "cargo:" + piece.PieceId, cargo);
                    }
                }
                foreach (SpacePassengerState passenger in State.Passengers)
                {
                    if (assignedPassengers.Contains(passenger.ProfileId)) continue;
                    Player player = null;
                    if (ItemMgr.Instance != null) ItemMgr.Instance.Player_DIC.TryGetValue(passenger.ProfileId, out player);
                    Data_Player playerData = player?.Data;
                    if (playerData == null) owner.PlayerData_Dict.TryGetValue(passenger.ProfileId, out playerData);
                    if (playerData == null || !TryGetPassengerMassPosition(passenger, ship, player, playerData, out double x, out double y)) continue;
                    assignedPassengers.Add(passenger.ProfileId);
                    double mass = reader.ReadItem(playerData, player, 0);
                    if (mass > 0d) ship.MassContributions.Add(new ShipMassContribution
                    {
                        SourceId = "person:" + passenger.ProfileId, Kind = ShipMassKind.Occupant,
                        LocalX = x, LocalY = y, MassKg = mass
                    });
                }
                AddLooseCargoMass(ship, reader);
                ShipStructureService.RecalculateMass(ship);
            }
        }
        private static void AddCargoMass(ShipState ship, ShipPieceState piece, string source, double mass)
        {
            if (mass <= 0d) return;
            int width = (piece.QuarterTurns & 1) == 0 ? piece.Width : piece.Height;
            int height = (piece.QuarterTurns & 1) == 0 ? piece.Height : piece.Width;
            double w = width * ship.CellSizeMeters, h = height * ship.CellSizeMeters;
            ship.MassContributions.Add(new ShipMassContribution { SourceId = source, SupportPieceId = piece.PieceId,
                Kind = ShipMassKind.Cargo, LocalX = (piece.CellX + width * .5d) * ship.CellSizeMeters,
                LocalY = (piece.CellY + height * .5d) * ship.CellSizeMeters, MassKg = mass,
                IntrinsicInertiaKgM2 = mass * (w * w + h * h) / 12d });
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

        #region 实际嵌套库存质量读取
        private sealed class ShipMassReader
        {
            private readonly HashSet<int> itemIds = new();
            private readonly HashSet<ItemData> items = new();
            private readonly HashSet<Inventory_Data> inventories = new();
            private readonly HashSet<FluidInventoryState> fluids = new();
            private readonly HashSet<LiquidContainerState> liquids = new();
            private readonly HashSet<int> liveEquipmentStores = new();

            public void RegisterStructuralItem(ItemData data)
            { if (data == null) return; items.Add(data); if (data.Guid != 0) itemIds.Add(data.Guid); }
            public double ReadItem(ItemData data, Item live, int depth)
            {
                if (data == null || data.Stack?.Amount <= 0f || !items.Add(data)) return 0d;
                if (data.Guid != 0 && !itemIds.Add(data.Guid)) return 0d;
                if (depth > 64) throw new InvalidOperationException("物品库存嵌套过深。");
                if (data.SharedConfiguration == null) ItemDefinitionRuntime.RebasePersistedData(GameRes.ExistingInstance, data);
                return Math.Max(0d, data.Stack?.CurrentWeight ?? 0d) + ReadItemContents(data, live, null, depth + 1);
            }
            public double ReadInventory(Inventory_Data inventory, int depth)
            {
                if (inventory?.itemSlots == null || !inventories.Add(inventory)) return 0d;
                double mass = 0d;
                foreach (ItemSlot slot in inventory.itemSlots) mass += ReadItem(slot?.itemData, null, depth + 1);
                return mass;
            }
            private double ReadSnapshot(InventoryInstanceSnapshot snapshot, int depth)
                => snapshot == null ? 0d : ReadInventory(snapshot.CreateColdData(), depth + 1);
            public double ReadFluidMass(FluidInventoryState state)
            {
                if (state?.Components == null || !fluids.Add(state)) return 0d;
                double mass = 0d;
                foreach (FluidComponentState component in state.Components)
                    mass += (double)(component.GasMoles + component.LiquidMoles) * FluidCatalog.Default.Find(component.FluidId).MolarMassKgPerMol;
                return mass;
            }
            private double ReadLiquidMass(LiquidContainerState state)
            {
                if (state == null || !liquids.Add(state)) return 0d;
                MixedLiquidContents.Ensure(state);
                double mass = 0d;
                foreach (var entry in state.Composition)
                {
                    if (!FluidCatalog.Default.TryFromLiquidId(entry.Key, out FluidDefinition definition))
                        throw new InvalidOperationException("船载液体缺少真实密度定义：" + entry.Key);
                    mass += entry.Value * (double)definition.LitersPerServing * definition.LiquidDensityKgPerLiter;
                }
                return mass;
            }
            public double ReadItemContents(ItemData data, Item live, MachineEntity machine, int depth)
            {
                if (data == null) return 0d;
                if (depth > 64) throw new InvalidOperationException("物品库存嵌套过深。");
                double mass = 0d;
                var liveModules = new HashSet<string>(StringComparer.Ordinal);
                if (live != null)
                {
                    foreach (var entry in live.Mods)
                    {
                        Module module = entry.Value;
                        if (module == null || !module.Enabled || !module.IsRuntimeLoaded) continue;
                        if (module is Mod_Equipment equipment)
                        {
                            // 装备包先读运行态，跳过同一装备可能尚未刷新的冷载荷。
                            foreach (EquipmentInstance instance in equipment.EnumerateEquippedInstances())
                                if (instance is EquipmentInstance_Bag bag)
                                { if (bag.EquippedItem != null) liveEquipmentStores.Add(bag.EquippedItem.Guid); mass += ReadInventory(bag.BagData, depth + 1); }
                            mass += ReadInventory(equipment.EquipmentInventory?.Data, depth + 1);
                            liveModules.Add(entry.Key);
                        }
                        else if (module is Mod_Inventory storage)
                        {
                            if (storage.InventoryInstances != null) foreach (Inventory inventory in storage.InventoryInstances) mass += ReadInventory(inventory?.Data, depth + 1);
                            if (storage.InventoryRefDic != null) foreach (Inventory inventory in storage.InventoryRefDic.Values) mass += ReadInventory(inventory?.Data, depth + 1);
                            liveModules.Add(entry.Key);
                        }
                        else if (module is Mod_VesselContents vessel)
                        { mass += ReadInventory(vessel.Contents?.Data, depth + 1); liveModules.Add(entry.Key); }
                        else if (module is Mod_WaterVessel water)
                        { mass += ReadLiquidMass(water.Data); liveModules.Add(entry.Key); }
                        else if (module is Mod_Hand hand) mass += ReadInventory(hand.HandInventory?.Data, depth + 1);
                        else if (module is Mod_HotBar hotbar) mass += ReadInventory(hotbar.RuntimeInventory?.Data, depth + 1);
                        else if (module is Mod_HandCraftTable handcraft)
                            mass += ReadInventory(handcraft.inputInventory?.Data, depth + 1) + ReadInventory(handcraft.outputInventory?.Data, depth + 1);
                        else if (module is Mod_HandMade handmade && handmade.InventoryRefDic != null)
                            foreach (Inventory inventory in handmade.InventoryRefDic.Values) mass += ReadInventory(inventory?.Data, depth + 1);
                    }
                }
                if (data is Data_Player player && player._inventoryData != null)
                    foreach (Inventory_Data inventory in player._inventoryData.Values) mass += ReadInventory(inventory, depth + 1);
                if (machine?.Logic != null)
                    foreach (Inventory inventory in machine.Logic.Inventories) mass += ReadInventory(inventory?.Data, depth + 1);
                if (machine?.Processor != null)
                    mass += ReadInventory(machine.Processor.Input?.Data, depth + 1) + ReadInventory(machine.Processor.Output?.Data, depth + 1);
                if (machine?.Logic is VesselLogic actualVessel) mass += ReadLiquidMass(actualVessel.Data);
                bool portableRead = FluidTankStorage.TryGet(data, out FluidInventory portable, out _);
                if (portableRead) mass += ReadFluidMass(portable.State);

                foreach (var entry in data.ModuleDataDic)
                {
                    ModuleData module = entry.Value;
                    // 暂停能力不会凭空拿走已经装在物品内的真实载荷。
                    if (module == null || liveModules.Contains(entry.Key)) continue;
                    if (module is Inventory_ModuleData storage)
                    {
                        if (machine?.Logic == null && storage.Data != null)
                            foreach (Inventory_Data inventory in storage.Data.Values) mass += ReadInventory(inventory, depth + 1);
                        continue;
                    }
                    if (module is not Ex_ModData_MemoryPackable binary || binary.BitData?.Length is not > 0) continue;
                    if (module.ModuleId == ModText.Equipment_Module)
                    {
                        Mod_EquipmentSaveData equipment = binary.GetData<Mod_EquipmentSaveData>();
                        mass += ReadSnapshot(equipment?.EquipmentInventoryData, depth + 1);
                        if (equipment?.EquipmentInstances != null) foreach (List<EquipmentInstance> group in equipment.EquipmentInstances)
                            if (group != null) foreach (EquipmentInstance instance in group)
                                if (instance is EquipmentInstance_Bag bag) mass += ReadInventory(bag.BagData, depth + 1);
                    }
                    else if (module.ModuleId == ModText.Equipment_Store && !liveEquipmentStores.Contains(data.Guid))
                    {
                        List<EquipmentInstance> equipment = binary.GetData<List<EquipmentInstance>>();
                        if (equipment != null) foreach (EquipmentInstance instance in equipment)
                            if (instance is EquipmentInstance_Bag bag) mass += ReadInventory(bag.BagData, depth + 1);
                    }
                    else if (module.ModuleId == Mod_VesselContents.ModuleId && machine?.Logic is not VesselLogic)
                        mass += ReadSnapshot(binary.GetData<VesselContentsState>()?.Items, depth + 1);
                    else if (module.ModuleId == Mod_WaterVessel.ModuleId && machine?.Logic is not VesselLogic)
                        mass += ReadLiquidMass(binary.GetData<LiquidContainerState>());
                    else if (module.ModuleId == Mod_Spacesuit.ModuleId)
                    {
                        SpacesuitState suit = binary.GetData<SpacesuitState>();
                        mass += ReadSnapshot(suit?.OxygenTank, depth + 1) + ReadSnapshot(suit?.PropulsionTank, depth + 1);
                    }
                    else if (module.ModuleId == Mod_FluidTank.ModuleId && !portableRead)
                        mass += ReadFluidMass(binary.GetData<FluidTankState>()?.Contents);
                    else if (module.ModuleId == Mod_Cultivator.ModuleId && machine?.Logic is not CultivatorLogic)
                    {
                        CultivatorState plots = binary.GetData<CultivatorState>();
                        mass += ReadSnapshot(plots?.Seeds, depth + 1) + ReadSnapshot(plots?.Harvest, depth + 1) + ReadSnapshot(plots?.Fertilizer, depth + 1);
                    }
                }
                if (machine == null) mass += ReadColdMachine(data, depth + 1);
                if (Mod_Building.TryReadBuildingData(data, out _, out Mod_Building.Building_Data building) && !string.IsNullOrWhiteSpace(building.SnapshotBase64))
                {
                    if (!ItemNetworkStateSerialization.TryDeserializeItemData(Convert.FromBase64String(building.SnapshotBase64), out ItemData packed))
                        throw new InvalidOperationException("船载建筑库存快照无法读取。");
                    if (packed.Guid == 0 || itemIds.Add(packed.Guid)) mass += ReadItemContents(packed, null, null, depth + 1);
                }
                return mass;
            }
            private double ReadColdMachine(ItemData data, int depth)
            {
                if (string.IsNullOrWhiteSpace(data.ItemSpecialData) || !data.ItemSpecialData.Contains(MachinePersistence.Namespace)) return 0d;
                double mass = 0d;
                MachineStorageState storage = MachinePersistence.Read<MachineStorageState>(data, "storage");
                if (storage?.Inventories != null) foreach (Inventory_Data inventory in storage.Inventories) mass += ReadInventory(inventory, depth + 1);
                RecipeProcessingState workbench = MachinePersistence.Read<RecipeProcessingState>(data, "workbench");
                mass += ReadInventory(workbench?.Input, depth + 1) + ReadInventory(workbench?.Output, depth + 1);
                FurnaceRuntimeState furnace = MachinePersistence.Read<FurnaceRuntimeState>(data, "furnace");
                if (furnace?.Smelting?.InvData != null) foreach (Inventory_Data inventory in furnace.Smelting.InvData.Values) mass += ReadInventory(inventory, depth + 1);
                mass += ReadInventory(furnace?.Processing?.Input, depth + 1) + ReadInventory(furnace?.Processing?.Output, depth + 1);
                mass += ReadInventory(MachinePersistence.Read<SlotProcessingState>(data, "compost")?.Inventory, depth + 1);
                mass += ReadInventory(MachinePersistence.Read<SlotProcessingState>(data, "drying")?.Inventory, depth + 1);
                FluidMachineState fluid = MachinePersistence.Read<FluidMachineState>(data, "fluid");
                if (fluid?.Chambers != null) foreach (FluidInventoryState chamber in fluid.Chambers.Values) mass += ReadFluidMass(chamber);
                mass += ReadInventory(fluid?.PortableTank, depth + 1) + ReadInventory(fluid?.FilterMaterial, depth + 1) + ReadInventory(fluid?.Residue, depth + 1);
                return mass;
            }
        }
        #endregion
    }
}
