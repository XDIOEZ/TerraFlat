using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FlatWorld.WorldModel;
using Unity.Mathematics;
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

/// <summary>运行时水面流动类型；河流使用水文下游方向，海洋使用世界风场方向。</summary>
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
    private static readonly ConditionalWeakTable<ChunkGenerationProfileSnapshot, TileBlockLookup>
        TileBlockLookups = new();

    /// <summary>Profile 创建后不可变，预解析数字 TileId 映射，避免进格热路径拼接字符串。</summary>
    private sealed class TileBlockLookup
    {
        public readonly Dictionary<int, string> ByTileId = new();

        public TileBlockLookup(ChunkGenerationProfileSnapshot profile)
        {
            if (profile?.TextParameters == null)
                return;

            foreach (KeyValuePair<string, string> pair in profile.TextParameters)
            {
                string key = pair.Key;
                if (string.IsNullOrEmpty(key) ||
                    !key.StartsWith(TileBlockParameterPrefix, StringComparison.Ordinal) ||
                    !int.TryParse(key.Substring(TileBlockParameterPrefix.Length), out int tileId) ||
                    tileId == 0 || string.IsNullOrWhiteSpace(pair.Value))
                    continue;

                ByTileId[tileId] = pair.Value;
            }
        }
    }

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
        return TryCreateTileEffectData(profile, runtimeTileCatalog, terrain, localCell, worldCell,
            null, out tileData, out tileBlock);
    }

    /// <summary>允许调用方复用同类型 TileData，跨格移动时只重置模板内容，不重复分配。</summary>
    public static bool TryCreateTileEffectData(ChunkGenerationProfileSnapshot profile,
        ChunkGenerationProfileSnapshot runtimeTileCatalog,
        ChunkTerrainData terrain, Vector2Int localCell, Vector2Int worldCell,
        TileData reusableTileData,
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
        if (tileId == 0 ||
            !TryResolveTileBlockId(profile, runtimeTileCatalog, tileId, out string tileBlockId))
            return false;

        tileBlock = GameRes.ExistingInstance.GetTileBlock(tileBlockId);
        if (tileBlock?.tileDataTemplate == null)
        {
            tileBlock = null;
            return false;
        }

        tileData = tileBlock.CreateTileData(reusableTileData);
        tileData.position = new Vector3Int(worldCell.x, worldCell.y, 0);
        tileData.IsWalkable = terrain.IsWalkable(localCell.x, localCell.y);
        return true;
    }

    /// <summary>旧地形优先沿用冻结映射；仅在旧快照没有该 ID 时读取当前内容目录。</summary>
    private static bool TryResolveTileBlockId(
        ChunkGenerationProfileSnapshot profile,
        ChunkGenerationProfileSnapshot runtimeTileCatalog,
        int tileId,
        out string tileBlockId)
    {
        tileBlockId = null;
        if (TryResolveFromProfile(profile, tileId, out tileBlockId))
        {
            return true;
        }

        if (TryResolveFromProfile(runtimeTileCatalog, tileId, out tileBlockId))
            return true;

        // MOD 只需在 JSON 声明稳定数字 ID，不要求修改本体或冻结的生成 Profile。
        if (GameRes.ExistingInstance != null &&
            GameRes.ExistingInstance.TryGetTileDefinition(tileId, out var definition))
        {
            tileBlockId = definition.Id;
            return true;
        }
        return false;
    }

    private static bool TryResolveFromProfile(
        ChunkGenerationProfileSnapshot profile,
        int tileId,
        out string tileBlockId)
    {
        tileBlockId = null;
        if (profile == null)
            return false;

        TileBlockLookup lookup = TileBlockLookups.GetValue(profile, value => new TileBlockLookup(value));
        return lookup.ByTileId.TryGetValue(tileId, out tileBlockId) &&
               !string.IsNullOrWhiteSpace(tileBlockId);
    }

    #endregion
}

public partial class ChunkMgr
{
    #region 运行时地块查询

    /// <summary>单次飞行判定共用世界地址和区块缓存，不读取不需要的地块表面数据。</summary>
    internal struct RuntimeTerrainPresenceQuery
    {
        private readonly WorldRuntime world;
        private readonly WorldTopologyDomain topology;
        private readonly string dimensionId;
        private readonly int chunkWidth;
        private readonly int chunkHeight;
        private int cachedOriginX;
        private int cachedOriginY;
        private bool hasCachedOrigin;
        private ChunkRuntime cachedChunk;

        internal RuntimeTerrainPresenceQuery(WorldRuntime world, WorldTopologyDomain topology,
            string dimensionId, int chunkWidth, int chunkHeight)
        {
            this.world = world;
            this.topology = topology;
            this.dimensionId = dimensionId;
            this.chunkWidth = chunkWidth;
            this.chunkHeight = chunkHeight;
            cachedOriginX = 0;
            cachedOriginY = 0;
            hasCachedOrigin = false;
            cachedChunk = null;
        }

        internal Vector2 NormalizePosition(Vector2 position)
        {
            float2 normalized = topology.Normalize(new float2(position.x, position.y));
            return new Vector2(normalized.x, normalized.y);
        }

        internal Vector2 ShortestDelta(Vector2 origin, Vector2 target)
        {
            float2 delta = topology.ShortestDelta(
                new float2(origin.x, origin.y), new float2(target.x, target.y));
            return new Vector2(delta.x, delta.y);
        }

        internal bool IsLoadedNormalized(Vector2 position)
        {
            if (world == null)
                return false;

            int worldX = Mathf.FloorToInt(position.x);
            int worldY = Mathf.FloorToInt(position.y);
            int originX = topology.NormalizeX(Mathf.FloorToInt(position.x / chunkWidth) * chunkWidth);
            int originY = topology.NormalizeY(Mathf.FloorToInt(position.y / chunkHeight) * chunkHeight);
            if (!hasCachedOrigin || cachedOriginX != originX || cachedOriginY != originY)
            {
                cachedOriginX = originX;
                cachedOriginY = originY;
                hasCachedOrigin = true;
                world.TryGetChunk(new RuntimeWorldAddress(dimensionId, new Int2(originX, originY)),
                    out cachedChunk);
            }

            if (cachedChunk == null || cachedChunk.DataStatus != ChunkDataStatus.Ready ||
                cachedChunk.Terrain == null || cachedChunk.Terrain.IsDisposed)
                return false;

            ChunkTerrainData terrain = cachedChunk.Terrain;
            int localX = worldX - originX;
            int localY = worldY - originY;
            return (uint)localX < (uint)terrain.Width && (uint)localY < (uint)terrain.Height;
        }
    }

    internal bool TryCreateTerrainPresenceQuery(out RuntimeTerrainPresenceQuery query)
    {
        query = default;
        if (!IsSurfaceStreamingScene) return false;
        ChunkGenerationProfileSnapshot profile = ActiveGenerationProfile ?? defaultGenerationSnapshot;
        if (runtimeChunkManager == null || profile == null)
            return false;

        // 飞行导航必须和正式世界寻址使用同一份当前世界区块尺寸，否则自定义区块大小会被误判为未加载。
        query = new RuntimeTerrainPresenceQuery(runtimeChunkManager.World,
            WorldTopologyRuntime.GetActiveDomain(), ResolveCurrentDimensionId(),
            Mathf.Max(1, profile.Width),
            Mathf.Max(1, profile.Height));
        return true;
    }

    /// <summary>按世界坐标读取新版权威区块中的顶层地块。</summary>
    public bool TryGetRuntimeTerrainTile(Vector2 worldPosition, out RuntimeTerrainTileSample sample)
    {
        sample = default;
        if (runtimeChunkManager == null || !IsSurfaceStreamingScene)
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
        return TryGetRuntimeTileEffect(worldPosition, null, out sample, out tileData, out tileBlock);
    }

    /// <summary>地块接触接收器可复用上一个同类型 TileData，避免连续移动产生 GC。</summary>
    public bool TryGetRuntimeTileEffect(Vector2 worldPosition, TileData reusableTileData,
        out RuntimeTerrainTileSample sample, out TileData tileData, out RuntimeTileDefinition tileBlock)
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
                   reusableTileData,
                   out tileData,
                   out tileBlock);
    }

    /// <summary>
    /// 读取当前水格的表层流向。河流使用生成阶段保存的真实下游方向；
    /// 海洋没有独立洋流数据时使用地形风场方向，湖泊保持静止。
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
        Vector2 oceanDirection = WaterEnvironmentRules.ResolveOceanCurrentDirection(new Vector2(windX, windY));
        if (oceanDirection == Vector2.zero)
            return false;

        current = new RuntimeWaterCurrentSample(
            RuntimeWaterCurrentKind.Ocean, oceanDirection, 1f);
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
        if (runtimeChunkManager == null || searchRadius < 0f || !IsSurfaceStreamingScene)
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
                ChunkTerrainData.GrassPresent ||
                candidate.LiquidDepth > 0f ||
                !candidate.Terrain.IsWalkable(candidate.LocalCell.x, candidate.LocalCell.y))
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
