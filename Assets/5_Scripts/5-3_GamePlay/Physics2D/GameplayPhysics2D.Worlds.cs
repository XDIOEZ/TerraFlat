using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class GameplayPhysics2D
{
    #region 所属世界的物理查询
    private static PhysicsScene2D QueryScene => GameplayWorldRegistry.CurrentScene.GetPhysicsScene2D();
    private static ContactFilter2D LayerFilter(int mask)
    {
        var filter = new ContactFilter2D { useTriggers = Physics2D.queriesHitTriggers };
        filter.SetLayerMask(mask); return filter;
    }
    // 所有查询保留原筛选和结果缓冲，只把物理世界切到当前同步上下文。
    public static int OverlapPointNonAlloc(Vector2 point, Collider2D[] results, int mask = Physics2D.DefaultRaycastLayers)
        => QueryScene.OverlapPoint(point, LayerFilter(mask), results);
    public static int OverlapCircleNonAlloc(Vector2 point, float radius, Collider2D[] results, int mask = Physics2D.DefaultRaycastLayers)
        => QueryScene.OverlapCircle(point, radius, LayerFilter(mask), results);
    public static int OverlapBox(Vector2 point, Vector2 size, float angle, ContactFilter2D filter, List<Collider2D> results)
        => QueryScene.OverlapBox(point, size, angle, filter, results);
    public static int OverlapBox(Vector2 point, Vector2 size, float angle, ContactFilter2D filter, Collider2D[] results)
        => QueryScene.OverlapBox(point, size, angle, filter, results);
    public static int CircleCast(Vector2 origin, float radius, Vector2 direction, ContactFilter2D filter, RaycastHit2D[] results, float distance = Mathf.Infinity)
        => QueryScene.CircleCast(origin, radius, direction, distance, filter, results);
    public static int CircleCast(Vector2 origin, float radius, Vector2 direction, ContactFilter2D filter, List<RaycastHit2D> results, float distance = Mathf.Infinity)
        => QueryScene.CircleCast(origin, radius, direction, distance, filter, results);
    public static int BoxCast(Vector2 origin, Vector2 size, float angle, Vector2 direction, ContactFilter2D filter, List<RaycastHit2D> results, float distance = Mathf.Infinity)
        => QueryScene.BoxCast(origin, size, angle, direction, distance, filter, results);
    public static int BoxCastNonAlloc(Vector2 origin, Vector2 size, float angle, Vector2 direction, RaycastHit2D[] results, float distance = Mathf.Infinity, int mask = Physics2D.DefaultRaycastLayers)
        => QueryScene.BoxCast(origin, size, angle, direction, distance, LayerFilter(mask), results);
    #endregion
}
