using System;
using System.Collections.Generic;

namespace FlatWorld.WorldModel
{
    /// <summary>
    /// 一个区块在某一刻的完整副本，可以把它理解成“给区块拍了一张照片”。
    /// 拍完以后，原区块再怎么变化都不会影响这份副本，所以它适合存档、联机和测试。
    /// </summary>
    public sealed class ChunkRuntimeSnapshot
    {
        // 数组和字典都会重新复制一份，外面也只能查看，不能改掉这张“照片”。
        private readonly TerrainCell[] terrainCells;
        private readonly Dictionary<string, float[]> environmentLayers;
        private readonly byte[] grass;
        private readonly IReadOnlyDictionary<int, int[]> extendedTileStacks;
        private readonly IReadOnlyDictionary<Int2, int> occupancy;

        private ChunkRuntimeSnapshot(WorldAddress address, int width, int height,
            TerrainCell[] terrainCells, Dictionary<string, float[]> environmentLayers,
            byte[] grass, IReadOnlyDictionary<int, int[]> extendedTileStacks,
            IReadOnlyDictionary<Int2, int> occupancy, ulong stableHash)
        {
            Address = address;
            Width = width;
            Height = height;
            this.terrainCells = terrainCells;
            this.environmentLayers = environmentLayers;
            this.grass = grass ?? Array.Empty<byte>();
            this.extendedTileStacks = extendedTileStacks ?? new Dictionary<int, int[]>();
            this.occupancy = occupancy ?? new Dictionary<Int2, int>();
            StableHash = stableHash;
        }

        /// <summary>照片里的区块位于哪里。</summary>
        public WorldAddress Address { get; }
        /// <summary>地图有多少列格子。</summary>
        public int Width { get; }
        /// <summary>地图有多少行格子。</summary>
        public int Height { get; }
        /// <summary>所有地形格子的副本；二维地图被按行排成了一个长列表。</summary>
        public IReadOnlyList<TerrainCell> TerrainCells => terrainCells;
        /// <summary>温度、降水、高度等环境数据的副本。</summary>
        public IReadOnlyDictionary<string, float[]> EnvironmentLayers => environmentLayers;
        /// <summary>每个格子的草地数据。</summary>
        public IReadOnlyList<byte> Grass => grass;
        /// <summary>一个格子里地块层数很多时，这里保存完整的上下叠放顺序。</summary>
        public IReadOnlyDictionary<int, int[]> ExtendedTileStacks => extendedTileStacks;
        /// <summary>哪些格子被哪些物品占用的副本。</summary>
        public IReadOnlyDictionary<Int2, int> Occupancy => occupancy;
        /// <summary>地形内容的“指纹”；内容相同，通常就会得到相同数字。</summary>
        public ulong StableHash { get; }

        /// <summary>把一个已经准备好的区块完整复制出来。</summary>
        internal static ChunkRuntimeSnapshot Capture(ChunkRuntime chunk)
        {
            ChunkTerrainData terrain = chunk.Terrain;
            var layers = new Dictionary<string, float[]>(StringComparer.Ordinal);
            foreach (string layerId in terrain.EnvironmentLayerIds)
            {
                if (terrain.TryCopyEnvironmentLayer(layerId, out float[] values))
                    layers.Add(layerId, values);
            }
            var occupied = new Dictionary<Int2, int>();
            foreach (KeyValuePair<Int2, int> pair in chunk.Occupancy.Owners)
                occupied.Add(pair.Key, pair.Value);
            return new ChunkRuntimeSnapshot(chunk.Address, terrain.Width, terrain.Height,
                terrain.CopyCells(), layers, terrain.CopyGrass(), terrain.CopyExtendedTileStacks(),
                occupied, terrain.ComputeStableHash());
        }
    }

    /// <summary>整个世界在某一刻的照片，里面装着所有已经准备好的区块副本。</summary>
    public sealed class WorldRuntimeSnapshot
    {
        private readonly ChunkRuntimeSnapshot[] chunks;

        public WorldRuntimeSnapshot(string worldId, long epoch,
            IEnumerable<ChunkRuntimeSnapshot> chunks)
        {
            WorldId = worldId;
            Epoch = epoch;
            this.chunks = chunks == null
                ? Array.Empty<ChunkRuntimeSnapshot>()
                : new List<ChunkRuntimeSnapshot>(chunks).ToArray();
        }

        /// <summary>这张照片是哪个世界的。</summary>
        public string WorldId { get; }
        /// <summary>拍照时的世界版本号，用来判断照片是不是来自上一次进入的旧世界。</summary>
        public long Epoch { get; }
        /// <summary>照片里包含的所有区块。</summary>
        public IReadOnlyList<ChunkRuntimeSnapshot> Chunks => chunks;
    }
}
