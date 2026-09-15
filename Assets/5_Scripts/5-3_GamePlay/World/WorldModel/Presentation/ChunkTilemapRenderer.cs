using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 新版区块的基础 BRG 地图表现适配器。
///
/// Ground / Water / Back / Blocking 的视觉由共享 BatchRendererGroup 绘制，格子变化只上传脏格实例。
/// TilemapRenderer 保留为 Prefab 兼容和材质配置来源，其中 Blocking Tilemap 继续专供 TilemapCollider2D。
/// 岸线方向与连续水深改为每实例数据，不再为每个 Chunk 创建水深纹理或整块 SetTilesBlock。
/// </summary>
public sealed class ChunkTilemapRenderer : MonoBehaviour, IChunkViewRenderer, IWorldAwareChunkViewRenderer
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
    private WorldRuntime boundWorld;
    private ChunkRuntime boundChunk;
    private IDisposable chunkCommittedSubscription;
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
        DisableVisualTilemapRenderers();
        ClearVisualTilemaps();
        ChunkBatchRendererGroupService.RegisterOwner(this);
        SyncBlockingCollisionAll(chunk.Terrain);
        RefreshAllBatchVisuals(chunk.Terrain);
    }

    public void Unbind()
    {
        if (boundChunk?.Terrain != null)
            boundChunk.Terrain.Changed -= HandleTerrainChanged;
        ClearNeighbourTerrainSubscriptions();
        ChunkBatchRendererGroupService.UnregisterOwner(this);
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
    }

    private void HandleTerrainChanged(ChunkTerrainChanged changed)
    {
        if (boundChunk?.Terrain == null)
            return;
        if (changed.Kind != TerrainChangeKind.Cell &&
            changed.Kind != TerrainChangeKind.TileStack &&
            changed.Kind != TerrainChangeKind.Environment)
            return;

        if (changed.Kind == TerrainChangeKind.Cell || changed.Kind == TerrainChangeKind.TileStack)
            SyncBlockingCollisionCell(boundChunk.Terrain, changed.LocalCell.X, changed.LocalCell.Y);
        // 岸线、墙脚和四角水深最多依赖一圈邻格，因此只刷新 3x3 脏区。
        RefreshBatchArea(boundChunk.Terrain, changed.LocalCell.X, changed.LocalCell.Y, 1);
    }

    /// <summary>水体风格切换后，把现有水格迁移到新材质批次。</summary>
    public void NotifyWaterVisualStyleChanged(Renderer changedRenderer)
    {
        if (boundChunk?.Terrain == null || changedRenderer == null)
            return;
        Tilemap activeWater = renderCaveWater ? caveWaterTilemap : waterTilemap;
        if (activeWater == null || !ReferenceEquals(activeWater.GetComponent<TilemapRenderer>(), changedRenderer))
            return;

        ChunkTerrainData terrain = boundChunk.Terrain;
        for (int y = 0; y < terrain.Height; y++)
        for (int x = 0; x < terrain.Width; x++)
            if (IsWater(terrain.GetCell(x, y)))
                RefreshBatchCell(terrain, x, y);
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
            RefreshCommittedBatchEdge(boundChunk.Terrain, deltaX, deltaY);
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
             changed.Kind != TerrainChangeKind.TileStack &&
             changed.Kind != TerrainChangeKind.Environment))
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
            RefreshBatchEdge(boundChunk.Terrain, offsetX, offsetY,
                changed.LocalCell.X, changed.LocalCell.Y);
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

    #region BRG 表现与碰撞兼容

    /// <summary>视觉 TilemapRenderer 全部停用；Blocking Tilemap 仅继续喂给 TilemapCollider2D。</summary>
    private void DisableVisualTilemapRenderers()
    {
        SetRendererEnabled(groundTilemap, false);
        SetRendererEnabled(waterTilemap, false);
        SetRendererEnabled(caveWaterTilemap, false);
        SetRendererEnabled(backTilemap, false);
        SetRendererEnabled(blockingTilemap, false);
    }

    private static void SetRendererEnabled(Tilemap tilemap, bool enabled)
    {
        TilemapRenderer renderer = tilemap != null ? tilemap.GetComponent<TilemapRenderer>() : null;
        if (renderer != null)
            renderer.enabled = enabled;
    }

    /// <summary>清空旧视觉 Tilemap，避免热重载或池化后残留重复图像。</summary>
    private void ClearVisualTilemaps()
    {
        if (groundTilemap != null)
            groundTilemap.ClearAllTiles();
        if (waterTilemap != null)
            waterTilemap.ClearAllTiles();
        if (caveWaterTilemap != null)
            caveWaterTilemap.ClearAllTiles();
        if (backTilemap != null)
            backTilemap.ClearAllTiles();
        if (blockingTilemap != null)
            blockingTilemap.ClearAllTiles();
    }

    /// <summary>首次绑定时一次性建立 Blocking 碰撞格；视觉仍由 BRG 绘制。</summary>
    private void SyncBlockingCollisionAll(ChunkTerrainData terrain)
    {
        if (blockingTilemap == null)
            return;
        var tiles = new TileBase[terrain.CellCount];
        for (int y = 0; y < terrain.Height; y++)
        for (int x = 0; x < terrain.Width; x++)
        {
            TerrainCell cell = terrain.GetCell(x, y);
            if (cell.BlockingTileId != 0)
                palette.TryGetTile(cell.BlockingTileId, out tiles[y * terrain.Width + x]);
        }
        blockingTilemap.SetTilesBlock(new BoundsInt(0, 0, 0, terrain.Width, terrain.Height, 1), tiles);
    }

    private void SyncBlockingCollisionCell(ChunkTerrainData terrain, int x, int y)
    {
        if (blockingTilemap == null || x < 0 || x >= terrain.Width || y < 0 || y >= terrain.Height)
            return;
        TerrainCell cell = terrain.GetCell(x, y);
        TileBase tile = null;
        if (cell.BlockingTileId != 0)
            palette.TryGetTile(cell.BlockingTileId, out tile);
        blockingTilemap.SetTile(new Vector3Int(x, y, 0), tile);
    }

    private void RefreshAllBatchVisuals(ChunkTerrainData terrain)
    {
        for (int y = 0; y < terrain.Height; y++)
        for (int x = 0; x < terrain.Width; x++)
            RefreshBatchCell(terrain, x, y);
    }

    private void RefreshBatchArea(ChunkTerrainData terrain, int centerX, int centerY, int radius)
    {
        int minX = Mathf.Max(0, centerX - radius);
        int maxX = Mathf.Min(terrain.Width - 1, centerX + radius);
        int minY = Mathf.Max(0, centerY - radius);
        int maxY = Mathf.Min(terrain.Height - 1, centerY + radius);
        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
            RefreshBatchCell(terrain, x, y);
    }

    /// <summary>邻区变化映射到本区块最近边界格，再刷新一圈依赖格。</summary>
    private void RefreshBatchEdge(ChunkTerrainData terrain, int offsetX, int offsetY,
        int neighbourX, int neighbourY)
    {
        int x = offsetX < 0 ? 0 : offsetX > 0 ? terrain.Width - 1 : Mathf.Clamp(neighbourX, 0, terrain.Width - 1);
        int y = offsetY < 0 ? 0 : offsetY > 0 ? terrain.Height - 1 : Mathf.Clamp(neighbourY, 0, terrain.Height - 1);
        RefreshBatchArea(terrain, x, y, 1);
    }

    /// <summary>邻区首次提交会改变整条共享边界，而不是单一格。</summary>
    private void RefreshCommittedBatchEdge(ChunkTerrainData terrain, int offsetX, int offsetY)
    {
        if (offsetX != 0 && offsetY != 0)
        {
            int cornerX = offsetX < 0 ? 0 : terrain.Width - 1;
            int cornerY = offsetY < 0 ? 0 : terrain.Height - 1;
            RefreshBatchArea(terrain, cornerX, cornerY, 1);
            return;
        }

        if (offsetX != 0)
        {
            int x = offsetX < 0 ? 0 : terrain.Width - 1;
            for (int y = 0; y < terrain.Height; y++)
                RefreshBatchArea(terrain, x, y, 1);
            return;
        }

        int edgeY = offsetY < 0 ? 0 : terrain.Height - 1;
        for (int x = 0; x < terrain.Width; x++)
            RefreshBatchArea(terrain, x, edgeY, 1);
    }

    private void RefreshBatchCell(ChunkTerrainData terrain, int x, int y)
    {
        TerrainCell cell = terrain.GetCell(x, y);
        bool water = IsWater(cell);
        Material waterMaterial = GetActiveWaterMaterial();
        bool dedicatedWater = water && waterMaterial != null;

        if (cell.GroundTileId != 0 && !dedicatedWater)
        {
            SetBatchVisual(terrain, x, y, ChunkBatchRendererGroupService.VisualLayer.Ground,
                cell.GroundTileId, groundTilemap, GetMaterial(groundTilemap),
                BuildBatchGroundContactMask(terrain, x, y, cell), Vector4.zero);
        }
        else
        {
            ClearBatchVisual(terrain, x, y, ChunkBatchRendererGroupService.VisualLayer.Ground);
        }

        if (cell.GroundTileId != 0 && dedicatedWater)
        {
            SetBatchVisual(terrain, x, y, ChunkBatchRendererGroupService.VisualLayer.Water,
                cell.GroundTileId, renderCaveWater ? caveWaterTilemap : waterTilemap, waterMaterial,
                BuildWaterShoreMask(terrain, x, y), BuildWaterDepthCorners(terrain, x, y));
        }
        else
        {
            ClearBatchVisual(terrain, x, y, ChunkBatchRendererGroupService.VisualLayer.Water);
        }

        if (backTilemap != null && cell.BackTileId != 0)
        {
            SetBatchVisual(terrain, x, y, ChunkBatchRendererGroupService.VisualLayer.Back,
                cell.BackTileId, backTilemap, GetMaterial(backTilemap), Vector4.zero, Vector4.zero);
        }
        else
        {
            ClearBatchVisual(terrain, x, y, ChunkBatchRendererGroupService.VisualLayer.Back);
        }

        if (cell.BlockingTileId != 0)
        {
            SetBatchVisual(terrain, x, y, ChunkBatchRendererGroupService.VisualLayer.Blocking,
                cell.BlockingTileId, blockingTilemap, GetMaterial(blockingTilemap), Vector4.zero, Vector4.zero);
        }
        else
        {
            ClearBatchVisual(terrain, x, y, ChunkBatchRendererGroupService.VisualLayer.Blocking);
        }
    }

    private void SetBatchVisual(ChunkTerrainData terrain, int x, int y,
        ChunkBatchRendererGroupService.VisualLayer layer, int tileId, Tilemap sourceTilemap,
        Material sourceMaterial, Vector4 data0, Vector4 data1)
    {
        if (sourceMaterial == null ||
            !palette.TryGetVisual(tileId, out Sprite sprite, out Color tileColor, out Matrix4x4 tileTransform))
        {
            ClearBatchVisual(terrain, x, y, layer);
            return;
        }

        Int2 origin = boundChunk.Address.ChunkOrigin;
        Matrix4x4 localToWorld = Matrix4x4.Translate(new Vector3(
            origin.X + x + 0.5f,
            origin.Y + y + 0.5f,
            0f)) * tileTransform;
        Color tint = sourceTilemap != null ? tileColor * sourceTilemap.color : tileColor;
        var instanceData = ChunkBatchRendererGroupService.InstanceData.Create(localToWorld, data0, data1, tint);
        ChunkBatchRendererGroupService.SetVisual(this, GetBatchSlotKey(terrain, x, y, layer),
            new ChunkBatchRendererGroupService.Visual(layer, sprite, sourceMaterial, instanceData));
    }

    private void ClearBatchVisual(ChunkTerrainData terrain, int x, int y,
        ChunkBatchRendererGroupService.VisualLayer layer)
    {
        ChunkBatchRendererGroupService.ClearVisual(this, GetBatchSlotKey(terrain, x, y, layer));
    }

    private static int GetBatchSlotKey(ChunkTerrainData terrain, int x, int y,
        ChunkBatchRendererGroupService.VisualLayer layer)
    {
        return (int)layer * terrain.CellCount + y * terrain.Width + x;
    }

    private Material GetActiveWaterMaterial()
    {
        return GetMaterial(renderCaveWater ? caveWaterTilemap : waterTilemap);
    }

    private static Material GetMaterial(Tilemap tilemap)
    {
        TilemapRenderer renderer = tilemap != null ? tilemap.GetComponent<TilemapRenderer>() : null;
        return renderer != null ? renderer.sharedMaterial : null;
    }

    private Vector4 BuildBatchGroundContactMask(ChunkTerrainData terrain, int x, int y, TerrainCell cell)
    {
        bool cave = IsCaveDimension(boundChunk?.Address.DimensionId);
        if (cave)
        {
            bool receivesShadow = !IsBlocking(cell) && !IsWater(cell);
            return receivesShadow ? (Vector4)BuildContactMask(terrain, x, y, ContactKind.Wall) : Vector4.zero;
        }

        if (IsWater(cell))
            return (Vector4)BuildContactMask(terrain, x, y, ContactKind.Land);
        return !IsStone(cell)
            ? (Vector4)BuildContactMask(terrain, x, y, ContactKind.Stone)
            : Vector4.zero;
    }

    /// <summary>RGBA 依次保存左下、右下、左上、右上格角的连续水深。</summary>
    private Vector4 BuildWaterDepthCorners(ChunkTerrainData terrain, int x, int y)
    {
        return new Vector4(
            ResolveCornerDepth(terrain, x - 1, y - 1),
            ResolveCornerDepth(terrain, x, y - 1),
            ResolveCornerDepth(terrain, x - 1, y),
            ResolveCornerDepth(terrain, x, y));
    }

    private float ResolveCornerDepth(ChunkTerrainData terrain, int leftCellX, int bottomCellY)
    {
        return (
            ResolveExtendedWaterDepth(terrain, leftCellX, bottomCellY) +
            ResolveExtendedWaterDepth(terrain, leftCellX + 1, bottomCellY) +
            ResolveExtendedWaterDepth(terrain, leftCellX, bottomCellY + 1) +
            ResolveExtendedWaterDepth(terrain, leftCellX + 1, bottomCellY + 1)) * 0.25f;
    }

    #endregion

    #endregion

    #region BRG Shader 数据

    /// <summary>RGBA 分别记录左、右、下、上岸线方向。</summary>
    private Color BuildWaterShoreMask(ChunkTerrainData terrain, int x, int y)
    {
        return new Color(
            IsContactNeighbour(terrain, x - 1, y, ContactKind.Land) ? 1f : 0f,
            IsContactNeighbour(terrain, x + 1, y, ContactKind.Land) ? 1f : 0f,
            IsContactNeighbour(terrain, x, y - 1, ContactKind.Land) ? 1f : 0f,
            IsContactNeighbour(terrain, x, y + 1, ContactKind.Land) ? 1f : 0f);
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
