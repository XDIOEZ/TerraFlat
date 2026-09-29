using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>把权威阻挡格映射成 Chunk 的唯一静态物理碰撞体。</summary>
public sealed class ChunkCollisionRenderer : MonoBehaviour, IChunkViewRenderer
{
    #region 碰撞映射

    [SerializeField] private TilemapCollider2D tilemapCollider;
    [SerializeField] private CompositeCollider2D compositeCollider;
    private Tilemap collisionTilemap;
    private static Tile collisionTile;

    public ChunkRuntime BoundChunk { get; private set; }
    public TilemapCollider2D SourceTilemapCollider => tilemapCollider;
    public Collider2D SourceCollider => compositeCollider != null ? compositeCollider : tilemapCollider;
    internal static event System.Action<ChunkCollisionRenderer> PresentationChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPresentationEvents() => PresentationChanged = null;

    private void Awake()
    {
        collisionTilemap = tilemapCollider != null ? tilemapCollider.GetComponent<Tilemap>() : null;
        if (collisionTilemap == null || compositeCollider == null ||
            tilemapCollider.attachedRigidbody == null ||
            tilemapCollider.attachedRigidbody.bodyType != RigidbodyType2D.Static ||
            !tilemapCollider.usedByComposite)
        {
            throw new System.InvalidOperationException("Chunk Collision Tilemap 必须装配静态 Rigidbody2D、TilemapCollider2D 和 CompositeCollider2D。");
        }
    }

    private static Tile CollisionTile
    {
        get
        {
            if (collisionTile != null) return collisionTile;
            // 碰撞形状只由权威阻挡标记决定，不依赖美术 Tile 的 Sprite 物理轮廓。
            collisionTile = ScriptableObject.CreateInstance<Tile>();
            collisionTile.hideFlags = HideFlags.HideAndDontSave;
            collisionTile.colliderType = Tile.ColliderType.Grid;
            return collisionTile;
        }
    }

    public void Bind(ChunkRuntime chunk)
    {
        if (chunk == null)
            throw new System.ArgumentNullException(nameof(chunk));
        Unbind();
        BoundChunk = chunk;
        if (chunk.Terrain != null)
            chunk.Terrain.Changed += HandleTerrainChanged;
        tilemapCollider.enabled = true;
        SyncAll(chunk.Terrain);
        PresentationChanged?.Invoke(this);
    }

    public void Unbind()
    {
        if (BoundChunk?.Terrain != null)
            BoundChunk.Terrain.Changed -= HandleTerrainChanged;
        BoundChunk = null;
        if (collisionTilemap != null)
        {
            collisionTilemap.ClearAllTiles();
            FlushGeometry();
        }
        if (tilemapCollider != null)
            tilemapCollider.enabled = false;
        PresentationChanged?.Invoke(this);
    }

    private void HandleTerrainChanged(ChunkTerrainChanged changed)
    {
        if (BoundChunk?.Terrain == null ||
            (changed.Kind != TerrainChangeKind.Cell && changed.Kind != TerrainChangeKind.TileStack))
            return;
        int x = changed.LocalCell.X;
        int y = changed.LocalCell.Y;
        if (x < 0 || x >= BoundChunk.Terrain.Width || y < 0 || y >= BoundChunk.Terrain.Height)
            return;
        Vector3Int position = new(x, y, 0);
        TileBase tile = GetCollisionTile(BoundChunk.Terrain.GetCell(x, y));
        if (collisionTilemap.GetTile(position) == tile) return;
        collisionTilemap.SetTile(position, tile);
        FlushGeometry();
        PresentationChanged?.Invoke(this);
    }

    private void SyncAll(ChunkTerrainData terrain)
    {
        if (terrain == null) return;
        var tiles = new TileBase[terrain.CellCount];
        for (int y = 0; y < terrain.Height; y++)
        for (int x = 0; x < terrain.Width; x++)
            tiles[y * terrain.Width + x] = GetCollisionTile(terrain.GetCell(x, y));
        collisionTilemap.SetTilesBlock(new BoundsInt(0, 0, 0, terrain.Width, terrain.Height, 1), tiles);
        FlushGeometry();
    }

    private static TileBase GetCollisionTile(TerrainCell cell) =>
        cell.BlockingTileId != 0 && (cell.Flags & TerrainCellFlags.Blocking) != 0
            ? CollisionTile
            : null;

    private void FlushGeometry()
    {
        if (tilemapCollider != null && tilemapCollider.hasTilemapChanges)
            tilemapCollider.ProcessTilemapChanges();
        if (compositeCollider != null &&
            compositeCollider.generationType == CompositeCollider2D.GenerationType.Manual)
            compositeCollider.GenerateGeometry();
    }

    private void OnEnable() => PresentationChanged?.Invoke(this);
    private void OnDisable() => PresentationChanged?.Invoke(this);
    private void OnDestroy() => Unbind();

    #endregion
}
