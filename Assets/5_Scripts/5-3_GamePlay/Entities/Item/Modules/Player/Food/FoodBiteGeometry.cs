using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>只读取 Sprite 网格边界生成确定性圆形咬痕，不遍历纹理像素。</summary>
internal sealed class FoodBiteGeometry
{
    #region 网格轮廓
    private readonly Vector2[] vertices;
    private readonly List<uint> boundaryEdges = new();
    private readonly float area;
    private readonly Vector2 protectedInterior;

    internal FoodBiteGeometry(Sprite sprite)
    {
        vertices = sprite.vertices;
        ushort[] triangles = sprite.triangles;
        var edges = new Dictionary<uint, int>();
        float largestTriangleArea = 0f;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
            Vector2 first = vertices[b] - vertices[a], second = vertices[c] - vertices[a];
            float triangleArea = Mathf.Abs(first.x * second.y - first.y * second.x) * .5f;
            area += triangleArea;
            if (triangleArea > largestTriangleArea)
            {
                largestTriangleArea = triangleArea;
                protectedInterior = (vertices[a] + vertices[b] + vertices[c]) / 3f;
            }
            CountEdge(a, b);
            CountEdge(b, c);
            CountEdge(c, a);
        }
        foreach (var edge in edges)
            if (edge.Value == 1) boundaryEdges.Add(edge.Key);
        boundaryEdges.Sort();

        void CountEdge(int a, int b)
        {
            uint key = ((uint)Math.Min(a, b) << 16) | (uint)Math.Max(a, b);
            edges.TryGetValue(key, out int count);
            edges[key] = count + 1;
        }
    }
    #endregion

    #region 圆形咬痕
    internal Vector4[] BuildBiteCircles(int completedBites, int maximumBites, int randomSeed)
    {
        var circles = new List<Vector4>(completedBites);
        var remainingEdges = new List<(Vector2 Start, Vector2 End)>();
        foreach (uint edge in boundaryEdges)
            remainingEdges.Add((vertices[edge >> 16], vertices[edge & 65535]));
        var random = new System.Random(randomSeed);
        float radius = Mathf.Sqrt(2f * area / (Mathf.PI * maximumBites));
        for (int bite = 0; bite < completedBites && remainingEdges.Count > 0; bite++)
        {
            float perimeter = 0f;
            foreach (var edge in remainingEdges) perimeter += Vector2.Distance(edge.Start, edge.End);
            float distance = (float)random.NextDouble() * perimeter;
            Vector2 center = remainingEdges[0].Start;
            foreach (var edge in remainingEdges)
            {
                float length = Vector2.Distance(edge.Start, edge.End);
                if (distance <= length)
                { center = Vector2.Lerp(edge.Start, edge.End, distance / Mathf.Max(length, .000001f)); break; }
                distance -= length;
            }
            // 非最终一口保留一个真实三角形内部点，最后一口由 Shader 统一隐藏。
            float safeRadius = Mathf.Min(radius, Vector2.Distance(center, protectedInterior) * .9f);
            float radiusSquared = safeRadius * safeRadius;
            circles.Add(new Vector4(center.x, center.y, radiusSquared, 0f));
            RemoveCoveredBoundaryIntervals(remainingEdges, center, radiusSquared);
        }
        return circles.ToArray();
    }

    private static void RemoveCoveredBoundaryIntervals(List<(Vector2 Start, Vector2 End)> edges,
        Vector2 center, float radiusSquared)
    {
        for (int i = edges.Count - 1; i >= 0; i--)
        {
            var edge = edges[i];
            Vector2 delta = edge.End - edge.Start, offset = edge.Start - center;
            float a = delta.sqrMagnitude, b = Vector2.Dot(offset, delta);
            float discriminant = b * b - a * (offset.sqrMagnitude - radiusSquared);
            if (a <= 0f || discriminant <= 0f) continue;
            float root = Mathf.Sqrt(discriminant);
            float enter = Mathf.Max(0f, (-b - root) / a), exit = Mathf.Min(1f, (-b + root) / a);
            if (enter >= exit) continue;
            edges.RemoveAt(i);
            if (exit < 1f) edges.Insert(i, (Vector2.Lerp(edge.Start, edge.End, exit), edge.End));
            if (enter > 0f) edges.Insert(i, (edge.Start, Vector2.Lerp(edge.Start, edge.End, enter)));
        }
    }
    #endregion
}
