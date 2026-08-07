using System;
using System.Buffers;
using System.Collections.Generic;

namespace FlatWorld.WorldModel
{
    /// <summary>
    /// 生成区块时使用的临时“草稿纸”。
    /// 地形先写在这里。写完调用 Seal 后，里面的数据就交给正式地形对象，本草稿不能再使用。
    /// 大数组会重复利用，避免每生成一个区块都重新申请很多内存。
    /// </summary>
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

        /// <summary>地图有多少列格子。</summary>
        public int Width { get; }
        /// <summary>地图有多少行格子。</summary>
        public int Height { get; }
        /// <summary>格子总数，也就是宽乘以高。</summary>
        public int CellCount { get; }
        /// <summary>这张草稿是否已经交出去或被释放。</summary>
        public bool IsDisposed => _cells == null;

        /// <summary>设置区块里某个格子的地形。</summary>
        public void SetCell(int x, int y, TerrainCell value)
        {
            ThrowIfUnavailable();
            _cells[GetIndex(x, y)] = value;
        }

        /// <summary>读取草稿里的某个格子，后面的生成步骤可以在前一步结果上继续修改。</summary>
        public TerrainCell GetCell(int x, int y)
        {
            ThrowIfUnavailable();
            return _cells[GetIndex(x, y)];
        }

        /// <summary>
        /// 写入某种环境数据，例如高度、温度或降水。
        /// 第一次写某种数据时，才会为整个区块准备对应数组，没用到的环境数据不会白占内存。
        /// </summary>
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

        /// <summary>设置某个格子的草地状态。</summary>
        public void SetGrass(int x, int y, byte value)
        {
            ThrowIfUnavailable();
            _grass[GetIndex(x, y)] = value;
        }

        /// <summary>
        /// 保存一个格子里从下到上叠放的所有地块。
        /// 普通格子最多三层，直接存在 TerrainCell 里；只有超过三层时才额外保存完整列表。
        /// </summary>
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

        /// <summary>
        /// 草稿写完后，把全部数据交给正式的 ChunkTerrainData。
        /// 只能调用一次，交出去后这张草稿就不能再改。
        /// </summary>
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

        /// <summary>
        /// 丢弃还没交出去的草稿，并把临时数组清干净后留给以后重复使用。
        /// 如果数据已经通过 Seal 交出去了，这里就不再处理它。
        /// </summary>
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

            // 重复利用的数组可能比当前区块更大，只清理这次真正用过的部分即可。
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
            // 这种写法可以同时检查“小于 0”和“超过地图边界”。
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

    /// <summary>
    /// 一个已经生成完成、游戏正在正式使用的区块地形。
    /// 它保存格子、草地和温度等数据，也允许游戏过程中改动单个格子。
    /// 数据真的变化时会把版本号加 1，并通知画面、寻路等系统及时刷新。
    /// </summary>
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

        /// <summary>地图有多少列格子。</summary>
        public int Width { get; }
        /// <summary>地图有多少行格子。</summary>
        public int Height { get; }
        /// <summary>格子总数。</summary>
        public int CellCount { get; }
        /// <summary>这份地形是否已经释放，不能再用了。</summary>
        public bool IsDisposed => _cells == null;
        /// <summary>生成完成后又被修改了多少次；刚生成时是 0。</summary>
        public long Revision => _revision;
        /// <summary>这里目前保存了哪些环境数据，例如 height 或 temperature。</summary>
        public IEnumerable<string> EnvironmentLayerIds =>
            _environmentLayers == null
                ? (IEnumerable<string>)Array.Empty<string>()
                : _environmentLayers.Keys;

        /// <summary>某个格子的地形、叠层、草地或环境数据真的改变时发出通知。</summary>
        public event Action<ChunkTerrainChanged> Changed;

        /// <summary>读取区块里某个格子的核心地形数据。</summary>
        public TerrainCell GetCell(int x, int y)
        {
            ThrowIfDisposed();
            return _cells[GetIndex(x, y)];
        }

        /// <summary>修改某个格子的核心数据；新旧完全一样时就不做无用更新。</summary>
        public void SetCell(int x, int y, TerrainCell value)
        {
            ThrowIfDisposed();
            int index = GetIndex(x, y);
            if (_cells[index].Equals(value))
                return;
            _cells[index] = value;
            MarkChanged(x, y, TerrainChangeKind.Cell);
        }

        /// <summary>
        /// 判断角色能不能走过这个格子。
        /// 它必须标记为“可以走”，同时不能有固定障碍，也不能已经被别的东西占用。
        /// </summary>
        public bool IsWalkable(int x, int y)
        {
            TerrainCell cell = GetCell(x, y);
            return (cell.Flags & TerrainCellFlags.Walkable) != 0 &&
                   (cell.Flags & (TerrainCellFlags.Blocking | TerrainCellFlags.Occupied)) == 0;
        }

        /// <summary>看看这个格子从下到上一共叠了几层非空地块。</summary>
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

        /// <summary>
        /// 按从下到上的顺序读取第几层地块。
        /// 空层不会算进去，所以第 0 层就是最下面那层真正存在的地块。
        /// </summary>
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

        /// <summary>读取最上面那层地块；这个格子什么都没有时返回 0。</summary>
        public int GetTopTileId(int x, int y)
        {
            int count = GetTileLayerCount(x, y);
            return count == 0 ? 0 : GetTileIdAt(x, y, count - 1);
        }

        /// <summary>
        /// 用一个新列表替换这个格子的所有地块层。
        /// 0 代表空层，会被删掉；前三层放进普通格子数据，超过三层时再额外保存完整顺序。
        /// 如果最上面是障碍，也会顺便把“不可穿过”标记设好。
        /// </summary>
        public void ReplaceTileStack(int x, int y, IReadOnlyList<int> tileIds)
        {
            ThrowIfDisposed();
            if (tileIds == null)
                throw new ArgumentNullException(nameof(tileIds));

            int index = GetIndex(x, y);
            // 先删掉空层，后面读取时就能直接按第 1、2、3 层来数。
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
            // 有三层以上时，把最上面那层当作墙或岩石；完整叠放顺序仍会另外保存。
            int blocking = compact.Count > 2 ? compact[compact.Count - 1] : 0;
            TerrainCellFlags flags = previous.Flags;
            flags = blocking == 0
                ? flags & ~TerrainCellFlags.Blocking
                : flags | TerrainCellFlags.Blocking;
            _cells[index] = new TerrainCell(ground, back, blocking, previous.BiomeId,
                previous.NavigationCost, flags);
            MarkChanged(x, y, TerrainChangeKind.TileStack);
        }

        /// <summary>读取某个格子的草地状态。</summary>
        public byte GetGrass(int x, int y)
        {
            ThrowIfDisposed();
            return _grass[GetIndex(x, y)];
        }

        /// <summary>修改某个格子的草地；没有变化就不通知其他系统。</summary>
        public void SetGrass(int x, int y, byte value)
        {
            ThrowIfDisposed();
            int index = GetIndex(x, y);
            if (_grass[index] == value)
                return;
            _grass[index] = value;
            MarkChanged(x, y, TerrainChangeKind.Grass);
        }

        /// <summary>复制一份全部草地数据，调用方可以放心保存，不会影响原地图。</summary>
        public byte[] CopyGrass()
        {
            ThrowIfDisposed();
            var copy = new byte[CellCount];
            Array.Copy(_grass, copy, CellCount);
            return copy;
        }

        /// <summary>把所有超过三层的地块列表完整复制一份，防止外部改到原数据。</summary>
        public IReadOnlyDictionary<int, int[]> CopyExtendedTileStacks()
        {
            ThrowIfDisposed();
            var copy = new Dictionary<int, int[]>(_extendedTileStacks.Count);
            foreach (KeyValuePair<int, int[]> pair in _extendedTileStacks)
                copy.Add(pair.Key, (int[])pair.Value.Clone());
            return copy;
        }

        /// <summary>尝试读取某个格子的指定环境数据；这种数据不存在时返回 false。</summary>
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

        /// <summary>设置某个格子的环境数据；没有这种环境数据时会自动创建。</summary>
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

        /// <summary>复制一份所有格子的核心地形数据。</summary>
        public TerrainCell[] CopyCells()
        {
            ThrowIfDisposed();
            var copy = new TerrainCell[CellCount];
            Array.Copy(_cells, copy, CellCount);
            return copy;
        }

        /// <summary>尝试复制一整份温度、高度等环境数据；不存在时返回 null。</summary>
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

        /// <summary>
        /// 给整份地形算一个“内容指纹”。
        /// 同样的地形应该得到同样的数字，方便存档和测试快速判断两份地图是否一致。
        /// 修改次数、消息订阅和临时占用情况不会算进这个指纹。
        /// </summary>
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

            // 字典每次拿出数据的顺序可能不同，所以先按名字排序，再计算内容指纹。
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

        /// <summary>清空并释放这份地形占用的内存，同时取消所有变化通知。</summary>
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

    /// <summary>说明这次改动碰到了格子的哪一部分。</summary>
    public enum TerrainChangeKind
    {
        /// <summary>格子的核心地形数据变了。</summary>
        Cell,
        /// <summary>格子里地块的叠放顺序变了。</summary>
        TileStack,
        /// <summary>草地变了。</summary>
        Grass,
        /// <summary>温度、高度等环境数据变了。</summary>
        Environment,
        /// <summary>格子是否被物体占用发生了变化；目前这类通知由 ChunkOccupancyData 单独负责。</summary>
        Occupancy
    }

    /// <summary>一次“某个地形格子发生变化”的通知内容。</summary>
    public readonly struct ChunkTerrainChanged
    {
        public ChunkTerrainChanged(Int2 localCell, TerrainChangeKind kind, long revision)
        {
            LocalCell = localCell;
            Kind = kind;
            Revision = revision;
        }

        /// <summary>区块里的哪个格子变了。</summary>
        public Int2 LocalCell { get; }
        /// <summary>改的是地形、叠层、草地还是环境数据。</summary>
        public TerrainChangeKind Kind { get; }
        /// <summary>改完以后，这份地形是第几个版本。</summary>
        public long Revision { get; }
    }
}
