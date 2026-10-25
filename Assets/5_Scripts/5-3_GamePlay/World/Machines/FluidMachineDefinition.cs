using System;
using System.Collections.Generic;

/// <summary>工业设备声明端口、物质与动力能力；气液各自结算，设备腔体负责两相事务边界。</summary>
[MemoryPack.MemoryPackable]
[Serializable]
public sealed partial class FluidMachineDefinition
{
    #region 设备与端口配置
    public const string LiquidResiduePortId = "liquid-residue";
    public string Kind = "pipe";
    public bool PipeSegment; // 设备本身作为管段接入连线拓扑与管道表现。
    public string MaterialId = "iron";
    public double VolumeLiters = 1;
    public double MinimumGasSpaceLiters = .001;
    public double ThroughputLitersPerSecond = 5;
    public double BatchStandardLiters = 1;
    public double GasCapacityStandardLiters = 100;
    public double NaturalFillFraction = .2;
    public bool PullFromNetwork; // 既有消费者通过正式管路补充自己的工作库存。
    public double BatchLiquidServings = 1;
    public bool AllowPortableTank;
    public bool AllowManualAirFill; // 自然补气按固定容量占比扣取真实环境组分。
    public bool CombineAdjacent; // 相邻同定义罐体共享库存和总容量，管道仍不持有库存。
    public float SolidFuelCapacity; // 显式启用三槽燃料炉，零容量设备不持有固体燃烧能力。
    public float SolidFuelAbsorptionSeconds = 3f;
    public FluidMachinePortDefinition[] Ports = Array.Empty<FluidMachinePortDefinition>();
    public FluidMachineReactionDefinition[] Reactions = Array.Empty<FluidMachineReactionDefinition>();
    public string LiquidResidueFluidId = ""; // 液体副产物保留在独立腔内，仅通过容器端口取液。
    public string LiquidResidueChamber = "byproduct";
    public string PanelId = "UI_IndustrialFluid";
    public float? BodySizeCells; // 方块设备可声明满格机身，普通接头沿用原尺寸。

    public float ResolveBodySizeCells(int layer)
        => BodySizeCells ?? (layer == 2 ? .55f : .85f);

    public void Validate(string ownerId)
    {
        if (string.IsNullOrWhiteSpace(Kind) || string.IsNullOrWhiteSpace(MaterialId) ||
            !Positive(VolumeLiters) || !Positive(MinimumGasSpaceLiters) || MinimumGasSpaceLiters >= VolumeLiters ||
            !double.IsFinite(ThroughputLitersPerSecond) || ThroughputLitersPerSecond < 0 ||
            !Positive(BatchStandardLiters) || !Positive(GasCapacityStandardLiters) || !Positive(BatchLiquidServings) ||
            !float.IsFinite(SolidFuelCapacity) || SolidFuelCapacity < 0 || !Positive(SolidFuelAbsorptionSeconds) ||
            !double.IsFinite(NaturalFillFraction) || NaturalFillFraction < 0 || NaturalFillFraction > 1 ||
            (BodySizeCells.HasValue && !Positive(BodySizeCells.Value)) || Ports == null || Reactions == null)
            throw new ArgumentException("工业流体设备配置无效：" + ownerId);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (FluidMachinePortDefinition port in Ports)
        {
            port?.Validate(ownerId);
            if (port == null || !ids.Add(port.Id)) throw new ArgumentException("工业端口身份重复：" + ownerId);
        }
        if (PipeSegment && Ports.Length == 0) throw new ArgumentException("管段必须声明至少一个管道端口：" + ownerId);
        foreach (FluidMachineReactionDefinition reaction in Reactions) reaction.Validate(ownerId);
        if (!string.IsNullOrEmpty(LiquidResidueFluidId) && string.IsNullOrWhiteSpace(LiquidResidueChamber))
            throw new ArgumentException("液体副产物缺少库存腔身份：" + ownerId);
        if (!string.IsNullOrEmpty(LiquidResidueFluidId) && ids.Contains(LiquidResiduePortId))
            throw new ArgumentException("工业管道口与液体副产物取液口身份冲突：" + ownerId);
    }
    internal static bool Positive(double value) => double.IsFinite(value) && value > 0;
    #endregion
}

[MemoryPack.MemoryPackable]
[Serializable]
public sealed partial class FluidMachinePortDefinition
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

/// <summary>动力设备用明确的物料比例和单位功量结算实际消耗。</summary>
[MemoryPack.MemoryPackable]
[Serializable]
public sealed partial class FluidMachineReactionDefinition
{
    #region 守恒加工配方
    public string Id;
    public FluidReactionTerm[] Inputs = Array.Empty<FluidReactionTerm>();
    public FluidReactionTerm[] Outputs = Array.Empty<FluidReactionTerm>();
    public double MolesPerSecond = 1;
    public double ChemicalJoulesPerReaction;
    public double MechanicalEfficiency = .4;
    public void Validate(string ownerId)
    {
        if (string.IsNullOrWhiteSpace(Id) || Inputs == null || Inputs.Length == 0 || Outputs == null ||
            !FluidMachineDefinition.Positive(MolesPerSecond) ||
            !double.IsFinite(ChemicalJoulesPerReaction) || ChemicalJoulesPerReaction < 0 ||
            !FluidMachineDefinition.Positive(MechanicalEfficiency) || MechanicalEfficiency >= 1)
            throw new ArgumentException("工业反应配方配置无效：" + ownerId);
        foreach (FluidReactionTerm term in Inputs) term.Validate(ownerId);
        foreach (FluidReactionTerm term in Outputs) term.Validate(ownerId);
    }
    #endregion
}

[MemoryPack.MemoryPackable]
[Serializable]
public sealed partial class FluidReactionTerm
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
