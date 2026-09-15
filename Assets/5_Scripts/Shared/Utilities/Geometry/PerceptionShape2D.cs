using Unity.Mathematics;

namespace FlatWorld.Geometry
{
    /// <summary>
    /// 可进入 Burst 的圆形/AABB 几何，不引用 Item、Transform 或 Physics2D。
    /// Center/Extents 使用调用方的坐标空间；圆形非均匀缩放使用最大伸长率的包围圆。
    /// </summary>
    public struct PerceptionShape2D
    {
        // 几何中心与包围半尺寸。
        public float2 Center;
        public float2 Extents;
        // 圆形半径；IsCircle 为 0 时使用 AABB。
        public float Radius;
        public byte IsCircle;

        /// <summary>创建纯数据轴对齐矩形。</summary>
        public static PerceptionShape2D Aabb(float2 center, float2 extents)
        {
            return new PerceptionShape2D { Center = center, Extents = math.abs(extents) };
        }

        /// <summary>创建圆形，同时提供与圆一致的粗筛包围范围。</summary>
        public static PerceptionShape2D Circle(float2 center, float radius)
        {
            radius = math.max(0f, radius);
            return new PerceptionShape2D { Center = center, Extents = new float2(radius), Radius = radius, IsCircle = 1 };
        }

        /// <summary>使用冻结的二维仿射基向量变换形状，支持负缩放、旋转和父级剪切。</summary>
        public PerceptionShape2D Transform(float2 translation, float2 axisX, float2 axisY)
        {
            float2 center = translation + axisX * Center.x + axisY * Center.y;
            if (IsCircle == 0)
                return Aabb(center, math.abs(axisX) * Extents.x + math.abs(axisY) * Extents.y);
            float xx = math.lengthsq(axisX);
            float yy = math.lengthsq(axisY);
            float xy = math.dot(axisX, axisY);
            float stretch = math.sqrt(math.max(0f, 0.5f * (xx + yy + math.sqrt((xx - yy) * (xx - yy) + 4f * xy * xy))));
            return Circle(center, Radius * stretch);
        }

        /// <summary>完成圆形查询的最终距离判断，接触边界也算命中。</summary>
        public bool IntersectsCircle(float2 center, float radius)
        {
            if (IsCircle != 0)
            {
                float combined = radius + Radius;
                return math.lengthsq(Center - center) <= combined * combined;
            }
            float2 delta = math.max(math.abs(Center - center) - Extents, 0f);
            return math.lengthsq(delta) <= radius * radius;
        }
    }
}
