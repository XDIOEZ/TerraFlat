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
    [SerializeField] private ChunkNaturalItemRenderer naturalItemRenderer;
    [SerializeField] private ChunkLightOccluderRenderer lightOccluderRenderer;
    private ChunkTilemapRenderer terrainRenderer;

    public ChunkRuntime Model => chunk;
    public bool IsBound => chunk != null && presentationComplete;
    public bool IsBinding => chunk != null && !presentationComplete;
    /// <summary>基础地形已提交给 BRG，后续草地、碰撞和自然物可以继续分帧绑定。</summary>
    public bool IsBaseTerrainPresented =>
        chunk != null && terrainRenderer != null && terrainRenderer.IsBatchPresentationComplete;

    /// <summary>完整绑定或解绑后的表现状态通知，覆盖同步、增量绑定以及回池/销毁。</summary>
    public event Action<FlatWorld.WorldModel.WorldAddress> PresentationChanged;
    /// <summary>当前 View 保留的阴影槽数量，供流送 Profiler 与回归检查使用。</summary>
    public int RetainedOccluderCount => lightOccluderRenderer?.RetainedOccluderCount ?? 0;

    /// <summary>只读取得基础地形 BRG 状态；诊断调用不会创建、重建或修复渲染后端。</summary>
    public bool TryGetTerrainBatchDebugState(out bool registered, out int visualCount)
    {
        ChunkTilemapRenderer tilemapRenderer = null;
        for (int i = 0; i < renderers.Count; i++)
        {
            if (renderers[i] is ChunkTilemapRenderer candidate)
            {
                tilemapRenderer = candidate;
                break;
            }
        }

        if (tilemapRenderer == null)
            tilemapRenderer = GetComponentInChildren<ChunkTilemapRenderer>(true);
        if (tilemapRenderer == null)
        {
            registered = false;
            visualCount = 0;
            return false;
        }

        registered = ChunkBatchRendererGroupService.IsOwnerRegistered(tilemapRenderer);
        visualCount = ChunkBatchRendererGroupService.GetOwnerVisualCount(tilemapRenderer);
        return true;
    }

    /// <summary>低频检查可丢失的外部渲染后端登记，并只修复对应表现层。</summary>
    public bool RepairPresentationBackendIfNeeded()
    {
        if (chunk == null || !presentationComplete)
            return false;
        for (int i = 0; i < renderers.Count; i++)
        {
            if (renderers[i] is ChunkTilemapRenderer tilemapRenderer)
                return tilemapRenderer.RepairBatchPresentationIfNeeded();
        }

        // Editor 脚本热重载可能清掉接口缓存，但场景组件仍存在；按组件重新找一次即可自愈。
        ChunkTilemapRenderer fallback = GetComponentInChildren<ChunkTilemapRenderer>(true);
        return fallback != null && fallback.RepairBatchPresentationIfNeeded();
    }

    /// <summary>区块所需表现组件由 Prefab 明确装配，避免流送时动态添加脚本组件。</summary>
    private void Awake()
    {
        if (naturalItemRenderer == null || lightOccluderRenderer == null)
            throw new InvalidOperationException("ChunkView Prefab 缺少自然物或光遮挡表现组件。");
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
        PresentationChanged?.Invoke(chunk.Address);
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
            PresentationChanged?.Invoke(chunk.Address);
        }
    }

    /// <summary>先终止绑定与事件回调，再解除表现；重复禁用、销毁不再次清理。</summary>
    public void Unbind()
    {
        if (chunk == null && world == null)
            return;

        bool hadChunk = chunk != null;
        FlatWorld.WorldModel.WorldAddress previousAddress = hadChunk ? chunk.Address : default;
        bindVersion++;
        chunk = null;
        world = null;
        navigationEnabled = false;
        presentationComplete = false;
        committedSubscription?.Dispose();
        committedSubscription = null;
        try
        {
            for (int i = renderers.Count - 1; i >= 0; i--)
            {
                // 接口引用不具备 Unity null 语义，子组件可能已先于 View 销毁。
                if (renderers[i] is MonoBehaviour behaviour && behaviour != null)
                    renderers[i].Unbind();
            }
        }
        finally
        {
            // 即使某个表现器解绑失败，也必须释放世界事件订阅，避免池化后继续收到旧世界通知。
            for (int i = 0; i < renderers.Count; i++)
                if (renderers[i] is IWorldAwareChunkViewRenderer worldAware &&
                    renderers[i] is MonoBehaviour behaviour && behaviour != null)
                    worldAware.SetWorld(null);
            renderers.Clear();
            terrainRenderer = null;
            navigationLease?.Dispose();
            navigationLease = null;
            presentationLease?.Dispose();
            presentationLease = null;
            if (hadChunk)
                PresentationChanged?.Invoke(previousAddress);
        }
    }

    /// <summary>保存当前 ChunkView 下自然物的权威状态。</summary>
    public void CaptureNaturalItemState()
    {
        GetComponent<ChunkAgricultureRenderer>().CaptureState();
        naturalItemRenderer.CaptureState();
    }

    #region 池化资源

    /// <summary>从对象池取出时清除禁用期间遗留的裁剪状态。</summary>
    public void PrepareForPoolReuse()
    {
        if (chunk != null || world != null)
            Unbind();
        lightOccluderRenderer?.PrepareForPoolReuse();
    }

    /// <summary>由 ChunkMgr 在禁用状态下主动裁掉长期池化 View 的历史资源。</summary>
    public void TrimPooledResources()
    {
        if (chunk != null || world != null)
            return;
        lightOccluderRenderer?.TrimPooledResources();
    }

    #endregion

    /// <summary>自动保存专用的自然物分帧快照入口。</summary>
    public IEnumerator CaptureNaturalItemStateCoroutine()
    {
        GetComponent<ChunkAgricultureRenderer>().CaptureState();
        IEnumerator captureRoutine = naturalItemRenderer.CaptureStateCoroutine();
        while (captureRoutine.MoveNext())
            yield return captureRoutine.Current;
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
        terrainRenderer = null;
        MonoBehaviour[] behaviours = GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IChunkViewRenderer renderer && !ReferenceEquals(renderer, this))
            {
                renderers.Add(renderer);
                if (renderer is ChunkTilemapRenderer terrain)
                    terrainRenderer = terrain;
            }
        }
        renderers.Sort((left, right) =>
            ResolveRendererPriority(left).CompareTo(ResolveRendererPriority(right)));
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
        CacheRenderers();
        for (int i = 0; i < renderers.Count; i++)
            if (renderers[i] is IWorldAwareChunkViewRenderer worldAware)
                worldAware.SetWorld(worldRuntime);
        presentationLease = chunk.AcquireLease(ChunkLeaseKind.Presentation);
        if (includeNavigation)
            navigationLease = chunk.AcquireLease(ChunkLeaseKind.Navigation);
        committedSubscription = world.Events.Subscribe<ChunkCommitted>(HandleChunkCommitted);
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
        if (renderer is ChunkLightOccluderRenderer)
            return 3;
        if (renderer is ChunkGrassRenderer)
            return 4;
        if (renderer is ChunkNavigationBinder)
            return 5;
        if (renderer is ChunkNaturalItemRenderer)
            return 6;
        if (renderer is ChunkAgricultureRenderer)
            return 7;
        return 2;
    }
}
