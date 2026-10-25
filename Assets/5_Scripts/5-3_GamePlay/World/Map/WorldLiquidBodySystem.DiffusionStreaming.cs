using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlatWorld.WorldModel;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

public sealed partial class WorldLiquidBodySystem
{
    #region 扩散边界数据需求与预算
    private const int MaximumRetainedDiffusionChunks = 24;
    private const int MaximumQueuedDiffusionChunks = 64;
    private readonly Dictionary<RuntimeWorldAddress, ChunkLease> diffusionDataLeases = new();
    private readonly Dictionary<RuntimeWorldAddress, DeferredBoundaryDataRequest> deferredBoundaryRequests = new();
    private readonly Queue<RuntimeWorldAddress> boundaryRequestOrder = new();
    private readonly HashSet<RuntimeWorldAddress> failedBoundaryRequests = new();
    private readonly List<RuntimeWorldAddress> releaseDiffusionChunks = new();
    private DeferredBoundaryDataRequest loadingBoundaryRequest;
    public int RetainedDiffusionDataChunkCount => diffusionDataLeases.Count;
    public int PendingDiffusionBoundaryChunkCount => deferredBoundaryRequests.Count;
    public int LoadingDiffusionBoundaryChunkCount => loadingBoundaryRequest == null ? 0 : 1;
    public int PendingDiffusionCellCount => pendingSet.Count;

    private sealed class DeferredBoundaryDataRequest
    {
        public RuntimeWorldAddress Address;
        public readonly HashSet<Vector2Int> SourceCells = new();
        public CancellationTokenSource Cancellation;
        public Task<ChunkRuntime> Completion;
    }

    private bool TryRetainDiffusionChunkData(RuntimeWorldAddress address, bool reserveBoundaryRequestSlot = false)
    {
        if (diffusionDataLeases.ContainsKey(address)) return true;
        if (diffusionDataLeases.Count >= MaximumRetainedDiffusionChunks - (reserveBoundaryRequestSlot ? 1 : 0)) return false;
        diffusionDataLeases.Add(address, owner.AcquireChunkLease(address, ChunkLeaseKind.DataRetention));
        return true;
    }

    private void QueueDiffusionBoundaryDataRequest(RuntimeTerrainTileSample source, Vector2Int neighbour)
    {
        RuntimeWorldAddress address = owner.ResolveWorldAddress((Vector2)neighbour + Vector2.one * .5f, dimension);
        if (failedBoundaryRequests.Contains(address)) return;
        if (!TryRetainDiffusionChunkData(source.Address, true))
        {
            QueueCell(source.WorldCell);
            return;
        }
        if (!deferredBoundaryRequests.TryGetValue(address, out DeferredBoundaryDataRequest request))
        {
            if (deferredBoundaryRequests.Count >= MaximumQueuedDiffusionChunks)
            {
                QueueCell(source.WorldCell);
                return;
            }
            request = new DeferredBoundaryDataRequest { Address = address };
            deferredBoundaryRequests.Add(address, request);
            boundaryRequestOrder.Enqueue(address);
        }
        if (request.SourceCells.Add(source.WorldCell) &&
            TryGetState(source.WorldCell, out ChunkState state, out _, out _))
            state.DeferredBoundaryCount++;
    }

    private void StartNextDiffusionBoundaryDataRequest()
    {
        if (loadingBoundaryRequest != null || boundaryRequestOrder.Count == 0) return;
        RuntimeWorldAddress address = boundaryRequestOrder.Peek();
        if (!TryRetainDiffusionChunkData(address)) return;
        boundaryRequestOrder.Dequeue();
        DeferredBoundaryDataRequest request = deferredBoundaryRequests[address];
        loadingBoundaryRequest = request;
        request.Cancellation = new CancellationTokenSource();
        try { request.Completion = owner.RequestLiquidFlowChunkAsync(address, request.Cancellation.Token); }
        catch (Exception exception)
        {
            FinishDiffusionBoundaryDataRequest(request, false);
            Debug.LogError($"[LiquidDiffusion] 边界数据请求失败 {address}：{exception}");
        }
    }

    private void CompleteDiffusionBoundaryDataRequest()
    {
        DeferredBoundaryDataRequest request = loadingBoundaryRequest;
        if (request?.Completion == null || !request.Completion.IsCompleted) return;
        bool restored = false;
        try
        {
            ChunkRuntime chunk = request.Completion.GetAwaiter().GetResult();
            // 只恢复地形、液体及地形阻挡差量，不实例化远端建筑或自然物，也不触发表现绑定。
            SaveDataMgr.Instance?.RestoreRuntimeTerrainForChunk(request.Address, chunk);
            Observe(chunk);
            restored = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            Debug.LogError($"[LiquidDiffusion] 边界数据准备失败 {request.Address}：{exception}");
        }
        finally { FinishDiffusionBoundaryDataRequest(request, restored); }
    }

    private void FinishDiffusionBoundaryDataRequest(DeferredBoundaryDataRequest request, bool restored)
    {
        deferredBoundaryRequests.Remove(request.Address);
        loadingBoundaryRequest = null;
        request.Cancellation?.Dispose();
        if (!restored) failedBoundaryRequests.Add(request.Address);
        foreach (Vector2Int cell in request.SourceCells)
        {
            if (TryGetState(cell, out ChunkState state, out _, out _))
                state.DeferredBoundaryCount = Math.Max(0, state.DeferredBoundaryCount - 1);
            if (restored) WakeCross(cell);
        }
    }

    private void ReleaseDormantDiffusionDataChunks()
    {
        releaseDiffusionChunks.Clear();
        foreach (var pair in diffusionDataLeases)
        {
            if (deferredBoundaryRequests.ContainsKey(pair.Key)) continue;
            if (observed.TryGetValue(pair.Key, out ChunkState state) &&
                (state.PendingCellCount > 0 || state.DeferredBoundaryCount > 0)) continue;
            releaseDiffusionChunks.Add(pair.Key);
        }
        foreach (RuntimeWorldAddress address in releaseDiffusionChunks)
        {
            diffusionDataLeases[address].Dispose();
            diffusionDataLeases.Remove(address);
            owner.TryEvictUnleasedDiffusionChunkOutsideRuntimeWindows(address);
        }
    }

    private void ReleaseAllDiffusionBoundaryData()
    {
        if (loadingBoundaryRequest != null)
        {
            loadingBoundaryRequest.Cancellation?.Cancel();
            loadingBoundaryRequest.Cancellation?.Dispose();
            if (loadingBoundaryRequest.Completion != null)
                _ = loadingBoundaryRequest.Completion.ContinueWith(task => { _ = task.Exception; },
                    TaskContinuationOptions.OnlyOnFaulted);
        }
        loadingBoundaryRequest = null;
        deferredBoundaryRequests.Clear();
        boundaryRequestOrder.Clear();
        failedBoundaryRequests.Clear();
        foreach (ChunkState state in observed.Values) state.DeferredBoundaryCount = 0;
        foreach (ChunkLease lease in diffusionDataLeases.Values) lease.Dispose();
        diffusionDataLeases.Clear();
    }
    #endregion
}
