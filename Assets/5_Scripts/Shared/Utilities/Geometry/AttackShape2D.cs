using Unity.Mathematics;

namespace FlatWorld.Geometry
{
    /// <summary>标准近战的纯数据 OBB；角度使用弧度，AABB 只用于粗筛，精筛保留实际旋转。</summary>
    public struct AttackShape2D
    {
        public float2 Center, HalfExtents; // 世界中心及正半尺寸。
        public float Rotation; // 绕 Z 轴的弧度。
        public float2 AxisX => new float2(math.cos(Rotation), math.sin(Rotation));
        public float2 AxisY => new float2(-math.sin(Rotation), math.cos(Rotation));
        public float2 BoundsExtents => math.abs(AxisX) * HalfExtents.x + math.abs(AxisY) * HalfExtents.y;

        /// <summary>以四条分离轴检测 OBB/AABB，圆目标转换到武器局部空间做最近点检测。</summary>
        public bool Intersects(PerceptionShape2D target)
        {
            float2 delta = target.Center - Center;
            float2 local = new float2(math.dot(delta, AxisX), math.dot(delta, AxisY));
            if (target.IsCircle != 0)
            {
                float2 outside = math.max(math.abs(local) - HalfExtents, 0f);
                return math.lengthsq(outside) <= target.Radius * target.Radius;
            }
            float2 bounds = BoundsExtents;
            if (math.any(math.abs(delta) > bounds + target.Extents)) return false;
            float2 projection = new float2(math.dot(math.abs(AxisX), target.Extents), math.dot(math.abs(AxisY), target.Extents));
            return math.all(math.abs(local) <= HalfExtents + projection);
        }
    }
}
