using System;
using System.Collections.Generic;

namespace FlatWorld.WorldModel
{
    /// <summary>以观察者的矩形边界维护活动、数据、表现和保留窗口的并集。</summary>
    public sealed class ChunkWindowCoverage
    {
        public enum Layer { Active, Data, Presentation, Retained }

        private sealed class CoverageLayer
        {
            public readonly Dictionary<WorldAddress, int> Counts = new Dictionary<WorldAddress, int>();
            public readonly Dictionary<WorldAddress, bool> Before = new Dictionary<WorldAddress, bool>();
            public readonly List<WorldAddress> Added = new List<WorldAddress>();
            public readonly List<WorldAddress> Removed = new List<WorldAddress>();

            public void Change(WorldAddress address, int amount)
            {
                Counts.TryGetValue(address, out int count);
                if (!Before.ContainsKey(address)) Before.Add(address, count > 0);
                count += amount;
                if (count == 0) Counts.Remove(address);
                else Counts[address] = count;
            }

            public void Finish()
            {
                Added.Clear();
                Removed.Clear();
                foreach (KeyValuePair<WorldAddress, bool> pair in Before)
                {
                    bool present = Counts.ContainsKey(pair.Key);
                    if (present && !pair.Value) Added.Add(pair.Key);
                    else if (!present && pair.Value) Removed.Add(pair.Key);
                }
                Added.Sort();
                Removed.Sort();
                Before.Clear();
            }
        }

        private readonly IWorldAddressNormalizer normalizer;
        private readonly int layerMask;
        private readonly CoverageLayer[] layers =
            { new CoverageLayer(), new CoverageLayer(), new CoverageLayer(), new CoverageLayer() };
        private readonly List<ChunkWindowRequest> previous = new List<ChunkWindowRequest>();
        public int LastVisitedCells { get; private set; }
        public bool LastFullRebuild { get; private set; }

        public ChunkWindowCoverage(IWorldAddressNormalizer normalizer = null, int layerMask = 15)
        {
            this.normalizer = normalizer ?? IdentityWorldAddressNormalizer.Instance;
            this.layerMask = layerMask;
        }

        public int Count(Layer layer) => layers[(int)layer].Counts.Count;
        public bool Contains(Layer layer, WorldAddress address) => layers[(int)layer].Counts.ContainsKey(address);
        public IEnumerable<WorldAddress> Addresses(Layer layer) => layers[(int)layer].Counts.Keys;
        public IReadOnlyList<WorldAddress> Added(Layer layer) => layers[(int)layer].Added;
        public IReadOnlyList<WorldAddress> Removed(Layer layer) => layers[(int)layer].Removed;

        #region 观察者窗口增量

        // 同配置先配对未移动观察者，再配对移动观察者，只有相减后的矩形边界需要访问。
        public void Update(IReadOnlyList<ChunkWindowRequest> requests)
        {
            if (requests == null) throw new ArgumentNullException(nameof(requests));
            LastVisitedCells = 0;
            int[] matches = new int[previous.Count];
            bool[] used = new bool[requests.Count];
            for (int i = 0; i < matches.Length; i++) matches[i] = -1;
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < previous.Count; i++)
                {
                    if (matches[i] >= 0) continue;
                    for (int j = 0; j < requests.Count; j++)
                    {
                        if (used[j] || !HasSameWindowConfiguration(previous[i], requests[j])) continue;
                        if (pass == 0 && normalizer.Normalize(previous[i].Center) != normalizer.Normalize(requests[j].Center)) continue;
                        matches[i] = j;
                        used[j] = true;
                        break;
                    }
                }
            bool unmatchedOld = false, unmatchedNew = false;
            for (int i = 0; i < matches.Length; i++) unmatchedOld |= matches[i] < 0;
            for (int i = 0; i < used.Length; i++) unmatchedNew |= !used[i];
            LastFullRebuild = previous.Count == 0 || unmatchedOld && unmatchedNew;
            if (LastFullRebuild)
            {
                foreach (CoverageLayer layer in layers)
                {
                    foreach (WorldAddress address in layer.Counts.Keys) layer.Before[address] = true;
                    layer.Counts.Clear();
                }
                for (int i = 0; i < requests.Count; i++) ApplyWholeWindow(requests[i], 1);
            }
            else
            {
                for (int i = 0; i < previous.Count; i++)
                {
                    if (matches[i] < 0) ApplyWholeWindow(previous[i], -1);
                    else ApplyMovedWindowBoundaries(previous[i], requests[matches[i]]);
                }
                for (int i = 0; i < requests.Count; i++)
                    if (!used[i]) ApplyWholeWindow(requests[i], 1);
            }
            previous.Clear();
            for (int i = 0; i < requests.Count; i++) previous.Add(requests[i]);
            foreach (CoverageLayer layer in layers) layer.Finish();
        }

        public void Clear()
        {
            previous.Clear();
            foreach (CoverageLayer layer in layers)
            {
                layer.Counts.Clear();
                layer.Before.Clear();
                layer.Added.Clear();
                layer.Removed.Clear();
            }
            LastVisitedCells = 0;
            LastFullRebuild = false;
        }

        private static bool HasSameWindowConfiguration(ChunkWindowRequest a, ChunkWindowRequest b)
        {
            return a.Center.DimensionId == b.Center.DimensionId && a.WorldSeed == b.WorldSeed &&
                ReferenceEquals(a.Profile, b.Profile) && a.RequestPresentation == b.RequestPresentation &&
                a.ActiveDistanceX == b.ActiveDistanceX && a.ActiveDistanceY == b.ActiveDistanceY &&
                a.DataDistanceX == b.DataDistanceX && a.DataDistanceY == b.DataDistanceY &&
                a.PresentationDistanceX == b.PresentationDistanceX && a.PresentationDistanceY == b.PresentationDistanceY &&
                a.DestroyDistanceX == b.DestroyDistanceX && a.DestroyDistanceY == b.DestroyDistanceY &&
                a.Topology.IsWrapped == b.Topology.IsWrapped && a.Topology.Min == b.Topology.Min && a.Topology.Span == b.Topology.Span;
        }

        private void ApplyWholeWindow(ChunkWindowRequest request, int change)
        {
            WorldAddress center = normalizer.Normalize(request.Center);
            for (int i = 0; i < layers.Length; i++)
            {
                if ((layerMask & (1 << i)) == 0 || i == (int)Layer.Presentation && !request.RequestPresentation) continue;
                GetRadius(request, (Layer)i, out int x, out int y);
                ApplyRectangle(layers[i], center, request.Profile.Width, request.Profile.Height, -x, x, -y, y, change);
            }
        }

        private void ApplyMovedWindowBoundaries(ChunkWindowRequest oldRequest, ChunkWindowRequest newRequest)
        {
            WorldAddress center = normalizer.Normalize(oldRequest.Center);
            WorldAddress target = normalizer.Normalize(newRequest.Center);
            int dx = target.ChunkOrigin.X - center.ChunkOrigin.X;
            int dy = target.ChunkOrigin.Y - center.ChunkOrigin.Y;
            if (oldRequest.Topology.IsWrapped)
            {
                dx = ShortestWrappedDelta(dx, oldRequest.Topology.Span.X);
                dy = ShortestWrappedDelta(dy, oldRequest.Topology.Span.Y);
            }
            int width = oldRequest.Profile.Width, height = oldRequest.Profile.Height;
            if (dx % width != 0 || dy % height != 0)
            {
                ApplyWholeWindow(oldRequest, -1);
                ApplyWholeWindow(newRequest, 1);
                return;
            }
            dx /= width;
            dy /= height;
            if (dx == 0 && dy == 0) return;
            for (int i = 0; i < layers.Length; i++)
            {
                if ((layerMask & (1 << i)) == 0 || i == (int)Layer.Presentation && !oldRequest.RequestPresentation) continue;
                GetRadius(oldRequest, (Layer)i, out int x, out int y);
                ApplyRectangleDifference(layers[i], center, width, height,
                    -x, x, -y, y, dx - x, dx + x, dy - y, dy + y, -1);
                ApplyRectangleDifference(layers[i], center, width, height,
                    dx - x, dx + x, dy - y, dy + y, -x, x, -y, y, 1);
            }
        }

        private static int ShortestWrappedDelta(int delta, int span)
        {
            delta %= span;
            if (delta > span / 2) delta -= span;
            else if (delta < -span / 2) delta += span;
            return delta;
        }

        private static void GetRadius(ChunkWindowRequest request, Layer layer, out int x, out int y)
        {
            switch (layer)
            {
                case Layer.Active: x = request.ActiveDistanceX - 1; y = request.ActiveDistanceY - 1; break;
                case Layer.Data: x = request.DataDistanceX - 1; y = request.DataDistanceY - 1; break;
                case Layer.Presentation: x = request.PresentationDistanceX - 1; y = request.PresentationDistanceY - 1; break;
                default: x = request.DestroyDistanceX - 1; y = request.DestroyDistanceY - 1; break;
            }
        }

        private void ApplyRectangleDifference(CoverageLayer layer, WorldAddress center, int width, int height,
            int minX, int maxX, int minY, int maxY, int otherMinX, int otherMaxX, int otherMinY, int otherMaxY, int change)
        {
            int overlapMinX = Math.Max(minX, otherMinX), overlapMaxX = Math.Min(maxX, otherMaxX);
            int overlapMinY = Math.Max(minY, otherMinY), overlapMaxY = Math.Min(maxY, otherMaxY);
            if (overlapMinX > overlapMaxX || overlapMinY > overlapMaxY)
            {
                ApplyRectangle(layer, center, width, height, minX, maxX, minY, maxY, change);
                return;
            }
            ApplyRectangle(layer, center, width, height, minX, overlapMinX - 1, minY, maxY, change);
            ApplyRectangle(layer, center, width, height, overlapMaxX + 1, maxX, minY, maxY, change);
            ApplyRectangle(layer, center, width, height, overlapMinX, overlapMaxX, minY, overlapMinY - 1, change);
            ApplyRectangle(layer, center, width, height, overlapMinX, overlapMaxX, overlapMaxY + 1, maxY, change);
        }

        private void ApplyRectangle(CoverageLayer layer, WorldAddress center, int width, int height,
            int minX, int maxX, int minY, int maxY, int change)
        {
            for (int x = minX; x <= maxX; x++)
                for (int y = minY; y <= maxY; y++)
                {
                    var address = new WorldAddress(center.DimensionId,
                        new Int2(center.ChunkOrigin.X + x * width, center.ChunkOrigin.Y + y * height));
                    layer.Change(normalizer.Normalize(address), change);
                    LastVisitedCells++;
                }
        }

        #endregion
    }
}
