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
                public List<ShipCell> Cells;
                public int Count;
                public double X, Y, InertiaPerKg;
            }
            public readonly ShipMassReader Reader = new();
            public readonly ShipMassLedger Ledger = new();
            public readonly Dictionary<int, PieceBinding> ById = new();
            public readonly List<PieceBinding> Pieces = new();
            public readonly HashSet<string> AssignedPassengers = new(StringComparer.Ordinal);
            private GameRes resourceSource;
            private int resourceRevision;
            private readonly HashSet<PieceBinding> dirtyBases = new();
            private readonly Dictionary<ShipCompartmentState, RoomGeometry> rooms = new();
            public ulong StructureRevision;
            public double CellSize;
            public List<ShipPieceState> PieceSource;
            public int PieceCount;
            public bool Initialized;
            public object FluidTopology;

            public bool Matches(ShipState ship) => Initialized && StructureRevision == ship.StructureRevision && CellSize == ship.CellSizeMeters &&
                ReferenceEquals(PieceSource, ship.Pieces) && PieceCount == ship.Pieces.Count &&
                ReferenceEquals(resourceSource, GameRes.ExistingInstance) && resourceRevision == (GameRes.ExistingInstance?.ResourceReloadVersion ?? 0);
            public void Rebuild(SpaceSession session, ShipState ship)
            {
                Dispose(); Ledger.Clear(); rooms.Clear();
                foreach (ShipPieceState piece in ship.Pieces)
                {
                    if (piece == null || !piece.IsAlive) continue;
                    int width = Math.Max(1, piece.Width), height = Math.Max(1, piece.Height);
                    if ((piece.QuarterTurns & 1) != 0) { int swap = width; width = height; height = swap; }
                    var binding = new PieceBinding { Piece = piece, X = (piece.CellX + width * .5d) * ship.CellSizeMeters,
                        Y = (piece.CellY + height * .5d) * ship.CellSizeMeters,
                        InertiaPerKg = (width * width + (double)height * height) * ship.CellSizeMeters * ship.CellSizeMeters / 12d };
                    binding.Changed = _ => dirtyBases.Add(binding);
                    Pieces.Add(binding);
                    if (int.TryParse(piece.PieceId, out int id)) { binding.Id = id; ById.Add(id, binding); }
                    UpdateData(binding, session.GetMassPieceData(piece, out _));
                    Reader.RegisterCachedStructuralItem(binding.Data);
                    dirtyBases.Add(binding);
                }
                StructureRevision = ship.StructureRevision; CellSize = ship.CellSizeMeters;
                PieceSource = ship.Pieces; PieceCount = ship.Pieces.Count; Initialized = true;
                resourceSource = GameRes.ExistingInstance; resourceRevision = resourceSource?.ResourceReloadVersion ?? 0;
            }
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
                        binding.Piece.MassKg * binding.InertiaPerKg, persistent: true);
                }
                dirtyBases.Clear();
            }
            public void RefreshRooms(ShipState ship)
            {
                foreach (ShipCompartmentState room in ship.Compartments)
                {
                    if (room.Cells.Count == 0) continue;
                    if (!rooms.TryGetValue(room, out RoomGeometry geometry) || !ReferenceEquals(geometry.Cells, room.Cells) || geometry.Count != room.Cells.Count)
                    {
                        geometry = new RoomGeometry { Cells = room.Cells, Count = room.Cells.Count };
                        foreach (ShipCell cell in room.Cells) { geometry.X += (cell.X + .5d) * ship.CellSizeMeters; geometry.Y += (cell.Y + .5d) * ship.CellSizeMeters; }
                        geometry.X /= room.Cells.Count; geometry.Y /= room.Cells.Count;
                        foreach (ShipCell cell in room.Cells)
                        {
                            double x = (cell.X + .5d) * ship.CellSizeMeters - geometry.X, y = (cell.Y + .5d) * ship.CellSizeMeters - geometry.Y;
                            geometry.InertiaPerKg += (x * x + y * y + ship.CellSizeMeters * ship.CellSizeMeters / 6d) / room.Cells.Count;
                        }
                        rooms[room] = geometry;
                    }
                    double mass = Reader.ReadCachedFluid(room, room.Gas, gasOnly: true);
                    Ledger.Set(room, mass, geometry.X, geometry.Y, mass * geometry.InertiaPerKg);
                }
            }
            public void Dispose()
            {
                foreach (PieceBinding binding in Pieces) if (binding.Data != null) binding.Data.MassChanged -= binding.Changed;
                Pieces.Clear(); ById.Clear(); dirtyBases.Clear(); Reader.ClearCache(); Initialized = false;
            }
            #endregion
        }
    }
}
