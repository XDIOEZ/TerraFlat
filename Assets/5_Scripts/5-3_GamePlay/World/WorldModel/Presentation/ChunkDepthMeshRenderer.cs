using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>区块内按一格高的 Y 行合并精灵网格；玩家继续使用连续脚点 Y 排序。</summary>
internal sealed partial class ChunkDepthMeshRenderer : IDisposable
{
    #region 所有权与提交
    internal const int NaturalDomain = 0;
    internal const int MachineDomain = 1;
    internal const int BuildingDomain = 2;
    private readonly Transform parent;
    private readonly Dictionary<PartKey, Entry> entries = new();
    private readonly Dictionary<int, Row> rows = new();
    private readonly HashSet<Group> dirtyGroups = new();
    private readonly List<PartKey> removalScratch = new();
    private static readonly HashSet<ChunkDepthMeshRenderer> active = new();
    private static readonly List<ChunkDepthMeshRenderer> flushScratch = new();
    private static readonly ProfilerMarker FlushMarker = new("FlatWorld.DepthMesh.Flush");
    private static bool hooked;
    internal bool IsDisposed { get; private set; }
    internal int PartCount => entries.Count;
    internal int RowCount => rows.Count;
    internal int DrawGroupCount { get; private set; }
    internal static event Action BeforeFlush;

    internal ChunkDepthMeshRenderer(Transform parent)
    {
        this.parent = parent != null ? parent : throw new ArgumentNullException(nameof(parent));
        active.Add(this);
        EnsureDispatcher();
    }

    internal static void EnsureDispatcher()
    {
        if (hooked) return;
        hooked = true;
        RenderPipelineManager.beginContextRendering += BeforeRendering;
        SharedSpriteMeshCache.Clearing += ReleaseAll;
    }

    /// <summary>同一部件不变时不写网格；业务身份、内部图层与材质批次分别管理。</summary>
    internal void Set(int domain, int entityId, int part, Sprite sprite, Material material,
        Matrix4x4 worldMatrix, Vector3 worldAnchor, int order, Color tint,
        Vector4 crop = default, Vector4 animation = default,
        bool occluder = false, bool highlighted = false, Vector4 state = default)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(ChunkDepthMeshRenderer));
        if (sprite == null || material == null) throw new ArgumentException("行合批缺少 Sprite 或共享材质。");
        if (order < short.MinValue || order > short.MaxValue) throw new ArgumentOutOfRangeException(nameof(order));
        Matrix4x4 local = parent.worldToLocalMatrix * worldMatrix;
        Vector3 anchor = parent.InverseTransformPoint(worldAnchor);
        if (!Finite(local) || !Finite(anchor.x) || !Finite(anchor.y) || !Finite(anchor.z))
            throw new ArgumentException("行合批不能提交非有限坐标。");
        SharedSpriteMeshCache.Geometry geometry = SharedSpriteMeshCache.GetGeometry(sprite);
        Vector4 region = geometry.UvRect;
        for (int i = 0; i < 4; i++)
            if (!Finite(crop[i]) || !Finite(animation[i]) || !Finite(state[i]) || !Finite(tint[i]))
                throw new ArgumentException("行合批不能提交非有限材质或动画参数。");
        // 一格一个落地实体：所属行只取根部占格，图片偏移和内部动画不改变这一行。
        int rowIndex = Mathf.FloorToInt(anchor.y);
        float rowY = rowIndex + .5f;
        local.m13 -= rowY;
        var key = new PartKey(domain, entityId, part);
        var groupKey = new GroupKey(material, sprite.texture, sprite.associatedAlphaSplitTexture, order);
        if (entries.TryGetValue(key, out Entry previous) &&
            (previous.Group.Row.Index != rowIndex || !previous.Group.Key.Equals(groupKey)))
        {
            Remove(domain, entityId, part);
            previous = null;
        }
        var value = new Visual(sprite, local, tint, crop, animation, region,
            occluder, highlighted, state);
        if (previous != null)
        {
            if (previous.Visual.Equals(value)) return;
            if (previous.Visual.Sprite != sprite) previous.Group.TopologyDirty = true;
            previous.Visual = value;
            previous.Geometry = geometry;
            previous.Dirty = true;
            dirtyGroups.Add(previous.Group);
            return;
        }
        if (!rows.TryGetValue(rowIndex, out Row row))
            rows.Add(rowIndex, row = new Row(parent, rowIndex));
        if (!row.Groups.TryGetValue(groupKey, out Group group))
        {
            try { row.Groups.Add(groupKey, group = new Group(row, groupKey)); }
            catch
            {
                if (row.Groups.Count == 0)
                {
                    rows.Remove(rowIndex);
                    DestroyObject(row.Root != null ? row.Root.gameObject : null);
                }
                throw;
            }
            DrawGroupCount++;
        }
        var entry = new Entry(key, value, group, geometry);
        entries.Add(key, entry);
        group.Entries.Add(entry);
        group.TopologyDirty = true;
        dirtyGroups.Add(group);
    }

    internal void Remove(int domain, int entityId, int part)
    {
        if (!entries.Remove(new PartKey(domain, entityId, part), out Entry entry)) return;
        Group group = entry.Group;
        group.Entries.Remove(entry);
        group.TopologyDirty = true;
        if (group.Entries.Count > 0) { dirtyGroups.Add(group); return; }
        dirtyGroups.Remove(group);
        group.Row.Groups.Remove(group.Key);
        group.Dispose();
        DrawGroupCount--;
        if (group.Row.Groups.Count == 0)
        {
            rows.Remove(group.Row.Index);
            DestroyObject(group.Row.Root != null ? group.Row.Root.gameObject : null);
        }
    }

    internal void RemoveEntity(int domain, int entityId)
    {
        removalScratch.Clear();
        foreach (PartKey key in entries.Keys)
            if (key.Domain == domain && key.Entity == entityId) removalScratch.Add(key);
        foreach (PartKey key in removalScratch) Remove(key.Domain, key.Entity, key.Part);
        removalScratch.Clear();
    }

    internal void Clear()
    {
        foreach (Row row in rows.Values)
        {
            foreach (Group group in row.Groups.Values) group.Dispose();
            DestroyObject(row.Root != null ? row.Root.gameObject : null);
        }
        entries.Clear(); rows.Clear(); dirtyGroups.Clear(); removalScratch.Clear();
        DrawGroupCount = 0;
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        active.Remove(this);
        Clear();
    }
    #endregion

    #region 帧末上传与排序
    /// <summary>所有 Update/LateUpdate 完成后、相机裁剪前上传脏行；多相机不重复上传静态网格。</summary>
    private static void BeforeRendering(ScriptableRenderContext context, List<Camera> cameras)
    {
        if (SharedSpriteMeshCache.IsSessionEnding) return;
        using (FlushMarker.Auto())
        {
            BeforeFlush?.Invoke();
            flushScratch.Clear();
            flushScratch.AddRange(active);
            foreach (ChunkDepthMeshRenderer renderer in flushScratch)
            {
                if (renderer.parent == null) renderer.Dispose();
                else if (!renderer.IsDisposed) renderer.Flush();
            }
            flushScratch.Clear();
        }
    }

    internal void Flush()
    {
        foreach (Group group in dirtyGroups) group.Flush();
        dirtyGroups.Clear();
    }

    private static void ReleaseAll()
    {
        flushScratch.Clear(); flushScratch.AddRange(active);
        foreach (ChunkDepthMeshRenderer renderer in flushScratch) renderer.Dispose();
        flushScratch.Clear();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        ReleaseAll();
        RenderPipelineManager.beginContextRendering -= BeforeRendering;
        SharedSpriteMeshCache.Clearing -= ReleaseAll;
        hooked = false;
        BeforeFlush = null;
    }

    private sealed class Row
    {
        internal readonly int Index;
        internal readonly Transform Root;
        internal readonly int Layer;
        internal readonly Dictionary<GroupKey, Group> Groups = new();
        internal Row(Transform parent, int index)
        {
            Index = index;
            Root = CreateNode("DepthRow_" + index, parent);
            Root.localPosition = new Vector3(0f, index + .5f, 0f);
            WorldSortingManager manager = WorldSortingManager.GetInstance();
            int layer, order;
            if (manager != null) manager.GetSortingKey(WorldSortingManager.WorldItemCategory, out layer, out order);
            else WorldSortingManager.GetResourceSortingKey(WorldSortingManager.WorldItemCategory, out layer, out order);
            Layer = layer;
            SortingGroup sorting = Root.gameObject.AddComponent<SortingGroup>();
            sorting.sortAtRoot = true;
            sorting.sortingLayerID = layer;
            sorting.sortingOrder = order;
        }
    }

    private static Transform CreateNode(string name, Transform parent)
    {
        var node = new GameObject(name) { hideFlags = HideFlags.DontSave, layer = parent.gameObject.layer };
        node.transform.SetParent(parent, false);
        return node.transform;
    }

    private static void DestroyObject(UnityEngine.Object value)
    {
        if (value == null) return;
        if (value is GameObject node) node.SetActive(false);
        if (Application.isPlaying) UnityEngine.Object.Destroy(value);
        else UnityEngine.Object.DestroyImmediate(value);
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Finite(Matrix4x4 matrix)
    {
        for (int i = 0; i < 16; i++) if (!Finite(matrix[i])) return false;
        return true;
    }
    #endregion

    #region 部件身份与冻结输入
    private readonly struct PartKey : IEquatable<PartKey>
    {
        internal readonly int Domain, Entity, Part;
        internal PartKey(int domain, int entity, int part) { Domain = domain; Entity = entity; Part = part; }
        public bool Equals(PartKey other) => Domain == other.Domain && Entity == other.Entity && Part == other.Part;
        public override bool Equals(object other) => other is PartKey key && Equals(key);
        public override int GetHashCode() => HashCode.Combine(Domain, Entity, Part);
    }

    private readonly struct GroupKey : IEquatable<GroupKey>
    {
        internal readonly Material Source;
        internal readonly Texture2D Texture, Alpha;
        internal readonly int Order;
        internal GroupKey(Material source, Texture2D texture, Texture2D alpha, int order)
        { Source = source; Texture = texture; Alpha = alpha; Order = order; }
        public bool Equals(GroupKey other) => Source == other.Source && Texture == other.Texture && Alpha == other.Alpha && Order == other.Order;
        public override bool Equals(object other) => other is GroupKey key && Equals(key);
        public override int GetHashCode() => HashCode.Combine(Source, Texture, Alpha, Order);
    }

    private readonly struct Visual : IEquatable<Visual>
    {
        internal readonly Sprite Sprite;
        internal readonly Matrix4x4 Matrix;
        internal readonly Color Tint;
        internal readonly Vector4 Crop, Animation, Region, State;
        internal readonly bool Occluder, Highlighted;
        internal Visual(Sprite sprite, Matrix4x4 matrix, Color tint, Vector4 crop, Vector4 animation,
            Vector4 region, bool occluder, bool highlighted, Vector4 state)
        { Sprite = sprite; Matrix = matrix; Tint = tint; Crop = crop; Animation = animation; Region = region; Occluder = occluder; Highlighted = highlighted; State = state; }
        public bool Equals(Visual other) => Sprite == other.Sprite && Matrix.Equals(other.Matrix) && Tint.Equals(other.Tint) &&
            Crop.Equals(other.Crop) && Animation.Equals(other.Animation) && Region.Equals(other.Region) &&
            Occluder == other.Occluder && Highlighted == other.Highlighted && State.Equals(other.State);
    }

    private sealed class Entry
    {
        internal readonly PartKey Key;
        internal readonly Group Group;
        internal Visual Visual;
        internal SharedSpriteMeshCache.Geometry Geometry;
        internal bool Dirty = true;
        internal int VertexStart;
        internal Bounds Bounds;
        internal Entry(PartKey key, Visual visual, Group group, SharedSpriteMeshCache.Geometry geometry)
        { Key = key; Visual = visual; Group = group; Geometry = geometry; }
    }
    #endregion
}
