public partial class PlanetData
{
    #region 星体静态布局与地表数据
    [MemoryPack.MemoryPackOrder(33)] public string name;
    [MemoryPack.MemoryPackOrder(3)] public string BodyId;
    [MemoryPack.MemoryPackOrder(20)] public float SelfRotateSpeed = 20f;
    // 生成预设与参数覆盖属于世界实例，不参与星球归属或大气选择。
    [MemoryPack.MemoryPackOrder(9)] public string GenerationPresetId;
    [MemoryPack.MemoryPackOrder(8)] public System.Collections.Generic.Dictionary<string, double> GenerationNumericOverrides = new(System.StringComparer.Ordinal);
    [MemoryPack.MemoryPackOrder(10)] public System.Collections.Generic.Dictionary<string, string> GenerationTextOverrides = new(System.StringComparer.Ordinal);
    [MemoryPack.MemoryPackOrder(15)] public double PhysicalRadiusMeters = 2000d;
    [MemoryPack.MemoryPackOrder(23)] public double SurfaceGravityMetersPerSecondSquared = 9.81d;

    [MemoryPack.MemoryPackIgnore] public string RuntimePlanetName
    {
        get
        {
            if (!string.IsNullOrEmpty(Name)) return Name;
            if (!string.IsNullOrEmpty(name)) return name;
            return BodyId;
        }
    }
    #endregion
}
