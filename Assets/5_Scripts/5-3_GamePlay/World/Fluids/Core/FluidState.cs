using System;
using System.Collections.Generic;
using MemoryPack;

public enum FluidPhase : byte { Gas = 0, Liquid = 1 }

/// <summary>唯一物质量真值；温度、压力和比例都从这里推导。</summary>
[Serializable, MemoryPackable]
public partial class FluidComponentState
{
    #region 两相物质量
    public string FluidId;
    public decimal GasMoles;
    public decimal LiquidMoles;
    public FluidComponentState Clone() => new() { FluidId = FluidId, GasMoles = GasMoles, LiquidMoles = LiquidMoles };
    #endregion
}

/// <summary>每个出口独占预留的单种批次，预留内容仍属于原库存。</summary>
[Serializable, MemoryPackable]
public partial class FluidOutputReservation
{
    #region 输出预留
    public string PortId;
    public string FluidId;
    public decimal GasMoles;
    public decimal LiquidMoles;
    [MemoryPackIgnore] public decimal TotalMoles => GasMoles + LiquidMoles;
    public FluidOutputReservation Clone() => new()
    { PortId = PortId, FluidId = FluidId, GasMoles = GasMoles, LiquidMoles = LiquidMoles };
    #endregion
}

/// <summary>管段、气罐和设备腔共用的可保存状态，不保存冲突的派生读数。</summary>
[Serializable, MemoryPackable]
public partial class FluidInventoryState
{
    #region 流体持久化
    public List<FluidComponentState> Components = new();
    public double TotalInternalEnergyJoules;
    public ulong RandomState = 0x9E3779B97F4A7C15UL;
    public List<FluidOutputReservation> Ports = new();
    public FluidInventoryState Clone()
    {
        var result = new FluidInventoryState
        { TotalInternalEnergyJoules = TotalInternalEnergyJoules, RandomState = RandomState };
        foreach (FluidComponentState component in Components) result.Components.Add(component.Clone());
        foreach (FluidOutputReservation port in Ports) result.Ports.Add(port.Clone());
        return result;
    }
    #endregion
}

/// <summary>实际转移的单种物质及其两相内能，离开来源后不再引用原库存。</summary>
public readonly struct FluidBatch
{
    #region 真实转移批次
    public FluidBatch(string fluidId, decimal gasMoles, decimal liquidMoles, double internalEnergyJoules)
    { FluidId = fluidId; GasMoles = gasMoles; LiquidMoles = liquidMoles; InternalEnergyJoules = internalEnergyJoules; }
    public string FluidId { get; }
    public decimal GasMoles { get; }
    public decimal LiquidMoles { get; }
    public double InternalEnergyJoules { get; }
    public decimal TotalMoles => GasMoles + LiquidMoles;
    public bool IsEmpty => TotalMoles <= 0m;
    public bool IsGas => GasMoles > 0m && LiquidMoles == 0m;
    public bool IsLiquid => LiquidMoles > 0m && GasMoles == 0m;
    public decimal Moles => TotalMoles;
    public double InternalEnergy => InternalEnergyJoules;
    public FluidBatch Scale(decimal fraction) => new(FluidId, GasMoles * fraction, LiquidMoles * fraction,
        InternalEnergyJoules * (double)fraction);
    #endregion
}

/// <summary>随机状态随实例保存，打开界面和失败重试不会产生新的抽签。</summary>
public static class FluidRandom
{
    #region 确定性抽签
    public static decimal Next01(ref ulong state)
    {
        if (state == 0) state = 0x9E3779B97F4A7C15UL;
        state ^= state >> 12; state ^= state << 25; state ^= state >> 27;
        ulong value = unchecked(state * 2685821657736338717UL);
        return (decimal)(value >> 11) / 9007199254740992m;
    }
    #endregion
}
