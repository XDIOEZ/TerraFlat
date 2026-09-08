using System;
using UnityEngine;

/// <summary>资源工具类别；采矿和挖掘不以武器伤害大小替代工具资格。</summary>
public enum ResourceToolKind { None, Pickaxe, Shovel, Axe }

/// <summary>工具声明的采集能力；等级控制可采资源，效率独立调整采集速度。</summary>
public interface IResourceHarvestTool
{
    ResourceToolKind HarvestKind { get; }
    int HarvestTier { get; }
    float HarvestEfficiency { get; }
}

/// <summary>资源节点的开采门槛。复用生命与掉落系统，未达到工具种类或等级时不产生伤害。</summary>
public sealed class Mod_ResourceHarvest : Module, IIncomingDamageRule
{
    #region 配置与生命周期
    public Ex_ModData ModData = new(); // 无独立运行状态。
    public ResourceToolKind requiredTool = ResourceToolKind.Pickaxe; // 允许工具。
    [Min(1)] public int minimumTier = 1; // 最低等级。
    private float nextFeedbackTime; // 错误工具提示冷却。
    public override string CanonicalModuleId => "Mod_ResourceHarvest";
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = value as Ex_ModData ?? throw new ArgumentException("资源开采模块数据类型错误。");
    }

    /// <summary>校验工具门槛，禁止无工具资源误配置成可开采节点。</summary>
    public override void Load()
    {
        nextFeedbackTime = 0f;
        if (requiredTool == ResourceToolKind.None || minimumTier < 1)
            throw new InvalidOperationException("资源节点必须配置工具类别和正等级。");
    }

    /// <summary>工具要求属于静态定义，无需写入资源存档。</summary>
    public override void Save() { }
    #endregion

    /// <summary>只接受达到门槛的工具，并使用其独立开采效率。</summary>
    public float GetDamageMultiplier(IDamageSender sender)
    {
        if (sender is IResourceHarvestTool tool && tool.HarvestKind == requiredTool && tool.HarvestTier >= minimumTier)
            return tool.HarvestEfficiency;
        if (Time.time >= nextFeedbackTime)
        {
            nextFeedbackTime = Time.time + 2f;
            Item actor = sender?.attacker?.Owner ?? sender?.attacker;
            string reason = requiredTool == ResourceToolKind.Shovel ? "需要使用铲子挖掘。" :
                sender is IResourceHarvestTool equipped && equipped.HarvestKind == requiredTool
                    ? "矿层太硬，需要更高级的镐。" : "需要使用镐开采。";
            ItemActionFeedback.Show(actor, reason);
        }
        return 0f;
    }
}
