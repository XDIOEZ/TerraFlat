using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

public sealed partial class WorldPhysicsService2D
{
    #region 通用形状提交与来源租约

    private readonly HashSet<Action> pendingGeometryUpdates = new();
    private readonly HashSet<Action> geometryUpdatesInFlight = new();
    private readonly HashSet<WorldPhysicsFixedRectangleSource2D> fixedRectangleSources = new();
    private bool flushingGeometry;
    internal event Action Disposing;
    internal int PendingGeometryUpdateCount => pendingGeometryUpdates.Count;

    internal void ScheduleGeometryUpdate(Action update)
    {
        if (!disposed && update != null) pendingGeometryUpdates.Add(update);
    }

    internal void CancelGeometryUpdate(Action update)
    {
        pendingGeometryUpdates.Remove(update);
        geometryUpdatesInFlight.Remove(update);
    }

    // 所有来源共用同一提交队列，处理中的再次失效留到下一物理步。
    internal void FlushPendingGeometry()
    {
        if (disposed || flushingGeometry || pendingGeometryUpdates.Count == 0) return;
        using var scratch = ListPool<Action>.Get(out var updates);
        updates.AddRange(pendingGeometryUpdates);
        geometryUpdatesInFlight.UnionWith(pendingGeometryUpdates);
        pendingGeometryUpdates.Clear();
        flushingGeometry = true;
        try
        {
            for (int i = 0; i < updates.Count; i++)
            {
                if (disposed) break;
                if (!geometryUpdatesInFlight.Contains(updates[i])) continue;
                updates[i]();
                geometryUpdatesInFlight.Remove(updates[i]);
            }
        }
        catch
        {
            // 失败只重试仍有效的来源，取消和回收也必须撤掉已取出的批次。
            if (!disposed) pendingGeometryUpdates.UnionWith(geometryUpdatesInFlight);
            throw;
        }
        finally { geometryUpdatesInFlight.Clear(); flushingGeometry = false; }
    }

    internal WorldPhysicsFixedRectangleSource2D CreateFixedRectangleSource(object identity, Vector2 logicalOrigin)
    {
        if (disposed) throw new ObjectDisposedException(nameof(WorldPhysicsService2D));
        if (identity == null) throw new ArgumentNullException(nameof(identity));
        var source = new WorldPhysicsFixedRectangleSource2D(this, identity, logicalOrigin);
        fixedRectangleSources.Add(source);
        return source;
    }

    internal void ReleaseFixedRectangleSource(WorldPhysicsFixedRectangleSource2D source)
        => fixedRectangleSources.Remove(source);

    private void DisposeGeometrySources()
    {
        if (Disposing != null)
            foreach (Action listener in Disposing.GetInvocationList())
                try { listener(); } catch (Exception exception) { Debug.LogException(exception); }
        Disposing = null;
        using var scratch = ListPool<WorldPhysicsFixedRectangleSource2D>.Get(out var sources);
        sources.AddRange(fixedRectangleSources);
        foreach (var source in sources) source.Dispose();
        pendingGeometryUpdates.Clear();
        geometryUpdatesInFlight.Clear();
    }

    #endregion

    #region 通用固定区域装配

    private static readonly ItemPhysicsDefinitionDto fixedGeometryDefinition = new();
    private static readonly ItemColliderDefinitionDto fixedRectangleDefinition = new()
        { Type = nameof(BoxCollider2D), Enabled = true, IsTrigger = false };

    internal WorldPhysicsEntityProxy2D AllocateFixedGeometryProxy(object identity, Vector2 logicalOrigin)
        => AllocateEntityProxy(identity, null, fixedGeometryDefinition, null, null, logicalPosition: logicalOrigin);

    // 地形与批量障碍只提交矩形，实际组件仍由普通实体代理的壳池和登记入口管理。
    internal void SynchronizeFixedRectangleGeometry(WorldPhysicsEntityProxy2D proxy,
        IReadOnlyList<WorldPhysicsRectangleMerger2D.MergedObstacleRectangle> rectangles)
    {
        if (proxy?.IsValid != true || proxy.Owner != this || !proxy.Shell.OwnsRoot ||
            proxy.Mode != WorldPhysicsProxyMode2D.WorldFixed)
            throw new InvalidOperationException("矩形批次需要本世界持有的固定代理。");
        EntityProxyShell shell = proxy.Shell;
        for (int i = 0; i < rectangles.Count; i++)
        {
            BoxCollider2D box;
            if (i < shell.ActiveColliders.Count) box = (BoxCollider2D)shell.ActiveColliders[i];
            else
            {
                box = (BoxCollider2D)RentEntityRegionCollider(shell,
                    new WorldPhysicsEntityRegion2D("blocking:" + i, WorldPhysicsPurpose2D.Blocking, fixedRectangleDefinition));
                RegisterCollider(box, regionId: "blocking:" + i, entity: proxy.Entity);
            }
            var rectangle = rectangles[i];
            if (!box.offset.Equals(rectangle.Center) || !box.size.Equals(rectangle.Size))
            {
                box.enabled = false;
                box.offset = rectangle.Center;
                box.size = rectangle.Size;
                box.enabled = true;
            }
        }
        for (int i = shell.ActiveColliders.Count - 1; i >= rectangles.Count; i--)
        {
            Collider2D collider = shell.ActiveColliders[i];
            UnregisterCollider(collider);
            collider.enabled = false;
            collider.gameObject.SetActive(false);
            shell.ActiveColliders.RemoveAt(i);
        }
        RefreshFixedProxyBoundaryImages(proxy);
    }

    #endregion
}

// 来源租约只保留几何数据，不拥有另一套刚体、碰撞池或模拟循环。
internal sealed class WorldPhysicsFixedRectangleSource2D : IDisposable
{
    #region 几何来源
    internal readonly WorldPhysicsService2D Owner;
    internal readonly object Identity;
    private readonly WorldPhysicsRectangleMerger2D geometry = new();
    private readonly Action synchronize;
    private bool disposed, pendingSubmission;
    internal Vector2 LogicalOrigin { get; private set; }
    internal WorldPhysicsEntityProxy2D Proxy { get; private set; }
    internal int RectangleSourceCount => geometry.SourceCount;
    internal bool IsValid => !disposed && Owner.IsValid;

    internal WorldPhysicsFixedRectangleSource2D(WorldPhysicsService2D owner, object identity, Vector2 origin)
    { Owner = owner; Identity = identity; LogicalOrigin = origin; synchronize = Synchronize; }

    internal void SetOrigin(Vector2 origin)
    {
        if (!IsValid || LogicalOrigin.Equals(origin)) return;
        LogicalOrigin = origin;
        pendingSubmission = true;
        Owner.ScheduleGeometryUpdate(synchronize);
    }

    internal void SetRectangle(int identity, Vector2 center, Vector2 size)
    {
        if (!IsValid) throw new ObjectDisposedException(nameof(WorldPhysicsFixedRectangleSource2D));
        if (geometry.SetSourceRectangle(identity, center, size)) Owner.ScheduleGeometryUpdate(synchronize);
    }

    internal void RemoveRectangle(int identity)
    {
        if (IsValid && geometry.RemoveSourceRectangle(identity)) Owner.ScheduleGeometryUpdate(synchronize);
    }

    // 同一来源变化再多也只重建一次，空来源不申请任何原生组件。
    internal void Synchronize()
    {
        Owner.CancelGeometryUpdate(synchronize);
        if (!IsValid) return;
        bool changed = geometry.RebuildMergedRectangles();
        if (!changed && !pendingSubmission) return;
        pendingSubmission = true;
        try
        {
            if (geometry.Rectangles.Count == 0) { Proxy?.Dispose(); Proxy = null; }
            else
            {
                if (Proxy?.IsValid != true) Proxy = Owner.AllocateFixedGeometryProxy(Identity, LogicalOrigin);
                else if (!Proxy.LogicalOrigin.Equals(LogicalOrigin))
                    Proxy.SetPose(new WorldPhysicsEntityPose2D(WorldLocalPresentation.ProjectPosition(LogicalOrigin), 0, Vector2.zero, 0));
                Owner.SynchronizeFixedRectangleGeometry(Proxy, geometry.Rectangles);
            }
            pendingSubmission = false;
        }
        catch { Owner.ScheduleGeometryUpdate(synchronize); throw; }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Owner.CancelGeometryUpdate(synchronize);
        Owner.ReleaseFixedRectangleSource(this);
        Proxy?.Dispose(); Proxy = null;
        geometry.Clear();
    }
    #endregion
}
