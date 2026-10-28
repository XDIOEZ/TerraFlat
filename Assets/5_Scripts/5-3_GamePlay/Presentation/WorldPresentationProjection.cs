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
        internal WorldPresentationHierarchyWatcher Watcher;
    }
    private sealed class RenderCopy
    {
        internal Renderer Source, Target;
        internal MeshFilter SourceMesh, TargetMesh;
        internal ParticleSystemRenderer SourceParticles;
        internal ParticleSystem ParticleSystem;
        internal Mesh ParticleMesh;
        internal bool OriginalForceOff, LogicalForceOff;
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
    private Item sourceItem;
    private bool excludeNestedItems;
    private bool hierarchyDirty = true;
    private static readonly FieldInfo ShadowShape = typeof(ShadowCaster2D).GetField("m_ShapePath", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo ShadowShapeHash = typeof(ShadowCaster2D).GetField("m_ShapePathHash", BindingFlags.Instance | BindingFlags.NonPublic);

    private void Awake() => properties = new MaterialPropertyBlock();

    public static WorldPresentationProjection Create(Transform source, Transform parent = null)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        var root = new GameObject("WorldPresentationProjection") { hideFlags = HideFlags.DontSave };
        if (parent != null) root.transform.SetParent(parent, false);
        var projection = root.AddComponent<WorldPresentationProjection>();
        projection.sourceRoot = source;
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

    internal void InvalidateHierarchy() => hierarchyDirty = true;

    internal static bool IsSourceProjected(Renderer source)
        => source != null && projectedSources.ContainsKey(source);

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

    // 结构变化才重新发现组件，逐帧只同步已经绑定的显示数据。
    public void Synchronize(Matrix4x4 worldToDisplay, bool visible)
    {
        if (sourceRoot == null) { gameObject.SetActive(false); return; }
        if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
        if (hierarchyDirty) RebuildHierarchy();
        if (!visible)
        {
            foreach (RenderCopy copy in copies.Values)
                if (copy.Source != null) copy.Source.forceRenderingOff = true;
            return;
        }
        Matrix4x4 world = worldToDisplay * sourceRoot.localToWorldMatrix;
        Matrix4x4 local = transform.parent != null ? transform.parent.worldToLocalMatrix * world : world;
        transform.localPosition = local.GetColumn(3);
        transform.localRotation = local.rotation;
        transform.localScale = local.lossyScale;
        foreach (NodeCopy node in nodes.Values)
        {
            if (node.Source == null || node.Target == null) { hierarchyDirty = true; continue; }
            if (node.Source != sourceRoot)
            {
                if (node.Target.localPosition != node.Source.localPosition) node.Target.localPosition = node.Source.localPosition;
                if (node.Target.localRotation != node.Source.localRotation) node.Target.localRotation = node.Source.localRotation;
                if (node.Target.localScale != node.Source.localScale) node.Target.localScale = node.Source.localScale;
                if (node.Target.gameObject.activeSelf != node.Source.gameObject.activeSelf)
                    node.Target.gameObject.SetActive(node.Source.gameObject.activeSelf);
            }
            SynchronizeSortingGroup(node);
        }
        foreach (RenderCopy copy in copies.Values)
        {
            if (copy.Source == null || copy.Target == null) { hierarchyDirty = true; continue; }
            SynchronizeRenderer(copy);
            if (!copy.Source.forceRenderingOff) copy.Source.forceRenderingOff = true;
        }
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
            copies.Add(source, new RenderCopy { Source = source, Target = target,
                SourceMesh = sourceMesh, TargetMesh = targetMesh, OriginalForceOff = source.forceRenderingOff,
                SourceParticles = particles, ParticleSystem = source.GetComponent<ParticleSystem>(), ParticleMesh = particleMesh,
                LogicalForceOff = GameplayWorldRegistry.GetPresentationOriginalForceRenderingOff(source) });
            projectedSources.TryGetValue(source, out int owners);
            projectedSources[source] = owners + 1;
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
            if (node.Watcher != null) node.Watcher.Remove(this);
            if (node.Target != null && node.Target != transform) Destroy(node.Target.gameObject);
            nodes.Remove(source);
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
        if (target.enabled != draw) target.enabled = draw;
        if (target.sortingLayerID != source.sortingLayerID) target.sortingLayerID = source.sortingLayerID;
        if (target.sortingOrder != source.sortingOrder) target.sortingOrder = source.sortingOrder;
        if (target.sharedMaterial != source.sharedMaterial) target.sharedMaterial = source.sharedMaterial;
        if (target.shadowCastingMode != source.shadowCastingMode) target.shadowCastingMode = source.shadowCastingMode;
        if (target.receiveShadows != source.receiveShadows) target.receiveShadows = source.receiveShadows;
        source.GetPropertyBlock(properties);
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

    private static void SynchronizeSortingGroup(NodeCopy node)
    {
        SortingGroup source = node.SourceSorting, target = node.TargetSorting;
        if (source == null || target == null) return;
        if (target.enabled != source.enabled) target.enabled = source.enabled;
        if (target.sortAtRoot != source.sortAtRoot) target.sortAtRoot = source.sortAtRoot;
        if (target.sortingLayerID != source.sortingLayerID) target.sortingLayerID = source.sortingLayerID;
        if (target.sortingOrder != source.sortingOrder) target.sortingOrder = source.sortingOrder;
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
        if (owners <= 1) projectedSources.Remove(source);
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

    private void OnDestroy()
    {
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
    private void Invalidate()
    {
        foreach (WorldPresentationProjection owner in owners)
            if (owner != null) owner.InvalidateHierarchy();
    }
}
