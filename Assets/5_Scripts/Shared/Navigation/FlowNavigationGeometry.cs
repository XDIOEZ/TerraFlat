using Unity.Mathematics;

namespace FlatWorld.Navigation
{
    /// <summary>
    /// 纯二维扫掠几何，供 Burst 移动约束使用，不读取网格或业务对象。
    /// 圆心线段到阻挡 AABB 的最短距离小于半径即发生扫掠重叠，避免只检查移动终点漏掉墙角。
    /// </summary>
    internal static class FlowNavigationGeometry
    {
        #region 线段与矩形距离
        /// <summary>计算闭线段到轴对齐矩形的精确平方距离；相交时为零。</summary>
        internal static float SegmentAabbDistanceSquared(float2 from, float2 to, float2 min, float2 max)
        {
            if (IntersectsAabb(from, to, min, max)) return 0f;
            float distance = math.min(math.lengthsq(from - math.clamp(from, min, max)),
                math.lengthsq(to - math.clamp(to, min, max)));
            distance = math.min(distance, PointSegmentDistanceSquared(min, from, to));
            distance = math.min(distance, PointSegmentDistanceSquared(max, from, to));
            distance = math.min(distance, PointSegmentDistanceSquared(new float2(min.x, max.y), from, to));
            return math.min(distance, PointSegmentDistanceSquared(new float2(max.x, min.y), from, to));
        }

        /// <summary>将线段参数裁剪到矩形两轴区间，显式处理平行轴以免除零。</summary>
        private static bool IntersectsAabb(float2 from, float2 to, float2 min, float2 max)
        {
            float enter = 0f, leave = 1f;
            float2 delta = to - from;
            for (int axis = 0; axis < 2; axis++)
            {
                if (delta[axis] == 0f)
                {
                    if (from[axis] < min[axis] || from[axis] > max[axis]) return false;
                    continue;
                }
                float first = (min[axis] - from[axis]) / delta[axis];
                float last = (max[axis] - from[axis]) / delta[axis];
                enter = math.max(enter, math.min(first, last));
                leave = math.min(leave, math.max(first, last));
                if (enter > leave) return false;
            }
            return true;
        }

        /// <summary>计算点到闭线段的平方距离，零长度线段按端点处理。</summary>
        private static float PointSegmentDistanceSquared(float2 point, float2 from, float2 to)
        {
            float2 delta = to - from;
            float lengthSquared = math.lengthsq(delta);
            float parameter = lengthSquared > 0f ? math.saturate(math.dot(point - from, delta) / lengthSquared) : 0f;
            return math.lengthsq(point - (from + delta * parameter));
        }
        #endregion
    }
}
