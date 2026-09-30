using UnityEngine;

public sealed partial class ChunkTilemapRenderer
{
    #region 深度行网格所有权
    private ChunkDepthMeshRenderer depthMesh;
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

    public int DepthMeshPartCount => depthMesh?.PartCount ?? 0;
    public int DepthMeshRowCount => depthMesh?.RowCount ?? 0;
    public int DepthMeshDrawGroupCount => depthMesh?.DrawGroupCount ?? 0;

    /// <summary>只用于视觉矩阵；区块逻辑地址、机械格和存档不跟随本机世界镜像移动。</summary>
    internal Vector3 DepthPresentationOffset => boundChunk == null ? Vector3.zero : transform.position -
        new Vector3(boundChunk.Address.ChunkOrigin.X, boundChunk.Address.ChunkOrigin.Y, 0f);

    private void ReleaseDepthPresentation()
    {
        depthMesh?.Dispose();
        depthMesh = null;
    }

    private void ClearDepthPresentation() => depthMesh?.Clear();
    internal void RemoveMachineDepthPart(int entityId, int part)
        => depthMesh?.Remove(ChunkDepthMeshRenderer.MachineDomain, entityId, part);
    internal void RemoveMachineDepthEntity(int entityId)
        => depthMesh?.RemoveEntity(ChunkDepthMeshRenderer.MachineDomain, entityId);
    internal void RemoveBuildingDepthPart(int entityId, int part)
        => depthMesh?.Remove(ChunkDepthMeshRenderer.BuildingDomain, entityId, part);

    /// <summary>区块重选镜像后同步绝对坐标阴影和交互桥，行网格仍由同一 View 持有。</summary>
    internal void RefreshDepthProjection()
    {
        if (boundChunk != null && batchPresentationComplete) RefreshMechanicalPresentation();
    }
    #endregion
}
