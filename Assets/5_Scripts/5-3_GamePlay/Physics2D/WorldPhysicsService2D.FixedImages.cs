using System;
using Unity.Mathematics;
using UnityEngine;

public sealed partial class WorldPhysicsService2D
{
    #region 通用固定代理的空间批次与环绕镜像

    // 物理批次尺寸独立于世界数据区块，所有固定实体共用同一规则。
    internal Vector2Int ResolveFixedBatchAddress(Vector2 logicalPosition)
        => new(Mathf.FloorToInt(logicalPosition.x / FixedBatchCellSize), Mathf.FloorToInt(logicalPosition.y / FixedBatchCellSize));

    internal void RefreshFixedProxyBoundaryImages(WorldPhysicsEntityProxy2D proxy)
    {
        if (proxy?.IsValid != true || proxy.Owner != this || !proxy.Shell.OwnsRoot ||
            proxy.Mode != WorldPhysicsProxyMode2D.WorldFixed) return;
        if (!GameplayWorldRegistry.TryGet(WorldKey, out _)) { ReleaseProxyBoundaryImages(proxy.Shell); return; }
        using var scope = GameplayWorldRegistry.UseWorld(WorldKey);
        RefreshFixedProxyBoundaryImages(proxy, WorldTopologyRuntime.GetActiveDomain());
    }

    internal void RefreshFixedProxyBoundaryImages(WorldPhysicsEntityProxy2D proxy, WorldTopologyDomain topology)
    {
        if (proxy?.IsValid != true || proxy.Owner != this || !proxy.Shell.OwnsRoot ||
            proxy.Mode != WorldPhysicsProxyMode2D.WorldFixed) return;
        EntityProxyShell shell = proxy.Shell;
        ReleaseProxyBoundaryImages(shell);
        if (!topology.IsWrapped) return;
        int sourceCount = shell.ActiveColliders.Count;
        for (int sourceIndex = 0; sourceIndex < sourceCount; sourceIndex++)
        {
            Collider2D source = shell.ActiveColliders[sourceIndex];
            if (source == null || !source.enabled || source.isTrigger) continue;
            Bounds bounds = ResolveFixedColliderBounds(source);
            float2 canonical = topology.Normalize(new float2(bounds.center.x, bounds.center.y));
            Vector2 canonicalDelta = new(canonical.x - bounds.center.x, canonical.y - bounds.center.y);
            float2 padding = new(bounds.extents.x + FixedBatchCellSize, bounds.extents.y + FixedBatchCellSize);
            WorldTopologyImageOffsets offsets = topology.GetRequiredImageOffsets(canonical - padding, canonical + padding, includeBoundary: true);
            if (canonicalDelta.sqrMagnitude > .000001f) CopyFixedProxyImage(shell, source, canonicalDelta);
            for (int i = 0; i < offsets.Count; i++)
            {
                float2 value = offsets[i];
                Vector2 delta = canonicalDelta + new Vector2(value.x, value.y);
                if (delta.sqrMagnitude > .000001f) CopyFixedProxyImage(shell, source, delta);
            }
        }
    }

    private void CopyFixedProxyImage(EntityProxyShell shell, Collider2D source, Vector2 offset)
    {
        var declaration = new ItemColliderDefinitionDto { Type = source.GetType().Name, Enabled = true, IsTrigger = false };
        Collider2D image = RentEntityRegionCollider(shell,
            new WorldPhysicsEntityRegion2D("wrapped-blocking", WorldPhysicsPurpose2D.Blocking, declaration));
        shell.ActiveColliders.RemoveAt(shell.ActiveColliders.Count - 1);
        shell.ImageColliders.Add(image);
        image.GetComponent<WorldPhysicsRegionTriggerRelay2D>()?.ClearBinding();
        image.enabled = false;
        image.transform.SetLocalPositionAndRotation(shell.Root.InverseTransformPoint(source.transform.position + (Vector3)offset),
            Quaternion.Inverse(shell.Root.rotation) * source.transform.rotation);
        image.transform.localScale = source.transform.localScale;
        image.gameObject.layer = source.gameObject.layer;
        image.offset = source.offset;
        image.density = source.density;
        CopyColliderPhysicsSettings(source, image);
        switch (source)
        {
            case BoxCollider2D box:
                ((BoxCollider2D)image).size = box.size; ((BoxCollider2D)image).edgeRadius = box.edgeRadius; break;
            case CircleCollider2D circle: ((CircleCollider2D)image).radius = circle.radius; break;
            case CapsuleCollider2D capsule:
                ((CapsuleCollider2D)image).size = capsule.size; ((CapsuleCollider2D)image).direction = capsule.direction; break;
            case PolygonCollider2D polygon:
                var copy = (PolygonCollider2D)image;
                copy.pathCount = polygon.pathCount;
                for (int i = 0; i < polygon.pathCount; i++) copy.SetPath(i, polygon.GetPath(i));
                break;
        }
        ColliderSource2D marker = image.GetComponent<ColliderSource2D>();
        if (marker == null) marker = image.gameObject.AddComponent<ColliderSource2D>();
        marker.Bind(source, offset);
        image.enabled = true;
    }

    private void ReleaseProxyBoundaryImages(EntityProxyShell shell)
    {
        foreach (Collider2D image in shell.ImageColliders)
        {
            UnregisterCollider(image);
            if (image == null) continue;
            image.enabled = false;
            image.GetComponent<ColliderSource2D>()?.ClearBinding();
            image.gameObject.SetActive(false);
        }
        shell.ImageColliders.Clear();
    }

    private static Bounds ResolveFixedColliderBounds(Collider2D collider)
    {
        Bounds local;
        switch (collider)
        {
            case BoxCollider2D box: local = new Bounds(box.offset, box.size + Vector2.one * (box.edgeRadius * 2f)); break;
            case CircleCollider2D circle: local = new Bounds(circle.offset, Vector2.one * (circle.radius * 2f)); break;
            case CapsuleCollider2D capsule: local = new Bounds(capsule.offset, capsule.size); break;
            case PolygonCollider2D polygon:
                local = new Bounds(polygon.offset, Vector3.zero);
                for (int path = 0; path < polygon.pathCount; path++)
                    foreach (Vector2 point in polygon.GetPath(path)) local.Encapsulate(point + polygon.offset);
                break;
            default: return collider.bounds;
        }
        Matrix4x4 matrix = collider.transform.localToWorldMatrix;
        Vector3 x = matrix.MultiplyVector(Vector3.right * local.extents.x);
        Vector3 y = matrix.MultiplyVector(Vector3.up * local.extents.y);
        return new Bounds(matrix.MultiplyPoint3x4(local.center),
            new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x), Mathf.Abs(x.y) + Mathf.Abs(y.y), 0) * 2f);
    }
    #endregion
}
