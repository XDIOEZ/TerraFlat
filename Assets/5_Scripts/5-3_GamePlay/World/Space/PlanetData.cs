public partial class PlanetData
{
    #region 星体静态布局与地表数据
    public string name;
    public string BodyId;
    public float SelfRotateSpeed = 20f;
    // 生成预设与参数覆盖属于世界实例，不参与星球归属或大气选择。
    public string GenerationPresetId;
    public System.Collections.Generic.Dictionary<string, double> GenerationNumericOverrides = new(System.StringComparer.Ordinal);
    public System.Collections.Generic.Dictionary<string, string> GenerationTextOverrides = new(System.StringComparer.Ordinal);
    public double PhysicalRadiusMeters = 2000d;
    public double SurfaceGravityMetersPerSecondSquared = 9.81d;

    public string RuntimePlanetName
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
