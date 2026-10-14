using System;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>环境阈值覆盖只装饰符合条件的地表，默认不覆盖独立液体层。</summary>
public sealed class ChunkEnvironmentRenderer : MonoBehaviour, IChunkViewRenderer
{
    #region 配置与绑定

    [SerializeField] private string environmentLayerId = "grass";
    [SerializeField] private float visibleThreshold = 0.5f;
    [SerializeField] private bool includeLiquidCells;
    [SerializeField] private Tile tile;
    [SerializeField] private Material material;
    [SerializeField] private Color tint = new(1f, 1f, 1f, 0.22f);
    private ChunkRuntime boundChunk;
    private ChunkTilemapRenderer owner;
    private CellVisualState[] cellVisualStates = Array.Empty<CellVisualState>();
    private bool visualConfigurationChanged;

    private enum CellVisualState : byte
    {
        Unknown,
        Hidden,
        Visible
    }

    private void OnValidate() => visualConfigurationChanged = true;

    public void Bind(ChunkRuntime chunk)
    {
        if (chunk == null)
            throw new System.ArgumentNullException(nameof(chunk));
        if (chunk.Terrain == null)
            throw new System.InvalidOperationException("Cannot bind environment rendering before data is ready.");
        if (ReferenceEquals(boundChunk, chunk))
            return;
        Unbind();
        owner = GetComponent<ChunkTilemapRenderer>();
        boundChunk = chunk;
        boundChunk.Terrain.Changed += HandleTerrainChanged;
        boundChunk.Terrain.LiquidBatchChanged += HandleLiquidBatchChanged;
        owner.BatchPresentationRebuilt += HandleBatchPresentationRebuilt;
        Render(chunk.Terrain);
    }

    public void Unbind()
    {
        if (boundChunk?.Terrain != null)
        {
            boundChunk.Terrain.Changed -= HandleTerrainChanged;
            boundChunk.Terrain.LiquidBatchChanged -= HandleLiquidBatchChanged;
        }
        if (owner != null)
        {
            owner.BatchPresentationRebuilt -= HandleBatchPresentationRebuilt;
            if (boundChunk?.Terrain != null && owner.IsBatchPresentationRegistered)
                for (int y = 0; y < boundChunk.Terrain.Height; y++)
                for (int x = 0; x < boundChunk.Terrain.Width; x++)
                    owner.ClearLayerVisual(ChunkBatchRendererGroupService.VisualLayer.Environment, x, y);
        }
        boundChunk = null;
        owner = null;
        cellVisualStates = Array.Empty<CellVisualState>();
    }

    #endregion

    #region 地表覆盖刷新

    private void HandleTerrainChanged(ChunkTerrainChanged changed)
    {
        if (boundChunk?.Terrain == null ||
            (changed.Kind != TerrainChangeKind.Environment && changed.Kind != TerrainChangeKind.Liquid &&
             changed.Kind != TerrainChangeKind.Cell && changed.Kind != TerrainChangeKind.TileStack) ||
            changed.Kind == TerrainChangeKind.Environment && !changed.AffectsEnvironmentLayer(environmentLayerId) ||
            tile == null || material == null)
            return;
        if (!changed.IsBatch)
        {
            RefreshCellFromTerrain(changed.LocalCell.X, changed.LocalCell.Y);
            return;
        }
        // 跨过显示阈值的格子也合并上传，土壤批次不会逐格提交显卡数据。
        ChunkBatchRendererGroupService.BeginBulkSubmit();
        try
        {
            int width = boundChunk.Terrain.Width;
            foreach (int index in changed.GetCellIndices(width))
                RefreshCellFromTerrain(index % width, index / width);
        }
        finally
        {
            ChunkBatchRendererGroupService.EndBulkSubmit();
        }
    }

    private void RefreshCellFromTerrain(int x, int y)
    {
        if (visualConfigurationChanged)
        {
            Render(boundChunk.Terrain);
            return;
        }
        bool visible = boundChunk.Terrain.TryGetEnvironmentValue(environmentLayerId,
                           x, y, out float value) &&
                       value >= visibleThreshold;
        RefreshCell(x, y, visible);
    }

    /// <summary>倒水、抽水与批量流动后同步地表覆盖，不在水面留下旧染色。</summary>
    private void HandleLiquidBatchChanged(ChunkLiquidBatchChanged changed)
    {
        if (includeLiquidCells || boundChunk?.Terrain == null || tile == null || material == null ||
            !ReferenceEquals(changed.Terrain, boundChunk.Terrain))
            return;
        foreach (int index in changed.CellIndices.Span)
            RefreshCellFromTerrain(index % changed.Terrain.Width, index / changed.Terrain.Width);
    }

    /// <summary>Owner 重建后从权威环境层补齐本图层。</summary>
    private void HandleBatchPresentationRebuilt()
    {
        if (boundChunk?.Terrain != null)
            Render(boundChunk.Terrain);
    }

    private void Render(ChunkTerrainData terrain)
    {
        if (tile == null || material == null || terrain == null)
            return;
        if (cellVisualStates.Length != terrain.CellCount)
            cellVisualStates = new CellVisualState[terrain.CellCount];
        else
            Array.Clear(cellVisualStates, 0, cellVisualStates.Length);
        visualConfigurationChanged = false;
        terrain.TryCopyEnvironmentLayer(environmentLayerId, out float[] values);
        for (int y = 0; y < terrain.Height; y++)
        for (int x = 0; x < terrain.Width; x++)
            RefreshCell(x, y, values != null && values[y * terrain.Width + x] >= visibleThreshold);
    }

    /// <summary>单格环境覆盖的 BRG 提交与清除。</summary>
    private void RefreshCell(int x, int y, bool visible)
    {
        ChunkTerrainData terrain = boundChunk.Terrain;
        visible = visible && terrain.GetCell(x, y).GroundTileId != 0 &&
                  (includeLiquidCells || !(terrain.GetLiquidDepth(x, y) > 0f));
        int index = y * terrain.Width + x;
        CellVisualState nextState = visible ? CellVisualState.Visible : CellVisualState.Hidden;
        // 只记住实际提交的显示状态，湿度继续变化但外观相同时不重复上传。
        if (cellVisualStates[index] == nextState)
            return;
        if (!visible)
        {
            owner.ClearLayerVisual(ChunkBatchRendererGroupService.VisualLayer.Environment, x, y);
            cellVisualStates[index] = CellVisualState.Hidden;
            return;
        }
        Int2 origin = boundChunk.Address.ChunkOrigin;
        Matrix4x4 matrix = Matrix4x4.Translate(new Vector3(origin.X + x + 0.5f, origin.Y + y + 0.5f));
        if (owner.SetLayerVisual(ChunkBatchRendererGroupService.VisualLayer.Environment, x, y,
                tile.sprite, material, matrix * tile.transform, tile.color * tint))
            cellVisualStates[index] = CellVisualState.Visible;
    }

    #endregion
}
