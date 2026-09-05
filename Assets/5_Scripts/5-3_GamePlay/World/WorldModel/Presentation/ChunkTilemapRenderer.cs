using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 新版区块的基础 Tilemap 表现层。
///
/// 地表陆地、地表水岸、矿洞墙脚和水体分别使用对应的 Tilemap 表现层。
/// 左、右、下、上四个接触方向编码到 Tile Color RGBA，连续水深由每个 Chunk 的独立纹理提供，
/// 不再让岸线位与水深共用颜色通道，也不为接触阴影创建 SpriteRenderer 游戏对象。
/// </summary>
public sealed class ChunkTilemapRenderer : MonoBehaviour, IChunkViewRenderer
{
    #region 配置与状态

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

    private readonly List<NeighbourTerrainSubscription> neighbourTerrainSubscriptions = new(8);
    private static readonly int WaterDepthTextureId = Shader.PropertyToID("_WaterDepthTexture");
    private static readonly int WaterDepthUvScaleOffsetId =
        Shader.PropertyToID("_WaterDepthUvScaleOffset");
    private WorldRuntime boundWorld;
    private ChunkRuntime boundChunk;
    private IDisposable chunkCommittedSubscription;
    private MaterialPropertyBlock waterPropertyBlock;
    private Texture2D waterDepthTexture;
    private Color32[] waterDepthPixels;
    private bool renderCaveWater;

    /// <summary>阻挡层渲染器，为裂缝等格子表现提供一致的材质与排序基准。</summary>
    public TilemapRenderer BlockingTilemapRenderer =>
        blockingTilemap != null ? blockingTilemap.GetComponent<TilemapRenderer>() : null;

    #endregion

    #region 绑定与生命周期

    /// <summary>注入当前世界，用于计算跨 Chunk 的接触方向与连续水深边界。</summary>
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
        RefreshNeighbourTerrainSubscriptions();
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
        RefreshNeighbourTerrainSubscriptions();
        Render(chunk.Terrain);
    }

    public void Unbind()
    {
        if (boundChunk?.Terrain != null)
            boundChunk.Terrain.Changed -= HandleTerrainChanged;
        ClearNeighbourTerrainSubscriptions();
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

    private void OnDestroy()
    {
        if (boundChunk?.Terrain != null)
            boundChunk.Terrain.Changed -= HandleTerrainChanged;
        chunkCommittedSubscription?.Dispose();
        chunkCommittedSubscription = null;
        ClearNeighbourTerrainSubscriptions();
        if (waterDepthTexture != null)
            Destroy(waterDepthTexture);
    }

    private void HandleTerrainChanged(ChunkTerrainChanged changed)
    {
        if (boundChunk?.Terrain == null)
            return;
        if (changed.Kind != TerrainChangeKind.Cell &&
            changed.Kind != TerrainChangeKind.TileStack)
            return;

        // 接触方向与连续水深边界会影响邻格，地形变化时刷新本 Chunk 的轻量 Tilemap 数据。
        Render(boundChunk.Terrain);
    }

    /// <summary>相邻区块生成后刷新边界格的岸向与水深纹理边框。</summary>
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
        int deltaX = changed.X - origin.X;
        int deltaY = changed.Y - origin.Y;
        bool isNeighbour =
            (deltaX == -width || deltaX == 0 || deltaX == width) &&
            (deltaY == -height || deltaY == 0 || deltaY == height) &&
            (deltaX != 0 || deltaY != 0);
        if (isNeighbour)
        {
            RefreshNeighbourTerrainSubscriptions();
            Render(boundChunk.Terrain);
        }
    }

    /// <summary>订阅八个相邻区块；正交边界负责接触遮罩，对角区块补齐水深纹理边角。</summary>
    private void RefreshNeighbourTerrainSubscriptions()
    {
        ClearNeighbourTerrainSubscriptions();
        if (boundWorld == null || boundChunk?.Terrain == null)
            return;

        ChunkTerrainData terrain = boundChunk.Terrain;
        SubscribeNeighbourTerrain(-terrain.Width, 0);
        SubscribeNeighbourTerrain(terrain.Width, 0);
        SubscribeNeighbourTerrain(0, -terrain.Height);
        SubscribeNeighbourTerrain(0, terrain.Height);
        SubscribeNeighbourTerrain(-terrain.Width, -terrain.Height);
        SubscribeNeighbourTerrain(terrain.Width, -terrain.Height);
        SubscribeNeighbourTerrain(-terrain.Width, terrain.Height);
        SubscribeNeighbourTerrain(terrain.Width, terrain.Height);
    }

    private void SubscribeNeighbourTerrain(int offsetX, int offsetY)
    {
        Int2 origin = boundChunk.Address.ChunkOrigin;
        var address = new FlatWorld.WorldModel.WorldAddress(boundChunk.Address.DimensionId,
            new Int2(origin.X + offsetX, origin.Y + offsetY));
        if (!boundWorld.TryGetChunkTerrain(address, out ChunkTerrainData neighbourTerrain))
            return;

        Action<ChunkTerrainChanged> handler = changed =>
            HandleNeighbourTerrainChanged(neighbourTerrain, offsetX, offsetY, changed);
        neighbourTerrain.Changed += handler;
        neighbourTerrainSubscriptions.Add(
            new NeighbourTerrainSubscription(neighbourTerrain, handler));
    }

    private void HandleNeighbourTerrainChanged(
        ChunkTerrainData neighbourTerrain,
        int offsetX,
        int offsetY,
        ChunkTerrainChanged changed)
    {
        if (boundChunk?.Terrain == null ||
            (changed.Kind != TerrainChangeKind.Cell &&
             changed.Kind != TerrainChangeKind.TileStack))
        {
            return;
        }

        bool touchesSharedX = offsetX == 0 || (offsetX < 0
            ? changed.LocalCell.X == neighbourTerrain.Width - 1
            : changed.LocalCell.X == 0);
        bool touchesSharedY = offsetY == 0 || (offsetY < 0
            ? changed.LocalCell.Y == neighbourTerrain.Height - 1
            : changed.LocalCell.Y == 0);
        if (touchesSharedX && touchesSharedY)
            Render(boundChunk.Terrain);
    }

    private void ClearNeighbourTerrainSubscriptions()
    {
        for (int i = 0; i < neighbourTerrainSubscriptions.Count; i++)
        {
            NeighbourTerrainSubscription subscription = neighbourTerrainSubscriptions[i];
            subscription.Terrain.Changed -= subscription.Handler;
        }

        neighbourTerrainSubscriptions.Clear();
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

    /// <summary>Tile Color 只写岸线方向，连续水深通过独立纹理提交给当前 Chunk 渲染器。</summary>
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
                targetTilemap.SetColor(position, BuildWaterShoreMask(terrain, x, y));
            }
        }

        ApplyWaterDepthTexture(targetTilemap, terrain);
    }

    /// <summary>RGBA 分别记录左、右、下、上岸线方向。</summary>
    private Color BuildWaterShoreMask(ChunkTerrainData terrain, int x, int y)
    {
        return new Color(
            IsContactNeighbour(terrain, x - 1, y, ContactKind.Land) ? 1f : 0f,
            IsContactNeighbour(terrain, x + 1, y, ContactKind.Land) ? 1f : 0f,
            IsContactNeighbour(terrain, x, y - 1, ContactKind.Land) ? 1f : 0f,
            IsContactNeighbour(terrain, x, y + 1, ContactKind.Land) ? 1f : 0f);
    }

    /// <summary>建立包含一格邻区边框的水深纹理，让 GPU 在水格中心之间连续插值。</summary>
    private void ApplyWaterDepthTexture(Tilemap targetTilemap, ChunkTerrainData terrain)
    {
        int textureWidth = terrain.Width + 2;
        int textureHeight = terrain.Height + 2;
        EnsureWaterDepthTexture(textureWidth, textureHeight);
        for (int textureY = 0; textureY < textureHeight; textureY++)
        {
            for (int textureX = 0; textureX < textureWidth; textureX++)
            {
                float depth = ResolveExtendedWaterDepth(
                    terrain,
                    textureX - 1,
                    textureY - 1);
                byte encodedDepth = (byte)Mathf.RoundToInt(Mathf.Clamp01(depth) * 255f);
                waterDepthPixels[textureY * textureWidth + textureX] =
                    new Color32(encodedDepth, 0, 0, byte.MaxValue);
            }
        }

        waterDepthTexture.SetPixels32(waterDepthPixels);
        waterDepthTexture.Apply(false, false);

        TilemapRenderer targetRenderer = targetTilemap.GetComponent<TilemapRenderer>();
        if (targetRenderer == null)
            throw new InvalidOperationException("Water Tilemap requires a TilemapRenderer.");

        waterPropertyBlock ??= new MaterialPropertyBlock();
        targetRenderer.GetPropertyBlock(waterPropertyBlock);
        waterPropertyBlock.SetTexture(WaterDepthTextureId, waterDepthTexture);
        // Tilemap 合批后顶点不保证处于 Chunk 局部空间，显式从世界坐标映射到水深纹理。
        Vector3 waterOrigin = targetTilemap.CellToWorld(Vector3Int.zero);
        waterPropertyBlock.SetVector(WaterDepthUvScaleOffsetId, new Vector4(
            1f / textureWidth,
            1f / textureHeight,
            (1f - waterOrigin.x) / textureWidth,
            (1f - waterOrigin.y) / textureHeight));
        targetRenderer.SetPropertyBlock(waterPropertyBlock);
    }

    /// <summary>按 Chunk 尺寸复用深度纹理与像素缓存。</summary>
    private void EnsureWaterDepthTexture(int width, int height)
    {
        if (waterDepthTexture != null &&
            waterDepthTexture.width == width &&
            waterDepthTexture.height == height)
        {
            return;
        }

        if (waterDepthTexture != null)
            Destroy(waterDepthTexture);
        waterDepthTexture = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
        {
            name = $"WaterDepth_{GetInstanceID()}",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        waterDepthPixels = new Color32[width * height];
    }

    /// <summary>非水格沿用周围水格平均深度，避免岸线参与颜色渐变。</summary>
    private float ResolveExtendedWaterDepth(ChunkTerrainData terrain, int x, int y)
    {
        if (TryResolveWaterDepth(terrain, x, y, out float depth))
            return depth;

        // 邻区边框落在陆地时，先向当前 Chunk 边缘延展，避免取样范围越过第二圈邻格。
        int sampleX = Mathf.Clamp(x, 0, terrain.Width - 1);
        int sampleY = Mathf.Clamp(y, 0, terrain.Height - 1);
        if ((sampleX != x || sampleY != y) &&
            TryResolveWaterDepth(terrain, sampleX, sampleY, out depth))
        {
            return depth;
        }

        float depthTotal = 0f;
        int waterCount = 0;
        for (int offsetY = -1; offsetY <= 1; offsetY++)
        {
            for (int offsetX = -1; offsetX <= 1; offsetX++)
            {
                if (!TryResolveWaterDepth(terrain, sampleX + offsetX, sampleY + offsetY,
                        out float neighbourDepth))
                {
                    continue;
                }

                depthTotal += neighbourDepth;
                waterCount++;
            }
        }

        return waterCount > 0 ? depthTotal / waterCount : 0f;
    }

    /// <summary>读取当前格或邻区水格的权威深度。</summary>
    private bool TryResolveWaterDepth(ChunkTerrainData terrain, int x, int y, out float depth)
    {
        depth = 0f;
        if (!TryResolveTerrainCell(terrain, x, y,
                out ChunkTerrainData resolvedTerrain, out int localX, out int localY,
                out TerrainCell cell) || !IsWater(cell))
        {
            return false;
        }

        depth = ResolveWaterDepth(resolvedTerrain, localX, localY);
        return true;
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

    /// <summary>读取本区块或已就绪的八方向相邻区块格子。</summary>
    private bool TryGetCell(ChunkTerrainData terrain, int x, int y, out TerrainCell cell)
    {
        return TryResolveTerrainCell(terrain, x, y, out _, out _, out _, out cell);
    }

    /// <summary>把越界坐标解析到相邻 Chunk，并返回实际地形与局部坐标。</summary>
    private bool TryResolveTerrainCell(ChunkTerrainData terrain, int x, int y,
        out ChunkTerrainData resolvedTerrain, out int resolvedX, out int resolvedY,
        out TerrainCell cell)
    {
        resolvedTerrain = null;
        resolvedX = 0;
        resolvedY = 0;
        cell = default;
        if (x >= 0 && x < terrain.Width && y >= 0 && y < terrain.Height)
        {
            resolvedTerrain = terrain;
            resolvedX = x;
            resolvedY = y;
            cell = terrain.GetCell(x, y);
            return true;
        }

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

        resolvedTerrain = neighbourTerrain;
        resolvedX = localX;
        resolvedY = localY;
        cell = resolvedTerrain.GetCell(resolvedX, resolvedY);
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

    private readonly struct NeighbourTerrainSubscription
    {
        public NeighbourTerrainSubscription(
            ChunkTerrainData terrain,
            Action<ChunkTerrainChanged> handler)
        {
            Terrain = terrain;
            Handler = handler;
        }

        public ChunkTerrainData Terrain { get; }
        public Action<ChunkTerrainChanged> Handler { get; }
    }

    #endregion
}
