using System;

namespace FlatWorld.WorldModel
{
    /// <summary>
    /// 一个只保存 X、Y 两个整数的小坐标类型。
    /// 地图格子、区块起点和相对位置都会用到它。自己定义这个类型后，这部分代码就不用依赖 Unity。
    /// </summary>
    public readonly struct Int2 : IEquatable<Int2>
    {
        public Int2(int x, int y)
        {
            X = x;
            Y = y;
        }

        /// <summary>左右方向的位置。</summary>
        public int X { get; }
        /// <summary>上下方向的位置。</summary>
        public int Y { get; }

        // 只要 X 和 Y 都相同，就把两个坐标看成同一个位置。这样它也能用作字典的键。
        public bool Equals(Int2 other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is Int2 other && Equals(other);
        public override int GetHashCode() => unchecked((X * 397) ^ Y);
        public override string ToString() => $"({X}, {Y})";
        public static bool operator ==(Int2 left, Int2 right) => left.Equals(right);
        public static bool operator !=(Int2 left, Int2 right) => !left.Equals(right);
        public static Int2 operator +(Int2 left, Int2 right) => new Int2(left.X + right.X, left.Y + right.Y);
        public static Int2 operator -(Int2 left, Int2 right) => new Int2(left.X - right.X, left.Y - right.Y);
    }

    /// <summary>
    /// 一个区块在整个世界里的“门牌号”。
    /// 它由“属于哪个世界层（例如地表或矿洞）”和“区块从哪个格子开始”两部分组成。
    /// </summary>
    public readonly struct WorldAddress : IEquatable<WorldAddress>, IComparable<WorldAddress>
    {
        public WorldAddress(string dimensionId, Int2 chunkOrigin)
        {
            if (string.IsNullOrWhiteSpace(dimensionId))
                throw new ArgumentException("Dimension id is required.", nameof(dimensionId));

            DimensionId = dimensionId;
            ChunkOrigin = chunkOrigin;
        }

        /// <summary>世界层的名字，例如 surface（地表）或某个矿洞的 ID。</summary>
        public string DimensionId { get; }
        /// <summary>这个区块起始格子的世界坐标。</summary>
        public Int2 ChunkOrigin { get; }

        public bool Equals(WorldAddress other) =>
            StringComparer.Ordinal.Equals(DimensionId, other.DimensionId) && ChunkOrigin.Equals(other.ChunkOrigin);

        public override bool Equals(object obj) => obj is WorldAddress other && Equals(other);

        public override int GetHashCode() => unchecked(
            (StringComparer.Ordinal.GetHashCode(DimensionId ?? string.Empty) * 397) ^ ChunkOrigin.GetHashCode());

        public int CompareTo(WorldAddress other)
        {
            // 排序时先比世界层，再比 X，最后比 Y。固定顺序后，存档和测试结果才不会忽前忽后。
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

    /// <summary>区块数据从“还没有”到“生成完成”再到“被删除”的各个阶段。</summary>
    public enum ChunkDataStatus
    {
        /// <summary>还没有数据，或者数据已经被移除。</summary>
        Absent,
        /// <summary>已经说“我要这个区块了”，但还没真正开始生成。</summary>
        Requested,
        /// <summary>正在计算这个区块的地形。</summary>
        Generating,
        /// <summary>地形已经准备好，可以拿来运行游戏和显示画面。</summary>
        Ready,
        /// <summary>生成失败，这个区块暂时不能使用。</summary>
        Failed,
        /// <summary>正在删除这个区块，不再接受新的使用请求。</summary>
        Evicting
    }

    /// <summary>这个区块里的游戏逻辑现在要不要继续运行。</summary>
    public enum ChunkSimulationStatus
    {
        Dormant,
        Active
    }

    /// <summary>区块数据有没有连接到 Unity 里的画面对象。</summary>
    public enum ChunkPresentationStatus
    {
        Unbound,
        Binding,
        Bound
    }

    /// <summary>
    /// 别的系统为什么要继续使用这个区块。
    /// 可以把“Lease（租约）”理解成一张使用票：票还没全部退掉，区块就不能删除。
    /// </summary>
    public enum ChunkLeaseKind
    {
        Simulation,
        Presentation,
        Navigation
    }

    [Flags]
    /// <summary>一个地图格子可以同时拥有的几个简单标记。</summary>
    public enum TerrainCellFlags : byte
    {
        None = 0,
        /// <summary>角色可以从这里走过。</summary>
        Walkable = 1 << 0,
        /// <summary>这里有墙、岩石等固定障碍。</summary>
        Blocking = 1 << 1,
        /// <summary>这里是水。</summary>
        Water = 1 << 2,
        /// <summary>这里已经被建筑或其他物体占用。</summary>
        Occupied = 1 << 3
    }

    /// <summary>
    /// 一个地图格子的核心数据。
    /// 这里只记数字编号，不直接保存图片或 Unity 对象；上层代码会根据编号找到真正的地块资源。
    /// </summary>
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

        /// <summary>最下面那层地面的编号。</summary>
        public int GroundTileId { get; }
        /// <summary>第二层地块的编号；0 表示这一层没有东西。</summary>
        public int BackTileId { get; }
        /// <summary>墙或岩石这类固定障碍的编号；0 表示没有障碍。</summary>
        public int BlockingTileId { get; }
        /// <summary>这里属于哪种地区，例如海洋、沙滩或森林。</summary>
        public int BiomeId { get; }
        /// <summary>角色走进这个格子有多费劲；数值越大，寻路越不愿意走这里。</summary>
        public short NavigationCost { get; }
        /// <summary>这个格子的“可走、水、障碍、已占用”等标记。</summary>
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
