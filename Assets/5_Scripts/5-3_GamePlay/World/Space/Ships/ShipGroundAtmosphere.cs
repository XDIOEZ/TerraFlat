using System;
using System.Collections.Generic;
using System.Globalization;
using FlatWorld.Gameplay.Progress;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 普通建筑的独立密闭环境
        private readonly Dictionary<string, GroundAtmosphereState> groundAtmosphereIndex = new(StringComparer.Ordinal);
        private string groundGeometryWorld;
        private uint groundBuildingRevision;
        private bool groundMachinesDirty = true;
        private readonly Dictionary<string, ShipPartConfiguration> groundPartConfigurations = new(StringComparer.Ordinal);
        private GameRes groundResources;
        private ulong groundDefinitionRevision;

        private ShipPartConfiguration GroundPartConfiguration(ItemData data)
        {
            GameRes resources = GameRes.ExistingInstance;
            ulong revision = resources != null ? resources.ItemDefinitionRevision : 0;
            if (!ReferenceEquals(resources, groundResources) || revision != groundDefinitionRevision)
            {
                groundPartConfigurations.Clear(); groundResources = resources; groundDefinitionRevision = revision;
                groundGeometryWorld = null;
            }
            if (!groundPartConfigurations.TryGetValue(data.IDName, out ShipPartConfiguration config))
                groundPartConfigurations.Add(data.IDName, config = ResolvePartConfiguration(data));
            return config;
        }

        private void InvalidateGroundAtmosphere(Vector2Int cell) => groundMachinesDirty = true;

        private bool IsGroundAtmosphere(ShipState geometry) => geometry != null &&
            groundAtmosphereIndex.TryGetValue(geometry.WorldAddress ?? string.Empty, out GroundAtmosphereState state) &&
            ReferenceEquals(state.Geometry, geometry);

        private ShipState ResolveAtmosphereGeometry(string key) => GetShip(key) ?? GetGroundAtmosphere(key);

        private ShipState GetGroundAtmosphere(string world, bool refresh = true)
        {
            if (State == null || string.IsNullOrWhiteSpace(world) || SpaceMgr.IsSpaceScene(world) ||
                world.StartsWith("ship:", StringComparison.Ordinal)) return null;
            if (!groundAtmosphereIndex.TryGetValue(world, out GroundAtmosphereState state))
            {
                state = State.GroundAtmospheres.Find(value => value.WorldKey == world);
                if (state == null)
                {
                    if (!FlatWorld.Networking.GameNetwork.HasStateAuthority || world != SceneManager.GetActiveScene().name) return null;
                    state = new GroundAtmosphereState { WorldKey = world,
                        Geometry = new ShipState { ShipId = world, WorldAddress = world, PlatformKind = ShipPlatformKind.Land } };
                    State.GroundAtmospheres.Add(state);
                }
                groundAtmosphereIndex.Add(world, state);
            }
            if (refresh && FlatWorld.Networking.GameNetwork.HasStateAuthority && GameManager.Instance?.IsGameplayReady == true &&
                DimensionManager.ExistingInstance?.IsTransitioning != true && world == SceneManager.GetActiveScene().name &&
                (groundGeometryWorld != world || groundBuildingRevision != BuildingOccupancyRegistry.Revision || groundMachinesDirty ||
                 !ReferenceEquals(groundResources, GameRes.ExistingInstance) || groundDefinitionRevision != (GameRes.ExistingInstance?.ItemDefinitionRevision ?? 0)))
            {
                var pieces = new List<ShipPieceState>();
                var archived = new Dictionary<int, ItemData>();
                SaveDataMgr.Instance?.CollectArchivedRuntimeBuildings(world, archived);
                List<ShipGroundBuildingStore.Entry> entries = ShipGroundBuildingStore.Collect(world);
                foreach (ShipGroundBuildingStore.Entry entry in entries) archived.Remove(entry.Guid);
                foreach (ItemData data in archived.Values)
                {
                    Vector3 point = data.transform.position;
                    pieces.Add(CreateGroundAtmospherePiece(data, new Vector2Int(Mathf.FloorToInt(point.x), Mathf.FloorToInt(point.y)),
                        Mathf.RoundToInt(data.transform.rotation.eulerAngles.z / 90f) & 3));
                }
                foreach (ShipGroundBuildingStore.Entry entry in entries)
                    pieces.Add(CreateGroundAtmospherePiece(entry.Data, entry.Cell, entry.QuarterTurns));
                pieces.Sort((left, right) => string.CompareOrdinal(left.PieceId, right.PieceId));
                bool changed = state.Geometry.Pieces.Count != pieces.Count;
                for (int i = 0; !changed && i < pieces.Count; i++)
                {
                    ShipPieceState before = state.Geometry.Pieces[i], after = pieces[i];
                    changed = before.PieceId != after.PieceId || before.Kind != after.Kind ||
                        before.CellX != after.CellX || before.CellY != after.CellY || before.Width != after.Width ||
                        before.Height != after.Height || before.QuarterTurns != after.QuarterTurns ||
                        before.SealsAtmosphere != after.SealsAtmosphere || before.DisableHealth != after.DisableHealth;
                }
                if (changed || state.Geometry.AtmosphereStructureDirty)
                {
                    state.Geometry.Pieces = pieces;
                    state.Geometry.NotifyStructureChanged();
                    ShipAtmosphereService.RebuildCompartments(state.Geometry, GetGroundOutside(world));
                    InitializeGroundTemplateCabins(state.Geometry, entries, world);
                }
                groundResources = GameRes.ExistingInstance;
                groundDefinitionRevision = groundResources?.ItemDefinitionRevision ?? 0;
                groundGeometryWorld = world;
                groundBuildingRevision = BuildingOccupancyRegistry.Revision;
                groundMachinesDirty = false;
            }
            return state.Geometry;
        }

        private ShipPieceState CreateGroundAtmospherePiece(ItemData data, Vector2Int cell, int turns)
        {
            ShipPartConfiguration config = GroundPartConfiguration(data);
            Mod_Building.TryReadBuildingData(data, out _, out Mod_Building.Building_Data building);
            var piece = new ShipPieceState { PieceId = data.Guid.ToString(CultureInfo.InvariantCulture), ItemId = data.IDName,
                Kind = config.Kind, CellX = cell.x, CellY = cell.y, QuarterTurns = turns,
                Width = Math.Max(1, Math.Max(config.FootprintWidth, building?.FootprintWidth ?? 1)),
                Height = Math.Max(1, Math.Max(config.FootprintHeight, building?.FootprintHeight ?? 1)),
                SealsAtmosphere = config.SealsAtmosphere,
                DisableHealth = config.DisableHealth, MaxDisableHealth = config.DisableHealth };
            RestoreGroundPartState(data, piece);
            return piece;
        }

        private static IShipAtmosphereSource GetGroundOutside(string world)
        {
            AtmosphereService.TryGetForWorld(world, out AtmosphereState atmosphere);
            return new PlanetShipAtmosphereSource(atmosphere, atmosphere?.TemperatureKelvin - 273.15d ?? 20d);
        }

        private void InitializeGroundTemplateCabins(ShipState geometry, List<ShipGroundBuildingStore.Entry> entries, string world)
        {
            // 模板初始舱气在地面首次形成房间时落账，后续装船不会覆盖已经供入的气体。
            foreach (ShipGroundBuildingStore.Entry entry in entries)
            {
                if (entry.Machine == null || GroundPartConfiguration(entry.Data)?.Kind != ShipPieceKind.Console ||
                    ShipAtmosphereService.FindCompartment(geometry, entry.Cell.x, entry.Cell.y) == null) continue;
                Newtonsoft.Json.Linq.JObject root = ItemSpecialDataJsonStore.ReadRoot(entry.Data.ItemSpecialData);
                if (root[ShipFixedStructureBuilder.TemplateMarker]?["initialCabinGas"] == null) continue;
                var cells = new HashSet<ShipCell>();
                foreach (ShipGroundBuildingStore.Entry member in CollectGroundAssembly(world, entry.Guid))
                    foreach (Vector2Int cell in GroundFootprint(member)) cells.Add(new ShipCell(cell.x, cell.y));
                InitializeTemplateCabin(geometry, entry.Guid, world, cells);
            }
        }

        public bool TryGetGroundEnvironment(string world, Vector2 logicalPosition, out ShipEnvironmentSnapshot environment)
        {
            ShipState geometry = GetGroundAtmosphere(world);
            Vector2 point = PressureExplosionQueue.NormalizeInWorld(world, logicalPosition);
            environment = ShipAtmosphereService.GetEnvironment(geometry, Mathf.FloorToInt(point.x), Mathf.FloorToInt(point.y));
            return environment.IsSealed;
        }

        public void SupplyGroundAtmosphere(MachineEntity machine, ShipPartConfiguration config, float seconds)
        {
            if (!running || config == null || config.SupplyMolesPerSecond <= 0f || machine?.Definition.Fluid == null || seconds <= 0f ||
                machine.ScopeKey.StartsWith("ship:", StringComparison.Ordinal) || machine.State?.Engaged == false ||
                machine.State?.Hp == 0f || config.PowerWatts > 0f && machine.ElectricalPowerRatio <= .999f) return;
            ShipState geometry = GetGroundAtmosphere(machine.ScopeKey);
            if (geometry == null) return;
            string pieceId = machine.Id.ToString(CultureInfo.InvariantCulture);
            ShipPieceState piece = geometry.Pieces.Find(value => value.PieceId == pieceId);
            if (piece?.IsOperational != true) return;
            using var scope = MachineWorld.UseNodeScope(machine);
            var source = new TankShipAtmosphereSource(MachineWorld.GetFluidInventory(machine),
                MachineWorld.GetFluidVolumeLiters(machine), MachineWorld.GetFluidMinimumGasSpaceLiters(machine));
            // 地面设备和飞船设备共用真实扣气、目标压力及每秒供气上限。
            SupplyGroundGas(geometry, machine.Cell.x, machine.Cell.y, source, config, seconds,
                machine.State?.Engaged != false, piece.IsOperational, machine.ElectricalPowerRatio, out _);
        }

        internal static bool SupplyGroundGas(ShipState geometry, int x, int y, IShipAtmosphereSource source,
            ShipPartConfiguration config, float seconds, bool enabled, bool operational, float powerRatio, out decimal received)
        {
            received = 0m;
            if (geometry == null || config == null || !enabled || !operational || !MachineDefinition.Positive(seconds) ||
                !MachineDefinition.Positive(config.SupplyMolesPerSecond) || config.PowerWatts > 0f && !(powerRatio > .999f)) return false;
            return ShipAtmosphereService.TrySupplyGas(geometry, x, y, source, config.TargetPressureKPa,
                (decimal)(config.SupplyMolesPerSecond * seconds), out received);
        }

        private bool TrySampleGround(Item item, out ItemEnvironmentSample sample)
        {
            Vector2 point = WorldLocalPresentation.ToLogical((Vector2)item.transform.position);
            if (TryGetGroundEnvironment(item.gameObject.scene.name, point, out ShipEnvironmentSnapshot environment))
            {
                sample = new ItemEnvironmentSample(environment.PressureKPa, environment.TemperatureCelsius,
                    environment.CanBreathe, environment.HasFloorSupport, false);
                return true;
            }
            sample = default; return false;
        }
        #endregion

        #region 地面与飞行房间的气体所有权移交
        internal static void MoveRoomCells(ShipState source, ShipState destination, Func<ShipCell, ShipCell> transform,
            HashSet<ShipCell> selected)
        {
            // 只移交实际选中的房间份额，原库存同步扣除，失败可按相反坐标变换归还。
            foreach (ShipCompartmentState room in source.Compartments)
            {
                var moved = new List<ShipCell>();
                foreach (ShipCell cell in room.Cells) if (selected.Contains(cell)) moved.Add(cell);
                if (moved.Count == 0) continue;
                var target = new ShipCompartmentState();
                foreach (ShipCell cell in moved) target.Cells.Add(transform(cell));
                target.VolumeLiters = moved.Count * destination.CellSizeMeters * destination.CellSizeMeters * destination.CabinHeightMeters * 1000d;
                var inventory = ShipAtmosphereService.GetCompartmentInventory(room);
                decimal fraction = (decimal)moved.Count / room.Cells.Count;
                foreach (FluidComponentState gas in room.Gas.Components.ToArray())
                {
                    decimal amount = AtmosphereService.Quantize(inventory.GetAvailableMoles(gas.FluidId, FluidPhase.Gas) * fraction);
                    if (amount > 0m && inventory.TryTakeExact(gas.FluidId, FluidPhase.Gas, amount, out FluidBatch batch) &&
                        !ShipAtmosphereService.GetCompartmentInventory(target).TryAdd(batch, target.VolumeLiters,
                            ShipAtmosphereService.MinimumCabinGasSpaceLiters))
                        throw new InvalidOperationException("地面与船舱之间移交气体失败。");
                }
                destination.Compartments.Add(target);
                foreach (ShipCell cell in target.Cells)
                    if (!destination.EverEnclosedCells.Contains(cell)) destination.EverEnclosedCells.Add(cell);
                room.Cells.RemoveAll(selected.Contains);
                room.VolumeLiters = room.Cells.Count * source.CellSizeMeters * source.CellSizeMeters * source.CabinHeightMeters * 1000d;
            }
            source.Compartments.RemoveAll(room => room.Cells.Count == 0);
            source.AtmosphereStructureDirty = true;
            destination.AtmosphereStructureDirty = true;
        }

        private void TakeGroundAtmosphere(ShipState ship, string world)
        {
            ShipState ground = GetGroundAtmosphere(world);
            var cells = new HashSet<ShipCell>();
            foreach (ShipPieceState piece in ship.Pieces)
                if (piece.Kind == ShipPieceKind.Floor)
                    foreach (ShipCell cell in ShipGeometry.Footprint(piece))
                    {
                        ShipGeometry.LocalToWorld(ship, cell.X + .5d, cell.Y + .5d, out double x, out double y);
                        cells.Add(new ShipCell((int)Math.Floor(x), (int)Math.Floor(y)));
                    }
            foreach (ShipCell cell in ground.EverEnclosedCells)
                if (cells.Contains(cell))
                {
                    ShipGeometry.WorldToLocal(ship, cell.X + .5d, cell.Y + .5d, out double x, out double y);
                    var local = new ShipCell((int)Math.Floor(x), (int)Math.Floor(y));
                    if (!ship.EverEnclosedCells.Contains(local)) ship.EverEnclosedCells.Add(local);
                }
            MoveRoomCells(ground, ship, cell =>
            {
                ShipGeometry.WorldToLocal(ship, cell.X + .5d, cell.Y + .5d, out double x, out double y);
                return new ShipCell((int)Math.Floor(x), (int)Math.Floor(y));
            }, cells);
            groundGeometryWorld = null;
        }

        private void ReturnGroundAtmosphere(ShipState ship, string world)
        {
            ShipState ground = GetGroundAtmosphere(world, refresh: false);
            if (ground == null) throw new InvalidOperationException("地表气体移交缺少目标世界。");
            foreach (ShipCell cell in ship.EverEnclosedCells)
            {
                ShipGeometry.LocalToWorld(ship, cell.X + .5d, cell.Y + .5d, out double x, out double y);
                var point = new ShipCell((int)Math.Floor(x), (int)Math.Floor(y));
                if (!ground.EverEnclosedCells.Contains(point)) ground.EverEnclosedCells.Add(point);
            }
            var cells = new HashSet<ShipCell>();
            foreach (ShipCompartmentState room in ship.Compartments) cells.UnionWith(room.Cells);
            MoveRoomCells(ship, ground, cell =>
            {
                ShipGeometry.LocalToWorld(ship, cell.X + .5d, cell.Y + .5d, out double x, out double y);
                return new ShipCell((int)Math.Floor(x), (int)Math.Floor(y));
            }, cells);
            ship.Compartments.Clear();
            groundGeometryWorld = null;
        }
        #endregion
    }
}
