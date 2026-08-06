using System;
using System.Collections.Generic;

namespace FlatWorld.WorldModel
{
    public sealed class ChunkRuntimeSnapshot
    {
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

        public WorldAddress Address { get; }
        public int Width { get; }
        public int Height { get; }
        public IReadOnlyList<TerrainCell> TerrainCells => terrainCells;
        public IReadOnlyDictionary<string, float[]> EnvironmentLayers => environmentLayers;
        public IReadOnlyList<byte> Grass => grass;
        public IReadOnlyDictionary<int, int[]> ExtendedTileStacks => extendedTileStacks;
        public IReadOnlyDictionary<Int2, int> Occupancy => occupancy;
        public ulong StableHash { get; }

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

        public string WorldId { get; }
        public long Epoch { get; }
        public IReadOnlyList<ChunkRuntimeSnapshot> Chunks => chunks;
    }
}
