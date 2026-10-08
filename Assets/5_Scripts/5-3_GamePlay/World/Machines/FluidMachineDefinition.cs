using System;
using System.Collections.Generic;

/// <summary>工业流体设备只声明能力和预算，气液共用同一库存与运输入口。</summary>
[Serializable]
public sealed class FluidMachineDefinition
{
    #region 设备与端口配置
    public string Kind = "pipe";
    public string MaterialId = "iron";
    public double VolumeLiters = 1;
    public double MinimumGasSpaceLiters = .001;
    public double MaxSafePressureKPa = 500;
    public double ThroughputLitersPerSecond = 5;
    public double BatchStandardLiters = 1;
    public double GasBufferStandardLiters = 10;
    public double BatchLiquidServings = 1;
    public double MaximumPumpPressureKPa = 2000;
    public double PumpBatchesPerSecond = 1;
    public double WorkJoulesPerStandardLiter = 5;
    public double CompressionEfficiency = .7;
    public double HeatConductanceWattsPerKelvin = 20;
    public double MaximumPhaseMolesPerSecond = 10;
    public double OverpressureDamagePerSecond = 5;
    public double DefaultDisconnectPressureKPa = 450;
    public double DefaultReconnectPressureKPa = 400;
    public double OxygenServingsPerFuelUnit = .02;
    public double OxygenTemperatureBonus = 300;
    public bool SingleGas = true;
    public bool SingleLiquid = true;
    public bool CombineAdjacent;
    public bool AllowPortableTank;
    public FluidMachinePortDefinition[] Ports = Array.Empty<FluidMachinePortDefinition>();
    public FluidMachineReactionDefinition[] Reactions = Array.Empty<FluidMachineReactionDefinition>();
    public string FilterMaterialItemId = "Cloth";
    public double FilterWorkPerItem = 100;
    public string PanelId = "UI_IndustrialFluid";

    public void Validate(string ownerId)
    {
        if (string.IsNullOrWhiteSpace(Kind) || string.IsNullOrWhiteSpace(MaterialId) ||
            !Positive(VolumeLiters) || !Positive(MinimumGasSpaceLiters) || MinimumGasSpaceLiters >= VolumeLiters ||
            !Positive(MaxSafePressureKPa) || !Positive(ThroughputLitersPerSecond) ||
            !Positive(BatchStandardLiters) || !Positive(GasBufferStandardLiters) || !Positive(BatchLiquidServings) ||
            !Positive(MaximumPumpPressureKPa) || !Positive(PumpBatchesPerSecond) ||
            !Positive(WorkJoulesPerStandardLiter) || !Positive(CompressionEfficiency) || CompressionEfficiency > 1 ||
            !Positive(HeatConductanceWattsPerKelvin) || !Positive(MaximumPhaseMolesPerSecond) ||
            !Positive(OverpressureDamagePerSecond) || !Positive(DefaultDisconnectPressureKPa) ||
            DefaultReconnectPressureKPa < 0 || DefaultReconnectPressureKPa >= DefaultDisconnectPressureKPa ||
            !Positive(FilterWorkPerItem) || Ports == null || Reactions == null)
            throw new ArgumentException("工业流体设备配置无效：" + ownerId);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (FluidMachinePortDefinition port in Ports)
        {
            port?.Validate(ownerId);
            if (port == null || !ids.Add(port.Id)) throw new ArgumentException("工业端口身份重复：" + ownerId);
        }
        foreach (FluidMachineReactionDefinition reaction in Reactions) reaction.Validate(ownerId);
        if (CombineAdjacent && (SingleGas || SingleLiquid))
            throw new ArgumentException("组合储罐必须允许真实混合库存：" + ownerId);
    }
    internal static bool Positive(double value) => double.IsFinite(value) && value > 0;
    #endregion
}

[Serializable]
public sealed class FluidMachinePortDefinition
{
    #region 类型化世界端口
    public string Id;
    public string Chamber = "main";
    public int Direction;
    public string Mode = "both";
    public string Phase = "both";
    public string FluidId = "";
    public bool Outlet;
    public void Validate(string ownerId)
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Chamber) || Direction < 0 || Direction > 3 ||
            (Mode != "input" && Mode != "output" && Mode != "both") ||
            (Phase != "gas" && Phase != "liquid" && Phase != "both") ||
            Outlet && Mode == "input") throw new ArgumentException("工业端口配置无效：" + ownerId);
    }
    #endregion
}

/// <summary>反应物及产物以 mol 系数描述，热量和实际供能采用同一反应预算。</summary>
[Serializable]
public sealed class FluidMachineReactionDefinition
{
    #region 守恒加工配方
    public string Id;
    public FluidReactionTerm[] Inputs = Array.Empty<FluidReactionTerm>();
    public FluidReactionTerm[] Outputs = Array.Empty<FluidReactionTerm>();
    public double MolesPerSecond = 1;
    public double RequiredJoulesPerReaction;
    public double ChemicalJoulesPerReaction;
    public double StoredChemicalJoulesPerReaction;
    public double MechanicalEfficiency = .4;
    public string SolidResidueItemId = "";
    public double ResidueItemsPerReaction;
    public bool RequiresFilter;
    public void Validate(string ownerId)
    {
        if (string.IsNullOrWhiteSpace(Id) || Inputs == null || Inputs.Length == 0 || Outputs == null || Outputs.Length == 0 ||
            !FluidMachineDefinition.Positive(MolesPerSecond) || !double.IsFinite(RequiredJoulesPerReaction) || RequiredJoulesPerReaction < 0 ||
            !double.IsFinite(ChemicalJoulesPerReaction) || ChemicalJoulesPerReaction < 0 ||
            !double.IsFinite(StoredChemicalJoulesPerReaction) || StoredChemicalJoulesPerReaction < 0 || StoredChemicalJoulesPerReaction > RequiredJoulesPerReaction ||
            !FluidMachineDefinition.Positive(MechanicalEfficiency) || MechanicalEfficiency >= 1 ||
            !double.IsFinite(ResidueItemsPerReaction) || ResidueItemsPerReaction < 0)
            throw new ArgumentException("工业反应配方配置无效：" + ownerId);
        foreach (FluidReactionTerm term in Inputs) term.Validate(ownerId);
        foreach (FluidReactionTerm term in Outputs) term.Validate(ownerId);
    }
    #endregion
}

[Serializable]
public sealed class FluidReactionTerm
{
    #region 摩尔比例
    public string Chamber = "main";
    public string FluidId;
    public string Phase = "liquid";
    public double Moles = 1;
    public void Validate(string ownerId)
    {
        if (string.IsNullOrWhiteSpace(Chamber) || string.IsNullOrWhiteSpace(FluidId) ||
            (Phase != "gas" && Phase != "liquid") || !FluidMachineDefinition.Positive(Moles))
            throw new ArgumentException("工业反应物配置无效：" + ownerId);
    }
    #endregion
}
