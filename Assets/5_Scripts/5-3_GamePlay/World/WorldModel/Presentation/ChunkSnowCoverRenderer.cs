using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>只读积雪覆盖层；不替换原 Tile，水面不被伪装成陆地，天然雪地也不随季节消失。</summary>
public sealed class ChunkSnowCoverRenderer : MonoBehaviour, IChunkViewRenderer
{
    [SerializeField] private Tilemap tilemap; // 独立覆盖层。
    [SerializeField] private Tilemap wallTilemap; // 地块墙顶部的独立窄雪冠。
    [SerializeField] private TileBase snowTile; // 可透明叠加的地表外观。
    private ChunkRuntime chunk; // 当前区块。
    private float nextRefresh; // 一秒一批，不逐帧重绘。
    private byte[] visibleCoverage; // 只提交发生变化的格。
    private byte[] visibleWallCoverage; // 墙体出现和拆除也触发刷新。
    /// <summary>绑定后立即恢复已保存的覆盖状态。</summary>
    public void Bind(ChunkRuntime value)
    {
        Unbind(); chunk = value;
        visibleCoverage = new byte[value.Terrain.Width * value.Terrain.Height];
        visibleWallCoverage = new byte[visibleCoverage.Length];
        Refresh();
        nextRefresh = Time.unscaledTime + 0.5f + (unchecked((uint)value.Address.ChunkOrigin.X) % 10) * 0.05f;
    }
    /// <summary>回池时清空纯表现，世界中的积雪状态继续保留。</summary>
    public void Unbind()
    {
        chunk = null; visibleCoverage = null; visibleWallCoverage = null;
        if (tilemap != null) tilemap.ClearAllTiles();
        if (wallTilemap != null) wallTilemap.ClearAllTiles();
    }
    /// <summary>低频读取世界积雪，不注册逐格对象。</summary>
    private void Update()
    {
        if (chunk == null || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 1f;
        Refresh();
    }
    /// <summary>按当前有效支撑面遮罩绘制雪，平台移除后下方水面不留悬空白块。</summary>
    private void Refresh()
    {
        WeatherMgr weather = WeatherMgr.Instance;
        for (int y = 0; y < chunk.Terrain.Height; y++)
        for (int x = 0; x < chunk.Terrain.Width; x++)
        {
            TerrainCell surface = TerrainSupportLayer.GetSurfaceCell(chunk.Terrain, x, y);
            Vector3 world = new(chunk.Address.ChunkOrigin.X + x + 0.5f, chunk.Address.ChunkOrigin.Y + y + 0.5f);
            float coverage = (surface.Flags & TerrainCellFlags.Water) != 0 || surface.GroundTileId == 0
                ? 0f : weather.GetSnowCoverage(world);
            byte value = (byte)Mathf.RoundToInt(coverage * 100f);
            int index = y * chunk.Terrain.Width + x;
            Vector3Int cell = new(x, y, 0);
            byte wallValue = surface.BlockingTileId != 0 ? value : (byte)0;
            if (visibleWallCoverage[index] != wallValue)
            {
                visibleWallCoverage[index] = wallValue;
                wallTilemap.SetTile(cell, wallValue > 0 ? snowTile : null);
                if (wallValue > 0)
                {
                    wallTilemap.SetTileFlags(cell, TileFlags.None);
                    wallTilemap.SetTransformMatrix(cell, Matrix4x4.TRS(new Vector3(0f, 0.38f), Quaternion.identity, new Vector3(1f, 0.24f, 1f)));
                    wallTilemap.SetColor(cell, new Color(1f, 1f, 1f, wallValue / 100f));
                }
            }
            if (visibleCoverage[index] == value) continue;
            visibleCoverage[index] = value;
            tilemap.SetTile(cell, value > 0 ? snowTile : null);
            if (value == 0) continue;
            tilemap.SetTileFlags(cell, TileFlags.None);
            tilemap.SetColor(cell, new Color(1f, 1f, 1f, value / 100f));
        }
    }
}
