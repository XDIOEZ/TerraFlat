using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>支撑面独立 Tilemap 表现；只读取权威层，不遮改水深、岸线或存档。</summary>
public sealed class ChunkSupportSurfaceRenderer : MonoBehaviour, IChunkViewRenderer
{
    [SerializeField] private ChunkTilePaletteSO palette; // 材料到地块的映射。
    [SerializeField] private Tilemap tilemap; // 水面上方的平台图层。
    private ChunkRuntime chunk; // 当前绑定区块。

    /// <summary>绑定区块并从持久化支撑面重建表现。</summary>
    public void Bind(ChunkRuntime value)
    {
        Unbind();
        chunk = value;
        chunk.Terrain.Changed += HandleChanged;
        for (int y = 0; y < chunk.Terrain.Height; y++)
            for (int x = 0; x < chunk.Terrain.Width; x++)
                Refresh(x, y);
    }

    /// <summary>取消订阅并清除回池区块的旧平台。</summary>
    public void Unbind()
    {
        if (chunk != null)
            chunk.Terrain.Changed -= HandleChanged;
        chunk = null;
        if (tilemap != null)
            tilemap.ClearAllTiles();
    }

    /// <summary>仅更新发生变化的支撑格。</summary>
    private void HandleChanged(ChunkTerrainChanged change)
    {
        if (change.Kind == TerrainChangeKind.Environment)
            Refresh(change.LocalCell.X, change.LocalCell.Y);
    }

    /// <summary>按稳定地块 ID 选择平台外观。</summary>
    private void Refresh(int x, int y)
    {
        int id = TerrainSupportLayer.GetTileId(chunk.Terrain, x, y);
        palette.TryGetTile(id, out TileBase tile);
        tilemap.SetTile(new Vector3Int(x, y, 0), tile);
    }
}
