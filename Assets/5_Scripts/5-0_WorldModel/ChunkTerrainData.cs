using System;
using System.Buffers;
using System.Collections.Generic;

namespace FlatWorld.WorldModel
{
    public sealed class ChunkTerrainBuffer : IDisposable
    {
        private TerrainCell[] _cells;
        private Dictionary<string, float[]> _environmentLayers;
        private Dictionary<int, int[]> _extendedTileStacks;
        private byte[] _grass;
        private bool _sealed;

        public ChunkTerrainBuffer(int width, int height)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height));

            Width = width;
            Height = height;
            CellCount = checked(width * height);
            _cells = ArrayPool<TerrainCell>.Shared.Rent(CellCount);
            Array.Clear(_cells, 0, CellCount);
            _environmentLayers = new Dictionary<string, float[]>(StringComparer.Ordinal);
            _extendedTileStacks = new Dictionary<int, int[]>();
            _grass = ArrayPool<byte>.Shared.Rent(CellCount);
            Array.Clear(_grass, 0, CellCount);
        }

        public int Width { get; }
        public int Height { get; }
        public int CellCount { get; }
        public bool IsDisposed => _cells == null;

        public void SetCell(int x, int y, TerrainCell value)
        {
            ThrowIfUnavailable();
            _cells[GetIndex(x, y)] = value;
        }

        public TerrainCell GetCell(int x, int y)
        {
            ThrowIfUnavailable();
            return _cells[GetIndex(x, y)];
        }

        public void SetEnvironmentValue(string layerId, int x, int y, float value)
        {
            ThrowIfUnavailable();
            if (string.IsNullOrWhiteSpace(layerId))
                throw new ArgumentException("Environment layer id is required.", nameof(layerId));

            if (!_environmentLayers.TryGetValue(layerId, out float[] values))
            {
                values = ArrayPool<float>.Shared.Rent(CellCount);
                Array.Clear(values, 0, CellCount);
                _environmentLayers.Add(layerId, values);
            }

            values[GetIndex(x, y)] = value;
        }

        public void SetGrass(int x, int y, byte value)
        {
            ThrowIfUnavailable();
            _grass[GetIndex(x, y)] = value;
        }

        public void SetExtendedTileStack(int x, int y, IReadOnlyList<int> tileIds)
        {
            ThrowIfUnavailable();
            if (tileIds == null)
                throw new ArgumentNullException(nameof(tileIds));
            int index = GetIndex(x, y);
            if (tileIds.Count <= 3)
            {
                _extendedTileStacks.Remove(index);
                return;
            }
            var copy = new int[tileIds.Count];
            for (int i = 0; i < tileIds.Count; i++)
                copy[i] = tileIds[i];
            _extendedTileStacks[index] = copy;
        }

        public ChunkTerrainData Seal()
        {
            ThrowIfUnavailable();
            _sealed = true;
            var result = new ChunkTerrainData(Width, Height, _cells, _environmentLayers, _grass,
                _extendedTileStacks);
            _cells = null;
            _environmentLayers = null;
            _grass = null;
            _extendedTileStacks = null;
            return result;
        }

        public void Dispose()
        {
            if (_sealed)
                return;

            if (_cells != null)
            {
                Array.Clear(_cells, 0, Math.Min(CellCount, _cells.Length));
                ArrayPool<TerrainCell>.Shared.Return(_cells);
                _cells = null;
            }

            if (_environmentLayers == null)
            {
                ReturnGrass();
                return;
            }

            foreach (float[] values in _environmentLayers.Values)
            {
                Array.Clear(values, 0, Math.Min(CellCount, values.Length));
                ArrayPool<float>.Shared.Return(values);
            }
            _environmentLayers.Clear();
            _environmentLayers = null;
            _extendedTileStacks?.Clear();
            _extendedTileStacks = null;
            ReturnGrass();
        }

        private int GetIndex(int x, int y)
        {
            if ((uint)x >= (uint)Width)
                throw new ArgumentOutOfRangeException(nameof(x));
            if ((uint)y >= (uint)Height)
                throw new ArgumentOutOfRangeException(nameof(y));
            return y * Width + x;
        }

        private void ThrowIfUnavailable()
        {
            if (_sealed)
                throw new InvalidOperationException("Terrain buffer ownership was transferred.");
            if (_cells == null)
                throw new ObjectDisposedException(nameof(ChunkTerrainBuffer));
        }

        private void ReturnGrass()
        {
            if (_grass == null)
                return;
            Array.Clear(_grass, 0, Math.Min(CellCount, _grass.Length));
            ArrayPool<byte>.Shared.Return(_grass);
            _grass = null;
        }
    }

    public sealed class ChunkTerrainData : IDisposable
    {
        private TerrainCell[] _cells;
        private Dictionary<string, float[]> _environmentLayers;
        private Dictionary<int, int[]> _extendedTileStacks;
        private byte[] _grass;
        private long _revision;

        internal ChunkTerrainData(int width, int height, TerrainCell[] cells,
            Dictionary<string, float[]> environmentLayers, byte[] grass = null,
            Dictionary<int, int[]> extendedTileStacks = null)
        {
            Width = width;
            Height = height;
            CellCount = checked(width * height);
            _cells = cells ?? throw new ArgumentNullException(nameof(cells));
            _environmentLayers = environmentLayers ??
                new Dictionary<string, float[]>(StringComparer.Ordinal);
            _extendedTileStacks = extendedTileStacks ?? new Dictionary<int, int[]>();
            _grass = grass ?? ArrayPool<byte>.Shared.Rent(CellCount);
            if (grass == null)
                Array.Clear(_grass, 0, CellCount);
        }

        public int Width { get; }
        public int Height { get; }
        public int CellCount { get; }
        public bool IsDisposed => _cells == null;
        public long Revision => _revision;
        public IEnumerable<string> EnvironmentLayerIds =>
            _environmentLayers == null
                ? (IEnumerable<string>)Array.Empty<string>()
                : _environmentLayers.Keys;

        public event Action<ChunkTerrainChanged> Changed;

        public TerrainCell GetCell(int x, int y)
        {
            ThrowIfDisposed();
            return _cells[GetIndex(x, y)];
        }

        public void SetCell(int x, int y, TerrainCell value)
        {
            ThrowIfDisposed();
            int index = GetIndex(x, y);
            if (_cells[index].Equals(value))
                return;
            _cells[index] = value;
            MarkChanged(x, y, TerrainChangeKind.Cell);
        }

        public bool IsWalkable(int x, int y)
        {
            TerrainCell cell = GetCell(x, y);
            return (cell.Flags & TerrainCellFlags.Walkable) != 0 &&
                   (cell.Flags & (TerrainCellFlags.Blocking | TerrainCellFlags.Occupied)) == 0;
        }

        public int GetTileLayerCount(int x, int y)
        {
            ThrowIfDisposed();
            int index = GetIndex(x, y);
            if (_extendedTileStacks.TryGetValue(index, out int[] stack))
                return stack.Length;

            TerrainCell cell = _cells[index];
            int count = cell.GroundTileId != 0 ? 1 : 0;
            if (cell.BackTileId != 0)
                count++;
            if (cell.BlockingTileId != 0)
                count++;
            return count;
        }

        public int GetTileIdAt(int x, int y, int layerIndex)
        {
            ThrowIfDisposed();
            int index = GetIndex(x, y);
            if (_extendedTileStacks.TryGetValue(index, out int[] stack))
            {
                if ((uint)layerIndex >= (uint)stack.Length)
                    throw new ArgumentOutOfRangeException(nameof(layerIndex));
                return stack[layerIndex];
            }

            TerrainCell cell = _cells[index];
            int current = 0;
            if (cell.GroundTileId != 0)
            {
                if (current++ == layerIndex)
                    return cell.GroundTileId;
            }
            if (cell.BackTileId != 0)
            {
                if (current++ == layerIndex)
                    return cell.BackTileId;
            }
            if (cell.BlockingTileId != 0 && current == layerIndex)
                return cell.BlockingTileId;
            throw new ArgumentOutOfRangeException(nameof(layerIndex));
        }

        public int GetTopTileId(int x, int y)
        {
            int count = GetTileLayerCount(x, y);
            return count == 0 ? 0 : GetTileIdAt(x, y, count - 1);
        }

        public void ReplaceTileStack(int x, int y, IReadOnlyList<int> tileIds)
        {
            ThrowIfDisposed();
            if (tileIds == null)
                throw new ArgumentNullException(nameof(tileIds));

            int index = GetIndex(x, y);
            var compact = new List<int>(tileIds.Count);
            for (int i = 0; i < tileIds.Count; i++)
            {
                if (tileIds[i] != 0)
                    compact.Add(tileIds[i]);
            }

            if (compact.Count > 3)
                _extendedTileStacks[index] = compact.ToArray();
            else
                _extendedTileStacks.Remove(index);

            TerrainCell previous = _cells[index];
            int ground = compact.Count > 0 ? compact[0] : 0;
            int back = compact.Count > 1 ? compact[1] : 0;
            int blocking = compact.Count > 2 ? compact[compact.Count - 1] : 0;
            TerrainCellFlags flags = previous.Flags;
            flags = blocking == 0
                ? flags & ~TerrainCellFlags.Blocking
                : flags | TerrainCellFlags.Blocking;
            _cells[index] = new TerrainCell(ground, back, blocking, previous.BiomeId,
                previous.NavigationCost, flags);
            MarkChanged(x, y, TerrainChangeKind.TileStack);
        }

        public byte GetGrass(int x, int y)
        {
            ThrowIfDisposed();
            return _grass[GetIndex(x, y)];
        }

        public void SetGrass(int x, int y, byte value)
        {
            ThrowIfDisposed();
            int index = GetIndex(x, y);
            if (_grass[index] == value)
                return;
            _grass[index] = value;
            MarkChanged(x, y, TerrainChangeKind.Grass);
        }

        public byte[] CopyGrass()
        {
            ThrowIfDisposed();
            var copy = new byte[CellCount];
            Array.Copy(_grass, copy, CellCount);
            return copy;
        }

        public IReadOnlyDictionary<int, int[]> CopyExtendedTileStacks()
        {
            ThrowIfDisposed();
            var copy = new Dictionary<int, int[]>(_extendedTileStacks.Count);
            foreach (KeyValuePair<int, int[]> pair in _extendedTileStacks)
                copy.Add(pair.Key, (int[])pair.Value.Clone());
            return copy;
        }

        public bool TryGetEnvironmentValue(string layerId, int x, int y, out float value)
        {
            ThrowIfDisposed();
            if (_environmentLayers.TryGetValue(layerId, out float[] values))
            {
                value = values[GetIndex(x, y)];
                return true;
            }

            value = default;
            return false;
        }

        public void SetEnvironmentValue(string layerId, int x, int y, float value)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(layerId))
                throw new ArgumentException("Environment layer id is required.", nameof(layerId));
            int index = GetIndex(x, y);
            if (!_environmentLayers.TryGetValue(layerId, out float[] values))
            {
                values = ArrayPool<float>.Shared.Rent(CellCount);
                Array.Clear(values, 0, CellCount);
                _environmentLayers.Add(layerId, values);
            }
            if (values[index].Equals(value))
                return;
            values[index] = value;
            MarkChanged(x, y, TerrainChangeKind.Environment);
        }

        public TerrainCell[] CopyCells()
        {
            ThrowIfDisposed();
            var copy = new TerrainCell[CellCount];
            Array.Copy(_cells, copy, CellCount);
            return copy;
        }

        public bool TryCopyEnvironmentLayer(string layerId, out float[] copy)
        {
            ThrowIfDisposed();
            if (!_environmentLayers.TryGetValue(layerId, out float[] values))
            {
                copy = null;
                return false;
            }

            copy = new float[CellCount];
            Array.Copy(values, copy, CellCount);
            return true;
        }

        public ulong ComputeStableHash()
        {
            ThrowIfDisposed();
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offset;

            Hash(ref hash, Width, prime);
            Hash(ref hash, Height, prime);
            for (int i = 0; i < CellCount; i++)
            {
                TerrainCell cell = _cells[i];
                Hash(ref hash, cell.GroundTileId, prime);
                Hash(ref hash, cell.BackTileId, prime);
                Hash(ref hash, cell.BlockingTileId, prime);
                Hash(ref hash, cell.BiomeId, prime);
                Hash(ref hash, cell.NavigationCost, prime);
                Hash(ref hash, (byte)cell.Flags, prime);
                Hash(ref hash, _grass[i], prime);
                if (_extendedTileStacks.TryGetValue(i, out int[] stack))
                {
                    Hash(ref hash, stack.Length, prime);
                    for (int stackIndex = 0; stackIndex < stack.Length; stackIndex++)
                        Hash(ref hash, stack[stackIndex], prime);
                }
                else
                {
                    Hash(ref hash, 0, prime);
                }
            }

            var layerIds = new List<string>(_environmentLayers.Keys);
            layerIds.Sort(StringComparer.Ordinal);
            for (int layerIndex = 0; layerIndex < layerIds.Count; layerIndex++)
            {
                string layerId = layerIds[layerIndex];
                for (int charIndex = 0; charIndex < layerId.Length; charIndex++)
                    Hash(ref hash, layerId[charIndex], prime);

                float[] values = _environmentLayers[layerId];
                for (int valueIndex = 0; valueIndex < CellCount; valueIndex++)
                    Hash(ref hash, BitConverter.SingleToInt32Bits(values[valueIndex]), prime);
            }

            return hash;
        }

        public void Dispose()
        {
            if (_cells != null)
            {
                Array.Clear(_cells, 0, Math.Min(CellCount, _cells.Length));
                ArrayPool<TerrainCell>.Shared.Return(_cells);
                _cells = null;
            }

            if (_environmentLayers == null)
                return;

            foreach (float[] values in _environmentLayers.Values)
            {
                Array.Clear(values, 0, Math.Min(CellCount, values.Length));
                ArrayPool<float>.Shared.Return(values);
            }
            _environmentLayers.Clear();
            _environmentLayers = null;
            _extendedTileStacks?.Clear();
            _extendedTileStacks = null;
            if (_grass != null)
            {
                Array.Clear(_grass, 0, Math.Min(CellCount, _grass.Length));
                ArrayPool<byte>.Shared.Return(_grass);
                _grass = null;
            }
            Changed = null;
        }

        private int GetIndex(int x, int y)
        {
            if ((uint)x >= (uint)Width)
                throw new ArgumentOutOfRangeException(nameof(x));
            if ((uint)y >= (uint)Height)
                throw new ArgumentOutOfRangeException(nameof(y));
            return y * Width + x;
        }

        private void ThrowIfDisposed()
        {
            if (_cells == null)
                throw new ObjectDisposedException(nameof(ChunkTerrainData));
        }

        private void MarkChanged(int x, int y, TerrainChangeKind kind)
        {
            _revision++;
            Changed?.Invoke(new ChunkTerrainChanged(new Int2(x, y), kind, _revision));
        }

        private static void Hash(ref ulong hash, int value, ulong prime)
        {
            unchecked
            {
                hash ^= (byte)value;
                hash *= prime;
                hash ^= (byte)(value >> 8);
                hash *= prime;
                hash ^= (byte)(value >> 16);
                hash *= prime;
                hash ^= (byte)(value >> 24);
                hash *= prime;
            }
        }
    }

    public enum TerrainChangeKind
    {
        Cell,
        TileStack,
        Grass,
        Environment,
        Occupancy
    }

    public readonly struct ChunkTerrainChanged
    {
        public ChunkTerrainChanged(Int2 localCell, TerrainChangeKind kind, long revision)
        {
            LocalCell = localCell;
            Kind = kind;
            Revision = revision;
        }

        public Int2 LocalCell { get; }
        public TerrainChangeKind Kind { get; }
        public long Revision { get; }
    }
}
