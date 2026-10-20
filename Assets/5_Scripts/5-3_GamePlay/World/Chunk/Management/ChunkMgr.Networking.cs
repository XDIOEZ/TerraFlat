using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;

public partial class ChunkMgr
{
    #region 多观察者世界窗口

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
