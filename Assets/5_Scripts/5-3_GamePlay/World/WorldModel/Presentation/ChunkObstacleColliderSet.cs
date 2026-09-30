using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 区块独立障碍的物理表现容器：每个实体一个可复用 BoxCollider2D，0 个独立刚体、0 个更新回调。
/// 子 Collider 共用父节点静态刚体，明确排除 Composite；身份键分开机器与自然物，解绑清形状但保留对象池。
/// </summary>
internal sealed class ChunkObstacleColliderSet
{
    #region 身份与对象池

    internal const byte MachineKind = 1, NaturalKind = 2;
    private readonly Transform parent;
    private readonly Dictionary<long, BoxCollider2D> colliders = new();
    private readonly Stack<BoxCollider2D> pool = new();
    private readonly Dictionary<long, ulong> revisions = new();
    private readonly Dictionary<long, CopyState> copiedStates = new();
    private ulong nextRevision;
    private readonly List<long> removeScratch = new();
    private readonly HashSet<long> copiedKeys = new();

    internal ChunkObstacleColliderSet(Transform parent) => this.parent = parent;
    internal int Count => colliders.Count;
    internal static long Key(byte kind, int id) => ((long)kind << 32) | (uint)id;
    internal bool Contains(long key) => colliders.ContainsKey(key);

    /// <summary>复用组件后先保持禁用，设置完整形状再启用，避免默认尺寸参加一次物理合成。</summary>
    private BoxCollider2D Rent()
    {
        while (pool.Count > 0)
        {
            BoxCollider2D cached = pool.Pop();
            if (cached != null) return cached;
        }
        var root = new GameObject("Data Obstacle Box");
        root.hideFlags = HideFlags.DontSave;
        root.SetActive(false);
        root.transform.SetParent(parent, false);
        BoxCollider2D collider = root.AddComponent<BoxCollider2D>();
        collider.enabled = false;
        collider.usedByComposite = false;
        root.SetActive(true);
        return collider;
    }

    internal bool Remove(long key)
    {
        if (!colliders.TryGetValue(key, out BoxCollider2D collider)) return false;
        colliders.Remove(key);
        revisions.Remove(key);
        copiedStates.Remove(key);
        if (collider != null)
        {
            collider.enabled = false;
            pool.Push(collider);
        }
        return true;
    }

    internal int ClearKind(byte kind)
    {
        removeScratch.Clear();
        foreach (long key in colliders.Keys)
            if ((byte)(key >> 32) == kind) removeScratch.Add(key);
        return RemoveCollected();
    }

    internal int RemoveExcept(byte kind, HashSet<int> retained)
    {
        removeScratch.Clear();
        foreach (long key in colliders.Keys)
            if ((byte)(key >> 32) == kind && !retained.Contains(unchecked((int)(uint)key))) removeScratch.Add(key);
        return RemoveCollected();
    }

    private int RemoveCollected()
    {
        int count = removeScratch.Count;
        for (int i = 0; i < count; i++) Remove(removeScratch[i]);
        removeScratch.Clear();
        return count;
    }

    internal void Clear()
    {
        foreach (BoxCollider2D collider in colliders.Values)
            if (collider != null)
            {
                collider.enabled = false;
                pool.Push(collider);
            }
        colliders.Clear();
        revisions.Clear();
        copiedStates.Clear();
        copiedKeys.Clear();
    }

    internal void Dispose()
    {
        Clear();
        while (pool.Count > 0)
        {
            BoxCollider2D collider = pool.Pop();
            if (collider != null) Object.Destroy(collider.gameObject);
        }
    }

    #endregion

    #region 增量形状与镜像

    /// <summary>只写实际变化的物理属性；纯视觉、库存和其它状态变化不重建原生形状。</summary>
    internal bool Set(long key, Vector2 center, Vector2 size, Collider2D template,
        Collider2D source = null, Vector2 imageOffset = default)
    {
        bool changed = false;
        if (!colliders.TryGetValue(key, out BoxCollider2D collider) || collider == null)
        {
            collider = Rent();
            colliders[key] = collider;
            changed = true;
        }
        bool centerChanged = !collider.offset.Equals(center), sizeChanged = !collider.size.Equals(size);
        // 改尺寸/偏移时只创建最终矩形，避免两个 Setter 连续重建原生形状。
        if ((centerChanged || sizeChanged) && collider.enabled) collider.enabled = false;
        if (centerChanged) { collider.offset = center; changed = true; }
        if (sizeChanged) { collider.size = size; changed = true; }
        if (collider.gameObject.layer != template.gameObject.layer)
        { collider.gameObject.layer = template.gameObject.layer; changed = true; }
        if (collider.sharedMaterial != template.sharedMaterial)
        { collider.sharedMaterial = template.sharedMaterial; changed = true; }
        if (collider.isTrigger != template.isTrigger) { collider.isTrigger = template.isTrigger; changed = true; }
        if (collider.usedByEffector != template.usedByEffector)
        { collider.usedByEffector = template.usedByEffector; changed = true; }
        if (collider.layerOverridePriority != template.layerOverridePriority)
        { collider.layerOverridePriority = template.layerOverridePriority; changed = true; }
        if (collider.includeLayers != template.includeLayers)
        { collider.includeLayers = template.includeLayers; changed = true; }
        if (collider.excludeLayers != template.excludeLayers)
        { collider.excludeLayers = template.excludeLayers; changed = true; }
        if (collider.forceSendLayers != template.forceSendLayers)
        { collider.forceSendLayers = template.forceSendLayers; changed = true; }
        if (collider.forceReceiveLayers != template.forceReceiveLayers)
        { collider.forceReceiveLayers = template.forceReceiveLayers; changed = true; }
        if (collider.contactCaptureLayers != template.contactCaptureLayers)
        { collider.contactCaptureLayers = template.contactCaptureLayers; changed = true; }
        if (collider.callbackLayers != template.callbackLayers)
        { collider.callbackLayers = template.callbackLayers; changed = true; }
        if (source != null)
        {
            ColliderSource2D marker = collider.GetComponent<ColliderSource2D>();
            if (marker == null) marker = collider.gameObject.AddComponent<ColliderSource2D>();
            if (marker.SourceCollider != source || !marker.ImageOffset.Equals(imageOffset))
            { marker.Bind(source, imageOffset); changed = true; }
        }
        if (!collider.enabled) { collider.enabled = true; changed = true; }
        if (changed) revisions[key] = ++nextRevision;
        return changed;
    }

    /// <summary>镜像保留源身份和独立矩形，不复制 Polygon 路径或调用 Composite.GenerateGeometry。</summary>
    internal void CopyTo(ChunkObstacleColliderSet target, Vector2 imageOffset)
    {
        target.copiedKeys.Clear();
        foreach (var pair in colliders)
        {
            BoxCollider2D source = pair.Value;
            if (source == null || !source.enabled) continue;
            target.copiedKeys.Add(pair.Key);
            ulong revision = revisions[pair.Key];
            if (target.copiedStates.TryGetValue(pair.Key, out CopyState copied) &&
                copied.Source == source && copied.Revision == revision && copied.ImageOffset.Equals(imageOffset)) continue;
            target.Set(pair.Key, source.offset, source.size, source, source, imageOffset);
            target.copiedStates[pair.Key] = new CopyState(source, revision, imageOffset);
        }
        target.removeScratch.Clear();
        foreach (long key in target.colliders.Keys)
            if (!target.copiedKeys.Contains(key)) target.removeScratch.Add(key);
        target.RemoveCollected();
    }

    private readonly struct CopyState
    {
        public readonly BoxCollider2D Source;
        public readonly ulong Revision;
        public readonly Vector2 ImageOffset;
        public CopyState(BoxCollider2D source, ulong revision, Vector2 imageOffset)
        { Source = source; Revision = revision; ImageOffset = imageOffset; }
    }

    #endregion
}
