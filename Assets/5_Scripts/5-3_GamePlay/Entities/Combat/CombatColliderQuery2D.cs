using System;
using System.Collections.Generic;
using FlatWorld.Geometry;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>一次攻击窗口内的 Collider2D 查询结果，身份仍由调用方的数据索引解释。</summary>
public readonly struct CombatColliderHit2D
{
    #region 接触候选
    public readonly int CandidateId;
    public readonly float Fraction;
    public readonly Vector2 Point;

    public CombatColliderHit2D(int candidateId, float fraction, Vector2 point)
    {
        CandidateId = candidateId;
        Fraction = fraction;
        Point = point;
    }
    #endregion
}

/// <summary>把少量纯数据候选临时投影为 Collider2D，命中几何只由 Physics2D 查询。</summary>
public sealed class CombatColliderQuery2D : IDisposable
{
    #region 临时投影池

    private sealed class Projection
    {
        public readonly GameObject Root;
        private BoxCollider2D box;
        private CircleCollider2D circle;
        public Collider2D ActiveCollider;

        public Projection(int layer, WorldPhysicsScene2D space)
        {
            Root = new GameObject("Combat Collider Query") { hideFlags = HideFlags.HideInHierarchy, layer = layer };
            Root.SetActive(false);
            SceneManager.MoveGameObjectToScene(Root, space.Scene);
        }

        public void Bind(PerceptionShape2D shape)
        {
            Root.transform.position = new Vector3(shape.Center.x, shape.Center.y, 0f);
            Root.transform.rotation = Quaternion.identity;
            // 首次形状在停用节点完成配置，避免先生成默认实体再重建为受击框。
            if (shape.IsCircle != 0 ? circle == null : box == null) Root.SetActive(false);
            if (shape.IsCircle != 0)
            {
                if (circle == null) { circle = Root.AddComponent<CircleCollider2D>(); circle.isTrigger = true; }
                circle.radius = Mathf.Max(0.0001f, shape.Radius);
                ActiveCollider = circle;
            }
            else
            {
                if (box == null) { box = Root.AddComponent<BoxCollider2D>(); box.isTrigger = true; }
                box.size = new Vector2(Mathf.Max(0.0001f, shape.Extents.x * 2f),
                    Mathf.Max(0.0001f, shape.Extents.y * 2f));
                ActiveCollider = box;
            }
            ActiveCollider.enabled = true;
            if (!Root.activeSelf) Root.SetActive(true);
        }

        public void Clear()
        {
            if (ActiveCollider != null) ActiveCollider.enabled = false;
            ActiveCollider = null;
        }
    }

    private readonly WorldPhysicsService2D owner;
    private WorldPhysicsScene2D Space => owner.PrimarySpace;
    private readonly List<Projection> projections = new();
    private Projection attackProjection;
    private readonly Dictionary<Collider2D, int> candidateIds = new();
    private readonly List<Collider2D> overlaps = new();
    private readonly List<RaycastHit2D> casts = new();
    private int activeCount;
    private bool rented;
    public string WorldKey => owner.WorldKey;

    internal CombatColliderQuery2D(WorldPhysicsService2D owner) => this.owner = owner;

    internal void ReleaseOwnedProjections()
    {
        Clear();
        for (int i = 0; i < projections.Count; i++)
        {
            GameObject root = projections[i].Root;
            if (root == null) continue;
            if (Application.isPlaying) UnityEngine.Object.Destroy(root);
            else UnityEngine.Object.DestroyImmediate(root);
        }
        projections.Clear();
        if (attackProjection != null)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(attackProjection.Root);
            else UnityEngine.Object.DestroyImmediate(attackProjection.Root);
            attackProjection = null;
        }
    }

    internal void BeginLease() => rented = true;

    /// <summary>空间索引只交出候选与数据形状，这里才临时建立物理几何。</summary>
    public void Add(int candidateId, PerceptionShape2D shape, float rotationDegrees = 0f)
    {
        if (!rented) throw new InvalidOperationException("Collider 查询必须先取得租约。");
        if (!owner.IsValid) throw new ObjectDisposedException(nameof(WorldPhysicsService2D));
        if (!math.all(math.isfinite(new float4(shape.Center, shape.Extents))) ||
            !math.isfinite(shape.Radius))
            throw new InvalidOperationException("受击范围包含无效坐标或尺寸。");
        if (activeCount == projections.Count)
            projections.Add(new Projection(CombatPhysicsChannels.DamageReceiverLayer, Space));
        else if (projections[activeCount].Root == null)
            projections[activeCount] = new Projection(CombatPhysicsChannels.DamageReceiverLayer, Space);
        Projection projection = projections[activeCount++];
        projection.Bind(shape);
        projection.Root.transform.rotation = Quaternion.Euler(0f, 0f, rotationDegrees);
        candidateIds.Add(projection.ActiveCollider, candidateId);
    }

    private void Clear()
    {
        for (int i = 0; i < activeCount; i++) projections[i].Clear();
        attackProjection?.Clear();
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
        owner.ReturnCombatDamageQuery(this);
    }

    #endregion

    #region Unity 几何查询

    /// <summary>优先使用重叠区域内的武器表面点，目标被武器包含时使用目标表面点。</summary>
    internal static Vector2 CalculateOverlapContactPoint(Collider2D attackCollider, Collider2D targetCollider)
    {
        ColliderDistance2D contact = attackCollider.Distance(targetCollider);
        Vector2 attackPoint = attackCollider.ClosestPoint(contact.pointA);
        return targetCollider.OverlapPoint(attackPoint) ? attackPoint : targetCollider.ClosestPoint(contact.pointB);
    }

    /// <summary>一次 Overlap/BoxCast 返回真实接触候选，调用方再判断身份和伤害规则。</summary>
    public void Query(AttackShape2D attack, List<CombatColliderHit2D> results)
    {
        if (!rented) throw new InvalidOperationException("Collider 查询必须先取得租约。");
        if (!owner.IsValid) throw new ObjectDisposedException(nameof(WorldPhysicsService2D));
        if (results == null) throw new ArgumentNullException(nameof(results));
        results.Clear();
        if (activeCount == 0) return;

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
        Vector2 start = (Vector2)(attack.Center - attack.SweepDelta);
        attackProjection ??= new Projection(CombatPhysicsChannels.DamageSenderLayer, Space);
        attackProjection.Bind(new PerceptionShape2D {
            Center = (float2)start, Extents = (float2)(size * 0.5f) });
        attackProjection.Root.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        Physics2D.SyncTransforms();
        if (distance <= 0.0001f)
        {
            overlaps.Clear();
            GameplayPhysics2D.OverlapBox(Space, (Vector2)attack.Center, size, angle, filter, overlaps);
            for (int i = 0; i < overlaps.Count; i++)
            {
                Collider2D collider = overlaps[i];
                if (collider != null && candidateIds.TryGetValue(collider, out int id))
                    results.Add(new CombatColliderHit2D(id, 0f,
                        CalculateOverlapContactPoint(attackProjection.ActiveCollider, collider)));
            }
            return;
        }

        casts.Clear();
        GameplayPhysics2D.BoxCast(Space, start, size, angle, travel / distance, filter, casts, distance);
        for (int i = 0; i < casts.Count; i++)
        {
            RaycastHit2D hit = casts[i];
            if (hit.collider != null && candidateIds.TryGetValue(hit.collider, out int id))
                results.Add(new CombatColliderHit2D(id, Mathf.Clamp01(hit.distance / distance),
                    hit.fraction <= 0f ? CalculateOverlapContactPoint(attackProjection.ActiveCollider, hit.collider) : hit.point));
        }
    }

    #endregion
}

public sealed partial class WorldPhysicsService2D
{
    #region 世界统一领取的受击碰撞集合
    private readonly Stack<CombatColliderQuery2D> availableCombatQueries = new();
    private readonly List<CombatColliderQuery2D> ownedCombatQueries = new();

    public CombatColliderQuery2D RentCombatDamageQuery()
    {
        if (!IsValid) throw new ObjectDisposedException(nameof(WorldPhysicsService2D));
        CombatPhysicsChannels.EnsureConfigured();
        if (CombatPhysicsChannels.DamageReceiverLayer < 0)
            throw new InvalidOperationException("战斗受击层缺失，无法领取碰撞集合。");
        CombatColliderQuery2D query;
        if (availableCombatQueries.Count > 0) query = availableCombatQueries.Pop();
        else { query = new CombatColliderQuery2D(this); ownedCombatQueries.Add(query); }
        query.BeginLease();
        return query;
    }

    internal void ReturnCombatDamageQuery(CombatColliderQuery2D query)
    {
        if (IsValid) availableCombatQueries.Push(query);
        else query.ReleaseOwnedProjections();
    }

    // 地板、建筑和生物共用同一个世界池，世界关闭时一起撤销租约和原生形状。
    private void DisposeCombatDamageQueries()
    {
        foreach (CombatColliderQuery2D query in ownedCombatQueries) query.ReleaseOwnedProjections();
        ownedCombatQueries.Clear();
        availableCombatQueries.Clear();
    }
    #endregion
}
