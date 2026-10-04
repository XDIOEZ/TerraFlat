using System;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 只读区块积雪覆盖层：按绑定地形的基础气温采样季节状态，仅在状态变化时刷新。
/// 保留原始地块和水面身份；每个区块最多绘制 Width×Height 个雪格及墙顶雪冠。
/// </summary>
public sealed class ChunkSnowCoverRenderer : MonoBehaviour, IChunkViewRenderer
{
    #region 配置与缓存

    [SerializeField] private Tile snowTile; // 可透明叠加的地表外观。
    [SerializeField] private Material snowMaterial; // 与地面同域的雪层材质。
    private ChunkTilemapRenderer owner; // 共用区块 BRG Owner。
    private ChunkRuntime chunk; // 当前区块。
    private WeatherMgr weather; // 雨转雪状态的订阅来源。
    private float nextRefresh; // 区块错峰比较积雪状态。
    private float refreshPhase; // 降雪重新开始时保持错峰。
    private int[] visibleCoverage; // 保存真实层数，玩家雪堆可以超过十层。
    private int[] visibleWallCoverage; // 墙体出现和拆除也触发刷新。
    private float[] sampledCoverage; // 上次绘制时的 161 档积雪状态。
    private SnowCoverState sampledSnow; // 区分换世界或星球后的状态实例。
    private float sampledBaselineOffset; // 星球基温变化也会改变雪量。
    private long sampledTerrainRevision; // 地形、水面或支撑面变化时重新绘制。
    private bool hasVisibleSnow; // 完全无雪时跳过逐格采样。
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
        sampledCoverage ??= new float[SnowCoverState.BandCount];
        weather = WeatherMgr.Instance;
        weather.SnowingChanged += HandleSnowingChanged;
        chunk.Terrain.Changed += HandleTerrainChanged;
        chunk.Terrain.LiquidBatchChanged += HandleLiquidBatchChanged;
        weather.TryGetSnowCoverageContext(out SnowCoverState snow, out float baselineOffset);
        Refresh(snow, baselineOffset);
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
        if (weather != null) weather.SnowingChanged -= HandleSnowingChanged;
        if (chunk?.Terrain != null)
        {
            chunk.Terrain.Changed -= HandleTerrainChanged;
            chunk.Terrain.LiquidBatchChanged -= HandleLiquidBatchChanged;
        }
        if (owner != null)
        {
            owner.BatchPresentationRebuilt -= ResubmitVisible;
            if (chunk?.Terrain != null && owner.IsBatchPresentationRegistered)
                for (int y = 0; y < chunk.Terrain.Height; y++)
                for (int x = 0; x < chunk.Terrain.Width; x++)
                {
                    owner.ClearLayerVisual(ChunkBatchRendererGroupService.VisualLayer.Snow, x, y);
                    owner.ClearLayerVisual(ChunkBatchRendererGroupService.VisualLayer.SnowWall, x, y);
                }
        }
        weather = null;
        chunk = null; visibleCoverage = null; visibleWallCoverage = null;
        sampledSnow = null;
        sampledTerrainRevision = -1;
        hasVisibleSnow = false;
        terrainDirty = false;
        enabled = false;
    }

    /// <summary>区块销毁时释放天气订阅。</summary>
    private void OnDestroy() => Unbind();

    #endregion

    #region 增量刷新

    /// <summary>仅降雪中或本区块尚有残雪时低频比较积雪快照。</summary>
    private void Update()
    {
        if (chunk == null || Time.unscaledTime < nextRefresh) return;
        nextRefresh += Mathf.Floor(Time.unscaledTime - nextRefresh) + 1f;
        weather.TryGetSnowCoverageContext(out SnowCoverState snow, out float baselineOffset);
        if (RequiresRefresh(snow, baselineOffset)) Refresh(snow, baselineOffset);
        terrainDirty = false;
        UpdateActivation();
    }

    /// <summary>降雪开始后按区块相位错峰启动，停雪时只保留有残雪的区块刷新。</summary>
    private void HandleSnowingChanged(bool snowing)
    {
        if (chunk == null) return;
        if (snowing && !enabled) ScheduleNextRefresh();
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

    /// <summary>雪天持续更新；无雪区块停用 Update，残雪融净后也停用。</summary>
    private void UpdateActivation() => enabled = chunk != null && (weather.IsSnowingNow || hasVisibleSnow || terrainDirty);

    /// <summary>重启刷新时保留 X/Y 区块错峰，避免天气切换造成同帧尖峰。</summary>
    private void ScheduleNextRefresh() => nextRefresh = Time.unscaledTime + 0.5f + refreshPhase;

    /// <summary>地形版本、星球基温或任一积雪档发生变化才重新采样区块。</summary>
    private bool RequiresRefresh(SnowCoverState snow, float baselineOffset)
    {
        if (chunk.Terrain.Revision != sampledTerrainRevision ||
            !ReferenceEquals(snow, sampledSnow) || baselineOffset != sampledBaselineOffset)
            return true;
        if (snow == null) return false;
        for (int i = 0; i < SnowCoverState.BandCount; i++)
            if (snow.Coverage[i] != sampledCoverage[i]) return true;
        return false;
    }

    /// <summary>按当前有效支撑面遮罩绘制雪，平台移除后下方水面不留悬空白块。</summary>
    private void Refresh(SnowCoverState snow, float baselineOffset)
    {
        bool nowVisible = false;
        for (int y = 0; y < chunk.Terrain.Height; y++)
        for (int x = 0; x < chunk.Terrain.Width; x++)
        {
            TerrainCell surface = TerrainSupportLayer.GetSurfaceCell(chunk.Terrain, x, y);
            float coverage = WorldSnowSystem.GetSurfaceDepth(
                chunk.Terrain, x, y, snow, baselineOffset);
            int value = Mathf.RoundToInt(coverage * SnowDepthLayer.LayerCount);
            if (value > 0) nowVisible = true;
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
        hasVisibleSnow = nowVisible;
        RememberSnapshot(snow, baselineOffset);
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

    /// <summary>积雪覆盖与窄雪冠只更新发生变化的 BRG 槽。</summary>
    private void SubmitSnow(int x, int y, int coverage, bool wall)
    {
        var layer = wall ? ChunkBatchRendererGroupService.VisualLayer.SnowWall :
            ChunkBatchRendererGroupService.VisualLayer.Snow;
        if (coverage == 0)
        {
            owner.ClearLayerVisual(layer, x, y);
            return;
        }
        Int2 origin = chunk.Address.ChunkOrigin;
        Matrix4x4 matrix = Matrix4x4.Translate(new Vector3(origin.X + x + 0.5f,
            origin.Y + y + 0.5f));
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
        owner.SetLayerVisual(layer, x, y, snowTile.sprite, snowMaterial,
            matrix * snowTile.transform, snowTile.color * new Color(1f, 1f, 1f,
                opacity));
    }

    /// <summary>保存本次采样依据，避免无雪或状态不变时重复扫完整区块。</summary>
    private void RememberSnapshot(SnowCoverState snow, float baselineOffset)
    {
        sampledSnow = snow;
        sampledBaselineOffset = baselineOffset;
        sampledTerrainRevision = chunk.Terrain.Revision;
        if (snow != null)
            Array.Copy(snow.Coverage, sampledCoverage, SnowCoverState.BandCount);
    }

    #endregion
}
