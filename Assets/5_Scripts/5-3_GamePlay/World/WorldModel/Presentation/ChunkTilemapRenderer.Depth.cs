using UnityEngine;
using FlatWorld.WorldModel;

public sealed partial class ChunkTilemapRenderer
{
    #region 深度行网格所有权
    private ChunkDepthMeshRenderer depthMesh;
    private ChunkDepthMeshRenderer fireDepthMesh;
    private ChunkDepthMeshRenderer mechanicalConnectorDepthMesh;
    private ChunkDepthMeshRenderer groundBuildingDepthMesh;
    private ChunkDepthMeshRenderer submergedBuildingDepthMesh;
    private bool entitySortingForProjection;
    private readonly System.Collections.Generic.Dictionary<(Material Source, int Queue), Material> buildingOverlayMaterials = new();
    internal ChunkDepthMeshRenderer DepthMesh
    {
        get
        {
            if (boundChunk?.Terrain == null)
                throw new System.InvalidOperationException("行合批必须先绑定区块数据。");
            if (depthMesh == null || depthMesh.IsDisposed)
            {
                depthMesh = new ChunkDepthMeshRenderer(transform);
                depthMesh.SetEntitySortingEnabled(entitySortingForProjection);
            }
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
            {
                WorldSortingManager.GetResourceSortingKey(WorldSortingManager.MechanicalConnectorCategory, out int layer, out int order);
                mechanicalConnectorDepthMesh = new ChunkDepthMeshRenderer(transform, layer, order);
                mechanicalConnectorDepthMesh.SetEntitySortingEnabled(entitySortingForProjection);
            }
            return mechanicalConnectorDepthMesh;
        }
    }

    /// <summary>地形火场单独共享一张深度网格，统一按 world-effect 排序并继承区块投影锚点。</summary>
    internal ChunkDepthMeshRenderer FireDepthMesh
    {
        get
        {
            if (boundChunk?.Terrain == null)
                throw new System.InvalidOperationException("火场行合批必须先绑定区块数据。");
            if (fireDepthMesh == null || fireDepthMesh.IsDisposed)
            {
                int sortingLayerId;
                int sortingOrder;
                WorldRenderingManager manager = WorldRenderingManager.GetInstance();
                if (manager != null)
                    manager.GetSortingKey(WorldSortingManager.WorldEffectCategory, out sortingLayerId, out sortingOrder);
                else
                    WorldSortingManager.GetResourceSortingKey(WorldSortingManager.WorldEffectCategory, out sortingLayerId, out sortingOrder);
                fireDepthMesh = new ChunkDepthMeshRenderer(transform, sortingLayerId, sortingOrder, useParentSortingAnchor: true);
            }
            return fireDepthMesh;
        }
    }

    public int DepthMeshPartCount => (depthMesh?.PartCount ?? 0) + (fireDepthMesh?.PartCount ?? 0) + (mechanicalConnectorDepthMesh?.PartCount ?? 0) + (groundBuildingDepthMesh?.PartCount ?? 0) + (submergedBuildingDepthMesh?.PartCount ?? 0);
    public int DepthMeshRowCount => (depthMesh?.RowCount ?? 0) + (fireDepthMesh?.RowCount ?? 0) + (mechanicalConnectorDepthMesh?.RowCount ?? 0) + (groundBuildingDepthMesh?.RowCount ?? 0) + (submergedBuildingDepthMesh?.RowCount ?? 0);
    public int DepthMeshDrawGroupCount => (depthMesh?.DrawGroupCount ?? 0) + (fireDepthMesh?.DrawGroupCount ?? 0) + (mechanicalConnectorDepthMesh?.DrawGroupCount ?? 0) + (groundBuildingDepthMesh?.DrawGroupCount ?? 0) + (submergedBuildingDepthMesh?.DrawGroupCount ?? 0);

    internal void CollectVisualSnapshots(Matrix4x4 worldToDisplay, System.Collections.Generic.List<WorldVisualSnapshot> destination)
    {
        ChunkBatchRendererGroupService.CollectVisualSnapshots(this, destination);
        depthMesh?.CollectVisualSnapshots(worldToDisplay, destination);
        fireDepthMesh?.CollectVisualSnapshots(worldToDisplay, destination);
        mechanicalConnectorDepthMesh?.CollectVisualSnapshots(worldToDisplay, destination);
        groundBuildingDepthMesh?.CollectVisualSnapshots(worldToDisplay, destination);
        submergedBuildingDepthMesh?.CollectVisualSnapshots(worldToDisplay, destination);
    }

    /// <summary>只用于视觉矩阵；区块逻辑地址、机械格和存档不跟随本机世界镜像移动。</summary>
    internal Vector3 DepthPresentationOffset => boundChunk == null ? Vector3.zero : transform.position -
        new Vector3(boundChunk.Address.ChunkOrigin.X, boundChunk.Address.ChunkOrigin.Y, 0f);

    internal void SetDepthEntitySortingForProjection(bool enabled)
    {
        if (entitySortingForProjection == enabled) return;
        entitySortingForProjection = enabled;
        foreach (MechanicalDepthVisual visual in mechanicalDepthVisuals.Values) visual?.DetachDepthSortingAttachments();
        try
        {
            depthMesh?.SetEntitySortingEnabled(enabled);
            // useParentSortingAnchor 固定 fire 在一个区块批次中，不拆成逐火排序组。
            fireDepthMesh?.SetEntitySortingEnabled(enabled);
            mechanicalConnectorDepthMesh?.SetEntitySortingEnabled(enabled);
            groundBuildingDepthMesh?.SetEntitySortingEnabled(enabled);
            submergedBuildingDepthMesh?.SetEntitySortingEnabled(enabled);
        }
        finally
        {
            foreach (MechanicalDepthVisual visual in mechanicalDepthVisuals.Values) visual?.RestoreDepthSortingAttachments();
        }
    }

    internal void RemoveBuildingConnectionDepthPart(int entityId, int part)
        => mechanicalConnectorDepthMesh?.Remove(ChunkDepthMeshRenderer.BuildingDomain, entityId, part);

    private void ReleaseDepthPresentation()
    {
        depthMesh?.Dispose();
        fireDepthMesh?.Dispose();
        mechanicalConnectorDepthMesh?.Dispose();
        groundBuildingDepthMesh?.Dispose();
        submergedBuildingDepthMesh?.Dispose();
        depthMesh = null;
        fireDepthMesh = null;
        mechanicalConnectorDepthMesh = null;
        groundBuildingDepthMesh = null;
        submergedBuildingDepthMesh = null;
        entitySortingForProjection = false;
        foreach (Material material in buildingOverlayMaterials.Values)
            if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
        buildingOverlayMaterials.Clear();
    }

    private void ClearDepthPresentation()
    {
        depthMesh?.Clear();
        fireDepthMesh?.Clear();
        mechanicalConnectorDepthMesh?.Clear();
        groundBuildingDepthMesh?.Clear();
        submergedBuildingDepthMesh?.Clear();
    }
    internal void RemoveMachineDepthPart(int entityId, int part)
    {
        depthMesh?.Remove(ChunkDepthMeshRenderer.MachineDomain, entityId, part);
        mechanicalConnectorDepthMesh?.Remove(ChunkDepthMeshRenderer.MachineDomain, entityId, part);
        groundBuildingDepthMesh?.Remove(ChunkDepthMeshRenderer.MachineDomain, entityId, part);
        submergedBuildingDepthMesh?.Remove(ChunkDepthMeshRenderer.MachineDomain, entityId, part);
    }
    internal void RemoveMachineDepthEntity(int entityId)
    {
        depthMesh?.RemoveEntity(ChunkDepthMeshRenderer.MachineDomain, entityId);
        mechanicalConnectorDepthMesh?.RemoveEntity(ChunkDepthMeshRenderer.MachineDomain, entityId);
        groundBuildingDepthMesh?.RemoveEntity(ChunkDepthMeshRenderer.MachineDomain, entityId);
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
    internal ChunkDepthMeshRenderer GetBuildingDepthRenderer(bool submerged, bool connectionLine = false, bool groundSorting = false)
    {
        if (!submerged)
        {
            if (connectionLine) return MechanicalConnectorDepthMesh;
            if (!groundSorting) return DepthMesh;
            if (groundBuildingDepthMesh == null || groundBuildingDepthMesh.IsDisposed)
            {
                WorldSortingManager.GetResourceSortingKey(WorldSortingManager.GroundBuildingCategory, out int layer, out int order);
                groundBuildingDepthMesh = new ChunkDepthMeshRenderer(transform, layer, order,
                    ChunkBatchRendererGroupService.GetLayerRenderQueue(ChunkBatchRendererGroupService.VisualLayer.GroundFacility));
                groundBuildingDepthMesh.SetEntitySortingEnabled(entitySortingForProjection);
            }
            return groundBuildingDepthMesh;
        }
        if (submergedBuildingDepthMesh == null || submergedBuildingDepthMesh.IsDisposed)
        {
            int layer = SortingLayer.NameToID(WorldRenderingConfigCatalog.Default.sorting.terrainSortingLayer);
            submergedBuildingDepthMesh = new ChunkDepthMeshRenderer(transform, layer, 0, ResolveSubmergedBuildingRenderQueue());
            submergedBuildingDepthMesh.SetEntitySortingEnabled(entitySortingForProjection);
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

    /// <summary>发光与火焰复用区块材质副本，保持主体的地面或水下队列。</summary>
    internal Material ResolveBuildingOverlayMaterial(Material source, bool submerged, bool groundSorting = false)
    {
        if ((!submerged && !groundSorting) || source == null) return source;
        int queue = submerged ? ResolveSubmergedBuildingRenderQueue() :
            ChunkBatchRendererGroupService.GetLayerRenderQueue(ChunkBatchRendererGroupService.VisualLayer.GroundFacility);
        var key = (source, queue);
        if (!buildingOverlayMaterials.TryGetValue(key, out Material material))
        {
            material = new Material(source) { name = source.name + "_GroundBuilding",
                hideFlags = HideFlags.HideAndDontSave, renderQueue = queue };
            buildingOverlayMaterials.Add(key, material);
        }
        return material;
    }
    #endregion
}
