using System;
using UnityEngine;

/// <summary>资源工具类别；只表达擅长的采集方式，不再作为伤害资格门槛。</summary>
public enum ResourceToolKind { None, Pickaxe, Shovel, Axe, Hammer }

/// <summary>工具声明的采集身份；旧等级和效率字段保留配置兼容。</summary>
public interface IResourceHarvestTool
{
    ResourceToolKind HarvestKind { get; }
    int HarvestTier { get; }
    float HarvestEfficiency { get; }
}

/// <summary>资源节点的工具弱点；全部攻击按物理防御结算，匹配工具在防御后乘二。</summary>
public sealed class Mod_ResourceHarvest : Module, IIncomingDamageRule, IIncomingDamageContextRule
{
    #region 配置与生命周期
    public Ex_ModData ModData = new(); // 无独立运行状态。
    public ResourceToolKind requiredTool = ResourceToolKind.Pickaxe; // 最适合的工具类别；旧字段名保留给现有内容配置。
    [Min(1)] public int minimumTier = 1; // 旧配置兼容，不参与弱点倍率。
    public override string CanonicalModuleId => "Mod_ResourceHarvest";
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = value as Ex_ModData ?? throw new ArgumentException("资源开采模块数据类型错误。");
    }

    /// <summary>校验资源专精配置，避免把无类别配置误当成有效加成。</summary>
    protected override void OnLoad()
    {
        if (requiredTool == ResourceToolKind.None || minimumTier < 1)
            throw new InvalidOperationException("资源节点必须配置专精工具类别和正等级。");
    }

    /// <summary>工具要求属于静态定义，无需写入资源存档。</summary>
    protected override void OnSave() { }
    #endregion

    #region 工具弱点结算

    /// <summary>普通武器倍率为一，匹配工具固定为两倍。</summary>
    public float GetDamageMultiplier(IDamageSender sender)
    {
        IResourceHarvestTool tool = ResolveTool(sender);
        if (tool == null)
            return 1f;

        return ResolveAffinityMultiplier(requiredTool, minimumTier,
            tool.HarvestKind, tool.HarvestTier, tool.HarvestEfficiency);
    }

    /// <summary>优先从物品组合模块读取工具能力，伤害发送器只负责投送伤害。</summary>
    private static IResourceHarvestTool ResolveTool(IDamageSender sender)
    {
        Item attacker = sender?.attacker;
        if (attacker?.itemMods != null)
        {
            IResourceHarvestTool resolved = null;
            int count = 0;
            foreach (Module module in attacker.itemMods.Mods.Values)
            {
                if (module is not IResourceHarvestTool candidate)
                    continue;
                resolved = candidate;
                count++;
            }

            if (count > 1)
                throw new InvalidOperationException($"物品 {attacker.name} 同时声明了多个资源工具能力。");
            if (resolved != null)
                return resolved;
        }

        return sender as IResourceHarvestTool;
    }

    /// <summary>纯数据后端使用相同的软专精规则，不再拒绝非匹配武器。</summary>
    public float GetDamageMultiplier(in FlatWorld.Combat.CombatDamageContext context)
    {
        if (context.IsTrueDamage != 0)
            return 1f;

        return ResolveAffinityMultiplier(requiredTool, minimumTier,
            (ResourceToolKind)context.ResourceToolKind, context.ResourceToolTier, context.ResourceToolEfficiency);
    }

    /// <summary>匹配弱点固定在防御后乘二；旧等级和效率字段仅保留内容兼容。</summary>
    public static float ResolveAffinityMultiplier(ResourceToolKind preferredTool, int fullEfficiencyTier,
        ResourceToolKind toolKind, int toolTier, float toolEfficiency)
    {
        if (preferredTool == ResourceToolKind.None || toolKind != preferredTool)
            return 1f;
        return 2f;
    }

    #endregion
}
