using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

/// <summary>培育器只声明配置，真实种植、库存与供水由机器领域持有。</summary>
public sealed class Mod_Cultivator : Mod_MachineAuthoring, IModuleJsonParameterValidator
{
    #region 机器配置
    public const string ModuleId = "培育器模块";
    public override string CanonicalModuleId => ModuleId;
    public Ex_ModData_MemoryPackable ModData = new() { ID = ModuleId };
    public override ModuleData _Data { get => ModData; set => ModData = (Ex_ModData_MemoryPackable)value; }
    public CultivatorConfiguration Configuration = new();
    public void ValidateJsonParameters(JObject parameters)
        => (parameters[nameof(Configuration)]?.ToObject<CultivatorConfiguration>() ?? Configuration).Validate();
    #endregion
}

[Serializable]
public sealed class CultivatorConfiguration
{
    #region 多槽培育条件
    public int PlotCount = 4;
    public int HarvestSlotCount = 9;
    public string PanelId = "UI_Cultivator";
    public decimal WaterLitersPerSecondPerPlot = .002m;
    public string[] WaterFluidIds = { "core:pure_water", "core:filtered_water", "core:drinkable_water" };
    public string[] FertilizerItemIds = { "Fertilizer", "Humus" };
    public float FertilizerSecondsPerItem = 600;
    public float FertilizerGrowthMultiplier = 1.5f;
    public void Validate()
    {
        if (PlotCount < 1 || PlotCount > 16 || HarvestSlotCount < 1 || HarvestSlotCount > 64 || string.IsNullOrWhiteSpace(PanelId) ||
            WaterLitersPerSecondPerPlot <= 0m || WaterLitersPerSecondPerPlot > 100m ||
            WaterFluidIds == null || WaterFluidIds.Length == 0 || FertilizerItemIds == null ||
            !MachineDefinition.Positive(FertilizerSecondsPerItem) || !float.IsFinite(FertilizerGrowthMultiplier) ||
            FertilizerGrowthMultiplier < 1f || FertilizerGrowthMultiplier > 100f)
            throw new InvalidOperationException("培育器槽位、供水或肥料配置无效。");
        var waterIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in WaterFluidIds)
            if (string.IsNullOrWhiteSpace(id) || !waterIds.Add(id)) throw new InvalidOperationException("培育器灌溉水身份不能为空或重复。");
        var fertilizerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in FertilizerItemIds)
            if (string.IsNullOrWhiteSpace(id) || !fertilizerIds.Add(id)) throw new InvalidOperationException("培育器肥料身份不能为空或重复。");
    }
    #endregion
}
