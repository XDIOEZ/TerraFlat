/// <summary>铲子通过共用采挖能力处理地表，工具类别独立于伤害模块。</summary>
public sealed class Mod_Shovel : Mod_GroundHarvestToolBase
{
    #region 工具身份
    public const string ModuleId = "Mod_Shovel";
    public override string CanonicalModuleId => ModuleId;
    public override ResourceToolKind HarvestKind => ResourceToolKind.Shovel;
    #endregion
}
