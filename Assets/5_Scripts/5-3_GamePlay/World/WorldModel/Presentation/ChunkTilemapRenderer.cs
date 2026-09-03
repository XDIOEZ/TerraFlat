using System;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 新版区块的基础 Tilemap 表现层。
///
/// 地表陆地、地表水岸、矿洞墙脚和水体分别使用对应的 Tilemap 表现层。
/// 左、右、下、上四个接触方向与水深共同编码到 Tile Color RGBA，由 Tilemap Shader 绘制渐变，
/// 不再为接触阴影创建 SpriteRenderer 游戏对象。Tile Color 只作为表现数据。
/// </summary>
public sealed class ChunkTilemapRenderer : MonoBehaviour, IChunkViewRenderer
{
    #region 配置与状态

    /// <summary>水深占据颜色通道低值区间，给岸线方向位保留稳定阈值。</summary>
    private const float WaterDepthChannelScale = 0.45f;
    /// <summary>岸线方向位偏移，与 Shader 端的解码常量保持一致。</summary>
    private const float WaterContactChannelOffset = 0.55f;
    /// <summary>河流与地下水使用的权威水深层。</summary>
    private const string RiverDepthLayerId = "riverDepth";
    /// <summary>海洋深度换算使用的权威高度层。</summary>
    private const string HeightLayerId = "height";

    [SerializeField] private ChunkTilePaletteSO palette;
    [SerializeField] private Tilemap groundTilemap;
    [SerializeField] private Tilemap waterTilemap;
    [SerializeField] private Tilemap caveWaterTilemap;
    [SerializeField] private Tilemap backTilemap;
    [SerializeField] private Tilemap blockingTilemap;

    private WorldRuntime boundWorld;
    private ChunkRuntime boundChunk;
    private IDisposable chunkCommittedSubscription;
    private bool renderCaveWater;

    #endregion

    #region 绑定与生命周期

    /// <summary>注入当前世界，用于计算跨 Chunk 的地下水岸线方向。</summary>
    public void SetWorld(WorldRuntime worldRuntime)
    {
        if (ReferenceEquals(boundWorld, worldRuntime))
            return;

        chunkCommittedSubscription?.Dispose();
        chunkCommittedSubscription = null;
        boundWorld = worldRuntime;
        if (boundWorld != null)
            chunkCommittedSubscription =
                boundWorld.Events.Subscribe<ChunkCommitted>(HandleChunkCommitted);
    }

    public void Bind(ChunkRuntime chunk)
    {
        if (chunk == null)
            throw new System.ArgumentNullException(nameof(chunk));
        if (chunk.Terrain == null)
            throw new System.InvalidOperationException("Cannot bind terrain rendering before data is ready.");
        if (ReferenceEquals(boundChunk, chunk))
            return;

        Unbind();
        boundChunk = chunk;
        renderCaveWater = IsCaveDimension(chunk.Address.DimensionId);
        if (waterTilemap != null)
            waterTilemap.gameObject.SetActive(!renderCaveWater);
        if (caveWaterTilemap != null)
            caveWaterTilemap.gameObject.SetActive(renderCaveWater);
        boundChunk.Terrain.Changed += HandleTerrainChanged;
        Render(chunk.Terrain);
    }

    public void Unbind()
    {
        if (boundChunk?.Terrain != null)
            boundChunk.Terrain.Changed -= HandleTerrainChanged;
        if (groundTilemap != null)
            groundTilemap.ClearAllTiles();
        if (waterTilemap != null)
        {
            waterTilemap.ClearAllTiles();
            waterTilemap.gameObject.SetActive(false);
        }
        if (caveWaterTilemap != null)
        {
            caveWaterTilemap.ClearAllTiles();
            caveWaterTilemap.gameObject.SetActive(false);
        }
        if (backTilemap != null)
            backTilemap.ClearAllTiles();
        if (blockingTilemap != null)
            blockingTilemap.ClearAllTiles();
        renderCaveWater = false;
        boundChunk = null;
    }

    private void HandleTerrainChanged(ChunkTerrainChanged changed)
    {
        if (boundChunk?.Terrain == null)
            return;
        if (changed.Kind != TerrainChangeKind.Cell &&
            changed.Kind != TerrainChangeKind.TileStack)
            return;

        // 接触方向会连带影响四个邻格，地形变化时刷新本 Chunk 的轻量 Tilemap 数据。
        Render(boundChunk.Terrain);
    }

    /// <summary>相邻区块生成后刷新边界格的岸向数据。</summary>
    private void HandleChunkCommitted(ChunkCommitted committed)
    {
        if (boundChunk?.Terrain == null ||
            !string.Equals(committed.Address.DimensionId, boundChunk.Address.DimensionId,
                StringComparison.Ordinal))
        {
            return;
        }

        int width = boundChunk.Terrain.Width;
        int height = boundChunk.Terrain.Height;
        Int2 origin = boundChunk.Address.ChunkOrigin;
        Int2 changed = committed.Address.ChunkOrigin;
        bool isNeighbour =
            (changed.X == origin.X - width && changed.Y == origin.Y) ||
            (changed.X == origin.X + width && changed.Y == origin.Y) ||
            (changed.X == origin.X && changed.Y == origin.Y - height) ||
            (changed.X == origin.X && changed.Y == origin.Y + height);
        if (isNeighbour)
            Render(boundChunk.Terrain);
    }

    #endregion

    #region Tilemap 绘制

    private void Render(ChunkTerrainData terrain)
    {
        if (terrain == null)
            throw new System.InvalidOperationException("Cannot bind a ChunkView before data is ready.");
        if (palette == null)
            throw new System.InvalidOperationException("ChunkTilePaletteSO is not assigned.");

        int count = terrain.CellCount;
        var ground = groundTilemap != null ? new TileBase[count] : null;
        var surfaceWater = !renderCaveWater && waterTilemap != null
            ? new TileBase[count]
            : null;
        var caveWater = renderCaveWater && caveWaterTilemap != null
            ? new TileBase[count]
            : null;
        var back = backTilemap != null ? new TileBase[count] : null;
        var blocking = blockingTilemap != null ? new TileBase[count] : null;
        for (int y = 0; y < terrain.Height; y++)
        {
            for (int x = 0; x < terrain.Width; x++)
            {
                int index = y * terrain.Width + x;
                TerrainCell cell = terrain.GetCell(x, y);
                bool isWaterCell = IsWater(cell);
                if (isWaterCell && cell.GroundTileId != 0)
                {
                    if (caveWater != null)
                        palette.TryGetTile(cell.GroundTileId, out caveWater[index]);
                    else if (surfaceWater != null)
                        palette.TryGetTile(cell.GroundTileId, out surfaceWater[index]);
                    else if (ground != null)
                        palette.TryGetTile(cell.GroundTileId, out ground[index]);
                }
                else if (ground != null && cell.GroundTileId != 0)
                    palette.TryGetTile(cell.GroundTileId, out ground[index]);
                if (back != null && cell.BackTileId != 0)
                    palette.TryGetTile(cell.BackTileId, out back[index]);
                if (blocking != null && cell.BlockingTileId != 0)
                    palette.TryGetTile(cell.BlockingTileId, out blocking[index]);
            }
        }

        var bounds = new BoundsInt(0, 0, 0, terrain.Width, terrain.Height, 1);
        if (ground != null)
        {
            groundTilemap.SetTilesBlock(bounds, ground);
            ApplyGroundContactMasks(terrain);
        }
        if (surfaceWater != null)
        {
            waterTilemap.SetTilesBlock(bounds, surfaceWater);
            ApplyWaterShaderData(waterTilemap, terrain);
        }
        if (caveWater != null)
        {
            caveWaterTilemap.SetTilesBlock(bounds, caveWater);
            ApplyWaterShaderData(caveWaterTilemap, terrain);
        }
        if (back != null)
            backTilemap.SetTilesBlock(bounds, back);
        if (blocking != null)
            blockingTilemap.SetTilesBlock(bounds, blocking);
    }

    #endregion

    #region Tilemap Shader 数据

    /// <summary>矿洞地面编码墙脚方向；地表水格和陆地格分别编码水岸、石地边缘方向。</summary>
    private void ApplyGroundContactMasks(ChunkTerrainData terrain)
    {
        bool cave = IsCaveDimension(boundChunk?.Address.DimensionId);
        for (int y = 0; y < terrain.Height; y++)
        {
            for (int x = 0; x < terrain.Width; x++)
            {
                TerrainCell cell = terrain.GetCell(x, y);
                if (cell.GroundTileId == 0)
                    continue;

                // 有独立水面层时，水格由对应 Water Tilemap 负责，Ground 不再写入其颜色遮罩。
                bool dedicatedWater = IsWater(cell) &&
                    ((cave && caveWaterTilemap != null) ||
                     (!cave && waterTilemap != null));
                if (dedicatedWater)
                {
                    continue;
                }

                bool receivesShadow;
                ContactKind contactKind;
                if (cave)
                {
                    receivesShadow = !IsBlocking(cell) && !IsWater(cell);
                    contactKind = ContactKind.Wall;
                }
                else if (IsWater(cell))
                {
                    // 水岸阴影继续画在水格内侧，保持现有水面边缘效果。
                    receivesShadow = true;
                    contactKind = ContactKind.Land;
                }
                else
                {
                    // 石地与草地共用 Ground Tilemap；把阴影写到石地外侧的陆地格上。
                    receivesShadow = !IsStone(cell);
                    contactKind = ContactKind.Stone;
                }

                Vector3Int position = new(x, y, 0);
                groundTilemap.SetTileFlags(position, TileFlags.None);
                groundTilemap.SetColor(position, receivesShadow
                    ? BuildContactMask(terrain, x, y, contactKind)
                    : Color.clear);
            }
        }
    }

    /// <summary>把四向岸线与真实水深共同编码到水格颜色，供三个水面 Shader 共用。</summary>
    private void ApplyWaterShaderData(Tilemap targetTilemap, ChunkTerrainData terrain)
    {
        if (targetTilemap == null)
            return;

        for (int y = 0; y < terrain.Height; y++)
        {
            for (int x = 0; x < terrain.Width; x++)
            {
                TerrainCell cell = terrain.GetCell(x, y);
                if (!IsWater(cell))
                    continue;

                Vector3Int position = new(x, y, 0);
                targetTilemap.SetTileFlags(position, TileFlags.None);
                targetTilemap.SetColor(position,
                    BuildWaterShaderData(terrain, x, y));
            }
        }
    }

    /// <summary>每个颜色通道以高位记录岸线方向，并在低位重复记录该格水深。</summary>
    private Color BuildWaterShaderData(ChunkTerrainData terrain, int x, int y)
    {
        float depthSignal = ResolveWaterDepth(terrain, x, y) * WaterDepthChannelScale;
        return new Color(
            depthSignal + (IsContactNeighbour(terrain, x - 1, y, ContactKind.Land)
                ? WaterContactChannelOffset : 0f),
            depthSignal + (IsContactNeighbour(terrain, x + 1, y, ContactKind.Land)
                ? WaterContactChannelOffset : 0f),
            depthSignal + (IsContactNeighbour(terrain, x, y - 1, ContactKind.Land)
                ? WaterContactChannelOffset : 0f),
            depthSignal + (IsContactNeighbour(terrain, x, y + 1, ContactKind.Land)
                ? WaterContactChannelOffset : 0f));
    }

    /// <summary>优先读取河流或地下水深度，海洋则复用玩法系统的高度换算规则。</summary>
    private static float ResolveWaterDepth(ChunkTerrainData terrain, int x, int y)
    {
        if (terrain.TryGetEnvironmentValue(RiverDepthLayerId, x, y,
                out float hydrologyDepth) && hydrologyDepth > 0f)
        {
            return Mathf.Clamp01(hydrologyDepth);
        }

        return terrain.TryGetEnvironmentValue(HeightLayerId, x, y, out float height)
            ? Mathf.Clamp01(TileData_Water.CalculateDepthFromHeight(height))
            : 0f;
    }

    /// <summary>RGBA 分别表示左、右、下、上是否需要绘制接触阴影。</summary>
    private Color BuildContactMask(ChunkTerrainData terrain, int x, int y, ContactKind kind)
    {
        return new Color(
            IsContactNeighbour(terrain, x - 1, y, kind) ? 1f : 0f,
            IsContactNeighbour(terrain, x + 1, y, kind) ? 1f : 0f,
            IsContactNeighbour(terrain, x, y - 1, kind) ? 1f : 0f,
            IsContactNeighbour(terrain, x, y + 1, kind) ? 1f : 0f);
    }

    private bool IsContactNeighbour(ChunkTerrainData terrain, int x, int y, ContactKind kind)
    {
        if (!TryGetCell(terrain, x, y, out TerrainCell neighbour))
            return false;
        return kind switch
        {
            ContactKind.Wall => IsBlocking(neighbour),
            ContactKind.Land => !IsWater(neighbour),
            ContactKind.Stone => IsStone(neighbour),
            _ => false
        };
    }

    /// <summary>读取本区块或已就绪的正交相邻区块格子。</summary>
    private bool TryGetCell(ChunkTerrainData terrain, int x, int y, out TerrainCell cell)
    {
        if (x >= 0 && x < terrain.Width && y >= 0 && y < terrain.Height)
        {
            cell = terrain.GetCell(x, y);
            return true;
        }

        cell = default;
        if (boundWorld == null || boundChunk == null)
            return false;

        Int2 origin = boundChunk.Address.ChunkOrigin;
        int targetOriginX = origin.X;
        int targetOriginY = origin.Y;
        int localX = x;
        int localY = y;
        if (x < 0)
            targetOriginX -= terrain.Width;
        else if (x >= terrain.Width)
            targetOriginX += terrain.Width;
        if (y < 0)
            targetOriginY -= terrain.Height;
        else if (y >= terrain.Height)
            targetOriginY += terrain.Height;

        var address = new FlatWorld.WorldModel.WorldAddress(boundChunk.Address.DimensionId,
            new Int2(targetOriginX, targetOriginY));
        if (!boundWorld.TryGetChunkTerrain(address, out ChunkTerrainData neighbourTerrain))
            return false;

        if (localX < 0)
            localX += neighbourTerrain.Width;
        else if (localX >= terrain.Width)
            localX -= terrain.Width;
        if (localY < 0)
            localY += neighbourTerrain.Height;
        else if (localY >= terrain.Height)
            localY -= terrain.Height;
        if (localX < 0 || localX >= neighbourTerrain.Width ||
            localY < 0 || localY >= neighbourTerrain.Height)
        {
            return false;
        }

        cell = neighbourTerrain.GetCell(localX, localY);
        return true;
    }

    private static bool IsCaveDimension(string dimensionId)
    {
        if (DimensionManager.Instance != null &&
            DimensionManager.Instance.TryGetDefinition(dimensionId,
                out DimensionDefinition definition))
        {
            return definition.GenerationMode == DimensionGenerationMode.Cave;
        }

        return string.Equals(dimensionId, "cave", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWater(TerrainCell cell) =>
        (cell.Flags & TerrainCellFlags.Water) != 0;

    private static bool IsBlocking(TerrainCell cell) =>
        cell.BlockingTileId != 0 && (cell.Flags & TerrainCellFlags.Blocking) != 0;

    /// <summary>通过稳定群系编号识别地表石地，避免依赖可变的 TileId 配置。</summary>
    private static bool IsStone(TerrainCell cell) =>
        cell.BiomeId == (int)SurfaceBiomeKind.Stone;

    private enum ContactKind
    {
        Wall,
        Land,
        Stone
    }

    #endregion
}
