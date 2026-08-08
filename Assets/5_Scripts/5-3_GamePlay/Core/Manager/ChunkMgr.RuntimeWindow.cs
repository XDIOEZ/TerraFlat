using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

public partial class ChunkMgr
{
    private sealed class RuntimeChunkBinding
    {
        public ChunkView View;
        public bool WantsPresentation;
    }

    private readonly Dictionary<RuntimeWorldAddress, RuntimeChunkBinding> activeRuntimeBindings = new();
    private readonly HashSet<RuntimeWorldAddress> runtimeWindowTargets = new();
    private readonly List<RuntimeWorldAddress> runtimeWindowRemovalBuffer = new();
    private readonly Queue<ChunkView> chunkViewPool = new();

    /// <summary>尝试找到指定区块当前正在使用的画面对象。</summary>
    public bool TryGetRuntimeChunkView(RuntimeWorldAddress address, out ChunkView view)
    {
        view = null;
        if (!activeRuntimeBindings.TryGetValue(address, out RuntimeChunkBinding binding))
            return false;
        view = binding.View;
        return view != null && view.IsBound;
    }

    /// <summary>以玩家附近位置刷新区块窗口，启动生成、绑定画面并回收远处区块。</summary>
    public void RefreshRuntimeWindow(Vector2 center, int activeDistance, int destroyDistance,
        bool includeLocalPresentation)
    {
        EnsureWorldRuntime();
        activeDistance = Mathf.Max(1, activeDistance);
        destroyDistance = Mathf.Max(activeDistance, destroyDistance);
        string dimensionId = ResolveCurrentDimensionId();
        ChunkGenerationProfileSO profileAsset = DimensionManager.Instance?.GetActiveGenerationProfile();
        ChunkGenerationProfileSnapshot profile = profileAsset != null
            ? profileAsset.CreateSnapshot()
            : defaultGenerationSnapshot;
        profile = ApplyWorldCoordinateScale(profile);
        activeGenerationSnapshot = profile;
        ChunkGenerationTopologySnapshot topology = ResolveActiveGenerationTopology();
        int stepX = profile.Width;
        int stepY = profile.Height;
        Vector2Int centerOrigin = NormalizeChunkPosition(new Vector2Int(
            Mathf.FloorToInt(center.x / stepX) * stepX,
            Mathf.FloorToInt(center.y / stepY) * stepY));
        int seed = SaveDataMgr.Instance?.SaveData?.Seed ?? 1;
        var centerAddress = new RuntimeWorldAddress(dimensionId,
            new Int2(centerOrigin.x, centerOrigin.y));
        runtimeChunkManager.RefreshWindow(new ChunkWindowRequest(centerAddress,
            activeDistance, destroyDistance, includeLocalPresentation, seed, profile, topology));

        runtimeWindowTargets.Clear();
        int radius = activeDistance - 1;
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                Vector2Int origin = NormalizeChunkPosition(new Vector2Int(
                    centerOrigin.x + dx * stepX,
                    centerOrigin.y + dy * stepY));
                var address = new RuntimeWorldAddress(dimensionId, new Int2(origin.x, origin.y));
                runtimeWindowTargets.Add(address);
                if (!activeRuntimeBindings.TryGetValue(address, out RuntimeChunkBinding binding))
                {
                    binding = new RuntimeChunkBinding();
                    activeRuntimeBindings.Add(address, binding);
                }

                binding.WantsPresentation = includeLocalPresentation;
                BeginRuntimeChunkBinding(address, binding, profile, seed, topology);
            }
        }

        runtimeWindowRemovalBuffer.Clear();
        foreach (KeyValuePair<RuntimeWorldAddress, RuntimeChunkBinding> pair in activeRuntimeBindings)
        {
            if (!runtimeWindowTargets.Contains(pair.Key))
                runtimeWindowRemovalBuffer.Add(pair.Key);
        }
        for (int i = 0; i < runtimeWindowRemovalBuffer.Count; i++)
            DeactivateRuntimeBinding(runtimeWindowRemovalBuffer[i]);

    }

    /// <summary>等待区块数据生成完成，再把区块绑定到可复用的 ChunkView。</summary>
    private async void BeginRuntimeChunkBinding(RuntimeWorldAddress address,
        RuntimeChunkBinding binding, ChunkGenerationProfileSnapshot snapshot, int seed,
        ChunkGenerationTopologySnapshot topology)
    {
        try
        {
            ChunkRuntime chunk = await RequestChunkDataAsync(address, seed, snapshot,
                topology: topology);
            if (!activeRuntimeBindings.TryGetValue(address, out RuntimeChunkBinding current) ||
                !ReferenceEquals(current, binding) || !binding.WantsPresentation || binding.View != null)
                return;

            ChunkView prefab = DimensionManager.Instance?.GetActiveChunkViewPrefab();
            if (prefab == null)
                return;
            ChunkView view = AcquireChunkView(prefab);
            binding.View = view;
            view.gameObject.SetActive(true);
            view.Bind(WorldRuntime, chunk, includeNavigation: true);
        }
        catch (OperationCanceledException)
        {
            // Window moved before generation finished.
        }
        catch (Exception exception)
        {
            Debug.LogError($"[ChunkMgr] 无头区块绑定失败 {address}: {exception}", this);
        }
    }

    /// <summary>优先从对象池取出 ChunkView，没有可用对象时才实例化新的。</summary>
    private ChunkView AcquireChunkView(ChunkView prefab)
    {
        while (chunkViewPool.Count > 0)
        {
            ChunkView pooled = chunkViewPool.Dequeue();
            if (pooled != null)
                return pooled;
        }
        return Instantiate(prefab, transform);
    }

    /// <summary>后台生成完成后修复画面绑定，处理旧任务晚到或区块被回收的情况。</summary>
    private void ReconcileRuntimeWindowBindings()
    {
        if (runtimeChunkManager == null || activeRuntimeBindings.Count == 0)
            return;
        ChunkView prefab = DimensionManager.Instance?.GetActiveChunkViewPrefab();
        if (prefab == null)
            return;

        foreach (KeyValuePair<RuntimeWorldAddress, RuntimeChunkBinding> pair in activeRuntimeBindings)
        {
            RuntimeChunkBinding binding = pair.Value;
            if (!binding.WantsPresentation)
                continue;
            if (!runtimeChunkManager.TryGetChunk(pair.Key, out ChunkRuntime current) ||
                current.DataStatus != ChunkDataStatus.Ready || current.Terrain == null)
                continue;

            if (binding.View != null && binding.View.IsBound &&
                ReferenceEquals(binding.View.Model, current))
                continue;
            if (binding.View != null)
            {
                binding.View.Unbind();
                binding.View.gameObject.SetActive(false);
                binding.View.transform.SetParent(transform, false);
                chunkViewPool.Enqueue(binding.View);
            }

            ChunkView view = AcquireChunkView(prefab);
            binding.View = view;
            view.gameObject.SetActive(true);
            view.Bind(WorldRuntime, current, includeNavigation: true);
        }
    }

    /// <summary>解除指定区块的画面绑定，并把 ChunkView 放回对象池。</summary>
    private void DeactivateRuntimeBinding(RuntimeWorldAddress address)
    {
        if (!activeRuntimeBindings.TryGetValue(address, out RuntimeChunkBinding binding))
            return;
        activeRuntimeBindings.Remove(address);
        if (binding.View != null)
        {
            binding.View.Unbind();
            binding.View.gameObject.SetActive(false);
            binding.View.transform.SetParent(transform, false);
            chunkViewPool.Enqueue(binding.View);
            binding.View = null;
        }
    }

    /// <summary>清空全部区块画面绑定和窗口目标记录。</summary>
    private void ClearRuntimeWindowBindings()
    {
        runtimeWindowRemovalBuffer.Clear();
        runtimeWindowRemovalBuffer.AddRange(activeRuntimeBindings.Keys);
        for (int i = 0; i < runtimeWindowRemovalBuffer.Count; i++)
            DeactivateRuntimeBinding(runtimeWindowRemovalBuffer[i]);
        runtimeWindowRemovalBuffer.Clear();
        runtimeWindowTargets.Clear();
    }
}
