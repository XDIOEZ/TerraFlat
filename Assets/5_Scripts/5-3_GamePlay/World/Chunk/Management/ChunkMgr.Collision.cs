/// <summary>把静态世界碰撞的脏区刷新集中到一个物理 Tick，避免每个 Chunk 各自轮询。</summary>
public partial class ChunkMgr
{
    #region 世界碰撞脏队列

    private static readonly Unity.Profiling.ProfilerMarker CollisionFixedStepMarker = new("FlatWorld.ChunkPhysics.FixedStep");

    private void FixedUpdate()
    {
        if (IsWorldRuntimeShuttingDown) return;
        string worldKey = !string.IsNullOrEmpty(OwnedWorldKey) ? OwnedWorldKey : WorldRuntime?.WorldId;
        if (!ChunkCollisionRenderer.HasDirtyDataObstacles(worldKey)) return;
        // 空队列不进入世界作用域，真实工作仍在所属世界内完成并计入释放开销。
        using var marker = CollisionFixedStepMarker.Auto();
        if (!TryUseRuntimeWorldContext(out var scope)) return;
        using (scope) ChunkCollisionRenderer.FlushDirtyDataObstacles(worldKey);
    }

    #endregion
}
