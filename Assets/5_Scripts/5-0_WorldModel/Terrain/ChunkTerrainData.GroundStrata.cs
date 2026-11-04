using System;
using System.Buffers;
using System.Collections.Generic;

namespace FlatWorld.WorldModel
{
    public sealed partial class ChunkTerrainData
    {
        #region 三维权威地层
        public const string GroundHarvestProgressLayerId = "terrain.ground.harvestProgress";
        private GroundStratum[] groundStrata;
        private int[] groundTopZ, groundBottomZ;
        private bool groundStrataInitialized;
        private GroundStrataCatalog groundStrataCatalog;
        private readonly HashSet<int> modifiedGroundColumns = new();
        public int MinZ { get; private set; }
        public int Depth { get; private set; }
        public IReadOnlyCollection<int> ModifiedGroundColumns => modifiedGroundColumns;
        public bool IsGroundColumnModified(int x, int y) => modifiedGroundColumns.Contains(GetIndex(x, y));

        public void InitializeGroundStrata(GroundStrataCatalog catalog = null)
        {
            ThrowIfDisposed();
            if (groundStrataInitialized) return;
            groundStrataInitialized = true;
            groundStrataCatalog = catalog;
            groundTopZ = new int[CellCount];
            groundBottomZ = new int[CellCount];
            int minimum = int.MaxValue, maximum = int.MinValue;
            for (int i = 0; i < CellCount; i++)
            {
                int x = i % Width, y = i / Width;
                int top = TerrainSurfaceHeightLayer.GetBaseLevel(this, x, y, GenerationMode == ChunkGenerationMode.Surface) -
                    TerrainSurfaceHeightLayer.GetNaturalDepthLevels(this, x, y);
                int tile = _cells[i].GroundTileId;
                int substrate = FrozenSurfaceWaterLayer.TryReadSource(this, x, y, out FrozenSurfaceWaterSource frozen)
                    ? frozen.Bed.GroundTileId : tile;
                int count = tile == 0 ? 0 : catalog != null && catalog.TryGetNaturalColumn(substrate, out int[] layers) ? layers.Length : 1;
                groundTopZ[i] = top;
                groundBottomZ[i] = top - count + 1;
                if (count == 0) continue;
                minimum = Math.Min(minimum, groundBottomZ[i]);
                maximum = Math.Max(maximum, top);
            }
            if (minimum == int.MaxValue) return;
            EnsureGroundStrataCapacity(minimum, maximum);
            for (int i = 0; i < CellCount; i++)
            {
                int tile = _cells[i].GroundTileId;
                if (tile == 0) continue;
                TerrainCell substrateCell = FrozenSurfaceWaterLayer.TryReadSource(this, i % Width, i / Width, out FrozenSurfaceWaterSource frozen)
                    ? frozen.Bed : _cells[i];
                int substrate = substrateCell.GroundTileId;
                int[] column = null;
                catalog?.TryGetNaturalColumn(substrate, out column);
                int top = groundTopZ[i];
                for (int z = top; z >= groundBottomZ[i]; z--)
                    groundStrata[(z - MinZ) * CellCount + i] = new GroundStratum
                    { TileId = z == top || column == null ? tile : column[top - z], Grass = z == top ? _grass[i] : GrassEmpty,
                        NavigationCost = z == top ? _cells[i].NavigationCost : substrateCell.NavigationCost,
                        Walkable = ((z == top ? _cells[i].Flags : substrateCell.Flags) & TerrainCellFlags.Walkable) != 0 };
            }
        }

        public IReadOnlyList<int> GetNaturalGroundTileIds(int tileId) => tileId == 0 ? Array.Empty<int>() :
            groundStrataCatalog?.GetNaturalColumn(tileId) ?? new[] { tileId };
        public bool IsGroundTileHarvestable(int tileId) => groundStrataCatalog?.IsHarvestable(tileId) == true;

        public int GetTopGroundZ(int x, int y)
        {
            InitializeGroundStrata();
            return groundTopZ[GetIndex(x, y)];
        }
        public GroundStratum GetGroundStratum(int x, int y, int z)
        {
            InitializeGroundStrata();
            int index = GetIndex(x, y);
            return z < groundBottomZ[index] || z > groundTopZ[index] ? default :
                groundStrata[(z - MinZ) * CellCount + index];
        }
        public void SetGroundStratum(int x, int y, int z, GroundStratum value)
        {
            InitializeGroundStrata();
            int index = GetIndex(x, y);
            if (value.TileId <= 0 || z < groundBottomZ[index] || z > groundTopZ[index])
                throw new ArgumentOutOfRangeException(nameof(z));
            groundStrata[(z - MinZ) * CellCount + index] = value;
            modifiedGroundColumns.Add(index);
            if (z == groundTopZ[index]) ProjectTopGroundStratum(x, y, index);
            else MarkChanged(x, y, TerrainChangeKind.Cell);
        }
        public float GetGroundHarvestProgress(int x, int y) => GetGroundStratum(x, y, GetTopGroundZ(x, y)).HarvestProgress;
        public void SetGroundHarvestProgress(int x, int y, float progress)
        {
            progress = Math.Clamp(progress, 0f, 1f);
            int z = GetTopGroundZ(x, y);
            GroundStratum layer = GetGroundStratum(x, y, z);
            if (layer.TileId == 0 || layer.HarvestProgress == progress) return;
            int index = GetIndex(x, y);
            groundStrata[(z - MinZ) * CellCount + index].HarvestProgress = progress;
            modifiedGroundColumns.Add(index);
            MarkChanged(x, y, TerrainChangeKind.Environment, GroundHarvestProgressLayerId);
        }
        public bool TryGetUnderlyingGroundStratum(int x, int y, out GroundStratum layer)
        {
            int z = GetTopGroundZ(x, y) - 1;
            layer = GetGroundStratum(x, y, z);
            return layer.TileId != 0;
        }
        public void PushGroundStratum(int x, int y, int tileId, short navigationCost = 1)
        {
            if (tileId <= 0) throw new ArgumentOutOfRangeException(nameof(tileId));
            int z = GetTopGroundZ(x, y) + 1;
            int index = GetIndex(x, y);
            EnsureGroundStrataCapacity(z, z);
            groundTopZ[index] = z;
            groundStrata[(z - MinZ) * CellCount + index] = new GroundStratum
            { TileId = tileId, Grass = GrassEmpty, PlayerPlaced = true, NavigationCost = navigationCost, Walkable = true };
            modifiedGroundColumns.Add(index);
            ProjectTopGroundStratum(x, y, index);
        }
        public bool CanRemoveTopGroundStratum(int x, int y)
        {
            int z = GetTopGroundZ(x, y), index = GetIndex(x, y);
            return z >= groundBottomZ[index] && (z > groundBottomZ[index] || GetGroundStratum(x, y, z).PlayerPlaced);
        }
        public bool TryRemoveTopGroundStratum(int x, int y)
        {
            int z = GetTopGroundZ(x, y), index = GetIndex(x, y);
            if (!CanRemoveTopGroundStratum(x, y)) return false;
            groundStrata[(z - MinZ) * CellCount + index] = default;
            groundTopZ[index]--;
            modifiedGroundColumns.Add(index);
            ProjectTopGroundStratum(x, y, index);
            return true;
        }
        /// <summary>整列清空用于局部世界重置和移交，普通表面替换不能隐式删除地下。</summary>
        public void ClearGroundColumn(int x, int y) => RestoreGroundColumn(x, y,
            new GroundColumnSnapshot { BottomZ = GetTopGroundZ(x, y) + 1, Layers = Array.Empty<GroundStratum>() });

        public GroundColumnSnapshot CaptureGroundColumn(int x, int y)
        {
            InitializeGroundStrata();
            int index = GetIndex(x, y), bottom = groundBottomZ[index];
            var layers = new GroundStratum[Math.Max(0, groundTopZ[index] - bottom + 1)];
            for (int n = 0; n < layers.Length; n++) layers[n] = groundStrata[(bottom + n - MinZ) * CellCount + index];
            return new GroundColumnSnapshot { BottomZ = bottom, Layers = layers };
        }
        public GroundColumnSnapshot[] CopyGroundColumns()
        {
            var columns = new GroundColumnSnapshot[CellCount];
            for (int i = 0; i < CellCount; i++) columns[i] = CaptureGroundColumn(i % Width, i / Width);
            return columns;
        }
        public void RestoreGroundColumn(int x, int y, GroundColumnSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.IsValid) throw new ArgumentException("三维地层列快照无效。");
            InitializeGroundStrata();
            int index = GetIndex(x, y);
            if (snapshot.Layers.Length > 0) EnsureGroundStrataCapacity(snapshot.BottomZ, snapshot.TopZ);
            for (int z = groundBottomZ[index]; z <= groundTopZ[index]; z++)
                groundStrata[(z - MinZ) * CellCount + index] = default;
            groundBottomZ[index] = snapshot.BottomZ;
            groundTopZ[index] = snapshot.TopZ;
            for (int n = 0; n < snapshot.Layers.Length; n++)
                groundStrata[(snapshot.BottomZ + n - MinZ) * CellCount + index] = snapshot.Layers[n];
            modifiedGroundColumns.Add(index);
            ProjectTopGroundStratum(x, y, index);
        }
        // 表面缓存只由本列投影产生；Back/Blocking 继续属于独立表面叠层。
        private void ProjectTopGroundStratum(int x, int y, int index)
        {
            GroundStratum top = groundTopZ[index] < groundBottomZ[index] ? default :
                groundStrata[(groundTopZ[index] - MinZ) * CellCount + index];
            TerrainCell cell = _cells[index];
            _grass[index] = top.Grass;
            _surfaceLiquidRevision++;
            TerrainCellFlags flags = top.TileId != 0 && top.Walkable ? cell.Flags | TerrainCellFlags.Walkable : cell.Flags & ~TerrainCellFlags.Walkable;
            WriteCell(x, y, index, new TerrainCell(top.TileId, cell.BackTileId, cell.BlockingTileId,
                cell.BiomeId, top.NavigationCost, flags), TerrainChangeKind.Cell);
        }
        private void SynchronizeGroundSurfaceCell(int index, TerrainCell value)
        {
            if (!groundStrataInitialized) return;
            if (groundTopZ[index] < groundBottomZ[index])
            {
                if (value.GroundTileId == 0) return;
                int z = groundTopZ[index];
                EnsureGroundStrataCapacity(z, z);
                groundBottomZ[index] = z;
            }
            int slot = (groundTopZ[index] - MinZ) * CellCount + index;
            if (value.GroundTileId == 0)
                throw new InvalidOperationException("清空地格必须显式调用 ClearGroundColumn，不能通过表面替换删除地下。");
            bool walkable = (value.Flags & TerrainCellFlags.Walkable) != 0;
            if (groundStrata[slot].TileId == value.GroundTileId && groundStrata[slot].NavigationCost == value.NavigationCost &&
                groundStrata[slot].Walkable == walkable) return;
            groundStrata[slot].TileId = value.GroundTileId;
            groundStrata[slot].NavigationCost = value.NavigationCost;
            groundStrata[slot].Walkable = walkable;
            modifiedGroundColumns.Add(index);
        }
        private void SynchronizeGroundSurfaceGrass(int index, byte value)
        {
            if (!groundStrataInitialized || groundTopZ[index] < groundBottomZ[index]) return;
            groundStrata[(groundTopZ[index] - MinZ) * CellCount + index].Grass = value;
            modifiedGroundColumns.Add(index);
        }
        private void EnsureGroundStrataCapacity(int minimum, int maximum)
        {
            if (groundStrata != null && minimum >= MinZ && maximum < MinZ + Depth) return;
            int newMinimum = Depth == 0 ? minimum : Math.Min(MinZ, minimum);
            int required = Depth == 0 ? maximum - newMinimum + 1 : Math.Max(maximum + 1, MinZ + Depth) - newMinimum;
            int depth = Math.Max(1, Depth);
            while (depth < required) depth = checked(depth * 2);
            int count = checked(depth * CellCount);
            var next = ArrayPool<GroundStratum>.Shared.Rent(count);
            Array.Clear(next, 0, count);
            if (groundStrata != null)
            {
                Array.Copy(groundStrata, 0, next, (MinZ - newMinimum) * CellCount, Depth * CellCount);
                ArrayPool<GroundStratum>.Shared.Return(groundStrata);
            }
            groundStrata = next;
            MinZ = newMinimum;
            Depth = depth;
        }
        private void ReleaseGroundStrata()
        {
            if (groundStrata != null) ArrayPool<GroundStratum>.Shared.Return(groundStrata);
            groundStrata = null;
            groundTopZ = groundBottomZ = null;
            modifiedGroundColumns.Clear();
        }
        #endregion
    }
}
