using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;

namespace FlatWorld.WorldModel
{
    public sealed partial class DeterministicChunkGenerator
    {
        #region 入海口流场

        /// <summary>只在河道下游首次进入海水的位置展开扇面，不改海水、湖泊或河床。</summary>
        private static Dictionary<Int2, EstuaryCurrentSample> BuildEstuaryCurrents(
            ChunkGenerationSettingsSnapshot settings, HydrologySamplingContext sampling,
            HydrologyOutputWindow output, IReadOnlyDictionary<Int2, ChannelCenterSample> centers,
            CancellationToken cancellation)
        {
            var result = new Dictionary<Int2, EstuaryCurrentSample>();
            if (settings.RiverEstuaryLength <= 0d) return result;
            var mouths = new List<KeyValuePair<Int2, ChannelCenterSample>>();
            int checkedCenters = 0;
            foreach (var pair in centers)
            {
                if ((checkedCenters++ & 63) == 0) cancellation.ThrowIfCancellationRequested();
                ChannelCenterSample flow = pair.Value;
                if (flow.Flow <= 0d || sampling.HeightNormalized(pair.Key) <= settings.SeaLevel)
                    continue;
                int stepX = (int)Math.Round(flow.DirectionX), stepY = (int)Math.Round(flow.DirectionY);
                if (stepX == 0 && stepY == 0) continue;
                Int2 next = sampling.Normalize(new Int2(pair.Key.X + stepX, pair.Key.Y + stepY));
                if (sampling.HeightNormalized(next) < settings.SeaLevel) mouths.Add(pair);
            }
            // 相交扇面选影响最大的来源；同强度按规范坐标破同分，不相互抵消。
            mouths.Sort((a, b) => a.Key.X != b.Key.X
                ? a.Key.X.CompareTo(b.Key.X) : a.Key.Y.CompareTo(b.Key.Y));
            int reach = settings.RiverEstuaryLength;
            WorldTopologyDomain domain = sampling.Request.Topology.ToDomain();
            foreach (var mouth in mouths)
            {
                cancellation.ThrowIfCancellationRequested();
                ChannelCenterSample flow = mouth.Value;
                Int2 origin = sampling.Normalize(new Int2(mouth.Key.X + (int)Math.Round(flow.DirectionX),
                    mouth.Key.Y + (int)Math.Round(flow.DirectionY)));
                double widthT = InverseLerp(settings.RiverStartFlow,
                    Math.Max(settings.RiverStartFlow + 0.001d, settings.RiverFullWidthFlow), flow.Flow);
                double halfWidth = 1d + Math.Round(widthT * (settings.RiverMaxWidth - 1),
                    MidpointRounding.AwayFromZero) * 0.5d;
                for (int dy = -reach; dy <= reach; dy++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    for (int dx = -reach; dx <= reach; dx++)
                    {
                        Int2 target = sampling.Normalize(new Int2(origin.X + dx, origin.Y + dy));
                        if (!output.Contains(target)) continue;
                        int2 delta = domain.ShortestDelta(new int2(origin.X, origin.Y),
                            new int2(target.X, target.Y));
                        if (!EstuaryCurrentKernel.TrySample(delta.x, delta.y, flow.DirectionX, flow.DirectionY,
                                halfWidth, settings.RiverEstuaryLength, settings.RiverEstuarySpread,
                                out EstuaryCurrentSample sample) ||
                            sampling.HeightNormalized(target) >= settings.SeaLevel ||
                            !HasOpenEstuaryRay(sampling, origin, delta.x, delta.y, settings.SeaLevel)) continue;
                        if (!result.TryGetValue(target, out EstuaryCurrentSample previous) ||
                            sample.Influence > previous.Influence)
                            result[target] = sample;
                    }
                }
            }
            return result;
        }

        /// <summary>逐半格检查海水通路，扇面不能穿过陆地或小岛影响另一侧海湾。</summary>
        private static bool HasOpenEstuaryRay(HydrologySamplingContext sampling,
            Int2 origin, int dx, int dy, double seaLevel)
        {
            int steps = Math.Max(Math.Abs(dx), Math.Abs(dy)) * 2;
            for (int i = 1; i <= steps; i++)
            {
                var cell = new Int2(origin.X + (int)Math.Floor(0.5d + dx * (i / (double)steps)),
                    origin.Y + (int)Math.Floor(0.5d + dy * (i / (double)steps)));
                if (sampling.Height(cell) >= seaLevel) return false;
            }
            return true;
        }

        #endregion
    }
}
