using System;
using System.Collections.Generic;
using System.Threading;

namespace FlatWorld.WorldModel
{
    public sealed partial class DeterministicChunkGenerator
    {
        #region 稀疏河源与终点淡水湖

        /// <summary>每个 3×3 径流单元组只选一个源头；固定坐标选择保证相邻区域算出同一条跨界河。</summary>
        private static bool ShouldTraceRunoffSource(
            int worldSeed, Int2 grid)
        {
            const int sourceGroupSize = 3;
            int cellX = grid.X;
            int cellY = grid.Y;
            int groupX = FloorDiv(cellX, sourceGroupSize);
            int groupY = FloorDiv(cellY, sourceGroupSize);
            int localX = cellX - groupX * sourceGroupSize;
            int localY = cellY - groupY * sourceGroupSize;
            int selected = (int)(Hash(worldSeed, groupX, groupY, 0x638bf4a1u) %
                                 (uint)(sourceGroupSize * sourceGroupSize));
            return localY * sourceGroupSize + localX == selected;
        }

        // 极圈源点只保留固定种子筛选出的 1/20，整条径流继续追踪而不逐格挖断河道。
        internal static bool ShouldGeneratePolarRiverSource(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, Int2 source)
        {
            if (settings.PolarRiverSourceChanceMultiplier >= 1d ||
                !IsInsidePolarBand(request, settings, source.X, source.Y))
                return true;
            return Hash01(request.WorldSeed, request.Topology.NormalizeX(source.X),
                request.Topology.NormalizeY(source.Y), 0x724fa8d3u) <
                settings.PolarRiverSourceChanceMultiplier;
        }


        /// <summary>恢复旧版汇流终点扩湖，降水只负责河源水量而不反复决定湖面蒸发。</summary>
        private static void AddHeightDrivenTerminalLakes(
            ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings,
            HydrologySamplingContext sampling,
            HydrologyOutputWindow outputWindow,
            IReadOnlyDictionary<Int2, double> terminalFlowByCell,
            Dictionary<Int2, GeneratedHydrologyCell> riverCells,
            CancellationToken cancellationToken)
        {
            if (settings.RiverLakeChance <= 0d)
                return;

            foreach (KeyValuePair<Int2, double> terminal in terminalFlowByCell)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (terminal.Value < settings.RiverLakeMinFlow ||
                    Hash01(request.WorldSeed, terminal.Key.X, terminal.Key.Y, 0x6c8e9cf5u) >=
                    settings.RiverLakeChance)
                    continue;

                double sinkHeight = sampling.Height(terminal.Key);
                if (sinkHeight <= settings.SeaLevel)
                    continue;

                var basin = new HashSet<Int2> { terminal.Key };
                var frontier = new List<Int2>();
                for (int i = 0; i < RiverNeighbors.Length; i++)
                    frontier.Add(sampling.Normalize(terminal.Key + RiverNeighbors[i]));

                while (frontier.Count > 0 && basin.Count < settings.RiverMaxLakeCells)
                {
                    int lowestIndex = 0;
                    double lowestHeight = sampling.Height(frontier[0]);
                    for (int i = 1; i < frontier.Count; i++)
                    {
                        double candidateHeight = sampling.Height(frontier[i]);
                        if (candidateHeight >= lowestHeight)
                            continue;
                        lowestIndex = i;
                        lowestHeight = candidateHeight;
                    }

                    Int2 current = frontier[lowestIndex];
                    frontier.RemoveAt(lowestIndex);
                    if (basin.Contains(current) ||
                        lowestHeight - sinkHeight > settings.RiverMaxLakeLevelRise)
                        continue;
                    basin.Add(current);
                    for (int i = 0; i < RiverNeighbors.Length; i++)
                    {
                        Int2 neighbor = sampling.Normalize(current + RiverNeighbors[i]);
                        if (!basin.Contains(neighbor) && !frontier.Contains(neighbor))
                            frontier.Add(neighbor);
                    }
                }

                if (basin.Count < settings.RiverMinLakeCells)
                    continue;
                foreach (Int2 lakePosition in basin)
                {
                    if (!outputWindow.Contains(lakePosition) ||
                        sampling.Height(lakePosition) <= settings.SeaLevel)
                        continue;
                    double depthT = Clamp01((sinkHeight + settings.RiverMaxLakeLevelRise -
                                             sampling.Height(lakePosition)) /
                                            settings.RiverMaxLakeLevelRise);
                    SetRiverCell(riverCells, lakePosition, new GeneratedHydrologyCell(
                        GeneratedHydrologyKind.Lake,
                        terminal.Value,
                        Lerp(settings.RiverDepthMin, settings.RiverDepthMax,
                            Math.Max(0.15d, depthT)),
                        sinkHeight + settings.RiverMaxLakeLevelRise));
                }
            }
        }

        #endregion
    }
}
