using System;
using System.Globalization;
using Newtonsoft.Json;

namespace FlatWorld.Spaceflight
{
    [Serializable]
    public struct SpaceSectorId : IEquatable<SpaceSectorId>
    {
        #region 稳定星区身份
        public long X, Y;
        public SpaceSectorId(long x, long y) { X = x; Y = y; }
        public bool Equals(SpaceSectorId other) => X == other.X && Y == other.Y;
        public override bool Equals(object value) => value is SpaceSectorId other && Equals(other);
        public override int GetHashCode() => unchecked(X.GetHashCode() * 397 ^ Y.GetHashCode());
        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "({0}, {1})", X, Y);
        public static bool operator ==(SpaceSectorId a, SpaceSectorId b) => a.Equals(b);
        public static bool operator !=(SpaceSectorId a, SpaceSectorId b) => !a.Equals(b);
        #endregion
    }

    [Serializable]
    public struct SpaceSectorPosition
    {
        #region 星区内双精度位置
        public SpaceSectorId Sector;
        public SpaceVector2 LocalMeters;
        public SpaceSectorPosition(SpaceSectorId sector, SpaceVector2 localMeters)
        { Sector = sector; LocalMeters = localMeters; }
        [JsonIgnore] public bool IsFinite => LocalMeters.IsFinite;
        #endregion
    }

    public static class SpaceSectorCoordinates
    {
        #region 星区换算与附近距离
        public const double SizeMeters = 256000d;
        public const double HalfSizeMeters = SizeMeters * .5d;
        private const string ScenePrefix = "SpaceSector__";

        public static SpaceSectorPosition Normalize(SpaceSectorId sector, SpaceVector2 localMeters)
        {
            if (!localMeters.IsFinite) throw new ArgumentOutOfRangeException(nameof(localMeters));
            NormalizeAxis(sector.X, localMeters.X, out long x, out double localX);
            NormalizeAxis(sector.Y, localMeters.Y, out long y, out double localY);
            return new SpaceSectorPosition(new SpaceSectorId(x, y), new SpaceVector2(localX, localY));
        }

        public static void Normalize(ref SpaceSectorId sector, ref SpaceVector2 localMeters)
        {
            SpaceSectorPosition point = Normalize(sector, localMeters);
            sector = point.Sector; localMeters = point.LocalMeters;
        }

        private static void NormalizeAxis(long origin, double local, out long sector, out double normalized)
        {
            double crossings = Math.Floor((local + HalfSizeMeters) / SizeMeters);
            if (crossings < long.MinValue || crossings >= 9223372036854775808d)
                throw new OverflowException("单步跨越星区数量超过坐标范围。");
            sector = checked(origin + (long)crossings);
            normalized = local - crossings * SizeMeters;
            if (!SpaceVector2.Finite(normalized)) throw new OverflowException("星区局部坐标溢出。");
        }

        // 先精确计算整数编号差，再转附近坐标，避免遥远星区吃掉米级精度。
        public static SpaceVector2 Relative(SpaceSectorId source, SpaceVector2 localMeters, SpaceSectorId reference)
        {
            if (!localMeters.IsFinite) throw new ArgumentOutOfRangeException(nameof(localMeters));
            double dx = AxisDifference(source.X, reference.X), dy = AxisDifference(source.Y, reference.Y);
            return new SpaceVector2(localMeters.X + dx * SizeMeters, localMeters.Y + dy * SizeMeters);
        }

        private static double AxisDifference(long source, long reference)
        {
            // 相邻星区走纯整数热路径，只有两端编号差溢出时才使用 decimal。
            long difference = unchecked(source - reference);
            bool overflow = ((source ^ reference) & (source ^ difference)) < 0;
            return overflow ? (double)((decimal)source - reference) : difference;
        }

        public static SpaceVector2 Relative(SpaceSectorPosition position, SpaceSectorId reference)
            => Relative(position.Sector, position.LocalMeters, reference);
        public static SpaceVector2 Difference(SpaceSectorPosition left, SpaceSectorPosition right)
            => Relative(left, right.Sector) - right.LocalMeters;
        public static bool AreNeighbors(SpaceSectorId left, SpaceSectorId right)
            => Math.Abs(AxisDifference(left.X, right.X)) <= 1d && Math.Abs(AxisDifference(left.Y, right.Y)) <= 1d;

        public static string SceneKey(SpaceSectorId sector)
            => string.Format(CultureInfo.InvariantCulture, "{0}{1}__{2}", ScenePrefix, sector.X, sector.Y);
        public static bool TryParseSceneKey(string value, out SpaceSectorId sector)
        {
            sector = default;
            if (value == null || !value.StartsWith(ScenePrefix, StringComparison.Ordinal)) return false;
            string[] parts = value.Substring(ScenePrefix.Length).Split(new[] { "__" }, StringSplitOptions.None);
            if (parts.Length != 2 || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long x) ||
                !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long y)) return false;
            sector = new SpaceSectorId(x, y); return true;
        }

        public static string BodyId(SpaceSectorId sector, string templateBodyId)
            => string.Format(CultureInfo.InvariantCulture, "sector:{0}:{1}/body:{2}", sector.X, sector.Y, templateBodyId);
        public static string PlanetId(SpaceSectorId sector, string templatePlanetId)
            => string.Format(CultureInfo.InvariantCulture, "sector:{0}:{1}/planet:{2}", sector.X, sector.Y, templatePlanetId);
        public static bool TryParseIdentity(string identity, out SpaceSectorId sector, out string localId)
        {
            sector = default; localId = null;
            if (identity == null || !identity.StartsWith("sector:", StringComparison.Ordinal)) return false;
            int slash = identity.IndexOf('/'), colon = identity.IndexOf(':', slash >= 0 ? slash : identity.Length);
            if (slash < 0 || colon < 0) return false;
            string[] coordinates = identity.Substring(7, slash - 7).Split(':');
            if (coordinates.Length != 2 || !long.TryParse(coordinates[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long x) ||
                !long.TryParse(coordinates[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long y)) return false;
            sector = new SpaceSectorId(x, y); localId = identity.Substring(colon + 1); return localId.Length > 0;
        }
        #endregion
    }
}
