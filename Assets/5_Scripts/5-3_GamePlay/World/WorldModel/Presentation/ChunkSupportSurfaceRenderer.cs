using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 支撑面独立 Tilemap 表现；只读取权威层，不遮改水深、岸线或存档。
/// 平台接触阴影通过 Tile Color RGBA 编码左、右、下、上四个外露方向，
/// 相邻支撑平台之间不再绘制内部阴影，并支持跨 Chunk 连续平台。
/// </summary>
public sealed class ChunkSupportSurfaceRenderer : MonoBehaviour, IChunkViewRenderer, IWorldAwareChunkViewRenderer
{
    #region 配置与状态

    [SerializeField] private ChunkTilePaletteSO palette; // 材料到地块的映射。
    [SerializeField] private Tilemap tilemap; // 水面上方的平台图层。

    private readonly List<NeighbourTerrainSubscription> neighbourTerrainSubscriptions = new(4);
    private WorldRuntime boundWorld;
    private ChunkRuntime chunk;
    private IDisposable chunkCommittedSubscription;

    #endregion

    #region 绑定与生命周期

    /// <summary>注入当前世界，用于读取跨 Chunk 的相邻平台。</summary>
    public void SetWorld(WorldRuntime worldRuntime)
    {
        if (ReferenceEquals(boundWorld, worldRuntime))
            return;

        chunkCommittedSubscription?.Dispose();
        chunkCommittedSubscription = null;
        boundWorld = worldRuntime;
        if (boundWorld != null)
            chunkCommittedSubscription = boundWorld.Events.Subscribe<ChunkCommitted>(HandleChunkCommitted);
        RefreshNeighbourTerrainSubscriptions();
    }

    /// <summary>绑定区块并从持久化支撑面重建表现。</summary>
    public void Bind(ChunkRuntime value)
    {
        if (value == null)
            throw new ArgumentNullException(nameof(value));
        if (value.Terrain == null)
            throw new InvalidOperationException("Cannot bind support rendering before terrain data is ready.");

        Unbind();
        chunk = value;
        chunk.Terrain.Changed += HandleChanged;
        RefreshNeighbourTerrainSubscriptions();
        RefreshAll();
    }

    /// <summary>取消订阅并清除回池区块的旧平台。</summary>
    public void Unbind()
    {
        if (chunk?.Terrain != null)
            chunk.Terrain.Changed -= HandleChanged;
        ClearNeighbourTerrainSubscriptions();
        chunk = null;
        if (tilemap != null)
            tilemap.ClearAllTiles();
    }

    private void OnDestroy()
    {
        Unbind();
        chunkCommittedSubscription?.Dispose();
        chunkCommittedSubscription = null;
        boundWorld = null;
    }

    #endregion

    #region 平台绘制

    /// <summary>支撑层改变时同步自身和四邻格的外轮廓。</summary>
    private void HandleChanged(ChunkTerrainChanged change)
    {
        if (change.Kind != TerrainChangeKind.Environment || chunk?.Terrain == null)
            return;

        int x = change.LocalCell.X;
        int y = change.LocalCell.Y;
        Refresh(x, y);
        Refresh(x - 1, y);
        Refresh(x + 1, y);
        Refresh(x, y - 1);
        Refresh(x, y + 1);
    }

    /// <summary>重建当前 Chunk 的平台和外露边缘遮罩。</summary>
    private void RefreshAll()
    {
        if (chunk?.Terrain == null)
            return;

        for (int y = 0; y < chunk.Terrain.Height; y++)
            for (int x = 0; x < chunk.Terrain.Width; x++)
                Refresh(x, y);
    }

    /// <summary>按稳定地块 ID 选择平台外观，并只保留平台整体外围的接触阴影。</summary>
    private void Refresh(int x, int y)
    {
        if (chunk?.Terrain == null || tilemap == null ||
            x < 0 || x >= chunk.Terrain.Width || y < 0 || y >= chunk.Terrain.Height)
        {
            return;
        }

        int id = TerrainSupportLayer.GetTileId(chunk.Terrain, x, y);
        palette.TryGetTile(id, out TileBase tile);
        Vector3Int position = new(x, y, 0);
        tilemap.SetTile(position, tile);
        if (id == 0 || tile == null)
            return;

        // Tile 默认锁定颜色；解除后用 RGBA 分别控制左、右、下、上的接触阴影。
        tilemap.SetTileFlags(position, TileFlags.None);
        tilemap.SetColor(position, BuildPerimeterMask(chunk.Terrain, x, y));
    }

    /// <summary>RGBA 分别表示左、右、下、上是否属于平台整体的外露边缘。</summary>
    private Color BuildPerimeterMask(ChunkTerrainData terrain, int x, int y)
    {
        return new Color(
            HasSupport(terrain, x - 1, y) ? 0f : 1f,
            HasSupport(terrain, x + 1, y) ? 0f : 1f,
            HasSupport(terrain, x, y - 1) ? 0f : 1f,
            HasSupport(terrain, x, y + 1) ? 0f : 1f);
    }

    /// <summary>任意支撑地块都视为同一高度的平台连接面，不在材质交界处制造凹陷阴影。</summary>
    private bool HasSupport(ChunkTerrainData terrain, int x, int y)
    {
        return TryGetSupportTileId(terrain, x, y, out int tileId) && tileId != 0;
    }

    /// <summary>读取本 Chunk 或正交相邻 Chunk 的支撑层。</summary>
    private bool TryGetSupportTileId(ChunkTerrainData terrain, int x, int y, out int tileId)
    {
        tileId = 0;
        if (x >= 0 && x < terrain.Width && y >= 0 && y < terrain.Height)
        {
            tileId = TerrainSupportLayer.GetTileId(terrain, x, y);
            return true;
        }

        if (boundWorld == null || chunk == null)
            return false;

        Int2 origin = chunk.Address.ChunkOrigin;
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

        var address = new FlatWorld.WorldModel.WorldAddress(
            chunk.Address.DimensionId, new Int2(targetOriginX, targetOriginY));
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

        tileId = TerrainSupportLayer.GetTileId(neighbourTerrain, localX, localY);
        return true;
    }

    #endregion

    #region 相邻 Chunk 同步

    /// <summary>相邻 Chunk 就绪后重新建立边界订阅并刷新共享边。</summary>
    private void HandleChunkCommitted(ChunkCommitted committed)
    {
        if (chunk?.Terrain == null ||
            !string.Equals(committed.Address.DimensionId, chunk.Address.DimensionId, StringComparison.Ordinal))
        {
            return;
        }

        ChunkTerrainData terrain = chunk.Terrain;
        Int2 origin = chunk.Address.ChunkOrigin;
        Int2 changed = committed.Address.ChunkOrigin;
        int deltaX = changed.X - origin.X;
        int deltaY = changed.Y - origin.Y;
        bool orthogonalNeighbour =
            (Math.Abs(deltaX) == terrain.Width && deltaY == 0) ||
            (Math.Abs(deltaY) == terrain.Height && deltaX == 0);
        if (!orthogonalNeighbour)
            return;

        RefreshNeighbourTerrainSubscriptions();
        if (deltaX < 0)
            RefreshVerticalEdge(0);
        else if (deltaX > 0)
            RefreshVerticalEdge(terrain.Width - 1);
        else if (deltaY < 0)
            RefreshHorizontalEdge(0);
        else
            RefreshHorizontalEdge(terrain.Height - 1);
    }

    /// <summary>订阅四个正交相邻区块，平台变化时同步共享边阴影。</summary>
    private void RefreshNeighbourTerrainSubscriptions()
    {
        ClearNeighbourTerrainSubscriptions();
        if (boundWorld == null || chunk?.Terrain == null)
            return;

        ChunkTerrainData terrain = chunk.Terrain;
        SubscribeNeighbourTerrain(-terrain.Width, 0);
        SubscribeNeighbourTerrain(terrain.Width, 0);
        SubscribeNeighbourTerrain(0, -terrain.Height);
        SubscribeNeighbourTerrain(0, terrain.Height);
    }

    private void SubscribeNeighbourTerrain(int offsetX, int offsetY)
    {
        Int2 origin = chunk.Address.ChunkOrigin;
        var address = new FlatWorld.WorldModel.WorldAddress(chunk.Address.DimensionId,
            new Int2(origin.X + offsetX, origin.Y + offsetY));
        if (!boundWorld.TryGetChunkTerrain(address, out ChunkTerrainData neighbourTerrain))
            return;

        Action<ChunkTerrainChanged> handler = change =>
            HandleNeighbourTerrainChanged(neighbourTerrain, offsetX, offsetY, change);
        neighbourTerrain.Changed += handler;
        neighbourTerrainSubscriptions.Add(new NeighbourTerrainSubscription(neighbourTerrain, handler));
    }

    /// <summary>邻区共享边支撑状态改变时，只刷新当前 Chunk 对应的一个边缘格。</summary>
    private void HandleNeighbourTerrainChanged(
        ChunkTerrainData neighbourTerrain,
        int offsetX,
        int offsetY,
        ChunkTerrainChanged change)
    {
        if (chunk?.Terrain == null || change.Kind != TerrainChangeKind.Environment)
            return;

        if (offsetX < 0)
        {
            if (change.LocalCell.X == neighbourTerrain.Width - 1)
                Refresh(0, change.LocalCell.Y);
            return;
        }
        if (offsetX > 0)
        {
            if (change.LocalCell.X == 0)
                Refresh(chunk.Terrain.Width - 1, change.LocalCell.Y);
            return;
        }
        if (offsetY < 0)
        {
            if (change.LocalCell.Y == neighbourTerrain.Height - 1)
                Refresh(change.LocalCell.X, 0);
            return;
        }
        if (offsetY > 0 && change.LocalCell.Y == 0)
            Refresh(change.LocalCell.X, chunk.Terrain.Height - 1);
    }

    private void RefreshVerticalEdge(int x)
    {
        for (int y = 0; y < chunk.Terrain.Height; y++)
            Refresh(x, y);
    }

    private void RefreshHorizontalEdge(int y)
    {
        for (int x = 0; x < chunk.Terrain.Width; x++)
            Refresh(x, y);
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

    private readonly struct NeighbourTerrainSubscription
    {
        public NeighbourTerrainSubscription(ChunkTerrainData terrain, Action<ChunkTerrainChanged> handler)
        {
            Terrain = terrain;
            Handler = handler;
        }

        public ChunkTerrainData Terrain { get; }
        public Action<ChunkTerrainChanged> Handler { get; }
    }

    #endregion
}
