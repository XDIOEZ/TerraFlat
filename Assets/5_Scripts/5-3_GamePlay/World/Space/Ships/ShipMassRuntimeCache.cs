using System;
using System.Collections.Generic;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        private sealed class ShipMassRuntimeCache : IDisposable
        {
            #region 固定结构登记与动态质量贡献
            public sealed class PieceBinding
            {
                public ShipPieceState Piece;
                public int Id;
                public ItemData Data;
                public double X, Y, InertiaPerKg;
                public Action<ItemData> Changed;
            }
            private sealed class RoomGeometry
            {
                public ShipCompartmentState Room;
                public FluidInventoryState Gas;
                public List<ShipCell> Cells;
                public int Count;
                public double X, Y, InertiaPerKg;
            }
            public readonly ShipMassReader Reader = new();
            public readonly ShipMassLedger Ledger = new();
            public readonly ShipMassContributionPool Contributions = new();
            public readonly Dictionary<int, PieceBinding> ById = new();
            public readonly List<PieceBinding> Pieces = new();
            public readonly HashSet<string> AssignedPassengers = new(StringComparer.Ordinal);
            private GameRes resourceSource;
            private int resourceRevision;
            private readonly HashSet<PieceBinding> dirtyBases = new();
            private readonly HashSet<object> dirtySources = new();
            private readonly List<object> sourceWork = new();
            private readonly List<MachineWorld.FluidTankGroup> fluidGroups = new();
            private readonly Dictionary<ShipCompartmentState, RoomGeometry> rooms = new();
            private List<ShipCompartmentState> roomSource;
            private string worldKey;
            private bool subscribed;
            public ulong StructureRevision;
            public double CellSize;
            public List<ShipPieceState> PieceSource;
            public int PieceCount;
            public bool Initialized;
            public object FluidTopology;

            public ShipMassRuntimeCache() => Reader.SourceInvalidated = QueueStationarySource;
            private void QueueStationarySource(object source)
            {
                if (source is PieceBinding or MachineWorld.FluidTankGroup or RoomGeometry) dirtySources.Add(source);
            }

            public bool Matches(ShipState ship) => Initialized && StructureRevision == ship.StructureRevision && CellSize == ship.CellSizeMeters &&
                ReferenceEquals(PieceSource, ship.Pieces) && PieceCount == ship.Pieces.Count &&
                ReferenceEquals(roomSource, ship.Compartments) && rooms.Count == ship.Compartments.Count &&
                ReferenceEquals(resourceSource, GameRes.ExistingInstance) && resourceRevision == (GameRes.ExistingInstance?.ResourceReloadVersion ?? 0);
            public bool MatchesFluidGroups(IReadOnlyList<MachineWorld.FluidTankGroup> current)
            {
                if (!ReferenceEquals(FluidTopology, MachineWorld.FluidGraph) || fluidGroups.Count != current.Count) return false;
                for (int i = 0; i < current.Count; i++) if (!ReferenceEquals(fluidGroups[i], current[i])) return false;
                return true;
            }
            public void Rebuild(SpaceSession session, ShipState ship, IReadOnlyList<MachineWorld.FluidTankGroup> currentGroups)
            {
                Dispose(); Ledger.Clear(); Contributions.Clear(ship);
                worldKey = MachineWorld.GetShipScopeKey(ship.ShipId);
                Item.RuntimeStructureChanged += OnRuntimeItemChanged;
                ItemMgr.RuntimeItemRegistered += OnRuntimeItemChanged;
                ItemMgr.RuntimeItemUnregistered += OnRuntimeItemChanged;
                MachineWorld.NodeStateChanged += OnMachineChanged;
                MachineWorld.NodeRemoved += OnMachineRemoved;
                subscribed = true;
                FluidTopology = MachineWorld.FluidGraph;
                fluidGroups.AddRange(currentGroups);
                foreach (MachineWorld.FluidTankGroup group in fluidGroups) dirtySources.Add(group);
                foreach (ShipPieceState piece in ship.Pieces)
                {
                    if (piece == null || !piece.IsAlive) continue;
                    int width = Math.Max(1, piece.Width), height = Math.Max(1, piece.Height);
                    if ((piece.QuarterTurns & 1) != 0) { int swap = width; width = height; height = swap; }
                    var binding = new PieceBinding { Piece = piece, X = (piece.CellX + width * .5d) * ship.CellSizeMeters,
                        Y = (piece.CellY + height * .5d) * ship.CellSizeMeters,
                        InertiaPerKg = (width * width + (double)height * height) * ship.CellSizeMeters * ship.CellSizeMeters / 12d };
                    binding.Changed = _ => { dirtyBases.Add(binding); dirtySources.Add(binding); };
                    Pieces.Add(binding);
                    if (int.TryParse(piece.PieceId, out int id)) { binding.Id = id; ById.Add(id, binding); }
                    UpdateData(binding, session.GetMassPieceData(piece, out _));
                    Reader.RegisterCachedStructuralItem(binding.Data);
                    dirtyBases.Add(binding); dirtySources.Add(binding);
                }
                roomSource = ship.Compartments;
                foreach (ShipCompartmentState room in roomSource)
                {
                    var geometry = new RoomGeometry { Room = room, Cells = room.Cells, Count = room.Cells.Count, Gas = room.Gas };
                    if (room.Cells.Count != 0)
                    {
                        foreach (ShipCell cell in room.Cells) { geometry.X += (cell.X + .5d) * ship.CellSizeMeters; geometry.Y += (cell.Y + .5d) * ship.CellSizeMeters; }
                        geometry.X /= room.Cells.Count; geometry.Y /= room.Cells.Count;
                        foreach (ShipCell cell in room.Cells)
                        {
                            double x = (cell.X + .5d) * ship.CellSizeMeters - geometry.X, y = (cell.Y + .5d) * ship.CellSizeMeters - geometry.Y;
                            geometry.InertiaPerKg += (x * x + y * y + ship.CellSizeMeters * ship.CellSizeMeters / 6d) / room.Cells.Count;
                        }
                    }
                    rooms.Add(room, geometry); dirtySources.Add(geometry);
                }
                StructureRevision = ship.StructureRevision; CellSize = ship.CellSizeMeters;
                PieceSource = ship.Pieces; PieceCount = ship.Pieces.Count; Initialized = true;
                resourceSource = GameRes.ExistingInstance; resourceRevision = resourceSource?.ResourceReloadVersion ?? 0;
            }
            private void OnRuntimeItemChanged(Item item)
            {
                if (item?.itemData != null && ById.TryGetValue(item.itemData.Guid, out PieceBinding binding))
                { dirtySources.Add(binding); Reader.InvalidateCachedSource(binding); }
            }
            private void OnMachineChanged(MachineEntity node)
            { if (node.ScopeKey == worldKey && ById.TryGetValue(node.Id, out PieceBinding binding)) dirtySources.Add(binding); }
            private void OnMachineRemoved(int id)
            { if (ById.TryGetValue(id, out PieceBinding binding)) dirtySources.Add(binding); }
            public void UpdateData(PieceBinding binding, ItemData data)
            {
                if (ReferenceEquals(binding.Data, data)) return;
                if (binding.Data != null) binding.Data.MassChanged -= binding.Changed;
                binding.Data = data;
                if (data != null) { _ = data.Stack; data.MassChanged += binding.Changed; Reader.RegisterCachedStructuralItem(data); }
                dirtyBases.Add(binding);
            }
            public void RefreshBases()
            {
                foreach (PieceBinding binding in dirtyBases)
                {
                    if (binding.Data?.Stack != null) binding.Piece.MassKg = Math.Max(0d, binding.Data.Stack.CurrentWeight);
                    Ledger.Set(binding.Piece, binding.Piece.MassKg, binding.X, binding.Y,
                        binding.Piece.MassKg * binding.InertiaPerKg);
                }
                dirtyBases.Clear();
            }
            public void RefreshStationaryMassSources(SpaceSession session, ShipState ship)
            {
                foreach (MachineWorld.FluidTankGroup group in fluidGroups)
                    if (Reader.IsFluidSourceReplaced(group, group.Inventory?.State)) dirtySources.Add(group);
                foreach (RoomGeometry room in rooms.Values)
                    if (!ReferenceEquals(room.Gas, room.Room.Gas)) { room.Gas = room.Room.Gas; dirtySources.Add(room); }
                sourceWork.Clear(); sourceWork.AddRange(dirtySources); dirtySources.Clear();
                // 共享储罐先登记实际来源，再读取部件库存，保持原有去重优先级。
                foreach (object source in sourceWork)
                {
                    if (source is not MachineWorld.FluidTankGroup group) continue;
                    double mass = Reader.ReadCachedFluid(group, group.Inventory?.State), remaining = mass;
                    for (int i = 0; i < group.Members.Count; i++)
                    {
                        MachineEntity member = group.Members[i];
                        PieceBinding binding = ById[member.Id];
                        double portion = i == group.Members.Count - 1 ? remaining : mass * member.Definition.Fluid.VolumeLiters / group.TotalVolume;
                        remaining -= portion;
                        session.AddCargoMass(ship, binding.Piece, "fluid-group:", portion, group.OwnerId);
                    }
                }
                foreach (object source in sourceWork)
                    if (source is PieceBinding binding)
                    {
                        ItemData data = session.GetMassPieceData(binding.Piece, out Item live);
                        UpdateData(binding, data);
                        double cargo = Reader.ReadCachedPiece(binding, data, live, binding.Id == 0 ? null : MachineWorld.GetById(binding.Id));
                        session.AddCargoMass(ship, binding.Piece, "cargo:", cargo);
                    }
                    else if (source is RoomGeometry room)
                    {
                        double mass = room.Count == 0 ? 0d : Reader.ReadCachedFluid(room, room.Gas, gasOnly: true);
                        Ledger.Set(room, mass, room.X, room.Y, mass * room.InertiaPerKg);
                    }
                sourceWork.Clear();
            }
            public void Dispose()
            {
                if (subscribed)
                {
                    Item.RuntimeStructureChanged -= OnRuntimeItemChanged;
                    ItemMgr.RuntimeItemRegistered -= OnRuntimeItemChanged;
                    ItemMgr.RuntimeItemUnregistered -= OnRuntimeItemChanged;
                    MachineWorld.NodeStateChanged -= OnMachineChanged;
                    MachineWorld.NodeRemoved -= OnMachineRemoved;
                    subscribed = false;
                }
                foreach (PieceBinding binding in Pieces) if (binding.Data != null) binding.Data.MassChanged -= binding.Changed;
                Pieces.Clear(); ById.Clear(); dirtyBases.Clear(); dirtySources.Clear(); sourceWork.Clear();
                fluidGroups.Clear(); rooms.Clear(); Reader.ClearCache(); Initialized = false;
            }
            #endregion
        }
    }
}
