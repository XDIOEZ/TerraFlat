/// <summary>把静态世界碰撞的脏区刷新集中到一个物理 Tick，避免每个 Chunk 各自轮询。</summary>
public partial class ChunkMgr
{
    private void FixedUpdate()
    {
        if (IsWorldRuntimeShuttingDown) return;
        using var scope = !string.IsNullOrEmpty(OwnedWorldKey) && GameplayWorldRegistry.TryGet(OwnedWorldKey, out _)
            ? GameplayWorldRegistry.UseWorld(OwnedWorldKey) : null;
        if (ExistingInstance != this) return;
        ChunkCollisionRenderer.FlushDirtyDataObstacles();
    }
}
