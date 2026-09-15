using System;
using System.Collections.Generic;
using FlatWorld.Geometry;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// 将 Actor 当前外壳的根级感知形状编译成共享纯数据；只在定义加载或旧外壳结构变化时读取作者配置。
/// 不读取 Collider2D.bounds/ClosestPoint，不持有碰撞体，不把攻击盒或子级生命受击盒扩大成感知体型。
/// </summary>
public static class ActorPerceptionShapeCompiler
{
    /// <summary>在内容加载边界编译根级几何，叠加当前 JSON 对根 Collider 的声明式覆盖。</summary>
    public static PerceptionShape2D[] Compile(GameObject shell, ItemColliderDefinitionDto declaration = null)
    {
        var shapes = new List<PerceptionShape2D>();
        bool rootOverride = declaration != null && string.IsNullOrWhiteSpace(declaration.Path);
        bool applied = false;
        foreach (Collider2D collider in shell.GetComponents<Collider2D>())
        {
            bool matches = rootOverride && !applied && (string.IsNullOrWhiteSpace(declaration.Type) || collider.GetType().Name == declaration.Type);
            ItemColliderDefinitionDto config = matches ? declaration : null;
            applied |= matches;
            if (!(config?.Enabled ?? collider.enabled)) continue;
            shapes.Add(CompileShape(collider, config));
        }
        if (rootOverride && !applied)
            throw new InvalidOperationException($"Actor 根级感知声明找不到作者碰撞体 {declaration.Type}，请先修复外壳或配置。");
        return shapes.ToArray();
    }

    /// <summary>从本地尺寸生成圆形或 AABB；复杂形状保持显式包围盒语义。</summary>
    private static PerceptionShape2D CompileShape(Collider2D collider, ItemColliderDefinitionDto config)
    {
        string type = string.IsNullOrWhiteSpace(config?.Type) ? collider.GetType().Name : config.Type;
        Vector2 offset = config?.Offset ?? (collider != null ? collider.offset : Vector2.zero);
        PerceptionShape2D result;
        switch (type)
        {
            case nameof(CircleCollider2D):
                float radius = config?.Radius ?? (collider is CircleCollider2D circle ? circle.radius : 0.5f);
                result = PerceptionShape2D.Circle(new float2(offset.x, offset.y), radius);
                break;
            case nameof(BoxCollider2D):
                Vector2 boxSize = config?.Size ?? (collider is BoxCollider2D box ? box.size : Vector2.one);
                float edge = config?.EdgeRadius ?? (collider is BoxCollider2D rounded ? rounded.edgeRadius : 0f);
                result = PerceptionShape2D.Aabb(new float2(offset.x, offset.y), new float2(boxSize.x, boxSize.y) * 0.5f + edge);
                break;
            case nameof(CapsuleCollider2D):
                Vector2 capsuleSize = config?.Size ?? (collider is CapsuleCollider2D capsule ? capsule.size : Vector2.one);
                result = PerceptionShape2D.Aabb(new float2(offset.x, offset.y), new float2(capsuleSize.x, capsuleSize.y) * 0.5f);
                break;
            case nameof(PolygonCollider2D):
                if (config?.Points != null && config.Points.Count > 0)
                    result = BoundsOfPoints(config.Points, offset, 0f);
                else if (collider is PolygonCollider2D polygon)
                {
                    var points = new List<Vector2>();
                    for (int path = 0; path < polygon.pathCount; path++) points.AddRange(polygon.GetPath(path));
                    result = BoundsOfPoints(points, offset, 0f);
                }
                else throw new InvalidOperationException("Actor Polygon 感知配置缺少顶点。");
                break;
            case nameof(EdgeCollider2D):
                var edgeCollider = collider as EdgeCollider2D;
                IReadOnlyList<Vector2> vertices = config?.Points ?? (IReadOnlyList<Vector2>)edgeCollider?.points;
                result = BoundsOfPoints(vertices, offset, config?.EdgeRadius ?? edgeCollider?.edgeRadius ?? 0f);
                break;
            default:
                throw new InvalidOperationException($"Actor 感知形状 {type} 尚未支持纯数据编译，不能回退为 Physics2D AI 感知。");
        }
        if (!math.all(math.isfinite(result.Center)) || !math.all(math.isfinite(result.Extents)) || !math.isfinite(result.Radius))
            throw new InvalidOperationException("Actor 感知形状包含非有限数。");
        return result;
    }

    /// <summary>把作者顶点转换为紧致 AABB，空形状直接报告配置错误。</summary>
    private static PerceptionShape2D BoundsOfPoints(IReadOnlyList<Vector2> points, Vector2 offset, float edge)
    {
        if (points == null || points.Count == 0) throw new InvalidOperationException("Actor 感知形状缺少顶点。");
        float2 min = new float2(float.PositiveInfinity);
        float2 max = new float2(float.NegativeInfinity);
        for (int index = 0; index < points.Count; index++)
        {
            float2 point = new float2(points[index].x, points[index].y);
            min = math.min(min, point); max = math.max(max, point);
        }
        return PerceptionShape2D.Aabb((min + max) * 0.5f + new float2(offset.x, offset.y), (max - min) * 0.5f + edge);
    }
}
