using System;
using System.Collections.Generic;
using System.Threading;

namespace FlatWorld.WorldModel
{
    public static partial class ChunkEcologyGenerator
    {
        #region 跨区块资源候选

        /// <summary>各代理区块独立复算同一规范锚点，不要求锚点所属区块已经加载。</summary>
        public static ChunkEcologyData GenerateShared(ChunkGenerationRequest request,
            Func<EcologySpawnRuleSnapshot, Int2, bool> accepts, CancellationToken cancellationToken)
        {
            if (request.Profile.EcologyGlobalMultiplier <= 0d) return ChunkEcologyData.Empty;
            var rules = request.Profile.SharedEcologyRules;
            var chances = request.Profile.SharedEcologyCumulativeChances;
            if (rules.Count == 0 || chances[chances.Count - 1] <= 0d) return ChunkEcologyData.Empty;
            cancellationToken.ThrowIfCancellationRequested();
            var result = new List<NaturalItemPlacement>();
            int spacing = request.Profile.SharedEcologySpacing;
            var topology = request.Topology;
            int countX = topology.IsWrapped ? topology.Span.X / spacing : 0;
            int countY = topology.IsWrapped ? topology.Span.Y / spacing : 0;
            // 小世界容不下完整独立矿山时不生成，不把它的镜像叠到自己身上。
            if (topology.IsWrapped && (countX == 0 || countY == 0)) return ChunkEcologyData.Empty;
            double width = topology.IsWrapped ? (double)topology.Span.X / countX : spacing;
            double height = topology.IsWrapped ? (double)topology.Span.Y / countY : spacing;
            ulong seed = CreateSeed(request, "shared.resources", 0x73687264U);
            double phaseX = Hash01(seed, 17, 31) * width;
            double phaseY = Hash01(seed, 43, 59) * height;
            double originX = (topology.IsWrapped ? topology.Min.X : 0d) + phaseX;
            double originY = (topology.IsWrapped ? topology.Min.Y : 0d) + phaseY;
            int startX = request.Address.ChunkOrigin.X, startY = request.Address.ChunkOrigin.Y;
            double radius = request.Profile.SharedEcologyMaximumRadius + 0.5d;
            long minX = (long)Math.Floor((startX - radius - originX) / width);
            long maxX = (long)Math.Floor((startX + request.Profile.Width + radius - originX) / width);
            long minY = (long)Math.Floor((startY - radius - originY) / height);
            long maxY = (long)Math.Floor((startY + request.Profile.Height + radius - originY) / height);
            var seen = new HashSet<Int2>();
            for (long by = minY; by <= maxY; by++)
            for (long bx = minX; bx <= maxX; bx++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int keyX = topology.IsWrapped ? Modulo(bx, countX) : checked((int)bx);
                int keyY = topology.IsWrapped ? Modulo(by, countY) : checked((int)by);
                // 同一个候选只抽一次互斥矿种；零概率区间不会命中，资格不符也不重新抽取。
                double roll = Hash(seed, keyX, keyY) / ((double)uint.MaxValue + 1d);
                int selected = 0;
                while (selected < rules.Count && roll >= chances[selected]) selected++;
                if (selected == rules.Count) continue;
                EcologySpawnRuleSnapshot rule = rules[selected];
                double ruleRadius = rule.SharedRadius + .5d;
                // 候选留在粗网格中央半区，邻候选之间至少相隔半个网格。
                double x = Math.Floor(originX + (bx + .25d + .5d * Hash01(seed ^ 13UL, keyX, keyY)) * width);
                double y = Math.Floor(originY + (by + .25d + .5d * Hash01(seed ^ 29UL, keyX, keyY)) * height);
                if (x < int.MinValue + radius || x > int.MaxValue - radius ||
                    y < int.MinValue + radius || y > int.MaxValue - radius) continue;
                Int2 anchor = new(topology.NormalizeX((int)x), topology.NormalizeY((int)y));
                if (!seen.Add(anchor)) continue;
                double localX = anchor.X - (double)startX;
                double localY = anchor.Y - (double)startY;
                if (topology.IsWrapped)
                {
                    localX = NearestDelta(localX - request.Profile.Width * .5d, topology.Span.X) + request.Profile.Width * .5d;
                    localY = NearestDelta(localY - request.Profile.Height * .5d, topology.Span.Y) + request.Profile.Height * .5d;
                }
                if (localX + .5d + ruleRadius <= 0d || localY + .5d + ruleRadius <= 0d ||
                    localX + .5d - ruleRadius >= request.Profile.Width ||
                    localY + .5d - ruleRadius >= request.Profile.Height) continue;
                if (accepts != null && !accepts(rule, anchor)) continue;
                // 共享资源使用负身份域，避免与普通生态的正 GUID 挤占同一存档键。
                ulong identitySeed = CreateSeed(request, rule.RuleId, 0x67756964U);
                int guid = unchecked((int)(Hash(identitySeed, anchor.X, anchor.Y) | 0x80000000U));
                result.Add(new NaturalItemPlacement(guid, rule.ItemId, (int)localX, (int)localY,
                    0f, 0f, rule.RuleId, sharedAnchor: anchor));
            }
            return result.Count == 0 ? ChunkEcologyData.Empty : new ChunkEcologyData(result);
        }

        /// <summary>生成和空间过滤共用环绕后的占地判定，不把远端镜像误当作另一座矿山。</summary>
        public static bool SharedFootprintContains(ChunkGenerationTopologySnapshot topology,
            EcologySpawnRuleSnapshot rule, Int2 anchor, Int2 cell, int padding = 0)
        {
            double dx = cell.X - (double)anchor.X, dy = cell.Y - (double)anchor.Y;
            if (topology.IsWrapped)
            {
                dx = NearestDelta(dx, topology.Span.X);
                dy = NearestDelta(dy, topology.Span.Y);
            }
            return Math.Abs(dx) <= rule.FootprintWidth / 2 + padding &&
                   Math.Abs(dy) <= rule.FootprintHeight / 2 + padding;
        }

        /// <summary>包括负坐标的规范区块寻址，不使用向零截断。</summary>
        public static Int2 SharedOwnerOrigin(Int2 anchor, int width, int height)
            => new(checked((int)((long)Math.Floor((double)anchor.X / width) * width)),
                checked((int)((long)Math.Floor((double)anchor.Y / height) * height)));

        private static int Modulo(long value, int divisor) => (int)((value % divisor + divisor) % divisor);
        private static double NearestDelta(double value, int span)
            => value - Math.Floor(value / span + .5d) * span;

        #endregion
    }
}
