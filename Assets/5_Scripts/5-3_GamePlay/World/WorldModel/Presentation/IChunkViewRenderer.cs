using System.Collections;
using FlatWorld.WorldModel;

public interface IChunkViewRenderer
{
    void Bind(ChunkRuntime chunk);
    void Unbind();
}

/// <summary>单次绑定可能创建大量对象的表现器按步骤提交；每次 MoveNext 只执行一份有界工作。</summary>
public interface IIncrementalChunkViewRenderer : IChunkViewRenderer
{
    IEnumerator BindIncremental(ChunkRuntime chunk);
}

/// <summary>需要读取相邻区块数据的表现器，通过统一契约接收当前世界。</summary>
public interface IWorldAwareChunkViewRenderer
{
    void SetWorld(WorldRuntime worldRuntime);
}
