using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>新版 ChunkView 的碰撞表现生命周期；发布绑定变化，由可选物理适配层消费。</summary>
public sealed class ChunkCollisionRenderer : MonoBehaviour, IChunkViewRenderer
{
    [SerializeField] private TilemapCollider2D tilemapCollider;
    [SerializeField] private CompositeCollider2D compositeCollider;

    public ChunkRuntime BoundChunk { get; private set; }
    public TilemapCollider2D SourceCollider => tilemapCollider;
    internal static event System.Action<ChunkCollisionRenderer> PresentationChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPresentationEvents() => PresentationChanged = null;

    public void Bind(ChunkRuntime chunk)
    {
        if (chunk == null)
            throw new System.ArgumentNullException(nameof(chunk));
        Unbind();
        BoundChunk = chunk;
        if (chunk.Terrain != null)
            chunk.Terrain.Changed += HandleTerrainChanged;
        if (tilemapCollider != null)
            tilemapCollider.enabled = true;
        if (compositeCollider != null)
            compositeCollider.enabled = true;
        PresentationChanged?.Invoke(this);
    }

    public void Unbind()
    {
        if (BoundChunk?.Terrain != null)
            BoundChunk.Terrain.Changed -= HandleTerrainChanged;
        BoundChunk = null;
        if (tilemapCollider != null)
            tilemapCollider.enabled = false;
        if (compositeCollider != null)
            compositeCollider.enabled = false;
        PresentationChanged?.Invoke(this);
    }

    private void HandleTerrainChanged(ChunkTerrainChanged changed)
    {
        if (changed.Kind == TerrainChangeKind.Cell || changed.Kind == TerrainChangeKind.TileStack)
            PresentationChanged?.Invoke(this);
    }

    private void OnEnable() => PresentationChanged?.Invoke(this);
    private void OnDisable() => PresentationChanged?.Invoke(this);
    private void OnDestroy() => Unbind();
}
