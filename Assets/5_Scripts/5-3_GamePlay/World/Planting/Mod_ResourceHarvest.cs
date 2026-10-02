using System;
using UnityEngine;

/// <summary>资源工具类别；只表达擅长的采集方式，不再作为伤害资格门槛。</summary>
public enum ResourceToolKind { None, Pickaxe, Shovel, Axe }

/// <summary>工具声明的采集能力；等级和效率只提供资源伤害加成。</summary>
public interface IResourceHarvestTool
{
    ResourceToolKind HarvestKind { get; }
    int HarvestTier { get; }
    float HarvestEfficiency { get; }
}

/// <summary>资源节点的采集专精。所有有效攻击都可伤害资源，匹配工具只获得额外效率。</summary>
public sealed class Mod_ResourceHarvest : Module, IIncomingDamageRule, IIncomingDamageContextRule
{
    #region 配置与生命周期
    public Ex_ModData ModData = new(); // 无独立运行状态。
    public ResourceToolKind requiredTool = ResourceToolKind.Pickaxe; // 最适合的工具类别；旧字段名保留给现有内容配置。
    [Min(1)] public int minimumTier = 1; // 获得完整专精加成的推荐等级。
    public override string CanonicalModuleId => "Mod_ResourceHarvest";
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = value as Ex_ModData ?? throw new ArgumentException("资源开采模块数据类型错误。");
    }

    /// <summary>校验资源专精配置，避免把无类别配置误当成有效加成。</summary>
    public override void Load()
    {
        if (requiredTool == ResourceToolKind.None || minimumTier < 1)
            throw new InvalidOperationException("资源节点必须配置专精工具类别和正等级。");
    }

    /// <summary>工具要求属于静态定义，无需写入资源存档。</summary>
    public override void Save() { }
    #endregion

    /// <summary>普通武器保持原始伤害；匹配工具按等级逐步获得采集效率加成。</summary>
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

    /// <summary>专精倍率永远不低于 1；硬度差异继续由伤害类型与目标防御自然结算。</summary>
    public static float ResolveAffinityMultiplier(ResourceToolKind preferredTool, int fullEfficiencyTier,
        ResourceToolKind toolKind, int toolTier, float toolEfficiency)
    {
        if (preferredTool == ResourceToolKind.None || toolKind != preferredTool)
            return 1f;
        if (float.IsNaN(toolEfficiency) || float.IsInfinity(toolEfficiency))
            return float.NaN;

        float tierFactor = fullEfficiencyTier <= 1
            ? 1f
            : Mathf.Clamp01((float)Mathf.Max(0, toolTier) / fullEfficiencyTier);
        return Mathf.Lerp(1f, Mathf.Max(1f, toolEfficiency), tierFactor);
    }
}
