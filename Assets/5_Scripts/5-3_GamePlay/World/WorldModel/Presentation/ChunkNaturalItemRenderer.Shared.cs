using System.Collections;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.WorldModel;

public sealed partial class ChunkNaturalItemRenderer
{
    #region 跨区块资源引用
    private readonly Dictionary<int, SharedNaturalEntityRegistry.Lease> sharedEntities = new();
    private readonly List<SharedNaturalEntityRegistry.Lease> sharedCaptureBuffer = new();

    private int PrimarySharedEntityCount
    {
        get
        {
            int count = 0;
            foreach (var lease in sharedEntities.Values)
                if (lease.Registry.IsPrimaryLease(lease)) count++;
            return count;
        }
    }

    private bool BindSharedEntity(NaturalItemPlacement placement, RuntimeItemDefinition definition)
    {
        if (sharedEntities.ContainsKey(placement.Guid)) return true;
        var lease = chunkManager.SharedNaturalEntities.Acquire(placement, definition, terrainOwner);
        if (lease != null) sharedEntities.Add(placement.Guid, lease);
        return true;
    }

    private void RefreshSharedEntities()
    {
        foreach (var lease in sharedEntities.Values) lease.Registry.Refresh(lease);
    }

    private void CaptureSharedEntities()
    {
        foreach (var lease in sharedEntities.Values) lease.Registry.Capture(lease);
    }

    private IEnumerator CaptureSharedEntitiesCoroutine(ChunkRuntime capturedChunk, AutoSaveWorkBudget budget)
    {
        sharedCaptureBuffer.Clear();
        sharedCaptureBuffer.AddRange(sharedEntities.Values);
        for (int i = 0; i < sharedCaptureBuffer.Count; i++)
        {
            if (applicationQuitting || !ReferenceEquals(boundChunk, capturedChunk) || chunkManager == null ||
                chunkManager.IsWorldRuntimeShuttingDown) yield break;
            var lease = sharedCaptureBuffer[i];
            lease.Registry.Capture(lease);
            if (budget.ShouldYield()) yield return budget.WaitForNextSlice();
        }
        sharedCaptureBuffer.Clear();
    }

    private void ReleaseSharedEntities()
    {
        bool capture = !applicationQuitting && chunkManager != null &&
            !chunkManager.IsWorldRuntimeShuttingDown && GameNetwork.HasStateAuthority;
        try
        {
            foreach (var lease in sharedEntities.Values)
            {
                try { lease.Registry.Release(lease, capture); }
                catch (System.Exception exception) { UnityEngine.Debug.LogException(exception); }
            }
        }
        finally { sharedEntities.Clear(); sharedCaptureBuffer.Clear(); }
    }
    #endregion
}
