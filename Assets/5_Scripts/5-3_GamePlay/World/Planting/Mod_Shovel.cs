/// <summary>铲子通过共用采挖能力处理地表，工具类别独立于伤害模块。</summary>
public sealed class Mod_Shovel : Mod_GroundHarvestToolBase
{
    #region 工具身份
    public const string ModuleId = "Mod_Shovel";
    public override string CanonicalModuleId => ModuleId;
    public override ResourceToolKind HarvestKind => ResourceToolKind.Shovel;
    #endregion

    #region 积雪采集
    protected override bool TryResolveOverlayTarget(out RuntimeTerrainTileSample sample) =>
        WorldSnowInteraction.TryResolveTarget(item, 2f, out sample, out _) &&
        WorldSnowInteraction.GetLayerCount(sample) > 0;

    protected override bool TryWorkOverlay(out UnityEngine.Vector2Int cell, out float interval, out string reason)
    {
        interval = 0.35f;
        return WorldSnowInteraction.TryShovel(item, 2f, out cell, out reason);
    }
    #endregion
}
