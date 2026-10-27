using FlatWorld.WorldModel;

public partial class ChunkMgr
{
    #region 世界持有的独立自然实体
    private IndependentNaturalEntityRegistry independentNaturalEntities;
    private WorldRuntime independentNaturalWorld;

    internal IndependentNaturalEntityRegistry IndependentNaturalEntities
    {
        get
        {
            if (independentNaturalEntities == null || !ReferenceEquals(independentNaturalWorld, WorldRuntime))
            {
                ReleaseIndependentNaturalEntities();
                independentNaturalWorld = WorldRuntime;
                independentNaturalEntities = new IndependentNaturalEntityRegistry(this);
            }
            return independentNaturalEntities;
        }
    }

    private void ReleaseIndependentNaturalEntities()
    {
        independentNaturalEntities?.Dispose();
        independentNaturalEntities = null;
        independentNaturalWorld = null;
    }
    #endregion
}
