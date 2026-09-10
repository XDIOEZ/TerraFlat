using FlatWorld.WorldModel;

public interface IChunkViewRenderer
{
    void Bind(ChunkRuntime chunk);
    void Unbind();
}

/// <summary>需要读取相邻区块数据的表现器，通过统一契约接收当前世界。</summary>
public interface IWorldAwareChunkViewRenderer
{
    void SetWorld(WorldRuntime worldRuntime);
}
