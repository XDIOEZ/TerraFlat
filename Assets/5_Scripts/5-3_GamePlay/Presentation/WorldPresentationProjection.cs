using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

#region 普通世界绘制快照
internal readonly struct WorldVisualSortingKey : IComparable<WorldVisualSortingKey>
{
    internal readonly int Layer, Order, Group, InternalOrder;
    internal readonly float SortY;
    private readonly int[] nestedSortingPath;

    internal WorldVisualSortingKey(int layer, int order, float sortY, int group = 0, int internalOrder = 0)
        : this(layer, order, sortY, group, internalOrder, null) { }

    private WorldVisualSortingKey(int layer, int order, float sortY, int group, int internalOrder, int[] nestedSortingPath)
    { Layer = layer; Order = order; SortY = sortY; Group = group; InternalOrder = internalOrder; this.nestedSortingPath = nestedSortingPath; }

    public int CompareTo(WorldVisualSortingKey other)
    {
        int result = Layer.CompareTo(other.Layer);
        if (result != 0) return result;
        result = Order.CompareTo(other.Order);
        if (result != 0) return result;
        result = other.SortY.CompareTo(SortY);
        if (result != 0) return result;
        result = Group.CompareTo(other.Group);
        if (result != 0) return result;
        int length = nestedSortingPath?.Length ?? 3, otherLength = other.nestedSortingPath?.Length ?? 3;
        for (int i = 0; i < Math.Min(length, otherLength); i++)
        {
            int left = nestedSortingPath != null ? nestedSortingPath[i] : i == 0 ? Layer : i == 1 ? InternalOrder : 0;
            int right = other.nestedSortingPath != null ? other.nestedSortingPath[i] : i == 0 ? other.Layer : i == 1 ? other.InternalOrder : 0;
            result = left.CompareTo(right);
            if (result != 0) return result;
        }
        return length.CompareTo(otherLength);
    }

    internal static WorldVisualSortingKey FromRenderer(Renderer renderer)
    {
        var groups = new List<SortingGroup>();
        for (Transform current = renderer.transform; current != null; current = current.parent)
        {
            SortingGroup sorting = current.GetComponent<SortingGroup>();
            if (sorting == null || !sorting.enabled) continue;
            groups.Add(sorting);
            if (sorting.sortAtRoot) break;
        }
        if (groups.Count == 0)
            return new WorldVisualSortingKey(SortingLayer.GetLayerValueFromID(renderer.sortingLayerID), renderer.sortingOrder,
                renderer.transform.position.y, renderer.GetInstanceID());

        SortingGroup outer = groups[groups.Count - 1];
        int[] path = new int[groups.Count * 3];
        int cursor = 0;
        for (int i = groups.Count - 2; i >= 0; i--)
        {
            path[cursor++] = SortingLayer.GetLayerValueFromID(groups[i].sortingLayerID);
            path[cursor++] = groups[i].sortingOrder;
            path[cursor++] = groups[i].GetInstanceID();
        }
        path[cursor++] = SortingLayer.GetLayerValueFromID(renderer.sortingLayerID);
        path[cursor++] = renderer.sortingOrder;
        path[cursor] = 0;
        return new WorldVisualSortingKey(SortingLayer.GetLayerValueFromID(outer.sortingLayerID), outer.sortingOrder,
            outer.transform.position.y, outer.GetInstanceID(), renderer.sortingOrder, path);
    }
}

internal readonly struct WorldVisualSnapshot
{
    internal readonly Sprite Sprite;
    internal readonly Matrix4x4 Matrix;
    internal readonly Color Color;
    internal readonly WorldVisualSortingKey SortingKey;
    internal WorldVisualSnapshot(Sprite sprite, Matrix4x4 matrix, Color color, int layer, int order, float sortY,
        int group = 0, int internalOrder = 0)
    { Sprite = sprite; Matrix = matrix; Color = color; SortingKey = new WorldVisualSortingKey(layer, order, sortY, group, internalOrder); }
}
#endregion

/// <summary>把世界中的绘制组件投影到显示坐标，不移动源物理和玩法对象。</summary>
public sealed class WorldPresentationProjection : MonoBehaviour
{
    #region 源层级与显示副本
    private sealed class NodeCopy
    {
        internal Transform Source, Target;
        internal SortingGroup SourceSorting, TargetSorting;
        internal Renderer QueueSource;
        internal bool SortingRegistered;
        internal WorldPresentationHierarchyWatcher Watcher;
        internal readonly List<SortingGroup> AncestorSortingGroups = new();
    }
    private sealed class RenderCopy
    {
        internal Renderer Source, Target;
        internal MeshFilter SourceMesh, TargetMesh;
        internal ParticleSystemRenderer SourceParticles;
        internal ParticleSystem ParticleSystem;
        internal Mesh ParticleMesh;
        internal bool OriginalForceOff, LogicalForceOff;
        internal bool SortingRegistered;
        internal int RenderQueue;
    }
    private sealed class LightCopy
    {
        internal Light2D Source, Target;
    }
    private sealed class ShadowCopy
    {
        internal ShadowCaster2D Source, Target;
        internal object Shape;
        internal int Hash = int.MinValue;
    }

    private readonly Dictionary<Transform, NodeCopy> nodes = new();
    private readonly Dictionary<Renderer, RenderCopy> copies = new();
    private static readonly Dictionary<Renderer, int> projectedSources = new();
    private static readonly Dictionary<Renderer, bool> projectedSourceOriginalForceOff = new();
    private static readonly Dictionary<Renderer, bool> projectedSourceLogicalForceOff = new();
    private static readonly Dictionary<Item, HashSet<WorldPresentationProjection>> itemProjectionOwners = new();
    private readonly Dictionary<Light2D, LightCopy> lights = new();
    private readonly Dictionary<ShadowCaster2D, ShadowCopy> shadows = new();
    private readonly List<Transform> sourceNodes = new();
    private readonly List<Renderer> sourceRenderers = new();
    private readonly List<Light2D> sourceLights = new();
    private readonly List<ShadowCaster2D> sourceShadows = new();
    private readonly HashSet<Transform> seenNodes = new();
    private readonly HashSet<Renderer> seenRenderers = new();
    private readonly HashSet<Light2D> seenLights = new();
    private readonly HashSet<ShadowCaster2D> seenShadows = new();
    private readonly List<Transform> removedNodes = new();
    private readonly List<Renderer> removedRenderers = new();
    private readonly List<Light2D> removedLights = new();
    private readonly List<ShadowCaster2D> removedShadows = new();
    private MaterialPropertyBlock properties;
    private Transform sourceRoot;
    private Transform originalParent;
    private WorldPresentationSortingDomain sortingDomain;
    private Matrix4x4 synchronizedProjection = Matrix4x4.identity;
    private Matrix4x4 synchronizedDisplayToSource = Matrix4x4.identity;
    private bool synchronizedVisible;
    private bool released;
    private Item sourceItem;
    private bool excludeNestedItems;
    private bool hierarchyDirty = true;
    public bool IsReleased => released;
    public WorldPresentationSortingDomain SortingDomain => sortingDomain;
    private static readonly FieldInfo ShadowShape = typeof(ShadowCaster2D).GetField("m_ShapePath", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo ShadowShapeHash = typeof(ShadowCaster2D).GetField("m_ShapePathHash", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly int DisplayToSourceId = Shader.PropertyToID("_WorldPresentationDisplayToSource");
    private static readonly int ProjectionEnabledId = Shader.PropertyToID("_WorldPresentationProjectionEnabled");

    private void Awake() => properties = new MaterialPropertyBlock();

    public static WorldPresentationProjection Create(Transform source, Transform parent = null)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        var root = new GameObject("WorldPresentationProjection") { hideFlags = HideFlags.DontSave };
        if (parent != null) root.transform.SetParent(parent, false);
        var projection = root.AddComponent<WorldPresentationProjection>();
        projection.sourceRoot = source;
        projection.originalParent = parent;
        projection.sourceItem = source.GetComponent<Item>();
        projection.excludeNestedItems = projection.sourceItem == null;
        if (projection.sourceItem != null)
        {
            if (!itemProjectionOwners.TryGetValue(projection.sourceItem, out var owners))
                itemProjectionOwners.Add(projection.sourceItem, owners = new HashSet<WorldPresentationProjection>());
            owners.Add(projection);
            Item.RuntimeStructureChanged -= OnItemStructureChanged;
            Item.RuntimeStructureChanged += OnItemStructureChanged;
        }
        return projection;
    }

    /// <summary>只移动显示副本，并让其中所有排序组留在所属世界域内。</summary>
    public void SetSortingDomain(WorldPresentationSortingDomain domain)
    {
        if (released) return;
        if (domain != null && domain.IsDisposed) throw new ObjectDisposedException(nameof(domain));
        if (ReferenceEquals(sortingDomain, domain)) return;
        RemoveLocalSortingUnits();
        RestoreEscapedSortingNodes();
        if (sortingDomain != null) { sortingDomain.DetachProjection(this); sortingDomain.Disposing -= OnSortingDomainDisposing; }
        sortingDomain = domain;
        if (sortingDomain != null) { sortingDomain.AttachProjection(this); sortingDomain.Disposing += OnSortingDomainDisposing; }
        transform.SetParent(domain != null ? domain.Root : originalParent, true);
        foreach (NodeCopy node in nodes.Values) SynchronizeSortingGroup(node);
    }

    private void OnSortingDomainDisposing() => Release();

    internal void RefreshBeforeLocalSorting()
    {
        if (!released && sortingDomain != null && synchronizedVisible)
            Synchronize(synchronizedProjection, true);
    }

    public void SetVisible(bool visible)
    {
        if (released) return;
        if (synchronizedVisible == visible && gameObject.activeSelf == visible) return;
        synchronizedVisible = visible;
        if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
        Transform domainRoot = sortingDomain?.Root;
        if (domainRoot == null) return;
        foreach (NodeCopy node in nodes.Values)
        {
            if (node.Target == null || node.Target == transform || node.Target.parent != domainRoot) continue;
            bool active = visible && node.Source != null && node.Source.gameObject.activeInHierarchy;
            if (node.Target.gameObject.activeSelf != active) node.Target.gameObject.SetActive(active);
        }
    }

    internal void InvalidateHierarchy() => hierarchyDirty = true;

    internal bool TryGetProjectedTransform(Transform source, out Transform target)
    {
        target = source != null && nodes.TryGetValue(source, out NodeCopy node) ? node.Target : null;
        return target != null;
    }

    public static void NotifySourceSortingStructureChanged(Transform source)
    {
        if (source != null && source.TryGetComponent(out WorldPresentationHierarchyWatcher watcher))
            watcher.Invalidate();
    }

    internal static bool IsSourceProjected(Renderer source)
        => source != null && projectedSources.ContainsKey(source);

    public static bool TryGetItemSortingDomain(Item item, out WorldPresentationSortingDomain domain)
        => TryGetItemSortingProjection(item, out domain, out _, out _);

    public static bool TryGetItemSortingProjection(Item item, out WorldPresentationSortingDomain domain,
        out Matrix4x4 worldToDisplay, out bool visible)
    {
        domain = null;
        worldToDisplay = Matrix4x4.identity;
        visible = false;
        if (item == null || !itemProjectionOwners.TryGetValue(item, out var owners)) return false;
        foreach (WorldPresentationProjection owner in owners)
        {
            if (owner == null || owner.released || owner.sortingDomain == null || owner.sortingDomain.IsDisposed) continue;
            domain = owner.sortingDomain;
            worldToDisplay = owner.synchronizedProjection;
            visible = owner.synchronizedVisible && owner.gameObject.activeInHierarchy;
            return true;
        }
        return false;
    }

    public static bool TryGetProjectedRendererOriginalForceRenderingOff(Renderer source, out bool forceOff)
    {
        if (source != null && projectedSourceLogicalForceOff.TryGetValue(source, out forceOff)) return true;
        forceOff = false;
        return false;
    }

    // 过渡画面读取现有投影，不另外创建船件视图。
    public void CollectProjectedSprites(List<SpriteRenderer> destination)
    {
        if (destination == null) throw new ArgumentNullException(nameof(destination));
        foreach (RenderCopy copy in copies.Values)
            if (copy.Target is SpriteRenderer sprite) destination.Add(sprite);
    }

    private static void OnItemStructureChanged(Item item)
    {
        if (item == null || !itemProjectionOwners.TryGetValue(item, out var owners)) return;
        foreach (WorldPresentationProjection owner in owners)
            if (owner != null) owner.hierarchyDirty = true;
    }

    // 先更新镜头可读取的根姿态，全部绘制数据留到渲染前统一读取一次。
    public void ScheduleSynchronizationBeforeRendering(Matrix4x4 worldToDisplay, bool visible)
    {
        if (released) return;
        if (sortingDomain == null || hierarchyDirty || !visible) Synchronize(worldToDisplay, visible);
        else SynchronizeProjectionRootPose(worldToDisplay, visible);
    }

    private void SynchronizeProjectionRootPose(Matrix4x4 worldToDisplay, bool visible)
    {
        if (synchronizedProjection != worldToDisplay)
        {
            synchronizedProjection = worldToDisplay;
            synchronizedDisplayToSource = worldToDisplay.inverse;
        }
        if (sourceRoot == null) { SetVisible(false); return; }
        SetVisible(visible);
        if (!visible) return;
        Matrix4x4 world = worldToDisplay * sourceRoot.localToWorldMatrix;
        Matrix4x4 local = transform.parent != null ? transform.parent.worldToLocalMatrix * world : world;
        Vector3 position = local.GetColumn(3);
        Quaternion rotation = local.rotation;
        Vector3 scale = local.lossyScale;
        if (transform.localPosition != position) transform.localPosition = position;
        if (transform.localRotation != rotation) transform.localRotation = rotation;
        if (transform.localScale != scale) transform.localScale = scale;
    }

    // 结构变化才重新发现组件，逐帧只同步已经绑定的显示数据。
    public void Synchronize(Matrix4x4 worldToDisplay, bool visible)
    {
        if (released) return;
        SynchronizeProjectionRootPose(worldToDisplay, visible);
        if (sourceRoot == null) return;
        if (hierarchyDirty) RebuildHierarchy();
        if (!visible)
        {
            foreach (RenderCopy copy in copies.Values)
                if (copy.Source != null) copy.Source.forceRenderingOff = true;
            return;
        }
        foreach (NodeCopy node in nodes.Values)
        {
            if (node.Source == null || node.Target == null) { hierarchyDirty = true; continue; }
            if (node.Source != sourceRoot)
            {
                bool escape = ShouldEscapeSourceSortingGroups(node);
                if (!escape && !nodes.TryGetValue(node.Source.parent, out _)) { hierarchyDirty = true; continue; }
                Transform parent = escape ? sortingDomain.Root : nodes[node.Source.parent].Target;
                if (node.Target.parent != parent) node.Target.SetParent(parent, true);
                if (escape)
                {
                    Matrix4x4 matrix = parent.worldToLocalMatrix * worldToDisplay * node.Source.localToWorldMatrix;
                    node.Target.localPosition = matrix.GetColumn(3);
                    node.Target.localRotation = matrix.rotation;
                    node.Target.localScale = matrix.lossyScale;
                }
                else
                {
                    if (node.Target.localPosition != node.Source.localPosition) node.Target.localPosition = node.Source.localPosition;
                    if (node.Target.localRotation != node.Source.localRotation) node.Target.localRotation = node.Source.localRotation;
                    if (node.Target.localScale != node.Source.localScale) node.Target.localScale = node.Source.localScale;
                }
                bool active = escape ? node.Source.gameObject.activeInHierarchy : node.Source.gameObject.activeSelf;
                if (node.Target.gameObject.activeSelf != active) node.Target.gameObject.SetActive(active);
            }
            SynchronizeSortingGroup(node);
        }
        foreach (RenderCopy copy in copies.Values)
        {
            if (copy.Source == null || copy.Target == null) { hierarchyDirty = true; continue; }
            SynchronizeRenderer(copy);
            if (!copy.Source.forceRenderingOff) copy.Source.forceRenderingOff = true;
        }
        SynchronizeLocalSortingUnits();
        foreach (LightCopy copy in lights.Values)
        {
            if (copy.Source == null || copy.Target == null) { hierarchyDirty = true; continue; }
            SynchronizeLight(copy);
        }
        foreach (ShadowCopy copy in shadows.Values)
        {
            if (copy.Source == null || copy.Target == null) { hierarchyDirty = true; continue; }
            SynchronizeShadow(copy);
        }
    }

    private void RebuildHierarchy()
    {
        hierarchyDirty = false;
        sortingDomain?.NotifyLocalSortingGroupsChanged();
        sourceNodes.Clear(); sourceRenderers.Clear(); sourceLights.Clear(); sourceShadows.Clear();
        seenNodes.Clear(); seenRenderers.Clear(); seenLights.Clear(); seenShadows.Clear();
        sourceRoot.GetComponentsInChildren(true, sourceNodes);
        sourceRoot.GetComponentsInChildren(true, sourceRenderers);
        sourceRoot.GetComponentsInChildren(true, sourceLights);
        sourceRoot.GetComponentsInChildren(true, sourceShadows);
        foreach (Transform source in sourceNodes)
        {
            if (source == null) continue;
            seenNodes.Add(source);
            NodeCopy node = GetOrCreateNode(source);
            node.SourceSorting = source.GetComponent<SortingGroup>();
            if (node.SourceSorting != null && node.TargetSorting == null)
                node.TargetSorting = node.Target.gameObject.AddComponent<SortingGroup>();
            if (node.SourceSorting == null && node.TargetSorting != null) node.TargetSorting.enabled = false;
        }
        foreach (Transform source in sourceNodes)
        {
            if (source == null || !nodes.TryGetValue(source, out NodeCopy node)) continue;
            node.AncestorSortingGroups.Clear();
            if (source == sourceRoot) continue;
            for (Transform parent = source.parent; parent != null; parent = parent.parent)
            {
                if (nodes.TryGetValue(parent, out NodeCopy ancestor) && ancestor.SourceSorting != null)
                    node.AncestorSortingGroups.Add(ancestor.SourceSorting);
                if (parent == sourceRoot) break;
            }
        }
        foreach (Renderer source in sourceRenderers)
        {
            if (source == null || source is not SpriteRenderer && source is not MeshRenderer && source is not ParticleSystemRenderer ||
                excludeNestedItems && source.GetComponentInParent<Item>(true) != null) continue;
            seenRenderers.Add(source);
            if (copies.ContainsKey(source)) continue;
            Transform mirror = GetOrCreateNode(source.transform).Target;
            Renderer target;
            MeshFilter sourceMesh = null, targetMesh = null;
            ParticleSystemRenderer particles = source as ParticleSystemRenderer;
            Mesh particleMesh = null;
            if (source is SpriteRenderer) target = mirror.gameObject.AddComponent<SpriteRenderer>();
            else
            {
                sourceMesh = source.GetComponent<MeshFilter>();
                if (particles != null)
                {
                    var particleRoot = new GameObject("ProjectedParticles") { hideFlags = HideFlags.DontSave };
                    particleRoot.transform.SetParent(mirror, false);
                    targetMesh = particleRoot.AddComponent<MeshFilter>();
                    target = particleRoot.AddComponent<MeshRenderer>();
                    particleMesh = new Mesh { name = "ProjectedParticles", hideFlags = HideFlags.DontSave };
                    targetMesh.sharedMesh = particleMesh;
                }
                else
                {
                    targetMesh = mirror.GetComponent<MeshFilter>();
                    if (targetMesh == null) targetMesh = mirror.gameObject.AddComponent<MeshFilter>();
                    target = mirror.gameObject.AddComponent<MeshRenderer>();
                }
            }
            bool logicalForceOff = projectedSourceLogicalForceOff.TryGetValue(source, out bool existingLogicalForceOff)
                ? existingLogicalForceOff : GameplayWorldRegistry.GetPresentationOriginalForceRenderingOff(source);
            bool originalForceOff = projectedSourceOriginalForceOff.TryGetValue(source, out bool existingOriginalForceOff)
                ? existingOriginalForceOff : source.forceRenderingOff;
            copies.Add(source, new RenderCopy { Source = source, Target = target,
                SourceMesh = sourceMesh, TargetMesh = targetMesh, OriginalForceOff = originalForceOff,
                SourceParticles = particles, ParticleSystem = source.GetComponent<ParticleSystem>(), ParticleMesh = particleMesh,
                LogicalForceOff = logicalForceOff });
            projectedSources.TryGetValue(source, out int owners);
            projectedSources[source] = owners + 1;
            projectedSourceOriginalForceOff[source] = originalForceOff;
            projectedSourceLogicalForceOff[source] = logicalForceOff;
        }
        foreach (Light2D source in sourceLights)
        {
            if (source == null || source.lightType != Light2D.LightType.Point ||
                excludeNestedItems && source.GetComponentInParent<Item>(true) != null) continue;
            seenLights.Add(source);
            if (lights.ContainsKey(source)) continue;
            Transform mirror = GetOrCreateNode(source.transform).Target;
            Light2D target = mirror.gameObject.AddComponent<Light2D>();
            target.lightType = Light2D.LightType.Point;
            WorldLightRegistry.RegisterProxy(target);
            Light2DSortingLayerUtility.BeginProjectedLight(source);
            lights.Add(source, new LightCopy { Source = source, Target = target });
        }
        foreach (ShadowCaster2D source in sourceShadows)
        {
            if (source == null || excludeNestedItems && source.GetComponentInParent<Item>(true) != null) continue;
            if (ShadowShape == null || ShadowShapeHash == null)
                throw new MissingFieldException(typeof(ShadowCaster2D).FullName, "m_ShapePath/m_ShapePathHash");
            seenShadows.Add(source);
            if (shadows.ContainsKey(source)) continue;
            Transform mirror = GetOrCreateNode(source.transform).Target;
            ShadowCaster2D target = mirror.gameObject.AddComponent<ShadowCaster2D>();
            Light2DSortingLayerUtility.BeginProjectedShadow(source);
            shadows.Add(source, new ShadowCopy { Source = source, Target = target });
        }
        removedRenderers.Clear();
        foreach (var pair in copies) if (pair.Key == null || !seenRenderers.Contains(pair.Key)) removedRenderers.Add(pair.Key);
        foreach (Renderer source in removedRenderers)
        {
            RenderCopy copy = copies[source];
            if (copy.SortingRegistered) sortingDomain?.RemoveLocalSortingKey(copy.Target);
            ForgetProjectedSource(copy.Source);
            RestoreSourceRenderer(copy);
            if (copy.Target != null)
            {
                if (copy.ParticleMesh != null) Destroy(copy.Target.gameObject);
                else Destroy(copy.Target);
            }
            if (copy.ParticleMesh != null) Destroy(copy.ParticleMesh);
            copies.Remove(source);
        }
        removedLights.Clear();
        foreach (var pair in lights) if (pair.Key == null || !seenLights.Contains(pair.Key)) removedLights.Add(pair.Key);
        foreach (Light2D source in removedLights)
        {
            LightCopy copy = lights[source];
            Light2DSortingLayerUtility.EndProjectedLight(copy.Source);
            if (copy.Target != null) Destroy(copy.Target);
            lights.Remove(source);
        }
        removedShadows.Clear();
        foreach (var pair in shadows) if (pair.Key == null || !seenShadows.Contains(pair.Key)) removedShadows.Add(pair.Key);
        foreach (ShadowCaster2D source in removedShadows)
        {
            ShadowCopy copy = shadows[source];
            Light2DSortingLayerUtility.EndProjectedShadow(copy.Source);
            if (copy.Target != null) Destroy(copy.Target);
            shadows.Remove(source);
        }
        removedNodes.Clear();
        foreach (var pair in nodes) if (pair.Key == null || !seenNodes.Contains(pair.Key)) removedNodes.Add(pair.Key);
        foreach (Transform source in removedNodes)
        {
            NodeCopy node = nodes[source];
            if (node.SortingRegistered) sortingDomain?.RemoveLocalSortingKey(node.TargetSorting);
            if (node.Watcher != null) node.Watcher.Remove(this);
            if (node.Target != null && node.Target != transform) Destroy(node.Target.gameObject);
            nodes.Remove(source);
        }
        foreach (NodeCopy node in nodes.Values) node.QueueSource = null;
        foreach (RenderCopy copy in copies.Values)
        {
            if (copy.Source == null || !nodes.TryGetValue(copy.Source.transform, out NodeCopy node)) continue;
            if (node.SourceSorting != null && node.QueueSource == null) node.QueueSource = copy.Source;
            foreach (SortingGroup group in node.AncestorSortingGroups)
                if (group != null && nodes.TryGetValue(group.transform, out NodeCopy ancestor) && ancestor.QueueSource == null)
                    ancestor.QueueSource = copy.Source;
        }
    }

    private NodeCopy GetOrCreateNode(Transform source)
    {
        if (nodes.TryGetValue(source, out NodeCopy existing)) return existing;
        Transform target;
        if (source == sourceRoot) target = transform;
        else
        {
            var node = new GameObject(source.name) { hideFlags = HideFlags.DontSave, layer = source.gameObject.layer };
            target = node.transform;
            target.SetParent(GetOrCreateNode(source.parent).Target, false);
        }
        WorldPresentationHierarchyWatcher watcher = source.GetComponent<WorldPresentationHierarchyWatcher>();
        if (watcher == null) watcher = source.gameObject.AddComponent<WorldPresentationHierarchyWatcher>();
        watcher.Add(this);
        var result = new NodeCopy { Source = source, Target = target, Watcher = watcher };
        nodes.Add(source, result);
        return result;
    }

    private void SynchronizeRenderer(RenderCopy copy)
    {
        Renderer source = copy.Source, target = copy.Target;
        bool draw = source.enabled && source.gameObject.activeInHierarchy && !copy.LogicalForceOff;
        if (source is SpriteRenderer sprite && BuildingDepthMeshBridge.IsProjected(sprite)) draw = false;
        if (copy.SourceParticles != null)
        {
            Camera camera = Camera.main;
            draw &= camera != null && copy.ParticleSystem != null && copy.ParticleSystem.particleCount > 0;
            if (draw) copy.SourceParticles.BakeMesh(copy.ParticleMesh, camera, ParticleSystemBakeMeshOptions.Default);
        }
        bool sortingChanged = false;
        if (target.enabled != draw) { target.enabled = draw; sortingChanged = true; }
        if (target.sortingLayerID != source.sortingLayerID) { target.sortingLayerID = source.sortingLayerID; sortingChanged = true; }
        if (!copy.SortingRegistered && target.sortingOrder != source.sortingOrder)
        { target.sortingOrder = source.sortingOrder; sortingChanged = true; }
        Material sourceMaterial = source.sharedMaterial;
        if (target.sharedMaterial != sourceMaterial) { target.sharedMaterial = sourceMaterial; sortingChanged = true; }
        copy.RenderQueue = sourceMaterial != null ? sourceMaterial.renderQueue : 3000;
        if (sortingChanged) sortingDomain?.NotifyLocalSortingGroupsChanged();
        if (target.shadowCastingMode != source.shadowCastingMode) target.shadowCastingMode = source.shadowCastingMode;
        if (target.receiveShadows != source.receiveShadows) target.receiveShadows = source.receiveShadows;
        source.GetPropertyBlock(properties);
        properties.SetMatrix(DisplayToSourceId, synchronizedDisplayToSource);
        properties.SetFloat(ProjectionEnabledId, 1f);
        target.SetPropertyBlock(properties);
        if (source is SpriteRenderer original && target is SpriteRenderer projected)
        {
            if (projected.sprite != original.sprite) projected.sprite = original.sprite;
            if (projected.color != original.color) projected.color = original.color;
            if (projected.flipX != original.flipX) projected.flipX = original.flipX;
            if (projected.flipY != original.flipY) projected.flipY = original.flipY;
            if (projected.drawMode != original.drawMode) projected.drawMode = original.drawMode;
            if (projected.size != original.size) projected.size = original.size;
            if (projected.tileMode != original.tileMode) projected.tileMode = original.tileMode;
            if (projected.maskInteraction != original.maskInteraction) projected.maskInteraction = original.maskInteraction;
            if (projected.spriteSortPoint != original.spriteSortPoint) projected.spriteSortPoint = original.spriteSortPoint;
        }
        else if (copy.TargetMesh != null && copy.SourceParticles == null)
        {
            Mesh mesh = copy.SourceMesh != null ? copy.SourceMesh.sharedMesh : null;
            if (copy.TargetMesh.sharedMesh != mesh) copy.TargetMesh.sharedMesh = mesh;
        }
    }

    private void SynchronizeSortingGroup(NodeCopy node)
    {
        SortingGroup source = node.SourceSorting, target = node.TargetSorting;
        if (source == null || target == null) return;
        bool changed = false;
        if (target.enabled != source.enabled) { target.enabled = source.enabled; changed = true; }
        bool sortAtRoot = sortingDomain == null && source.sortAtRoot;
        if (target.sortAtRoot != sortAtRoot) { target.sortAtRoot = sortAtRoot; changed = true; }
        if (target.sortingLayerID != source.sortingLayerID) { target.sortingLayerID = source.sortingLayerID; changed = true; }
        if (!node.SortingRegistered && target.sortingOrder != source.sortingOrder)
        { target.sortingOrder = source.sortingOrder; changed = true; }
        if (changed) sortingDomain?.NotifyLocalSortingGroupsChanged();
    }

    private void SynchronizeLocalSortingUnits()
    {
        if (sortingDomain == null) return;
        foreach (NodeCopy node in nodes.Values)
        {
            SortingGroup source = node.SourceSorting, target = node.TargetSorting;
            bool top = source != null && source.enabled && target != null && node.QueueSource != null &&
                FindSourceSortingUnit(node) == source;
            if (!top)
            {
                if (node.SortingRegistered)
                {
                    sortingDomain.RemoveLocalSortingKey(target); node.SortingRegistered = false;
                    if (source != null && target != null)
                    { target.sortingLayerID = source.sortingLayerID; target.sortingOrder = source.sortingOrder; }
                }
                continue;
            }
            int queue = node.QueueSource != null && copies.TryGetValue(node.QueueSource, out RenderCopy queueCopy)
                ? queueCopy.RenderQueue : 3000;
            // 从规范脚点只投影一次，避免不同显示层级的浮点误差拆开同格线路。
            float footY = synchronizedProjection.MultiplyPoint3x4(source.transform.position).y;
            sortingDomain.ApplyLocalSortingGroupKey(target, source.sortingLayerID, source.sortingOrder,
                queue, footY, sameFootOrder: node.QueueSource.sortingOrder);
            node.SortingRegistered = true;
        }
        foreach (RenderCopy copy in copies.Values)
        {
            if (copy.Source == null || copy.Target == null || !nodes.TryGetValue(copy.Source.transform, out NodeCopy node)) continue;
            if (FindSourceSortingUnit(node) != null)
            {
                if (copy.SortingRegistered)
                {
                    sortingDomain.RemoveLocalSortingKey(copy.Target); copy.SortingRegistered = false;
                    copy.Target.sortingLayerID = copy.Source.sortingLayerID;
                    copy.Target.sortingOrder = copy.Source.sortingOrder;
                }
                continue;
            }
            Vector3 sourceFoot = copy.Source is SpriteRenderer sprite && sprite.spriteSortPoint == SpriteSortPoint.Pivot
                ? copy.Source.transform.position : copy.Source.bounds.center;
            float footY = synchronizedProjection.MultiplyPoint3x4(sourceFoot).y;
            sortingDomain.ApplyLocalRendererSortingKey(copy.Target, copy.Source.sortingLayerID, copy.Source.sortingOrder,
                copy.RenderQueue, footY);
            copy.SortingRegistered = true;
        }
    }

    private static SortingGroup FindSourceSortingUnit(NodeCopy node)
    {
        SortingGroup top = node.SourceSorting;
        if (top == null || !top.enabled) top = null;
        else if (top.sortAtRoot) return top;
        foreach (SortingGroup group in node.AncestorSortingGroups)
        {
            if (group == null || !group.enabled) continue;
            top = group;
            if (group.sortAtRoot) break;
        }
        return top;
    }

    private void RemoveLocalSortingUnits()
    {
        if (sortingDomain == null) return;
        foreach (RenderCopy copy in copies.Values)
            if (copy.SortingRegistered) { sortingDomain.RemoveLocalSortingKey(copy.Target); copy.SortingRegistered = false; }
        foreach (NodeCopy node in nodes.Values)
            if (node.SortingRegistered) { sortingDomain.RemoveLocalSortingKey(node.TargetSorting); node.SortingRegistered = false; }
    }

    private bool ShouldEscapeSourceSortingGroups(NodeCopy node)
    {
        if (sortingDomain == null || node.SourceSorting == null || !node.SourceSorting.enabled ||
            !node.SourceSorting.sortAtRoot) return false;
        foreach (SortingGroup group in node.AncestorSortingGroups)
            if (group != null && group.enabled) return true;
        return false;
    }

    private void RestoreEscapedSortingNodes()
    {
        if (sortingDomain?.Root == null) return;
        foreach (NodeCopy node in nodes.Values)
        {
            if (node.Target == null || node.Target == transform || node.Target.parent != sortingDomain.Root) continue;
            Transform parent = node.Source != null && node.Source.parent != null &&
                nodes.TryGetValue(node.Source.parent, out NodeCopy sourceParent) ? sourceParent.Target : transform;
            if (parent != null) node.Target.SetParent(parent, true);
        }
    }

    private static void RestoreSourceRenderer(RenderCopy copy)
    {
        if (copy.Source == null) return;
        bool projectedBuilding = copy.Source is SpriteRenderer sprite && BuildingDepthMeshBridge.IsProjected(sprite);
        copy.Source.forceRenderingOff = projectedBuilding || IsSourceProjected(copy.Source) ||
            GameplayWorldRegistry.GetPresentationRequiredForceRenderingOff(copy.Source, copy.OriginalForceOff);
    }

    private static void ForgetProjectedSource(Renderer source)
    {
        if (ReferenceEquals(source, null) || !projectedSources.TryGetValue(source, out int owners)) return;
        if (owners <= 1)
        {
            projectedSources.Remove(source);
            projectedSourceOriginalForceOff.Remove(source);
            projectedSourceLogicalForceOff.Remove(source);
        }
        else projectedSources[source] = owners - 1;
    }

    // 源光保留运行状态，仅将接收层交给显示副本，避免本地原点出现第二盏灯。
    private static void SynchronizeLight(LightCopy copy)
    {
        Light2DSortingLayerUtility.CopyLightLayers(copy.Source, copy.Target);
        Light2D source = copy.Source, target = copy.Target;
        bool enabled = source.enabled && source.gameObject.activeInHierarchy;
        if (target.enabled != enabled) target.enabled = enabled;
        if (target.color != source.color) target.color = source.color;
        if (target.intensity != source.intensity) target.intensity = source.intensity;
        if (target.blendStyleIndex != source.blendStyleIndex) target.blendStyleIndex = source.blendStyleIndex;
        if (target.lightOrder != source.lightOrder) target.lightOrder = source.lightOrder;
        if (target.pointLightInnerRadius != source.pointLightInnerRadius) target.pointLightInnerRadius = source.pointLightInnerRadius;
        if (target.pointLightOuterRadius != source.pointLightOuterRadius) target.pointLightOuterRadius = source.pointLightOuterRadius;
        if (target.pointLightInnerAngle != source.pointLightInnerAngle) target.pointLightInnerAngle = source.pointLightInnerAngle;
        if (target.pointLightOuterAngle != source.pointLightOuterAngle) target.pointLightOuterAngle = source.pointLightOuterAngle;
        if (target.falloffIntensity != source.falloffIntensity) target.falloffIntensity = source.falloffIntensity;
        if (target.shadowsEnabled != source.shadowsEnabled) target.shadowsEnabled = source.shadowsEnabled;
        if (target.shadowIntensity != source.shadowIntensity) target.shadowIntensity = source.shadowIntensity;
        if (target.volumeIntensity != source.volumeIntensity) target.volumeIntensity = source.volumeIntensity;
        if (target.volumeIntensityEnabled != source.volumeIntensityEnabled) target.volumeIntensityEnabled = source.volumeIntensityEnabled;
        if (target.shadowVolumeIntensity != source.shadowVolumeIntensity) target.shadowVolumeIntensity = source.shadowVolumeIntensity;
        if (target.volumetricShadowsEnabled != source.volumetricShadowsEnabled) target.volumetricShadowsEnabled = source.volumetricShadowsEnabled;
    }

    // 阴影体复制普通遮挡路径，物理世界仍保留自己的碰撞与逻辑层。
    private static void SynchronizeShadow(ShadowCopy copy)
    {
        object shape = ShadowShape.GetValue(copy.Source);
        int hash = (int)ShadowShapeHash.GetValue(copy.Source);
        if (!ReferenceEquals(shape, copy.Shape) || hash != copy.Hash)
        {
            ShadowShape.SetValue(copy.Target, shape);
            ShadowShapeHash.SetValue(copy.Target, hash);
            copy.Shape = shape; copy.Hash = hash;
        }
        Light2DSortingLayerUtility.CopyShadowLayers(copy.Source, copy.Target);
        ShadowCaster2D source = copy.Source, target = copy.Target;
        bool enabled = source.enabled && source.gameObject.activeInHierarchy;
        if (target.enabled != enabled) target.enabled = enabled;
        if (target.castsShadows != source.castsShadows) target.castsShadows = source.castsShadows;
        if (target.selfShadows != source.selfShadows) target.selfShadows = source.selfShadows;
        if (target.useRendererSilhouette != source.useRendererSilhouette) target.useRendererSilhouette = source.useRendererSilhouette;
    }

    /// <summary>立即恢复源表现并撤销监听，延迟销毁不会让源对象消失一帧。</summary>
    public void Release()
    {
        if (released) return;
        ReleaseBindings();
        gameObject.SetActive(false);
        if (Application.isPlaying) Destroy(gameObject);
        else DestroyImmediate(gameObject);
    }

    private void OnDestroy() => ReleaseBindings();

    private void ReleaseBindings()
    {
        if (released) return;
        RemoveLocalSortingUnits();
        RestoreEscapedSortingNodes();
        if (sortingDomain != null) { sortingDomain.DetachProjection(this); sortingDomain.Disposing -= OnSortingDomainDisposing; }
        sortingDomain = null;
        released = true;
        if (!ReferenceEquals(sourceItem, null) && itemProjectionOwners.TryGetValue(sourceItem, out var owners))
        {
            owners.Remove(this);
            if (owners.Count == 0) itemProjectionOwners.Remove(sourceItem);
        }
        if (itemProjectionOwners.Count == 0) Item.RuntimeStructureChanged -= OnItemStructureChanged;
        foreach (RenderCopy copy in copies.Values)
        {
            ForgetProjectedSource(copy.Source);
            RestoreSourceRenderer(copy);
            if (copy.ParticleMesh != null) Destroy(copy.ParticleMesh);
        }
        foreach (LightCopy copy in lights.Values) Light2DSortingLayerUtility.EndProjectedLight(copy.Source);
        foreach (ShadowCopy copy in shadows.Values) Light2DSortingLayerUtility.EndProjectedShadow(copy.Source);
        foreach (NodeCopy node in nodes.Values) if (node.Watcher != null) node.Watcher.Remove(this);
        copies.Clear(); lights.Clear(); shadows.Clear(); nodes.Clear();
    }
    #endregion
}

/// <summary>源层级增删时通知投影重建，不参与帧更新。</summary>
internal sealed class WorldPresentationHierarchyWatcher : MonoBehaviour
{
    private readonly HashSet<WorldPresentationProjection> owners = new();
    internal void Add(WorldPresentationProjection owner) => owners.Add(owner);
    internal void Remove(WorldPresentationProjection owner) => owners.Remove(owner);
    private void OnTransformChildrenChanged() => Invalidate();
    private void OnDisable() => Invalidate();
    private void OnDestroy() => Invalidate();
    internal void Invalidate()
    {
        foreach (WorldPresentationProjection owner in owners)
            if (owner != null) owner.InvalidateHierarchy();
    }
}
