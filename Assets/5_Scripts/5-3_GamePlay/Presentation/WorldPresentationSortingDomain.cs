using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>独立世界共用外层排序组，域内按脚点排列物体并保留各自部件顺序。</summary>
public sealed class WorldPresentationSortingDomain : IDisposable
{
    #region 世界显示排序域
    private readonly GameObject root;
    private readonly SortingGroup sorting;
    private bool disposed;
    private bool disposing;
    private string category;
    private readonly Dictionary<UnityEngine.Object, LocalSortingEntry> localEntries = new();
    private readonly List<LocalSortingEntry> sortingScratch = new();
    private readonly List<UnityEngine.Object> removedScratch = new();
    private readonly HashSet<WorldPresentationProjection> projections = new();
    private static readonly HashSet<WorldPresentationSortingDomain> activeDomains = new();
    private static readonly List<WorldPresentationSortingDomain> domainScratch = new();
    private static readonly Comparison<LocalSortingEntry> compareLocalSortingEntries = LocalSortingEntry.Compare;
    private bool localSortingDirty;
    private bool localGroupsDirty;
    private bool preparing;
    private int lastPreparedFrame = -1;

    public Transform Root => !disposed && root != null ? root.transform : null;
    public string WorldKey { get; }
    public bool IsDisposed => disposed || root == null;
    public event Action Disposing;

    private WorldPresentationSortingDomain(Transform parent, string worldKey, string category)
    {
        if (string.IsNullOrWhiteSpace(worldKey)) throw new ArgumentException("排序域缺少世界身份。", nameof(worldKey));
        WorldKey = worldKey;
        root = new GameObject("WorldSortingDomain_" + worldKey) { hideFlags = HideFlags.DontSave };
        // 保留所属场景，但不继承宿主缩放或旋转。
        if (parent != null)
        {
            root.transform.SetParent(parent, true);
            root.transform.SetParent(null, true);
        }
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        root.transform.localScale = Vector3.one;
        sorting = root.AddComponent<SortingGroup>();
        sorting.sortAtRoot = true;
        root.AddComponent<WorldPresentationSortingDomainLifetime>().Bind(this);
        try { SetCategory(category); }
        catch { DestroyRoot(); throw; }
        if (activeDomains.Add(this) && activeDomains.Count == 1)
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
    }

    public static WorldPresentationSortingDomain Create(Transform parent, string worldKey, string category)
        => new(parent, worldKey, category);

    public void SetCategory(string sortingCategory)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(WorldPresentationSortingDomain));
        if (string.IsNullOrWhiteSpace(sortingCategory)) throw new ArgumentException("排序域缺少类别。", nameof(sortingCategory));
        if (category == sortingCategory) return;
        WorldSortingManager.GetResourceSortingKey(sortingCategory, out int layer, out int order);
        sorting.sortingLayerID = layer;
        sorting.sortingOrder = order;
        category = sortingCategory;
        localGroupsDirty = true;
    }

    public void Synchronize(Matrix4x4 worldToDisplay, bool visible)
    {
        if (disposed || root == null) return;
        Vector3 anchor = worldToDisplay.MultiplyPoint3x4(Vector3.zero);
        if (root.transform.position != anchor) root.transform.position = anchor;
        if (root.transform.rotation != Quaternion.identity) root.transform.rotation = Quaternion.identity;
        if (root.transform.localScale != Vector3.one) root.transform.localScale = Vector3.one;
        if (root.activeSelf != visible) root.SetActive(visible);
    }

    public void Dispose() => DisposeCore(true);

    private void DisposeCore(bool destroyRoot)
    {
        if (disposed || disposing) return;
        disposing = true;
        activeDomains.Remove(this);
        if (activeDomains.Count == 0) RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        if (Disposing != null)
        {
            foreach (Action handler in Disposing.GetInvocationList())
            {
                try { handler(); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
            Disposing = null;
        }
        foreach (LocalSortingEntry entry in localEntries.Values) entry.RestoreRawKey();
        localEntries.Clear(); sortingScratch.Clear(); removedScratch.Clear(); projections.Clear();
        disposed = true;
        if (destroyRoot) DestroyRoot();
    }

    private void DestroyRoot()
    {
        if (root == null) return;
        root.SetActive(false);
        if (Application.isPlaying) UnityEngine.Object.Destroy(root);
        else UnityEngine.Object.DestroyImmediate(root);
    }

    internal void OnRootDestroyed() => DisposeCore(false);
    #endregion

    #region 域内原子单元排序
    internal void AttachProjection(WorldPresentationProjection projection) => projections.Add(projection);
    internal void DetachProjection(WorldPresentationProjection projection) => projections.Remove(projection);
    internal void NotifyLocalSortingGroupsChanged() => localGroupsDirty = true;

    public void ApplyLocalRendererSortingKey(Renderer renderer, int layerId, int rawOrder,
        int renderQueue, float footY, int batchOrder = 0)
        => ApplyLocalSortingKey(renderer, layerId, rawOrder, renderQueue, footY, batchOrder);

    public void ApplyLocalSortingGroupKey(SortingGroup group, int layerId, int rawOrder,
        int renderQueue, float footY, int batchOrder = 0)
        => ApplyLocalSortingKey(group, layerId, rawOrder, renderQueue, footY, batchOrder);

    private void ApplyLocalSortingKey(Component target, int layerId, int rawOrder,
        int renderQueue, float footY, int batchOrder)
    {
        if (IsDisposed || disposing) throw new ObjectDisposedException(nameof(WorldPresentationSortingDomain));
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (rawOrder < short.MinValue || rawOrder > short.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(rawOrder));
        if (float.IsNaN(footY) || float.IsInfinity(footY)) throw new ArgumentOutOfRangeException(nameof(footY));
        footY -= root.transform.position.y;
        if (!localEntries.TryGetValue(target, out LocalSortingEntry entry))
        {
            if (!target.transform.IsChildOf(root.transform))
                throw new ArgumentException("排序单元不属于此世界域。", nameof(target));
            entry = new LocalSortingEntry(target);
            localEntries.Add(target, entry);
            localSortingDirty = true;
            localGroupsDirty = true;
        }
        if (entry.SetRawKey(layerId, rawOrder, renderQueue, footY, batchOrder)) localSortingDirty = true;
        else if (!localSortingDirty && entry.RepairAppliedRank()) localGroupsDirty = true;
    }

    public void RemoveLocalSortingKey(UnityEngine.Object target)
    {
        if (ReferenceEquals(target, null) || !localEntries.Remove(target, out LocalSortingEntry entry)) return;
        entry.RestoreRawKey();
        localSortingDirty = true;
        localGroupsDirty = true;
    }

    private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        bool groupsChanged = false;
        domainScratch.Clear(); domainScratch.AddRange(activeDomains);
        foreach (WorldPresentationSortingDomain domain in domainScratch)
        {
            bool refresh = domain.lastPreparedFrame != Time.frameCount;
            domain.lastPreparedFrame = Time.frameCount;
            groupsChanged |= domain.FlushLocalSortingKeysCore(refresh);
        }
        domainScratch.Clear();
        if (groupsChanged) SortingGroup.UpdateAllSortingGroups();
    }

    /// <summary>每层压缩原排序键，保留组内顺序并只更新发生变化的排序值。</summary>
    public void FlushLocalSortingKeys()
    {
        if (FlushLocalSortingKeysCore(true)) SortingGroup.UpdateAllSortingGroups();
    }

    private bool FlushLocalSortingKeysCore(bool refresh)
    {
        if (disposed || disposing || preparing) return false;
        if (refresh)
        {
            preparing = true;
            try
            {
                foreach (WorldPresentationProjection projection in projections)
                    if (projection != null) projection.RefreshBeforeLocalSorting();
            }
            finally { preparing = false; }
        }
        if (!localSortingDirty)
        {
            bool groupsChanged = localGroupsDirty;
            localGroupsDirty = false;
            return groupsChanged;
        }
        sortingScratch.Clear(); removedScratch.Clear();
        foreach (var pair in localEntries)
        {
            if (pair.Value.Target == null) removedScratch.Add(pair.Key);
            else sortingScratch.Add(pair.Value);
        }
        foreach (UnityEngine.Object key in removedScratch) localEntries.Remove(key);
        sortingScratch.Sort(compareLocalSortingEntries);
        int previousLayer = 0, rank = 0;
        bool first = true;
        foreach (LocalSortingEntry entry in sortingScratch)
        {
            if (first || entry.LayerId != previousLayer) { previousLayer = entry.LayerId; rank = short.MinValue; first = false; }
            else if (++rank > short.MaxValue) throw new InvalidOperationException("世界排序域的同层绘制单元超过排序序号容量。");
            if (entry.ApplyRank(rank)) localGroupsDirty = true;
        }
        localSortingDirty = false;
        bool changed = localGroupsDirty;
        localGroupsDirty = false;
        return changed;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSortingCallbacks()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        activeDomains.Clear(); domainScratch.Clear();
    }

    private sealed class LocalSortingEntry
    {
        private readonly Renderer renderer;
        private readonly SortingGroup group;
        private readonly int stableId;
        private int appliedRank = int.MinValue;
        internal readonly Component Target;
        internal int LayerId, RawOrder, RenderQueue, BatchOrder;
        internal float FootY;

        internal LocalSortingEntry(Component target)
        {
            Target = target;
            renderer = target as Renderer;
            group = target as SortingGroup;
            if (renderer == null && group == null) throw new ArgumentException("不支持的世界排序单元。", nameof(target));
            stableId = target.GetInstanceID();
        }

        internal bool SetRawKey(int layerId, int rawOrder, int renderQueue, float footY, int batchOrder)
        {
            bool changed = LayerId != layerId || RawOrder != rawOrder || RenderQueue != renderQueue ||
                           !FootY.Equals(footY) || BatchOrder != batchOrder || appliedRank == int.MinValue;
            LayerId = layerId; RawOrder = rawOrder; RenderQueue = renderQueue; FootY = footY; BatchOrder = batchOrder;
            return changed;
        }

        internal static int Compare(LocalSortingEntry left, LocalSortingEntry right)
        {
            int result = left.LayerId.CompareTo(right.LayerId);
            if (result != 0) return result;
            result = left.RawOrder.CompareTo(right.RawOrder);
            if (result != 0) return result;
            result = left.RenderQueue.CompareTo(right.RenderQueue);
            if (result != 0) return result;
            result = left.BatchOrder.CompareTo(right.BatchOrder);
            if (result != 0) return result;
            result = right.FootY.CompareTo(left.FootY);
            return result != 0 ? result : left.stableId.CompareTo(right.stableId);
        }

        internal bool ApplyRank(int rank)
        {
            bool groupChanged = false;
            if (renderer != null)
            {
                if (renderer.sortingLayerID != LayerId) { renderer.sortingLayerID = LayerId; groupChanged = true; }
                if (appliedRank != rank || renderer.sortingOrder != rank) { renderer.sortingOrder = rank; groupChanged = true; }
            }
            else if (group != null)
            {
                if (group.sortingLayerID != LayerId) { group.sortingLayerID = LayerId; groupChanged = true; }
                if (appliedRank != rank || group.sortingOrder != rank) { group.sortingOrder = rank; groupChanged = true; }
            }
            appliedRank = rank;
            return groupChanged;
        }

        internal bool RepairAppliedRank()
        {
            return appliedRank != int.MinValue && ApplyRank(appliedRank);
        }

        internal void RestoreRawKey()
        {
            if (renderer != null) { renderer.sortingLayerID = LayerId; renderer.sortingOrder = RawOrder; }
            else if (group != null) { group.sortingLayerID = LayerId; group.sortingOrder = RawOrder; }
        }
    }
    #endregion
}

/// <summary>场景卸载或外部销毁根节点时同步清理排序域。</summary>
internal sealed class WorldPresentationSortingDomainLifetime : MonoBehaviour
{
    #region 根节点生命周期
    private WorldPresentationSortingDomain domain;
    internal void Bind(WorldPresentationSortingDomain value) => domain = value;
    private void OnDestroy() { domain?.OnRootDestroyed(); domain = null; }
    #endregion
}
