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
}

/// <summary>
/// 把纯数据地形的数字 TileId 转换成现有 Tile_Block 行为与临时 TileData。
/// 映射由生成配置的 tile.block.&lt;TileId&gt; 文本参数提供，避免后台地形数据引用 Unity 资源。
/// </summary>
public static class ChunkRuntimeTileEffectResolver
{
    private const string TileBlockParameterPrefix = "tile.block.";

    #region 公共接口

    public static bool TryCreateTileEffectData(ChunkGenerationProfileSnapshot profile,
        ChunkTerrainData terrain, Vector2Int localCell, Vector2Int worldCell,
        out TileData tileData, out Tile_Block tileBlock)
    {
        tileData = null;
        tileBlock = null;
        if (profile == null || terrain == null || terrain.IsDisposed || GameRes.Instance == null)
            return false;
        if ((uint)localCell.x >= (uint)terrain.Width || (uint)localCell.y >= (uint)terrain.Height)
            return false;

        int tileId = terrain.GetTopTileId(localCell.x, localCell.y);
        string parameterId = TileBlockParameterPrefix + tileId;
        if (tileId == 0 || !profile.TextParameters.TryGetValue(parameterId, out string tileBlockId) ||
            string.IsNullOrWhiteSpace(tileBlockId))
            return false;

        tileBlock = GameRes.Instance.GetTileBlock(tileBlockId);
        if (tileBlock?.tileDataTemplate == null)
        {
            tileBlock = null;
            return false;
        }

        tileData = tileBlock.tileDataTemplate.Clone();
        tileData.position = new Vector3Int(worldCell.x, worldCell.y, 0);
        tileData.IsWalkable = terrain.IsWalkable(localCell.x, localCell.y);
        HydrateEnvironmentData(terrain, localCell, tileData);
        return true;
    }

    #endregion

    #region 环境数据

    private static void HydrateEnvironmentData(ChunkTerrainData terrain, Vector2Int localCell,
        TileData tileData)
    {
        if (tileData is not TileData_Water water)
            return;

        if (terrain.TryGetEnvironmentValue("riverDepth", localCell.x, localCell.y,
                out float riverDepth) && riverDepth > 0f)
        {
            water.deepValue = Mathf.Clamp01(riverDepth);
            return;
        }

        if (terrain.TryGetEnvironmentValue("height", localCell.x, localCell.y, out float height))
            water.deepValue = Mathf.Clamp01(TileData_Water.CalculateDepthFromHeight(height));
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

        TerrainCell cell = terrain.GetCell(localCell.x, localCell.y);
        sample = new RuntimeTerrainTileSample(address, terrain, worldCell, localCell, cell,
            terrain.GetTopTileId(localCell.x, localCell.y));
        return true;
    }

    /// <summary>把新版权威地形采样转换成现有地块行为数据。</summary>
    public bool TryGetRuntimeTileEffect(Vector2 worldPosition, out RuntimeTerrainTileSample sample,
        out TileData tileData, out Tile_Block tileBlock)
    {
        tileData = null;
        tileBlock = null;
        return TryGetRuntimeTerrainTile(worldPosition, out sample) &&
               ChunkRuntimeTileEffectResolver.TryCreateTileEffectData(ActiveGenerationProfile,
                   sample.Terrain, sample.LocalCell, sample.WorldCell, out tileData, out tileBlock);
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
               (sample.Cell.Flags & TerrainCellFlags.Water) == 0 &&
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
