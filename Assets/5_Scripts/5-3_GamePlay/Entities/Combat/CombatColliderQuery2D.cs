using System;
using System.Collections.Generic;
using FlatWorld.Geometry;
using Unity.Mathematics;
using UnityEngine;

/// <summary>一次攻击窗口内的 Collider2D 查询结果，身份仍由调用方的数据索引解释。</summary>
public readonly struct CombatColliderHit2D
{
    public readonly int CandidateId;
    public readonly float Fraction;
    public readonly Vector2 Point;

    public CombatColliderHit2D(int candidateId, float fraction, Vector2 point)
    {
        CandidateId = candidateId;
        Fraction = fraction;
        Point = point;
    }
}

/// <summary>把少量纯数据候选临时投影为 Collider2D，命中几何只由 Physics2D 查询。</summary>
public sealed class CombatColliderQuery2D : IDisposable
{
    #region 临时投影池

    private sealed class Projection
    {
        public readonly GameObject Root;
        public readonly BoxCollider2D Box;
        public readonly CircleCollider2D Circle;
        public Collider2D ActiveCollider;

        public Projection(int layer)
        {
            Root = new GameObject("Combat Collider Query") { hideFlags = HideFlags.HideInHierarchy, layer = layer };
            Box = Root.AddComponent<BoxCollider2D>();
            Box.isTrigger = true;
            Box.enabled = false;
            Circle = Root.AddComponent<CircleCollider2D>();
            Circle.isTrigger = true;
            Circle.enabled = false;
        }

        public void Bind(PerceptionShape2D shape)
        {
            Root.transform.position = new Vector3(shape.Center.x, shape.Center.y, 0f);
            Root.transform.rotation = Quaternion.identity;
            if (shape.IsCircle != 0)
            {
                Circle.radius = Mathf.Max(0.0001f, shape.Radius);
                ActiveCollider = Circle;
            }
            else
            {
                Box.size = new Vector2(Mathf.Max(0.0001f, shape.Extents.x * 2f),
                    Mathf.Max(0.0001f, shape.Extents.y * 2f));
                ActiveCollider = Box;
            }
            ActiveCollider.enabled = true;
        }

        public void Clear()
        {
            if (ActiveCollider != null) ActiveCollider.enabled = false;
            ActiveCollider = null;
        }
    }

    private static readonly Stack<CombatColliderQuery2D> Available = new();
    private static readonly List<CombatColliderQuery2D> All = new();
    private readonly List<Projection> projections = new();
    private readonly Dictionary<Collider2D, int> candidateIds = new();
    private readonly List<Collider2D> overlaps = new();
    private readonly List<RaycastHit2D> casts = new();
    private int activeCount;
    private bool rented;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPool()
    {
        for (int i = 0; i < All.Count; i++)
        {
            CombatColliderQuery2D query = All[i];
            query.Clear();
            for (int j = 0; j < query.projections.Count; j++)
            {
                GameObject root = query.projections[j].Root;
                if (root == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(root);
                else UnityEngine.Object.DestroyImmediate(root);
            }
            query.projections.Clear();
        }
        Available.Clear();
        All.Clear();
    }

    public static CombatColliderQuery2D Rent()
    {
        CombatPhysicsChannels.EnsureConfigured();
        if (CombatPhysicsChannels.DamageReceiverLayer < 0)
            throw new InvalidOperationException("战斗受击层缺失，无法建立 Collider2D 查询投影。");
        CombatColliderQuery2D query = Available.Count > 0 ? Available.Pop() : new CombatColliderQuery2D();
        if (!All.Contains(query)) All.Add(query);
        query.rented = true;
        return query;
    }

    /// <summary>空间索引只交出候选与数据形状，这里才临时建立物理几何。</summary>
    public void Add(int candidateId, PerceptionShape2D shape, float rotationDegrees = 0f)
    {
        if (!rented) throw new InvalidOperationException("Collider 查询必须先取得租约。");
        if (!math.all(math.isfinite(new float4(shape.Center, shape.Extents))) ||
            !math.isfinite(shape.Radius))
            throw new InvalidOperationException("受击范围包含无效坐标或尺寸。");
        if (activeCount == projections.Count)
            projections.Add(new Projection(CombatPhysicsChannels.DamageReceiverLayer));
        else if (projections[activeCount].Root == null)
            projections[activeCount] = new Projection(CombatPhysicsChannels.DamageReceiverLayer);
        Projection projection = projections[activeCount++];
        projection.Bind(shape);
        projection.Root.transform.rotation = Quaternion.Euler(0f, 0f, rotationDegrees);
        candidateIds.Add(projection.ActiveCollider, candidateId);
    }

    private void Clear()
    {
        for (int i = 0; i < activeCount; i++) projections[i].Clear();
        activeCount = 0;
        candidateIds.Clear();
        overlaps.Clear();
        casts.Clear();
        rented = false;
    }

    public void Dispose()
    {
        if (!rented) return;
        Clear();
        Available.Push(this);
    }

    #endregion

    #region Unity 几何查询

    /// <summary>一次 Overlap/BoxCast 返回真实接触候选，调用方再判断身份和伤害规则。</summary>
    public void Query(AttackShape2D attack, List<CombatColliderHit2D> results)
    {
        if (!rented) throw new InvalidOperationException("Collider 查询必须先取得租约。");
        if (results == null) throw new ArgumentNullException(nameof(results));
        results.Clear();
        if (activeCount == 0) return;

        Physics2D.SyncTransforms();
        var filter = new ContactFilter2D
        {
            useTriggers = true,
            useLayerMask = true,
            layerMask = 1 << CombatPhysicsChannels.DamageReceiverLayer
        };
        Vector2 size = new(Mathf.Max(0.0001f, attack.HalfExtents.x * 2f),
            Mathf.Max(0.0001f, attack.HalfExtents.y * 2f));
        float angle = attack.Rotation * Mathf.Rad2Deg;
        Vector2 travel = (Vector2)attack.SweepDelta;
        float distance = travel.magnitude;
        if (distance <= 0.0001f)
        {
            overlaps.Clear();
            Physics2D.OverlapBox((Vector2)attack.Center, size, angle, filter, overlaps);
            for (int i = 0; i < overlaps.Count; i++)
            {
                Collider2D collider = overlaps[i];
                if (collider != null && candidateIds.TryGetValue(collider, out int id))
                    results.Add(new CombatColliderHit2D(id, 0f, collider.ClosestPoint((Vector2)attack.Center)));
            }
            return;
        }

        casts.Clear();
        Vector2 start = (Vector2)(attack.Center - attack.SweepDelta);
        Physics2D.BoxCast(start, size, angle, travel / distance, filter, casts, distance);
        for (int i = 0; i < casts.Count; i++)
        {
            RaycastHit2D hit = casts[i];
            if (hit.collider != null && candidateIds.TryGetValue(hit.collider, out int id))
                results.Add(new CombatColliderHit2D(id, Mathf.Clamp01(hit.distance / distance),
                    hit.fraction <= 0f ? hit.collider.ClosestPoint(start) : hit.point));
        }
    }

    #endregion
}
