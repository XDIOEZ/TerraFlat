using UnityEngine;
using FlatWorld.WorldModel;

public sealed partial class ChunkTilemapRenderer
{
    #region 深度行网格所有权
    private const string MechanicalConnectorSortingLayerName = "MechanicalShaft";
    private ChunkDepthMeshRenderer depthMesh;
    private ChunkDepthMeshRenderer mechanicalConnectorDepthMesh;
    private ChunkDepthMeshRenderer submergedBuildingDepthMesh;
    private readonly System.Collections.Generic.Dictionary<Material, Material> submergedBuildingOverlayMaterials = new();
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

    public int DepthMeshPartCount => (depthMesh?.PartCount ?? 0) + (mechanicalConnectorDepthMesh?.PartCount ?? 0) + (submergedBuildingDepthMesh?.PartCount ?? 0);
    public int DepthMeshRowCount => (depthMesh?.RowCount ?? 0) + (mechanicalConnectorDepthMesh?.RowCount ?? 0) + (submergedBuildingDepthMesh?.RowCount ?? 0);
    public int DepthMeshDrawGroupCount => (depthMesh?.DrawGroupCount ?? 0) + (mechanicalConnectorDepthMesh?.DrawGroupCount ?? 0) + (submergedBuildingDepthMesh?.DrawGroupCount ?? 0);

    internal void CollectVisualSnapshots(Matrix4x4 worldToDisplay, System.Collections.Generic.List<WorldVisualSnapshot> destination)
    {
        ChunkBatchRendererGroupService.CollectVisualSnapshots(this, destination);
        depthMesh?.CollectVisualSnapshots(worldToDisplay, destination);
        mechanicalConnectorDepthMesh?.CollectVisualSnapshots(worldToDisplay, destination);
        submergedBuildingDepthMesh?.CollectVisualSnapshots(worldToDisplay, destination);
    }

    /// <summary>只用于视觉矩阵；区块逻辑地址、机械格和存档不跟随本机世界镜像移动。</summary>
    internal Vector3 DepthPresentationOffset => boundChunk == null ? Vector3.zero : transform.position -
        new Vector3(boundChunk.Address.ChunkOrigin.X, boundChunk.Address.ChunkOrigin.Y, 0f);

    internal void RemoveBuildingConnectionDepthPart(int entityId, int part)
        => mechanicalConnectorDepthMesh?.Remove(ChunkDepthMeshRenderer.BuildingDomain, entityId, part);

    private void ReleaseDepthPresentation()
    {
        depthMesh?.Dispose();
        mechanicalConnectorDepthMesh?.Dispose();
        submergedBuildingDepthMesh?.Dispose();
        depthMesh = null;
        mechanicalConnectorDepthMesh = null;
        submergedBuildingDepthMesh = null;
        foreach (Material material in submergedBuildingOverlayMaterials.Values)
            if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
        submergedBuildingOverlayMaterials.Clear();
    }

    private void ClearDepthPresentation()
    {
        depthMesh?.Clear();
        mechanicalConnectorDepthMesh?.Clear();
        submergedBuildingDepthMesh?.Clear();
    }
    internal void RemoveMachineDepthPart(int entityId, int part)
    {
        depthMesh?.Remove(ChunkDepthMeshRenderer.MachineDomain, entityId, part);
        mechanicalConnectorDepthMesh?.Remove(ChunkDepthMeshRenderer.MachineDomain, entityId, part);
        submergedBuildingDepthMesh?.Remove(ChunkDepthMeshRenderer.MachineDomain, entityId, part);
    }
    internal void RemoveMachineDepthEntity(int entityId)
    {
        depthMesh?.RemoveEntity(ChunkDepthMeshRenderer.MachineDomain, entityId);
        mechanicalConnectorDepthMesh?.RemoveEntity(ChunkDepthMeshRenderer.MachineDomain, entityId);
        submergedBuildingDepthMesh?.RemoveEntity(ChunkDepthMeshRenderer.MachineDomain, entityId);
    }
    internal void RemoveBuildingDepthPart(int entityId, int part)
    {
        depthMesh?.Remove(ChunkDepthMeshRenderer.BuildingDomain, entityId, part);
        submergedBuildingDepthMesh?.Remove(ChunkDepthMeshRenderer.BuildingDomain, entityId, part);
    }

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

    #region 建筑水面覆盖
    /// <summary>平台保留底层液体，只按实际支撑覆盖决定建筑是否位于水下。</summary>
    public static bool IsBuildingSubmergedByUnsupportedLiquid(ChunkTerrainData terrain, int x, int y) =>
        terrain != null && (uint)x < (uint)terrain.Width && (uint)y < (uint)terrain.Height &&
        terrain.GetLiquidDepth(x, y) > 0f && TerrainSupportLayer.GetTileId(terrain, x, y) == 0;

    internal bool IsBuildingSubmergedAtWorldAnchor(Vector3 worldAnchor)
    {
        Vector3 local = transform.InverseTransformPoint(worldAnchor);
        return IsBuildingSubmergedByUnsupportedLiquid(boundChunk?.Terrain, Mathf.FloorToInt(local.x), Mathf.FloorToInt(local.y));
    }

    /// <summary>直接复用真实水面的半透明覆盖，水下批次与海床使用同一排序层。</summary>
    internal ChunkDepthMeshRenderer GetBuildingDepthRenderer(bool submerged, bool connectionLine = false)
    {
        if (!submerged) return connectionLine ? MechanicalConnectorDepthMesh : DepthMesh;
        if (submergedBuildingDepthMesh == null || submergedBuildingDepthMesh.IsDisposed)
        {
            int layer = SortingLayer.NameToID(WorldRenderingConfigCatalog.Default.sorting.terrainSortingLayer);
            submergedBuildingDepthMesh = new ChunkDepthMeshRenderer(transform, layer, 0, ResolveSubmergedBuildingRenderQueue());
        }
        return submergedBuildingDepthMesh;
    }

    public static int ResolveSubmergedBuildingRenderQueue()
    {
        var sorting = WorldRenderingConfigCatalog.Default.sorting;
        foreach (var profile in sorting.batchProfiles)
            if (profile.visualLayer == "Water") return sorting.batchRenderQueueBase + profile.renderQueueOffset - 1;
        throw new System.InvalidOperationException("世界渲染配置缺少 Water 批次。");
    }

    /// <summary>发光与火焰借用区块共享副本，随主体放到水面之前且不修改源材质。</summary>
    internal Material ResolveBuildingOverlayMaterialForSubmergedSurface(Material source, bool submerged)
    {
        if (!submerged || source == null) return source;
        if (!submergedBuildingOverlayMaterials.TryGetValue(source, out Material material))
        {
            material = new Material(source) { name = source.name + "_SubmergedBuilding",
                hideFlags = HideFlags.HideAndDontSave, renderQueue = ResolveSubmergedBuildingRenderQueue() };
            submergedBuildingOverlayMaterials.Add(source, material);
        }
        return material;
    }
    #endregion
}
