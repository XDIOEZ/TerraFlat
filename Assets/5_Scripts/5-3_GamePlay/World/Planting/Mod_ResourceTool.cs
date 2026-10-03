using System;
using FlatWorld.Combat;
using UnityEngine;

/// <summary>资源工具能力基类；工具等级与效率独立于伤害发送模块。</summary>
public abstract class Mod_ResourceToolBase : Module, IResourceHarvestTool, ICombatDamageContextModifier
{
    #region 配置

    public Ex_ModData ModData = new();
    [SerializeField, Min(1)] private int harvestTier = 1;
    [SerializeField, Min(0.01f)] private float harvestEfficiency = 1f;

    public abstract ResourceToolKind HarvestKind { get; }
    public int HarvestTier => harvestTier;
    public float HarvestEfficiency => harvestEfficiency;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = value as Ex_ModData ?? throw new ArgumentException("资源工具模块数据类型错误。");
    }

    #endregion

    #region 生命周期与战斗上下文

    public override void Awake()
    {
        ModData.ID = CanonicalModuleId;
        base.Awake();
    }

    public override void Load() => ValidateConfiguration();
    public override void Save() { }
    public override void OnResourcesReloaded() => ValidateConfiguration();

    /// <summary>资源工具只向伤害上下文附加采集能力，不参与伤害碰撞和结算。</summary>
    public void ModifyDamageContext(ref CombatDamageContext context)
    {
        context.ResourceToolKind = (int)HarvestKind;
        context.ResourceToolTier = HarvestTier;
        context.ResourceToolEfficiency = HarvestEfficiency;
    }

    private void ValidateConfiguration()
    {
        if (HarvestKind == ResourceToolKind.None || harvestTier < 1 ||
            harvestEfficiency <= 0f || float.IsNaN(harvestEfficiency) || float.IsInfinity(harvestEfficiency))
        {
            throw new InvalidOperationException($"{CanonicalModuleId} 必须配置有效工具类别、等级和效率。");
        }
    }

    #endregion
}

/// <summary>普通资源工具提供采集加成；镐类额外复用右键地表采挖能力。</summary>
public sealed class Mod_ResourceTool : Mod_GroundHarvestToolBase
{
    #region 工具身份
    public const string ModuleId = "Mod_ResourceTool";
    [SerializeField] private ResourceToolKind toolKind = ResourceToolKind.Pickaxe;
    public override string CanonicalModuleId => ModuleId;
    public override ResourceToolKind HarvestKind => toolKind;
    protected override bool SupportsGroundHarvest => HarvestKind == ResourceToolKind.Pickaxe;
    #endregion
}
