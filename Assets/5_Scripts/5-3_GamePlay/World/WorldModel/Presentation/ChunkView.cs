using System;
using System.Collections;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;
using Unity.Profiling;

public sealed class ChunkView : MonoBehaviour
{
    private static readonly ProfilerMarker RendererBindMarker =
        new("FlatWorld.ChunkStreaming.BindRendererStep");
    private readonly List<IChunkViewRenderer> renderers = new();
    private WorldRuntime world;
    private ChunkRuntime chunk;
    private ChunkLease presentationLease;
    private ChunkLease navigationLease;
    private IDisposable committedSubscription;
    private bool navigationEnabled;
    private int bindVersion;
    private bool presentationComplete;

    public ChunkRuntime Model => chunk;
    public bool IsBound => chunk != null && presentationComplete;
    public bool IsBinding => chunk != null && !presentationComplete;

    /// <summary>确保运行时区块表现拥有独立的自然物品父节点。</summary>
    private void Awake()
    {
        EnsureNaturalItemRenderer();
    }

    public void Bind(WorldRuntime worldRuntime, ChunkRuntime chunkRuntime, bool includeNavigation = true)
    {
        if (worldRuntime == null)
            throw new ArgumentNullException(nameof(worldRuntime));
        if (chunkRuntime == null)
            throw new ArgumentNullException(nameof(chunkRuntime));
        if (chunkRuntime.DataStatus != ChunkDataStatus.Ready || chunkRuntime.Terrain == null)
            throw new InvalidOperationException($"Chunk data is not ready: {chunkRuntime.DataStatus}");
        if (ReferenceEquals(world, worldRuntime) && ReferenceEquals(chunk, chunkRuntime))
            return;

        PrepareBinding(worldRuntime, chunkRuntime, includeNavigation);
        for (int i = 0; i < renderers.Count; i++)
        {
            if (!navigationEnabled && renderers[i] is ChunkNavigationBinder)
                continue;
            renderers[i].Bind(chunk);
        }
        presentationComplete = true;
        chunk.MarkPresentationBound();
    }

    /// <summary>把同一区块的表现组件拆到多帧绑定；地面优先，草地和导航最后。</summary>
    public IEnumerator BindIncremental(WorldRuntime worldRuntime, ChunkRuntime chunkRuntime,
        bool includeNavigation = true, int renderersPerFrame = 1)
    {
        if (worldRuntime == null)
            throw new ArgumentNullException(nameof(worldRuntime));
        if (chunkRuntime == null)
            throw new ArgumentNullException(nameof(chunkRuntime));
        if (chunkRuntime.DataStatus != ChunkDataStatus.Ready || chunkRuntime.Terrain == null)
            throw new InvalidOperationException($"Chunk data is not ready: {chunkRuntime.DataStatus}");
        if (ReferenceEquals(world, worldRuntime) && ReferenceEquals(chunk, chunkRuntime))
            yield break;

        PrepareBinding(worldRuntime, chunkRuntime, includeNavigation);
        int version = bindVersion;
        int frameCount = 0;
        for (int i = 0; i < renderers.Count; i++)
        {
            if (version != bindVersion || !ReferenceEquals(chunk, chunkRuntime))
                yield break;
            if (!navigationEnabled && renderers[i] is ChunkNavigationBinder)
                continue;

            using (RendererBindMarker.Auto())
                renderers[i].Bind(chunk);
            frameCount++;
            if (frameCount >= Math.Max(1, renderersPerFrame) && i + 1 < renderers.Count)
            {
                frameCount = 0;
                yield return null;
            }
        }

        if (version == bindVersion && ReferenceEquals(chunk, chunkRuntime))
        {
            presentationComplete = true;
            chunk.MarkPresentationBound();
        }
    }

    public void Unbind()
    {
        bindVersion++;
        for (int i = renderers.Count - 1; i >= 0; i--)
            renderers[i].Unbind();
        committedSubscription?.Dispose();
        committedSubscription = null;
        navigationLease?.Dispose();
        navigationLease = null;
        presentationLease?.Dispose();
        presentationLease = null;
        chunk = null;
        world = null;
        navigationEnabled = false;
        presentationComplete = false;
    }

    /// <summary>保存当前 ChunkView 下自然物的权威状态。</summary>
    public void CaptureNaturalItemState()
    {
        ChunkNaturalItemRenderer renderer = GetComponentInChildren<ChunkNaturalItemRenderer>(true);
        renderer?.CaptureState();
    }

    private void OnDisable() => Unbind();
    private void OnDestroy() => Unbind();

    private void HandleChunkCommitted(ChunkCommitted committed)
    {
        if (chunk == null || committed.Address != chunk.Address ||
            chunk.DataStatus != ChunkDataStatus.Ready || chunk.Terrain == null)
            return;
        for (int i = 0; i < renderers.Count; i++)
        {
            if (!navigationEnabled && renderers[i] is ChunkNavigationBinder)
                continue;
            renderers[i].Unbind();
            using (RendererBindMarker.Auto())
                renderers[i].Bind(chunk);
        }
    }

    private void CacheRenderers()
    {
        renderers.Clear();
        MonoBehaviour[] behaviours = GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IChunkViewRenderer renderer && !ReferenceEquals(renderer, this))
                renderers.Add(renderer);
        }
        renderers.Sort((left, right) =>
            ResolveRendererPriority(left).CompareTo(ResolveRendererPriority(right)));
    }

    /// <summary>旧 ChunkView Prefab 无需手工改层级，首次加载时自动补齐 NaturalItems 子节点。</summary>
    private void EnsureNaturalItemRenderer()
    {
        ChunkNaturalItemRenderer renderer = GetComponentInChildren<ChunkNaturalItemRenderer>(true);
        if (renderer != null)
            return;

        var naturalItems = new GameObject("NaturalItems");
        naturalItems.transform.SetParent(transform, false);
        naturalItems.AddComponent<ChunkNaturalItemRenderer>();
    }

    /// <summary>建立租约和事件，再由同步或分帧入口绑定各表现组件。</summary>
    private void PrepareBinding(WorldRuntime worldRuntime, ChunkRuntime chunkRuntime,
        bool includeNavigation)
    {
        Unbind();
        world = worldRuntime;
        chunk = chunkRuntime;
        navigationEnabled = includeNavigation;
        presentationComplete = false;
        transform.position = new Vector3(chunk.Address.ChunkOrigin.X, chunk.Address.ChunkOrigin.Y, 0f);
        presentationLease = chunk.AcquireLease(ChunkLeaseKind.Presentation);
        if (includeNavigation)
            navigationLease = chunk.AcquireLease(ChunkLeaseKind.Navigation);
        committedSubscription = world.Events.Subscribe<ChunkCommitted>(HandleChunkCommitted);
        CacheRenderers();
    }

    /// <summary>先让地面可见，再补环境、碰撞、草地和导航。</summary>
    private static int ResolveRendererPriority(IChunkViewRenderer renderer)
    {
        if (renderer is ChunkTilemapRenderer)
            return 0;
        if (renderer is ChunkEnvironmentTilemapRenderer)
            return 1;
        if (renderer is ChunkCollisionRenderer)
            return 2;
        if (renderer is ChunkGrassRenderer)
            return 3;
        if (renderer is ChunkNavigationBinder)
            return 4;
        if (renderer is ChunkNaturalItemRenderer)
            return 5;
        return 2;
    }
}
