using Unity.Mathematics;

namespace FlatWorld.Geometry
{
    /// <summary>标准近战的纯数据 OBB；角度使用弧度，AABB 只用于粗筛，精筛保留实际旋转。</summary>
    public struct AttackShape2D
    {
        public float2 Center, HalfExtents; // 世界中心及正半尺寸。
        public float Rotation; // 绕 Z 轴的弧度。
        public float2 SweepDelta; // 上一位置到 Center 的平移；零值仍是普通近战 OBB。
        public float2 AxisX => new float2(math.cos(Rotation), math.sin(Rotation));
        public float2 AxisY => new float2(-math.sin(Rotation), math.cos(Rotation));
        public float2 BoundsCenter => Center - SweepDelta * 0.5f;
        public float2 BoundsExtents => math.abs(AxisX) * HalfExtents.x + math.abs(AxisY) * HalfExtents.y + math.abs(SweepDelta) * 0.5f;

        /// <summary>普通窗口和高速投射物共享精筛；不把扫掠的大包围盒误当成实际伤害区域。</summary>
        public bool Intersects(PerceptionShape2D target) => TryIntersect(target, out _);

        /// <summary>返回平移 OBB 首次接触比例，供后端先命中最近目标，再消费共享攻击配额。</summary>
        public bool TryIntersect(PerceptionShape2D target, out float fraction)
        {
            float2 delta = target.Center - (Center - SweepDelta);
            float2 local = new float2(math.dot(delta, AxisX), math.dot(delta, AxisY));
            if (target.IsCircle != 0)
            {
                float2 outside = math.max(math.abs(local) - HalfExtents, 0f);
                fraction = 0f;
                if (math.lengthsq(outside) <= target.Radius * target.Radius) return true;
                // 圆目标与移动矩形等价于线段进入圆角矩形：两条矩形带加四个角圆的精确并集。
                float2 velocity = -new float2(math.dot(SweepDelta, AxisX), math.dot(SweepDelta, AxisY));
                float best = float.PositiveInfinity;
                float radius = math.max(0f, target.Radius);
                if (SegmentBox(local, velocity, HalfExtents + new float2(radius, 0), out float t)) best = t;
                if (SegmentBox(local, velocity, HalfExtents + new float2(0, radius), out t)) best = math.min(best, t);
                for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                        if (SegmentCircle(local - HalfExtents * new float2(x, y), velocity, radius, out t))
                            best = math.min(best, t);
                fraction = best;
                return best <= 1f;
            }
            float enter = 0f, exit = 1f;
            float2 boxBounds = math.abs(AxisX) * HalfExtents.x + math.abs(AxisY) * HalfExtents.y;
            bool hit = Clip(delta.x, SweepDelta.x, boxBounds.x + target.Extents.x, ref enter, ref exit) &&
                Clip(delta.y, SweepDelta.y, boxBounds.y + target.Extents.y, ref enter, ref exit) &&
                Clip(local.x, math.dot(SweepDelta, AxisX), HalfExtents.x + math.dot(math.abs(AxisX), target.Extents), ref enter, ref exit) &&
                Clip(local.y, math.dot(SweepDelta, AxisY), HalfExtents.y + math.dot(math.abs(AxisY), target.Extents), ref enter, ref exit);
            fraction = enter;
            return hit;
        }

        #region 连续碰撞的区间运算

        private static bool Clip(float offset, float speed, float radius, ref float enter, ref float exit)
        {
            if (math.abs(speed) < 0.000001f) return math.abs(offset) <= radius;
            float a = (offset - radius) / speed, b = (offset + radius) / speed;
            enter = math.max(enter, math.min(a, b));
            exit = math.min(exit, math.max(a, b));
            return enter <= exit;
        }

        private static bool SegmentBox(float2 start, float2 velocity, float2 extents, out float fraction)
        {
            float enter = 0f, exit = 1f;
            bool hit = Clip(start.x, -velocity.x, extents.x, ref enter, ref exit) &&
                Clip(start.y, -velocity.y, extents.y, ref enter, ref exit);
            fraction = enter;
            return hit;
        }

        private static bool SegmentCircle(float2 start, float2 velocity, float radius, out float fraction)
        {
            fraction = float.PositiveInfinity;
            float a = math.lengthsq(velocity), b = math.dot(start, velocity), c = math.lengthsq(start) - radius * radius;
            if (c <= 0f) { fraction = 0f; return true; }
            if (a < 0.0000001f) return false;
            float discriminant = b * b - a * c;
            if (discriminant < 0f) return false;
            fraction = (-b - math.sqrt(discriminant)) / a;
            return fraction >= 0f && fraction <= 1f;
        }

        #endregion
    }
}
