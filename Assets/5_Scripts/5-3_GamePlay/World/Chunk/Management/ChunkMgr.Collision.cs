/// <summary>把静态世界碰撞的脏区刷新集中到一个物理 Tick，避免每个 Chunk 各自轮询。</summary>
public partial class ChunkMgr
{
    private void FixedUpdate()
    {
        if (ExistingInstance != this) return;
        ChunkCollisionRenderer.FlushDirtyDataObstacles();
    }
}
