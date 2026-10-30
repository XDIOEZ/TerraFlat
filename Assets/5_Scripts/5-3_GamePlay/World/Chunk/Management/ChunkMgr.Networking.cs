using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlatWorld.WorldModel;
using FlatWorld.Networking;
using UnityEngine;

public partial class ChunkMgr
{
    #region 多观察者世界窗口

    /// <summary>客户端以世界代际和区块地址向网络层请求权威差量。</summary>
    public static Func<string, long, FlatWorld.WorldModel.WorldAddress, CancellationToken, Task> PrepareNetworkChunkPersistenceAsync;

    // 返回已访问维度时先撤旧租约与区块，确保新代际重新请求服务器差量。
    public bool InvalidateRetainedClientChunksForNetworkTransfer()
    {
        if (GameNetwork.Session.IsServer || !GameNetwork.Session.IsClient || runtimeChunkManager == null) return true;
        ClearRuntimeWindowBindings();
        runtimeChunkManager.ClearWindow();
        runtimeChunkManager.CancelAllRequests();
        var world = runtimeChunkManager.World;
        world.BeginNewEpoch(world.Epoch + 1);
        runtimeEpoch = world.Epoch;
        runtimeGenerator?.BeginNewWorldHydrologyEpoch(runtimeEpoch);
        var addresses = new List<FlatWorld.WorldModel.WorldAddress>(world.Chunks.Keys);
        bool released = true;
        foreach (var address in addresses)
            if (!runtimeChunkManager.EvictChunk(address)) released = false;
        return released;
    }

    /// <summary>联机观察者共用正式世界模型，本机锚点只决定蒙版和画面调度优先级。</summary>
    public void RefreshChunksAroundObservers(IReadOnlyList<Vector3> observerPositions,
        Vector2 localAnchor, int loadDistance = 2, int inactiveDistance = 3, int destroyDistance = 5)
    {
        if (observerPositions == null || observerPositions.Count == 0)
        {
            ClearRuntimeObserverWindows();
            return;
        }
        if (!IsSurfaceStreamingScene) return;
        loadDistance = Mathf.Max(1, loadDistance);
        inactiveDistance = Mathf.Max(loadDistance, inactiveDistance);
        destroyDistance = Mathf.Max(inactiveDistance, destroyDistance);
        int presentationDistance = Mathf.Min(loadDistance + 1, inactiveDistance);
        RefreshRuntimeWindows(localAnchor,
            new Vector2Int(loadDistance, loadDistance),
            new Vector2Int(destroyDistance, destroyDistance), true,
            new Vector2Int(inactiveDistance, inactiveDistance),
            new Vector2Int(presentationDistance, presentationDistance), observerPositions);
    }

    /// <summary>观察者消失只撤销网络持有的窗口，单人已接回的窗口不受影响。</summary>
    public void ClearRuntimeObserverWindows()
    {
        if (!runtimeWindowUsesObservers) return;
        ClearRuntimeWindowBindings();
        runtimeChunkManager?.ClearWindow();
    }

    /// <summary>仅用冻结参数构造正式流送请求，重复观察者与环绕镜像共享同一窗口。</summary>
    public static List<ChunkWindowRequest> CreateRuntimeObserverWindows(
        IReadOnlyList<Vector3> observerPositions, string dimensionId,
        Vector2Int activeDistance, Vector2Int presentationDistance, Vector2Int destroyDistance,
        int seed, ChunkGenerationProfileSnapshot profile, ChunkGenerationTopologySnapshot topology = default)
    {
        if (observerPositions == null) throw new ArgumentNullException(nameof(observerPositions));
        if (profile == null) throw new ArgumentNullException(nameof(profile));
        var windows = new List<ChunkWindowRequest>(observerPositions.Count);
        var centers = new HashSet<Int2>();
        for (int i = 0; i < observerPositions.Count; i++)
        {
            Vector3 position = observerPositions[i];
            if (float.IsNaN(position.x) || float.IsInfinity(position.x) ||
                float.IsNaN(position.y) || float.IsInfinity(position.y))
                continue;
            var origin = new Int2(
                topology.NormalizeX(Mathf.FloorToInt(position.x / profile.Width) * profile.Width),
                topology.NormalizeY(Mathf.FloorToInt(position.y / profile.Height) * profile.Height));
            if (!centers.Add(origin)) continue;
            windows.Add(new ChunkWindowRequest(new FlatWorld.WorldModel.WorldAddress(dimensionId, origin),
                new Int2(activeDistance.x, activeDistance.y),
                new Int2(destroyDistance.x, destroyDistance.y), true, seed, profile, topology,
                dataDistance: new Int2(presentationDistance.x, presentationDistance.y),
                presentationDistance: new Int2(presentationDistance.x, presentationDistance.y)));
        }
        return windows;
    }

    #endregion
}
