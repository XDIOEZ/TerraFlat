public partial class PlanetData
{
    #region 星体静态布局与地表数据
    public string name;
    public string BodyId;
    public string PrefabName;
    public float SelfRotateSpeed = 20f;
    public string SurfaceGenerationProfileId;
    public double PhysicalRadiusMeters = 2000d;
    public double SurfaceGravityMetersPerSecondSquared = 9.81d;

    public string RuntimePlanetName
    {
        get
        {
            if (!string.IsNullOrEmpty(Name)) return Name;
            if (!string.IsNullOrEmpty(name)) return name;
            return PrefabName;
        }
    }
    #endregion
}
