using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.Tilemaps;

public sealed class ChunkCollisionRenderer : MonoBehaviour, IChunkViewRenderer
{
    [SerializeField] private TilemapCollider2D tilemapCollider;
    [SerializeField] private CompositeCollider2D compositeCollider;

    public void Bind(ChunkRuntime chunk)
    {
        if (chunk == null)
            throw new System.ArgumentNullException(nameof(chunk));
        if (tilemapCollider != null)
            tilemapCollider.enabled = true;
        if (compositeCollider != null)
            compositeCollider.enabled = true;
    }

    public void Unbind()
    {
        if (tilemapCollider != null)
            tilemapCollider.enabled = false;
        if (compositeCollider != null)
            compositeCollider.enabled = false;
    }
}
