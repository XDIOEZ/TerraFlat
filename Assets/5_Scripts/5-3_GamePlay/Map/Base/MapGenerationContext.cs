using System;

/// <summary>
/// 地图生成上下文：在一个“生成管线”中共享，后续生成器可以基于前面生成器写入的数据继续加工。
/// </summary>
public sealed class MapGenerationContext
{
    #region 只读数据
    public Map Map { get; }
    public PlanetData PlanetData { get; }
    public int WorldSeed { get; }
    public StructureGenerationMask StructureMask { get; }
    #endregion

    #region 构造
    public MapGenerationContext(Map map, PlanetData planetData, int worldSeed)
    {
        Map = map;
        PlanetData = planetData;
        WorldSeed = worldSeed == 0 ? 1 : worldSeed;
        int width = map?.Data?.Width ?? 0;
        int height = map?.Data?.Height ?? 0;
        StructureMask = new StructureGenerationMask(width, height);
    }
    #endregion
}
