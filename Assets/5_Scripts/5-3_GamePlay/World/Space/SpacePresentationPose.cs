using System;

namespace FlatWorld.Spaceflight
{
    public readonly struct SpacePresentationPose
    {
        #region 船体表现姿态
        public readonly SpaceSectorPosition Position;
        public readonly double AngleRadians;

        public SpacePresentationPose(SpaceSectorPosition position, double angleRadians)
        { Position = position; AngleRadians = angleRadians; }

        public static SpacePresentationPose FromShip(ShipState ship)
        {
            // 以船体本地格原点保存画面姿态，重心变化不会拉动部件和乘员。
            ShipGeometry.LocalToWorld(ship, 0d, 0d, out double x, out double y);
            return new SpacePresentationPose(SpaceSectorCoordinates.Normalize(ship.Sector, new SpaceVector2(x, y)), ship.AngleRadians);
        }

        public SpaceSectorPosition TransformPoint(double localX, double localY)
        {
            ShipGeometry.Rotate(localX, localY, AngleRadians, out double x, out double y);
            return SpaceSectorCoordinates.Normalize(Position.Sector, Position.LocalMeters + new SpaceVector2(x, y));
        }

        public static SpacePresentationPose Interpolate(SpacePresentationPose previous, SpacePresentationPose current, double alpha)
        {
            // 非法插值进度退回当前权威姿态，避免 NaN 坐标进入星区归一化。
            if (double.IsNaN(alpha)) return current;
            alpha = Math.Clamp(alpha, 0d, 1d);
            if (alpha <= 0d) return previous;
            if (alpha >= 1d) return current;
            SpaceVector2 delta = SpaceSectorCoordinates.Difference(current.Position, previous.Position);
            SpaceSectorPosition position = SpaceSectorCoordinates.Normalize(previous.Position.Sector,
                previous.Position.LocalMeters + delta * alpha);
            double turn = Math.IEEERemainder(current.AngleRadians - previous.AngleRadians, Math.PI * 2d);
            return new SpacePresentationPose(position, previous.AngleRadians + turn * alpha);
        }
        #endregion
    }

    public sealed class SpacePresentationTrack
    {
        #region 固定步表现历史
        private bool initialized;
        public SpacePresentationPose Previous { get; private set; }
        public SpacePresentationPose Current { get; private set; }

        public void Reset(SpacePresentationPose pose)
        {
            Previous = Current = pose;
            initialized = true;
        }

        public void Advance(SpacePresentationPose pose)
        {
            if (!initialized) { Reset(pose); return; }
            Previous = Current;
            Current = pose;
        }

        public SpacePresentationPose Evaluate(double alpha)
            => SpacePresentationPose.Interpolate(Previous, Current, alpha);
        #endregion
    }
}
