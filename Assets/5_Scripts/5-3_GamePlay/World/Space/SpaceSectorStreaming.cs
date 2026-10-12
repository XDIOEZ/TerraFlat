using System;
using System.Collections.Generic;

namespace FlatWorld.Spaceflight
{
    // 场景租约只跟玩家和边界过渡有关，无人星区仍由会话推进数据。
    public sealed class SpaceSectorStreaming
    {
        #region 玩家星区租约
        public const double TransitionMeters = 4096d;
        public const double PrefetchMeters = 8192d;
        private sealed class Viewer
        {
            public SpaceSectorId Current;
            public readonly HashSet<SpaceSectorId> Retained = new();
        }
        private readonly Dictionary<string, Viewer> viewers = new(StringComparer.Ordinal);
        private readonly HashSet<string> seen = new(StringComparer.Ordinal);
        private readonly List<string> removed = new();
        public readonly HashSet<SpaceSectorId> Required = new();
        public void BeginFrame() { Required.Clear(); seen.Clear(); }
        public void Observe(string profile, SpaceSectorPosition position, SpaceVector2 velocity)
        {
            if (string.IsNullOrWhiteSpace(profile)) return;
            position = SpaceSectorCoordinates.Normalize(position.Sector, position.LocalMeters);
            seen.Add(profile);
            if (!viewers.TryGetValue(profile, out Viewer viewer))
                viewers.Add(profile, viewer = new Viewer { Current = position.Sector });
            if (viewer.Current != position.Sector)
            {
                viewer.Retained.Add(viewer.Current);
                viewer.Current = position.Sector;
            }
            double half = SpaceSectorCoordinates.SizeMeters * .5d;
            double edge = half - Math.Max(Math.Abs(position.LocalMeters.X), Math.Abs(position.LocalMeters.Y));
            if (edge > TransitionMeters) viewer.Retained.Clear();
            Required.Add(viewer.Current);
            foreach (SpaceSectorId retained in viewer.Retained) Required.Add(retained);
            double lead = Math.Min(half * .5d, Math.Max(PrefetchMeters, velocity.Magnitude * 8d + TransitionMeters));
            int dx = position.LocalMeters.X <= -half + lead ? -1 : position.LocalMeters.X >= half - lead ? 1 : 0;
            int dy = position.LocalMeters.Y <= -half + lead ? -1 : position.LocalMeters.Y >= half - lead ? 1 : 0;
            bool towardX = dx != 0 && (velocity.X * dx >= 0d || edge <= TransitionMeters);
            bool towardY = dy != 0 && (velocity.Y * dy >= 0d || edge <= TransitionMeters);
            if (towardX && CanOffset(viewer.Current, dx, 0, out SpaceSectorId x)) Required.Add(x);
            if (towardY && CanOffset(viewer.Current, 0, dy, out SpaceSectorId y)) Required.Add(y);
            if (towardX && towardY && CanOffset(viewer.Current, dx, dy, out SpaceSectorId corner)) Required.Add(corner);
        }
        public void EndFrame()
        {
            removed.Clear();
            foreach (string profile in viewers.Keys) if (!seen.Contains(profile)) removed.Add(profile);
            foreach (string profile in removed) viewers.Remove(profile);
        }
        public void Clear() { viewers.Clear(); Required.Clear(); seen.Clear(); removed.Clear(); }
        private static bool CanOffset(SpaceSectorId sector, int x, int y, out SpaceSectorId result)
        {
            result = default;
            if (x < 0 && sector.X == long.MinValue || x > 0 && sector.X == long.MaxValue ||
                y < 0 && sector.Y == long.MinValue || y > 0 && sector.Y == long.MaxValue) return false;
            result = new SpaceSectorId(sector.X + x, sector.Y + y); return true;
        }
        #endregion
    }
}
