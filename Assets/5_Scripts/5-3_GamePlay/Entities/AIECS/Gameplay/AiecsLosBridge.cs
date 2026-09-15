using System;
using System.Collections.Generic;
using FlatWorld.Navigation;
using FlatWorld.WorldModel;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace FlatWorld.AIECS.Gameplay
{
    /// <summary>TerrainCell 与建筑占地到 Native LOS 的桥；缓存按导航 Chunk 排列，地形仅在版本变化时复制。</summary>
    public sealed class AiecsLosBridge : IDisposable
    {
        private struct ChunkStamp
        {
            public int2 Coordinate; // 发布顺序可能随导航窗口变化。
            public ChunkTerrainData Terrain; // 主线程来源。
            public long Revision; // 该地形版本。
        }
        private readonly List<ChunkStamp> stamps = new List<ChunkStamp>();
        private readonly HashSet<int2> dirtyBuildings = new HashSet<int2>(); // 只标记实际改变占地的 Chunk。
        private WorldTopologyDomain domain;
        private uint buildingRevision;
        private NativeArray<byte> cells;
        public long ChunkCopies { get; private set; }

        /// <summary>一个快照订阅一次占地通知，与 AI 数量无关。</summary>
        public AiecsLosBridge() { BuildingOccupancyRegistry.CellChanged += OnBuildingChanged; }

        /// <summary>占地更新只登记所在块；Native 写入延迟到上一批读取完成以后。</summary>
        private void OnBuildingChanged(Vector2Int cell) => dirtyBuildings.Add(FlowNavigationMath.ChunkOf(new int2(cell.x, cell.y), domain));

        /// <summary>调用前完成旧读取者；借用导航稀疏索引，绝不以可走性代替视线遮挡。</summary>
        public AiecsLosView Read(FlowNavigationSnapshot navigation)
        {
            int count = navigation.Chunks.Length;
            domain = navigation.Domain;
            bool resetBuildings = dirtyBuildings.Count == 0 && buildingRevision != BuildingOccupancyRegistry.Revision;
            if (!cells.IsCreated || cells.Length != count * 256)
            {
                if (cells.IsCreated) cells.Dispose();
                cells = new NativeArray<byte>(count * 256, Allocator.Persistent); stamps.Clear();
            }
            for (int index = 0; index < count; index++)
            {
                int2 coordinate = navigation.Chunks[index].Coordinate;
                int2 origin = FlowNavigationMath.Origin(coordinate, navigation.Domain);
                var address = ChunkMgr.Instance.ResolveWorldAddress(new Vector2(origin.x + 0.5f, origin.y + 0.5f));
                ChunkTerrainData terrain = null;
                if (ChunkMgr.Instance.TryGetChunkRuntime(address, out var chunk) && chunk.DataStatus == ChunkDataStatus.Ready &&
                    chunk.Terrain != null && !chunk.Terrain.IsDisposed) terrain = chunk.Terrain;
                var stamp = new ChunkStamp { Coordinate = coordinate, Terrain = terrain, Revision = terrain?.Revision ?? -1 };
                bool changed = index >= stamps.Count;
                if (!changed)
                {
                    var old = stamps[index]; changed = !old.Coordinate.Equals(coordinate) || old.Terrain != terrain ||
                        old.Revision != stamp.Revision || resetBuildings || dirtyBuildings.Contains(coordinate);
                }
                if (!changed) continue;
                if (index >= stamps.Count) stamps.Add(stamp); else stamps[index] = stamp;
                ChunkCopies++;
                for (int local = 0; local < 256; local++)
                {
                    int2 worldCell = navigation.Domain.Normalize(origin + FlowNavigationMath.LocalCell(local));
                    bool blocking = true;
                    int x = worldCell.x - address.ChunkOrigin.X, y = worldCell.y - address.ChunkOrigin.Y;
                    if (terrain != null && (uint)x < (uint)terrain.Width && (uint)y < (uint)terrain.Height)
                    {
                        TerrainCell value = terrain.GetCell(x, y);
                        blocking = value.BlockingTileId != 0 && (value.Flags & TerrainCellFlags.Blocking) != 0;
                    }
                    cells[index * 256 + local] = (byte)(blocking || BuildingOccupancyRegistry.IsOccupied(new Vector2Int(worldCell.x, worldCell.y)) ? 1 : 0);
                }
            }
            dirtyBuildings.Clear(); buildingRevision = BuildingOccupancyRegistry.Revision;
            return new AiecsLosView { Domain = navigation.Domain, Chunks = navigation.ChunkLookup, Cells = cells };
        }

        /// <summary>只释放本桥的遮挡数组，导航索引由原缓存管理。</summary>
        public void Dispose()
        {
            BuildingOccupancyRegistry.CellChanged -= OnBuildingChanged;
            if (cells.IsCreated) cells.Dispose(); cells = default; stamps.Clear(); dirtyBuildings.Clear();
        }
    }
}
