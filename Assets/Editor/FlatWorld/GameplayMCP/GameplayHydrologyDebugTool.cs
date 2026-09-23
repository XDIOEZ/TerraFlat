using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlatWorld.GameplayMCP
{
    /// <summary>
    /// 只读统计当前已加载地表水文的形状指标。
    /// 用于验证河道是否重新出现水平、垂直或 45° 栅格锁向，不修改任何世界数据。
    /// </summary>
    [McpForUnityTool(
        "gameplay_hydrology_debug",
        Description = "Read-only hydrology shape audit for loaded chunks: river cell count, connectivity, and longest identical quantized flow-direction streak.",
        Group = "core")]
    public static class GameplayHydrologyDebugTool
    {
        private readonly struct FlowCell
        {
            public FlowCell(Int2 direction, float depth)
            {
                Direction = direction;
                Depth = depth;
            }

            public Int2 Direction { get; }
            public float Depth { get; }
        }

        private static readonly Int2[] Neighbors =
        {
            new(-1, -1), new(0, -1), new(1, -1),
            new(-1, 0),               new(1, 0),
            new(-1, 1),  new(0, 1),  new(1, 1)
        };

        /// <summary>读取当前 Runtime Chunk 的 riverDepth / riverFlow 环境层。</summary>
        public static object HandleCommand(JObject parameters)
        {
            ChunkMgr chunkMgr = ChunkMgr.ExistingInstance;
            if (chunkMgr == null)
                return new ErrorResponse("chunk_runtime_not_ready: 当前世界没有可用的 ChunkMgr。");

            var riverCells = new HashSet<Int2>();
            var flowCells = new Dictionary<Int2, FlowCell>();
            int loadedChunks = 0;
            float maximumDepth = 0f;

            foreach (ChunkRuntime chunk in chunkMgr.Chunks.Values)
            {
                if (chunk?.Terrain == null ||
                    chunk.Terrain.IsDisposed ||
                    chunk.DataStatus != ChunkDataStatus.Ready)
                {
                    continue;
                }

                loadedChunks++;
                ChunkTerrainData terrain = chunk.Terrain;
                for (int y = 0; y < terrain.Height; y++)
                {
                    for (int x = 0; x < terrain.Width; x++)
                    {
                        if (!terrain.TryGetEnvironmentValue("riverDepth", x, y, out float depth) ||
                            depth <= 0f)
                        {
                            continue;
                        }

                        var position = new Int2(
                            chunk.Address.ChunkOrigin.X + x,
                            chunk.Address.ChunkOrigin.Y + y);
                        riverCells.Add(position);
                        maximumDepth = Mathf.Max(maximumDepth, depth);

                        terrain.TryGetEnvironmentValue("riverFlowX", x, y, out float flowX);
                        terrain.TryGetEnvironmentValue("riverFlowY", x, y, out float flowY);
                        Int2 direction = QuantizeDirection(flowX, flowY);
                        if (direction.X != 0 || direction.Y != 0)
                            flowCells[position] = new FlowCell(direction, depth);
                    }
                }
            }

            int components = CountComponents(riverCells);
            int longestAny = MeasureLongestDirectionStreak(flowCells, diagonalOnly: false);
            int longestDiagonal = MeasureLongestDirectionStreak(flowCells, diagonalOnly: true);

            return new SuccessResponse("FlatWorld hydrology shape audit.", new
            {
                loadedChunks,
                riverCells = riverCells.Count,
                flowingCells = flowCells.Count,
                connectedComponents = components,
                maximumDepth = Math.Round(maximumDepth, 3),
                longestSameDirectionRun = longestAny,
                longestDiagonalDirectionRun = longestDiagonal
            });
        }

        /// <summary>把连续流向量量化到 8 个相邻格方向。</summary>
        private static Int2 QuantizeDirection(float x, float y)
        {
            if (x * x + y * y < 0.04f)
                return default;

            float angle = Mathf.Atan2(y, x) * Mathf.Rad2Deg;
            int sector = Mathf.RoundToInt(angle / 45f);
            sector = (sector % 8 + 8) % 8;
            return sector switch
            {
                0 => new Int2(1, 0),
                1 => new Int2(1, 1),
                2 => new Int2(0, 1),
                3 => new Int2(-1, 1),
                4 => new Int2(-1, 0),
                5 => new Int2(-1, -1),
                6 => new Int2(0, -1),
                _ => new Int2(1, -1)
            };
        }

        /// <summary>统计已加载河水格的 8 邻域连通块数量。</summary>
        private static int CountComponents(HashSet<Int2> cells)
        {
            var visited = new HashSet<Int2>();
            var queue = new Queue<Int2>();
            int components = 0;
            foreach (Int2 start in cells)
            {
                if (!visited.Add(start))
                    continue;

                components++;
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    Int2 current = queue.Dequeue();
                    for (int i = 0; i < Neighbors.Length; i++)
                    {
                        Int2 next = current + Neighbors[i];
                        if (cells.Contains(next) && visited.Add(next))
                            queue.Enqueue(next);
                    }
                }
            }
            return components;
        }

        /// <summary>沿每格量化流向追踪，统计连续相同方向的最长链。</summary>
        private static int MeasureLongestDirectionStreak(
            IReadOnlyDictionary<Int2, FlowCell> cells,
            bool diagonalOnly)
        {
            int maximum = 0;
            foreach (KeyValuePair<Int2, FlowCell> pair in cells)
            {
                Int2 direction = pair.Value.Direction;
                if (diagonalOnly && (direction.X == 0 || direction.Y == 0))
                    continue;

                Int2 previous = pair.Key - direction;
                if (cells.TryGetValue(previous, out FlowCell previousCell) &&
                    previousCell.Direction.Equals(direction))
                {
                    continue;
                }

                int length = 0;
                Int2 current = pair.Key;
                while (cells.TryGetValue(current, out FlowCell currentCell) &&
                       currentCell.Direction.Equals(direction))
                {
                    length++;
                    current += direction;
                }
                maximum = Mathf.Max(maximum, length);
            }
            return maximum;
        }
    }
}
