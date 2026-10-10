using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 只读区块积雪覆盖层：由当地权威地形变化唤醒，镜头天气不决定其他区块积雪。
/// 保留原始地块和水面身份；每个区块最多绘制 Width×Height 个雪格及墙顶雪冠。
/// </summary>
public sealed class ChunkSnowCoverRenderer : MonoBehaviour, IChunkViewRenderer
{
    #region 配置与缓存

    [SerializeField] private Tile snowTile; // 可透明叠加的地表外观。
    [SerializeField] private Material snowMaterial; // 与地面同域的雪层材质。
    private ChunkTilemapRenderer owner; // 跟随区块地形资源重建。
    private ChunkDepthMeshRenderer groundSnowMesh; // 地面雪保留明确的 Unity 排序层。
    private ChunkDepthMeshRenderer wallSnowMesh; // 墙顶雪单独排在墙体上方。
    private ChunkRuntime chunk; // 当前区块。
    private float nextRefresh; // 区块错峰比较积雪状态。
    private float refreshPhase; // 降雪重新开始时保持错峰。
    private int[] visibleCoverage; // 保存真实层数，玩家雪堆可以超过十层。
    private int[] visibleWallCoverage; // 墙体出现和拆除也触发刷新。
    private long sampledTerrainRevision; // 地形、水面或支撑面变化时重新绘制。
    private bool terrainDirty; // 非雪天的地形改动也要更新现有覆盖。

    #endregion

    #region 绑定与解绑

    /// <summary>绑定后立即恢复已保存的覆盖状态。</summary>
    public void Bind(ChunkRuntime value)
    {
        Unbind(); chunk = value;
        owner = GetComponent<ChunkTilemapRenderer>();
        owner.BatchPresentationRebuilt += ResubmitVisible;
        visibleCoverage = new int[value.Terrain.Width * value.Terrain.Height];
        visibleWallCoverage = new int[visibleCoverage.Length];
        chunk.Terrain.Changed += HandleTerrainChanged;
        chunk.Terrain.LiquidBatchChanged += HandleLiquidBatchChanged;
        Refresh();
        int chunkX = value.Address.ChunkOrigin.X / value.Terrain.Width;
        int chunkY = value.Address.ChunkOrigin.Y / value.Terrain.Height;
        uint phase = unchecked((uint)chunkX * 73856093u ^ (uint)chunkY * 19349663u);
        refreshPhase = (phase % 1000u) * 0.001f;
        ScheduleNextRefresh();
        UpdateActivation();
    }

    /// <summary>回池时清空纯表现，世界中的积雪状态继续保留。</summary>
    public void Unbind()
    {
        if (chunk?.Terrain != null)
        {
            chunk.Terrain.Changed -= HandleTerrainChanged;
            chunk.Terrain.LiquidBatchChanged -= HandleLiquidBatchChanged;
        }
        if (owner != null)
        {
            owner.BatchPresentationRebuilt -= ResubmitVisible;
        }
        groundSnowMesh?.Dispose(); groundSnowMesh = null;
        wallSnowMesh?.Dispose(); wallSnowMesh = null;
        owner = null;
        chunk = null; visibleCoverage = null; visibleWallCoverage = null;
        sampledTerrainRevision = -1;
        terrainDirty = false;
        enabled = false;
    }

    /// <summary>区块销毁时释放地形订阅。</summary>
    private void OnDestroy() => Unbind();

    #endregion

    #region 增量刷新

    /// <summary>当地地形有变化时错峰刷新一次，没有变化就停用 Update。</summary>
    private void Update()
    {
        if (chunk == null || Time.unscaledTime < nextRefresh) return;
        nextRefresh += Mathf.Floor(Time.unscaledTime - nextRefresh) + 1f;
        if (chunk.Terrain.Revision != sampledTerrainRevision) Refresh();
        terrainDirty = false;
        UpdateActivation();
    }

    /// <summary>地块、支撑面和单格液体变化时唤醒覆盖层；草变化不影响雪。</summary>
    private void HandleTerrainChanged(ChunkTerrainChanged changed)
    {
        if (changed.Kind is TerrainChangeKind.Cell or TerrainChangeKind.TileStack or
            TerrainChangeKind.Environment or TerrainChangeKind.Liquid)
            QueueTerrainRefresh();
    }

    /// <summary>批量液体变化仅唤醒一次，刷新时读取完整权威地形。</summary>
    private void HandleLiquidBatchChanged(ChunkLiquidBatchChanged changed) => QueueTerrainRefresh();

    /// <summary>非雪天的地形事件按区块相位合并刷新。</summary>
    private void QueueTerrainRefresh()
    {
        terrainDirty = true;
        if (!enabled) ScheduleNextRefresh();
        UpdateActivation();
    }

    /// <summary>降雪与融雪都先修改当地层值，纯表现只在变化事件后工作。</summary>
    private void UpdateActivation() => enabled = chunk != null && terrainDirty;

    /// <summary>重启刷新时保留 X/Y 区块错峰，避免天气切换造成同帧尖峰。</summary>
    private void ScheduleNextRefresh() => nextRefresh = Time.unscaledTime + 0.5f + refreshPhase;

    /// <summary>按当前有效支撑面遮罩绘制雪，平台移除后下方水面不留悬空白块。</summary>
    private void Refresh()
    {
        for (int y = 0; y < chunk.Terrain.Height; y++)
        for (int x = 0; x < chunk.Terrain.Width; x++)
        {
            TerrainCell surface = TerrainSupportLayer.GetSurfaceCell(chunk.Terrain, x, y);
            float coverage = WorldSnowSystem.GetSurfaceDepth(
                chunk.Terrain, x, y, null, 0f);
            int value = Mathf.RoundToInt(coverage * SnowDepthLayer.LayerCount);
            int index = y * chunk.Terrain.Width + x;
            int wallValue = surface.BlockingTileId != 0 ? value : 0;
            if (visibleWallCoverage[index] != wallValue)
            {
                visibleWallCoverage[index] = wallValue;
                SubmitSnow(x, y, wallValue, true);
            }
            if (visibleCoverage[index] == value) continue;
            visibleCoverage[index] = value;
            SubmitSnow(x, y, value, false);
        }
        sampledTerrainRevision = chunk.Terrain.Revision;
    }

    /// <summary>Owner 全量重建后按已采样覆盖补齐积雪实例。</summary>
    private void ResubmitVisible()
    {
        if (chunk?.Terrain == null || visibleCoverage == null) return;
        for (int y = 0; y < chunk.Terrain.Height; y++)
        for (int x = 0; x < chunk.Terrain.Width; x++)
        {
            int index = y * chunk.Terrain.Width + x;
            if (visibleCoverage[index] > 0) SubmitSnow(x, y, visibleCoverage[index], false);
            if (visibleWallCoverage[index] > 0) SubmitSnow(x, y, visibleWallCoverage[index], true);
        }
    }

    /// <summary>积雪覆盖与窄雪冠按行合批，排序层明确低于玩家等动态实体。</summary>
    private void SubmitSnow(int x, int y, int coverage, bool wall)
    {
        int slot = y * chunk.Terrain.Width + x;
        ChunkDepthMeshRenderer mesh = wall ? wallSnowMesh : groundSnowMesh;
        if (coverage == 0)
        {
            if (mesh != null && !mesh.IsDisposed) mesh.Remove(0, slot, 0);
            return;
        }
        if (mesh == null || mesh.IsDisposed)
        {
            WorldSortingManager.GetResourceSortingKey(wall
                    ? WorldSortingManager.GroundMarkCategory : WorldSortingManager.GroundBuildingCategory,
                out int sortingLayerId, out int sortingOrder);
            // 地面雪高于地面，墙冠高于 Blocking，两个排序域都低于 Player。
            mesh = new ChunkDepthMeshRenderer(transform, sortingLayerId, checked(sortingOrder + (wall ? 2 : 1)));
            if (wall) wallSnowMesh = mesh;
            else groundSnowMesh = mesh;
        }
        Vector3 anchor = new Vector3(x + 0.5f, y + 0.5f);
        Matrix4x4 matrix = Matrix4x4.Translate(anchor);
        if (wall)
            matrix *= Matrix4x4.TRS(new Vector3(0f, 0.38f), Quaternion.identity,
                new Vector3(1f, 0.24f, 1f));
        else if (coverage > SnowDepthLayer.LayerCount)
        {
            // 厚雪堆只向画面上方增高表现，格子坐标与底层地形保持不变。
            float height = Mathf.Min(0.5f, (coverage - SnowDepthLayer.LayerCount) * 0.025f);
            matrix *= Matrix4x4.TRS(new Vector3(0f, height * 0.5f), Quaternion.identity,
                new Vector3(1f, 1f + height, 1f));
        }
        // 一层雪从 55% 不透明度开始，每层增加 5%，十层及以上完全不透明。
        float opacity = Mathf.Clamp01(0.5f + coverage * 0.05f);
        mesh.Set(0, slot, 0, snowTile.sprite, snowMaterial,
            transform.localToWorldMatrix * matrix * snowTile.transform,
            transform.TransformPoint(anchor), 0, snowTile.color * new Color(1f, 1f, 1f, opacity));
    }

    #endregion
}
