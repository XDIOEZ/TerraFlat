using FlatWorld.WorldModel;

public interface IChunkViewRenderer
{
    void Bind(ChunkRuntime chunk);
    void Unbind();
}
