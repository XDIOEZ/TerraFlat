using UnityEngine;

public sealed partial class ChunkTilemapRenderer
{
    #region 深度行网格所有权
    private const string MechanicalConnectorSortingLayerName = "MechanicalShaft";
    private ChunkDepthMeshRenderer depthMesh;
    private ChunkDepthMeshRenderer mechanicalConnectorDepthMesh;
    internal ChunkDepthMeshRenderer DepthMesh
    {
        get
        {
            if (boundChunk?.Terrain == null)
                throw new System.InvalidOperationException("行合批必须先绑定区块数据。");
            if (depthMesh == null || depthMesh.IsDisposed) depthMesh = new ChunkDepthMeshRenderer(transform);
            return depthMesh;
        }
    }

    /// <summary>传动杆连接件固定放在机械主体下层，不再参与主体之间的 Y 轴前后遮挡。</summary>
    internal ChunkDepthMeshRenderer MechanicalConnectorDepthMesh
    {
        get
        {
            if (boundChunk?.Terrain == null)
                throw new System.InvalidOperationException("传动杆合批必须先绑定区块数据。");
            if (mechanicalConnectorDepthMesh == null || mechanicalConnectorDepthMesh.IsDisposed)
                mechanicalConnectorDepthMesh = new ChunkDepthMeshRenderer(transform,
                    ResolveMechanicalConnectorSortingLayerId(), 0);
            return mechanicalConnectorDepthMesh;
        }
    }

    public int DepthMeshPartCount => (depthMesh?.PartCount ?? 0) + (mechanicalConnectorDepthMesh?.PartCount ?? 0);
    public int DepthMeshRowCount => (depthMesh?.RowCount ?? 0) + (mechanicalConnectorDepthMesh?.RowCount ?? 0);
    public int DepthMeshDrawGroupCount => (depthMesh?.DrawGroupCount ?? 0) + (mechanicalConnectorDepthMesh?.DrawGroupCount ?? 0);

    /// <summary>只用于视觉矩阵；区块逻辑地址、机械格和存档不跟随本机世界镜像移动。</summary>
    internal Vector3 DepthPresentationOffset => boundChunk == null ? Vector3.zero : transform.position -
        new Vector3(boundChunk.Address.ChunkOrigin.X, boundChunk.Address.ChunkOrigin.Y, 0f);

    private void ReleaseDepthPresentation()
    {
        depthMesh?.Dispose();
        mechanicalConnectorDepthMesh?.Dispose();
        depthMesh = null;
        mechanicalConnectorDepthMesh = null;
    }

    private void ClearDepthPresentation()
    {
        depthMesh?.Clear();
        mechanicalConnectorDepthMesh?.Clear();
    }
    internal void RemoveMachineDepthPart(int entityId, int part)
    {
        depthMesh?.Remove(ChunkDepthMeshRenderer.MachineDomain, entityId, part);
        mechanicalConnectorDepthMesh?.Remove(ChunkDepthMeshRenderer.MachineDomain, entityId, part);
    }
    internal void RemoveMachineDepthEntity(int entityId)
    {
        depthMesh?.RemoveEntity(ChunkDepthMeshRenderer.MachineDomain, entityId);
        mechanicalConnectorDepthMesh?.RemoveEntity(ChunkDepthMeshRenderer.MachineDomain, entityId);
    }
    internal void RemoveBuildingDepthPart(int entityId, int part)
        => depthMesh?.Remove(ChunkDepthMeshRenderer.BuildingDomain, entityId, part);

    /// <summary>区块重选镜像后同步绝对坐标阴影和交互桥，行网格仍由同一 View 持有。</summary>
    internal void RefreshDepthProjection()
    {
        if (boundChunk == null) return;
        ChunkBatchRendererGroupService.RefreshOwnerProjection(this);
        if (batchPresentationComplete) RefreshMechanicalPresentation();
    }

    private static int ResolveMechanicalConnectorSortingLayerId()
    {
        foreach (SortingLayer layer in SortingLayer.layers)
            if (layer.name == MechanicalConnectorSortingLayerName) return layer.id;
        throw new System.InvalidOperationException("项目缺少 Sorting Layer：" + MechanicalConnectorSortingLayerName);
    }
    #endregion
}
