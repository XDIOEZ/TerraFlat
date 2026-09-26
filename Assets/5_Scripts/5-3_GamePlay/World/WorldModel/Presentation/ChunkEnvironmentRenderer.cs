using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>环境阈值覆盖层直接向区块 BRG Owner 提交单格 Sprite，保持原有 0.22 透明度。</summary>
public sealed class ChunkEnvironmentRenderer : MonoBehaviour, IChunkViewRenderer
{
    #region 配置与绑定

    [SerializeField] private string environmentLayerId = "grass";
    [SerializeField] private float visibleThreshold = 0.5f;
    [SerializeField] private Tile tile;
    [SerializeField] private Material material;
    [SerializeField] private Color tint = new(1f, 1f, 1f, 0.22f);
    private ChunkRuntime boundChunk;
    private ChunkTilemapRenderer owner;

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
        owner.BatchPresentationRebuilt += HandleBatchPresentationRebuilt;
        Render(chunk.Terrain);
    }

    public void Unbind()
    {
        if (boundChunk?.Terrain != null)
            boundChunk.Terrain.Changed -= HandleTerrainChanged;
        if (owner != null)
        {
            owner.BatchPresentationRebuilt -= HandleBatchPresentationRebuilt;
            if (boundChunk?.Terrain != null && owner.IsBatchPresentationRegistered)
                for (int y = 0; y < boundChunk.Terrain.Height; y++)
                for (int x = 0; x < boundChunk.Terrain.Width; x++)
                    owner.ClearLayerVisual(ChunkBatchRendererGroupService.VisualLayer.Environment, x, y);
        }
        boundChunk = null;
    }

    private void HandleTerrainChanged(ChunkTerrainChanged changed)
    {
        if (changed.Kind != TerrainChangeKind.Environment || boundChunk?.Terrain == null ||
            tile == null || material == null)
            return;
        bool visible = boundChunk.Terrain.TryGetEnvironmentValue(environmentLayerId,
                           changed.LocalCell.X, changed.LocalCell.Y, out float value) &&
                       value >= visibleThreshold;
        RefreshCell(changed.LocalCell.X, changed.LocalCell.Y, visible);
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
        terrain.TryCopyEnvironmentLayer(environmentLayerId, out float[] values);
        for (int y = 0; y < terrain.Height; y++)
        for (int x = 0; x < terrain.Width; x++)
            RefreshCell(x, y, values != null && values[y * terrain.Width + x] >= visibleThreshold);
    }

    /// <summary>单格环境覆盖的 BRG 提交与清除。</summary>
    private void RefreshCell(int x, int y, bool visible)
    {
        if (!visible)
        {
            owner.ClearLayerVisual(ChunkBatchRendererGroupService.VisualLayer.Environment, x, y);
            return;
        }
        Int2 origin = boundChunk.Address.ChunkOrigin;
        Matrix4x4 matrix = Matrix4x4.Translate(new Vector3(origin.X + x + 0.5f, origin.Y + y + 0.5f));
        owner.SetLayerVisual(ChunkBatchRendererGroupService.VisualLayer.Environment, x, y,
            tile.sprite, material, matrix * tile.transform, tile.color * tint);
    }

    #endregion
}
