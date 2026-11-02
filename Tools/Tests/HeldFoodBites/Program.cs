using System.Diagnostics;
using UnityEngine;

internal static class Program
{
    #region 正式几何算法回归
    private static void Main()
    {
        var sprite = new Sprite(new[] { new Vector2(-1, -1), new Vector2(1, -1),
            new Vector2(1, 1), new Vector2(-1, 1) }, new ushort[] { 0, 1, 2, 0, 2, 3 });
        var geometry = new FoodBiteGeometry(sprite);
        Check(geometry.BuildBiteCircles(0, 3, 7).Length == 0, "zero progress");
        Vector4[] first = geometry.BuildBiteCircles(1, 3, 7);
        Vector4[] second = geometry.BuildBiteCircles(2, 3, 7);
        Check(Equal(first[0], second[0]), "progress preserves previous bites");
        Check(second.SequenceEqual(geometry.BuildBiteCircles(2, 3, 7)), "deterministic reconstruction");
        Check(!Equal(first[0], geometry.BuildBiteCircles(1, 3, 8)[0]), "independent seeds");
        Check(geometry.BuildBiteCircles(40, 1025, 123).Length == 40, "more than 32 circles");
        var protectedPoint = new Vector2(1f / 3, -1f / 3);
        for (int seed = 0; seed < 100; seed++)
        {
            foreach (Vector4 circle in geometry.BuildBiteCircles(40, 1025, seed))
            {
                Check(Math.Abs(Math.Abs(circle.x) - 1) < .00001f ||
                    Math.Abs(Math.Abs(circle.y) - 1) < .00001f, "original perimeter, no internal diagonal");
                Check(circle.z > 0 && circle.z < (new Vector2(circle.x, circle.y) - protectedPoint).sqrMagnitude,
                    "non-final bite preserves visible interior");
            }
        }
        Check(sprite.VertexReads == 1 && sprite.TriangleReads == 1, "geometry reads mesh once");
        var concave = new FoodBiteGeometry(new Sprite(new[] { new Vector2(0, 0), new Vector2(2, 0),
            new Vector2(2, 1), new Vector2(1, 1), new Vector2(1, 2), new Vector2(0, 2) },
            new ushort[] { 0, 1, 3, 1, 2, 3, 0, 3, 5, 3, 4, 5 }));
        for (int seed = 0; seed < 100; seed++)
        {
            Vector4 circle = concave.BuildBiteCircles(1, 3, seed)[0];
            bool border = Math.Abs(circle.x) < .00001f || Math.Abs(circle.y) < .00001f ||
                Math.Abs(circle.x - 2) < .00001f && circle.y <= 1 ||
                Math.Abs(circle.y - 2) < .00001f && circle.x <= 1 ||
                Math.Abs(circle.x - 1) < .00001f && circle.y >= 1 ||
                Math.Abs(circle.y - 1) < .00001f && circle.x >= 1;
            Check(border, "concave sprite contour");
        }
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < 10000; i++) new FoodBiteGeometry(sprite).BuildBiteCircles(2, 3, i);
        Console.WriteLine($"PASS: production geometry; 100 seeds, 40 circles, concave contour, repeat/reload and mesh read counts. 10,000 two-bite builds: {watch.Elapsed.TotalMilliseconds:F1} ms.");
    }
    private static bool Equal(Vector4 a, Vector4 b) => a.x == b.x && a.y == b.y && a.z == b.z;
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); }
    #endregion
}

namespace UnityEngine
{
    #region 无 Unity 进程的值类型与网格夹具
    internal readonly record struct Vector2(float x, float y)
    {
        public float sqrMagnitude => x * x + y * y;
        public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x - b.x, a.y - b.y);
        public static Vector2 operator /(Vector2 a, float divisor) => new(a.x / divisor, a.y / divisor);
        public static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
        public static float Distance(Vector2 a, Vector2 b) => MathF.Sqrt((a - b).sqrMagnitude);
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t)
            => new(a.x + (b.x - a.x) * Math.Clamp(t, 0, 1), a.y + (b.y - a.y) * Math.Clamp(t, 0, 1));
    }
    internal readonly record struct Vector4(float x, float y, float z, float w);
    internal static class Mathf
    {
        internal const float PI = MathF.PI;
        internal static float Abs(float value) => MathF.Abs(value);
        internal static float Sqrt(float value) => MathF.Sqrt(value);
        internal static float Min(float a, float b) => MathF.Min(a, b);
        internal static float Max(float a, float b) => MathF.Max(a, b);
    }
    internal sealed class Sprite(Vector2[] points, ushort[] indices)
    {
        internal int VertexReads, TriangleReads;
        internal Vector2[] vertices { get { VertexReads++; return points; } }
        internal ushort[] triangles { get { TriangleReads++; return indices; } }
    }
    #endregion
}
