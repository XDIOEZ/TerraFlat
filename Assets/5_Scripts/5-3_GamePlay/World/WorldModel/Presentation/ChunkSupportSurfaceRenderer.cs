using System;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>只绘制地板与平台本体，高低阴影统一交给 TerrainHeightShadowManager。</summary>
public sealed class ChunkSupportSurfaceRenderer : MonoBehaviour, IChunkViewRenderer
{
    #region 覆盖面绑定

    [SerializeField] private ChunkTilePaletteSO palette;
    [SerializeField] private Material material;
    private ChunkRuntime chunk;
    private ChunkTilemapRenderer owner;

    public void Bind(ChunkRuntime value)
    {
        Unbind();
        chunk = value ?? throw new ArgumentNullException(nameof(value));
        owner = GetComponent<ChunkTilemapRenderer>();
        if (chunk.Terrain == null || owner == null)
            throw new InvalidOperationException("覆盖面缺少权威地形或 BRG Owner。");
        chunk.Terrain.Changed += HandleChanged;
        owner.BatchPresentationRebuilt += RefreshAll;
        RefreshAll();
    }

    public void Unbind()
    {
        if (chunk?.Terrain != null) chunk.Terrain.Changed -= HandleChanged;
        if (owner != null)
        {
            owner.BatchPresentationRebuilt -= RefreshAll;
            ChunkBatchRendererGroupService.ClearOwnerLayer(owner, ChunkBatchRendererGroupService.VisualLayer.Support);
        }
        chunk = null;
        owner = null;
    }

    private void OnDestroy() => Unbind();

    #endregion

    #region 本体绘制

    private void HandleChanged(ChunkTerrainChanged change)
    {
        if (change.Kind != TerrainChangeKind.Cell && !change.AffectsEnvironmentLayer(TerrainSupportLayer.TileIdLayer) &&
            !change.AffectsEnvironmentLayer(TerrainSupportLayer.ExternalVisualLayer) &&
            !change.AffectsEnvironmentLayer(TerrainSurfaceHeightLayer.CoverLevelsLayerId)) return;
        int width = chunk.Terrain.Width;
        foreach (int index in change.GetCellIndices(width)) Refresh(index % width, index / width);
    }

    private void RefreshAll()
    {
        if (chunk?.Terrain == null) return;
        ChunkBatchRendererGroupService.BeginBulkSubmit();
        try
        {
            for (int y = 0; y < chunk.Terrain.Height; y++)
            for (int x = 0; x < chunk.Terrain.Width; x++)
                Refresh(x, y);
        }
        finally { ChunkBatchRendererGroupService.EndBulkSubmit(); }
    }

    private void Refresh(int x, int y)
    {
        int id = TerrainSupportLayer.GetExposedTileId(chunk.Terrain, x, y);
        bool hasEntityVisual = chunk.Terrain.TryGetEnvironmentValue(TerrainSupportLayer.ExternalVisualLayer,
            x, y, out float externalVisual) && externalVisual != 0f;
        if (id == 0 || hasEntityVisual || !palette.TryGetVisual(id, out Sprite sprite, out Color color, out Matrix4x4 transform))
        {
            owner.ClearLayerVisual(ChunkBatchRendererGroupService.VisualLayer.Support, x, y);
            return;
        }
        Int2 origin = chunk.Address.ChunkOrigin;
        Matrix4x4 matrix = Matrix4x4.Translate(new Vector3(origin.X + x + .5f, origin.Y + y + .5f)) * transform;
        owner.SetLayerVisual(ChunkBatchRendererGroupService.VisualLayer.Support, x, y,
            sprite, material, matrix, color);
    }

    #endregion
}
