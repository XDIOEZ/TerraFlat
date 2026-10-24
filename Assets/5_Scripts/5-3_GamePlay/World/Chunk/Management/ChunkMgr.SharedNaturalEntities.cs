using FlatWorld.WorldModel;

public partial class ChunkMgr
{
    #region 跨区块资源会话
    private SharedNaturalEntityRegistry sharedNaturalEntities;
    private WorldRuntime sharedNaturalWorld;

    internal SharedNaturalEntityRegistry SharedNaturalEntities
    {
        get
        {
            if (sharedNaturalEntities == null || !ReferenceEquals(sharedNaturalWorld, WorldRuntime))
            {
                ReleaseSharedNaturalEntities();
                sharedNaturalWorld = WorldRuntime;
                sharedNaturalEntities = new SharedNaturalEntityRegistry(this);
            }
            return sharedNaturalEntities;
        }
    }

    private void ReleaseSharedNaturalEntities()
    {
        sharedNaturalEntities?.Dispose();
        sharedNaturalEntities = null;
        sharedNaturalWorld = null;
    }
    #endregion
}
