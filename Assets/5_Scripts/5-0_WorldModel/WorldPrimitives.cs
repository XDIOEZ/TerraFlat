using System;

namespace FlatWorld.WorldModel
{
    public readonly struct Int2 : IEquatable<Int2>
    {
        public Int2(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }
        public int Y { get; }

        public bool Equals(Int2 other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is Int2 other && Equals(other);
        public override int GetHashCode() => unchecked((X * 397) ^ Y);
        public override string ToString() => $"({X}, {Y})";
        public static bool operator ==(Int2 left, Int2 right) => left.Equals(right);
        public static bool operator !=(Int2 left, Int2 right) => !left.Equals(right);
        public static Int2 operator +(Int2 left, Int2 right) => new Int2(left.X + right.X, left.Y + right.Y);
        public static Int2 operator -(Int2 left, Int2 right) => new Int2(left.X - right.X, left.Y - right.Y);
    }

    public readonly struct WorldAddress : IEquatable<WorldAddress>, IComparable<WorldAddress>
    {
        public WorldAddress(string dimensionId, Int2 chunkOrigin)
        {
            if (string.IsNullOrWhiteSpace(dimensionId))
                throw new ArgumentException("Dimension id is required.", nameof(dimensionId));

            DimensionId = dimensionId;
            ChunkOrigin = chunkOrigin;
        }

        public string DimensionId { get; }
        public Int2 ChunkOrigin { get; }

        public bool Equals(WorldAddress other) =>
            StringComparer.Ordinal.Equals(DimensionId, other.DimensionId) && ChunkOrigin.Equals(other.ChunkOrigin);

        public override bool Equals(object obj) => obj is WorldAddress other && Equals(other);

        public override int GetHashCode() => unchecked(
            (StringComparer.Ordinal.GetHashCode(DimensionId ?? string.Empty) * 397) ^ ChunkOrigin.GetHashCode());

        public int CompareTo(WorldAddress other)
        {
            int dimension = string.Compare(DimensionId, other.DimensionId, StringComparison.Ordinal);
            if (dimension != 0)
                return dimension;

            int x = ChunkOrigin.X.CompareTo(other.ChunkOrigin.X);
            return x != 0 ? x : ChunkOrigin.Y.CompareTo(other.ChunkOrigin.Y);
        }

        public override string ToString() => $"{DimensionId}:{ChunkOrigin.X},{ChunkOrigin.Y}";
        public static bool operator ==(WorldAddress left, WorldAddress right) => left.Equals(right);
        public static bool operator !=(WorldAddress left, WorldAddress right) => !left.Equals(right);
    }

    public enum ChunkDataStatus
    {
        Absent,
        Requested,
        Generating,
        Ready,
        Failed,
        Evicting
    }

    public enum ChunkSimulationStatus
    {
        Dormant,
        Active
    }

    public enum ChunkPresentationStatus
    {
        Unbound,
        Binding,
        Bound
    }

    public enum ChunkLeaseKind
    {
        Simulation,
        Presentation,
        Navigation
    }

    [Flags]
    public enum TerrainCellFlags : byte
    {
        None = 0,
        Walkable = 1 << 0,
        Blocking = 1 << 1,
        Water = 1 << 2,
        Occupied = 1 << 3
    }

    public readonly struct TerrainCell : IEquatable<TerrainCell>
    {
        public TerrainCell(int groundTileId, int backTileId, int blockingTileId, int biomeId,
            short navigationCost, TerrainCellFlags flags)
        {
            GroundTileId = groundTileId;
            BackTileId = backTileId;
            BlockingTileId = blockingTileId;
            BiomeId = biomeId;
            NavigationCost = navigationCost;
            Flags = flags;
        }

        public int GroundTileId { get; }
        public int BackTileId { get; }
        public int BlockingTileId { get; }
        public int BiomeId { get; }
        public short NavigationCost { get; }
        public TerrainCellFlags Flags { get; }

        public bool Equals(TerrainCell other) =>
            GroundTileId == other.GroundTileId && BackTileId == other.BackTileId &&
            BlockingTileId == other.BlockingTileId && BiomeId == other.BiomeId &&
            NavigationCost == other.NavigationCost && Flags == other.Flags;

        public override bool Equals(object obj) => obj is TerrainCell other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = GroundTileId;
                hash = (hash * 397) ^ BackTileId;
                hash = (hash * 397) ^ BlockingTileId;
                hash = (hash * 397) ^ BiomeId;
                hash = (hash * 397) ^ NavigationCost;
                hash = (hash * 397) ^ (byte)Flags;
                return hash;
            }
        }
    }
}
