using FlatWorld.WorldModel;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

public partial class ChunkMgr
{
    #region 单轮视线查询上下文

    // 与正式地址入口共用配置，只在本轮检测开始时解析一次维度和区块尺寸。
    internal void BeginLineOfSightQuery(GridLineOfSightQuery query, Vector2 observerPosition)
    {
        WorldTopologyDomain topology = WorldTopologyRuntime.GetActiveDomain();
        RuntimeWorldAddress address = ResolveWorldAddress(observerPosition);
        ChunkGenerationProfileSnapshot profile = ActiveGenerationProfile ?? defaultGenerationSnapshot;
        query.Begin(WorldRuntime, topology, address.DimensionId,
            Mathf.Max(1, profile?.Width ?? 16), Mathf.Max(1, profile?.Height ?? 16), observerPosition);
    }

    #endregion
}
