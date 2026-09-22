using FlatWorld.WorldModel;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>
/// 新版区块地形中的单格采样结果；用于让玩法系统读取权威地形，而不依赖旧 Map 表现对象。
/// </summary>
public readonly struct RuntimeTerrainTileSample
{
    public RuntimeTerrainTileSample(RuntimeWorldAddress address, ChunkTerrainData terrain,
        Vector2Int worldCell, Vector2Int localCell, TerrainCell cell, int topTileId)
    {
        Address = address;
        Terrain = terrain;
        WorldCell = worldCell;
        LocalCell = localCell;
        Cell = cell;
        TopTileId = topTileId;
    }

    public RuntimeWorldAddress Address { get; }
    public ChunkTerrainData Terrain { get; }
    public Vector2Int WorldCell { get; }
    public Vector2Int LocalCell { get; }
    public TerrainCell Cell { get; }
    public int TopTileId { get; }
    /// <summary>有效表面的液深；平台遮断接触，底部原始液体仍由 Terrain 查询。</summary>
    public float LiquidDepth => Terrain == null || Terrain.IsDisposed ? 0f :
        WorldLiquidSystem.GetSurfaceDepth(Terrain, LocalCell.x, LocalCell.y);
    public string LiquidId => Terrain?.GetLiquidId(LocalCell.x, LocalCell.y);
    public int LiquidTypeIndex => Terrain?.GetLiquidTypeIndex(LocalCell.x, LocalCell.y) ?? 0;
}

/// <summary>运行时水面流动类型；河流使用水文下游方向，海洋使用风场近似表层漂移。</summary>
public enum RuntimeWaterCurrentKind : byte
{
    None = 0,
    River = 1,
    Ocean = 2,
    ExperimentalLiquid = 3
}

/// <summary>世界水格的单位流向和原始流量，供漂浮物等玩法统一读取。</summary>
public readonly struct RuntimeWaterCurrentSample
{
    public RuntimeWaterCurrentSample(RuntimeWaterCurrentKind kind, Vector2 direction, float flow)
    {
        Kind = kind;
        Direction = direction.sqrMagnitude > 0.000001f ? direction.normalized : Vector2.zero;
        Flow = Mathf.Max(0f, flow);
    }

    public RuntimeWaterCurrentKind Kind { get; }
    public Vector2 Direction { get; }
    public float Flow { get; }
}

/// <summary>
/// 把纯数据地形的数字 TileId 转换成共享 RuntimeTileDefinition 与临时 TileData。
/// 映射由生成配置的 tile.block.&lt;TileId&gt; 文本参数提供，避免后台地形数据引用 Unity 资源。
/// </summary>
public static class ChunkRuntimeTileEffectResolver
{
    private const string TileBlockParameterPrefix = "tile.block.";

    #region 公共接口

    public static bool TryCreateTileEffectData(ChunkGenerationProfileSnapshot profile,
        ChunkTerrainData terrain, Vector2Int localCell, Vector2Int worldCell,
        out TileData tileData, out RuntimeTileDefinition tileBlock)
    {
        return TryCreateTileEffectData(profile, null, terrain, localCell, worldCell,
            out tileData, out tileBlock);
    }

    /// <summary>优先读取冻结生成映射，缺失时允许当前内容目录补充后来新增的玩家建筑 Tile。</summary>
    public static bool TryCreateTileEffectData(ChunkGenerationProfileSnapshot profile,
        ChunkGenerationProfileSnapshot runtimeTileCatalog,
        ChunkTerrainData terrain, Vector2Int localCell, Vector2Int worldCell,
        out TileData tileData, out RuntimeTileDefinition tileBlock)
    {
        tileData = null;
        tileBlock = null;
        if (profile == null || terrain == null || terrain.IsDisposed || GameRes.ExistingInstance == null)
            return false;
        if ((uint)localCell.x >= (uint)terrain.Width || (uint)localCell.y >= (uint)terrain.Height)
            return false;

        int tileId = terrain.GetTopTileId(localCell.x, localCell.y);
        TerrainCell effective = TerrainSupportLayer.GetSurfaceCell(terrain, localCell.x, localCell.y);
        int supportId = TerrainSupportLayer.GetTileId(terrain, localCell.x, localCell.y);
        if (supportId != 0 && effective.BlockingTileId == 0 && effective.BackTileId == 0)
            tileId = supportId;
        string parameterId = TileBlockParameterPrefix + tileId;
        if (tileId == 0 ||
            !TryResolveTileBlockId(profile, runtimeTileCatalog, parameterId, out string tileBlockId))
            return false;

        tileBlock = GameRes.ExistingInstance.GetTileBlock(tileBlockId);
        if (tileBlock?.tileDataTemplate == null)
        {
            tileBlock = null;
            return false;
        }

        tileData = tileBlock.CreateTileData();
        tileData.position = new Vector3Int(worldCell.x, worldCell.y, 0);
        tileData.IsWalkable = terrain.IsWalkable(localCell.x, localCell.y);
        return true;
    }

    /// <summary>旧地形优先沿用冻结映射；仅在旧快照没有该 ID 时读取当前内容目录。</summary>
    private static bool TryResolveTileBlockId(
        ChunkGenerationProfileSnapshot profile,
        ChunkGenerationProfileSnapshot runtimeTileCatalog,
        string parameterId,
        out string tileBlockId)
    {
        tileBlockId = null;
        if (profile?.TextParameters != null &&
            profile.TextParameters.TryGetValue(parameterId, out tileBlockId) &&
            !string.IsNullOrWhiteSpace(tileBlockId))
        {
            return true;
        }

        if (runtimeTileCatalog?.TextParameters != null &&
            runtimeTileCatalog.TextParameters.TryGetValue(parameterId, out tileBlockId) &&
            !string.IsNullOrWhiteSpace(tileBlockId)) return true;

        // MOD 只需在 JSON 声明稳定数字 ID，不要求修改本体或冻结的生成 Profile。
        if (int.TryParse(parameterId.Substring(TileBlockParameterPrefix.Length), out int tileId) &&
            GameRes.ExistingInstance != null && GameRes.ExistingInstance.TryGetTileDefinition(tileId, out var definition))
        {
            tileBlockId = definition.Id;
            return true;
        }
        return false;
    }

    #endregion

}

public partial class ChunkMgr
{
    #region 运行时地块查询

    /// <summary>按世界坐标读取新版权威区块中的顶层地块。</summary>
    public bool TryGetRuntimeTerrainTile(Vector2 worldPosition, out RuntimeTerrainTileSample sample)
    {
        sample = default;
        if (runtimeChunkManager == null)
            return false;

        Vector2 normalizedPosition = WorldTopologyRuntime.NormalizePosition(worldPosition);
        RuntimeWorldAddress address = ResolveWorldAddress(normalizedPosition);
        if (!TryGetChunkRuntime(address, out ChunkRuntime chunk) ||
            chunk.DataStatus != ChunkDataStatus.Ready || chunk.Terrain == null ||
            chunk.Terrain.IsDisposed)
            return false;

        var worldCell = new Vector2Int(
            Mathf.FloorToInt(normalizedPosition.x), Mathf.FloorToInt(normalizedPosition.y));
        var localCell = new Vector2Int(
            worldCell.x - address.ChunkOrigin.X, worldCell.y - address.ChunkOrigin.Y);
        ChunkTerrainData terrain = chunk.Terrain;
        if ((uint)localCell.x >= (uint)terrain.Width || (uint)localCell.y >= (uint)terrain.Height)
            return false;

        TerrainCell cell = TerrainSupportLayer.GetSurfaceCell(terrain, localCell.x, localCell.y);
        int supportId = TerrainSupportLayer.GetTileId(terrain, localCell.x, localCell.y);
        sample = new RuntimeTerrainTileSample(address, terrain, worldCell, localCell, cell,
            supportId != 0 && cell.BlockingTileId == 0 && cell.BackTileId == 0
                ? supportId : terrain.GetTopTileId(localCell.x, localCell.y));
        return true;
    }

    /// <summary>把新版权威地形采样转换成现有地块行为数据。</summary>
    public bool TryGetRuntimeTileEffect(Vector2 worldPosition, out RuntimeTerrainTileSample sample,
        out TileData tileData, out RuntimeTileDefinition tileBlock)
    {
        tileData = null;
        tileBlock = null;
        return TryGetRuntimeTerrainTile(worldPosition, out sample) &&
               ChunkRuntimeTileEffectResolver.TryCreateTileEffectData(
                   ActiveGenerationProfile,
                   RuntimeTileCatalogProfile,
                   sample.Terrain,
                   sample.LocalCell,
                   sample.WorldCell,
                   out tileData,
                   out tileBlock);
    }

    /// <summary>
    /// 读取当前水格的表层流向。河流使用生成阶段保存的真实下游方向；
    /// 海洋没有独立洋流数据时使用现有风场近似较弱的表层漂移，湖泊保持静止。
    /// </summary>
    public bool TryGetRuntimeWaterCurrent(Vector2 worldPosition,
        out RuntimeWaterCurrentSample current)
    {
        current = default;
        if (!TryGetRuntimeTerrainTile(worldPosition, out RuntimeTerrainTileSample sample) ||
            sample.LiquidDepth <= 0f)
        {
            return false;
        }

        ChunkTerrainData terrain = sample.Terrain;
        Vector2Int local = sample.LocalCell;
        if (TryGetExperimentalLiquidFlow(worldPosition, out Vector2 liquidFlow))
        {
            current = new RuntimeWaterCurrentSample(RuntimeWaterCurrentKind.ExperimentalLiquid, liquidFlow, liquidFlow.magnitude);
            return true;
        }
        terrain.TryGetEnvironmentValue("riverKind", local.x, local.y, out float riverKind);
        RuntimeWaterCurrentKind kind = WaterEnvironmentRules.ResolveCurrentKind(
            Mathf.RoundToInt(riverKind), (SurfaceBiomeKind)sample.Cell.BiomeId == SurfaceBiomeKind.Ocean);
        if (kind == RuntimeWaterCurrentKind.River)
        {
            terrain.TryGetEnvironmentValue("riverFlowX", local.x, local.y, out float flowX);
            terrain.TryGetEnvironmentValue("riverFlowY", local.x, local.y, out float flowY);
            terrain.TryGetEnvironmentValue("riverFlow", local.x, local.y, out float flow);
            Vector2 direction = new(flowX, flowY);
            if (direction.sqrMagnitude <= 0.000001f)
                return false;

            current = new RuntimeWaterCurrentSample(
                RuntimeWaterCurrentKind.River, direction, flow);
            return true;
        }

        if (kind != RuntimeWaterCurrentKind.Ocean)
            return false;

        terrain.TryGetEnvironmentValue("windX", local.x, local.y, out float windX);
        terrain.TryGetEnvironmentValue("windY", local.x, local.y, out float windY);
        Vector2 windDirection = new(windX, windY);
        if (windDirection.sqrMagnitude <= 0.000001f)
            return false;

        current = new RuntimeWaterCurrentSample(
            RuntimeWaterCurrentKind.Ocean, windDirection, 1f);
        return true;
    }

    /// <summary>读取权威区块的稳定群系名称，替代旧 Land 生成器的 BiomeData 缓存查询。</summary>
    public bool TryGetRuntimeBiomeName(Vector2 worldPosition, out string biomeName)
    {
        biomeName = string.Empty;
        if (!TryGetRuntimeTerrainTile(worldPosition, out RuntimeTerrainTileSample sample))
            return false;

        biomeName = SurfaceBiomeClassifier.GetLegacyName(sample.Cell.BiomeId);
        return !string.IsNullOrWhiteSpace(biomeName);
    }

    /// <summary>判断已提交权威区块中的格子是否为非水且可行走的陆地。</summary>
    public bool IsRuntimeWalkableLand(Vector2 worldPosition)
    {
        return TryGetRuntimeTerrainTile(worldPosition, out RuntimeTerrainTileSample sample) &&
               sample.LiquidDepth <= 0f &&
               sample.Terrain.IsWalkable(sample.LocalCell.x, sample.LocalCell.y);
    }

    /// <summary>在已加载的新版权威草层中寻找最近的一格草。</summary>
    public bool TryFindRuntimeGrassNear(Vector2 worldPosition, float searchRadius,
        out RuntimeTerrainTileSample sample)
    {
        sample = default;
        if (runtimeChunkManager == null || searchRadius < 0f)
            return false;

        Vector2 normalizedPosition = WorldTopologyRuntime.NormalizePosition(worldPosition);
        int minX = Mathf.FloorToInt(normalizedPosition.x - searchRadius);
        int maxX = Mathf.FloorToInt(normalizedPosition.x + searchRadius);
        int minY = Mathf.FloorToInt(normalizedPosition.y - searchRadius);
        int maxY = Mathf.FloorToInt(normalizedPosition.y + searchRadius);
        float radiusSqr = searchRadius * searchRadius;
        float closestDistanceSqr = float.MaxValue;
        bool found = false;

        for (int x = minX; x <= maxX; x++)
        for (int y = minY; y <= maxY; y++)
        {
            Vector2 candidateCenter = new(x + 0.5f, y + 0.5f);
            float distanceSqr = WorldTopologyRuntime.SqrDistance(normalizedPosition, candidateCenter);
            if (distanceSqr > radiusSqr || distanceSqr >= closestDistanceSqr)
                continue;

            if (!TryGetRuntimeTerrainTile(candidateCenter, out RuntimeTerrainTileSample candidate) ||
                candidate.Terrain.GetGrass(candidate.LocalCell.x, candidate.LocalCell.y) !=
                ChunkTerrainData.GrassPresent)
                continue;

            sample = candidate;
            closestDistanceSqr = distanceSqr;
            found = true;
        }

        return found;
    }

    /// <summary>从新版权威草层原子消费一格草，数据变化会驱动草 Tilemap 清除。</summary>
    public bool TryConsumeRuntimeGrass(Vector2Int worldCell)
    {
        if (!TryGetRuntimeTerrainTile(new Vector2(worldCell.x + 0.5f, worldCell.y + 0.5f),
                out RuntimeTerrainTileSample sample))
            return false;

        return sample.Terrain.TryConsumeGrass(sample.LocalCell.x, sample.LocalCell.y);
    }

    /// <summary>在已加载权威区块内按确定性方环顺序寻找最近可行走陆地。</summary>
    public bool TryFindRuntimeWalkableLandNear(Vector2Int anchor, int maxRadius,
        int sampleBudget, out Vector2Int worldCell)
    {
        worldCell = anchor;
        maxRadius = Mathf.Max(0, maxRadius);
        sampleBudget = Mathf.Max(1, sampleBudget);
        int sampled = 0;
        for (int radius = 0; radius <= maxRadius && sampled < sampleBudget; radius++)
        {
            int min = -radius;
            int max = radius;
            for (int offsetY = min; offsetY <= max && sampled < sampleBudget; offsetY++)
            for (int offsetX = min; offsetX <= max && sampled < sampleBudget; offsetX++)
            {
                if (radius > 0 && Mathf.Abs(offsetX) != radius && Mathf.Abs(offsetY) != radius)
                    continue;

                sampled++;
                var candidate = new Vector2Int(anchor.x + offsetX, anchor.y + offsetY);
                if (!IsRuntimeWalkableLand(candidate + new Vector2(0.5f, 0.5f)))
                    continue;

                worldCell = candidate;
                return true;
            }
        }

        return false;
    }

    #endregion
}
