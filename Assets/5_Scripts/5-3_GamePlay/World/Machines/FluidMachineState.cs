using System;
using System.Collections.Generic;
using MemoryPack;

/// <summary>工业设备保存库存和操作状态；罐体独立记录温度，流体温度与压力仍由库存内能推导。</summary>
[Serializable, MemoryPackable]
public partial class FluidMachineState
{
    // 只有组主保存气网载荷，成员本地 Chambers 仍只拥有自己的物质。
    public long GasNetworkEpoch;
    public FluidGasNetworkSnapshot GasNetwork;
    #region 权威持久状态
    public Dictionary<string, FluidInventoryState> Chambers = new(StringComparer.Ordinal);
    public Dictionary<string, string> BlockedPorts = new(StringComparer.Ordinal);
    public Dictionary<string, string> PhaseWarnings = new(StringComparer.Ordinal);
    public double PumpProgress;
    public double PumpStoredWorkJoules;
    public double WorldLiquidPumpLiters; // 地表抽液累计真实吞吐和做功预算，攒够一份才抽一级。
    public Dictionary<string, double> WorldLiquidOutputLiters = new(StringComparer.Ordinal); // 地表出口按端口保存已攒的吞吐预算。
    public double DissipatedHeatJoules;
    public double FilterRemainingWork;
    public Dictionary<string, double> ResidueProgress = new(StringComparer.Ordinal);
    public string SelectedGasId = "";
    public string OutletPhase = "both";
    public bool ValveOpen = true;
    public bool ProbeConnected = true;
    public double DisconnectPressureKPa = -1;
    public double ReconnectPressureKPa = -1;
    public double CompressionTargetKPa = 500;
    public ulong RandomState = 1;
    public int GroupOwnerId;
    public int OverpressureVictimId;
    public bool Ruptured;
    public string Status = "待机";
    public decimal LastEmittedStandardLiters;
    public decimal LastRetainedStandardLiters;
    public decimal LastEscapedStandardLiters;
    public Inventory_Data PortableTank = MachineInventory.NewData("气罐", 1);
    public Inventory_Data FilterMaterial = MachineInventory.NewData("滤材", 1);
    public Inventory_Data Residue = MachineInventory.NewData("残渣", 1);
    public string RuptureBudgetId = "";
    public int LastFlowDirection = -1;
    public string LastFlowPhase = "";
    public bool BodyTemperatureInitialized;
    public float BodyTemperatureCelsius;
    #endregion
}

/// <summary>同组破裂时锁定每块气相空间和强度份额，只有真正归零的成员才领取自己的爆炸。</summary>
[Serializable, MemoryPackable]
public partial class FluidTankRuptureBudget
{
    #region 有限破裂预算
    public string BatchId;
    public double PressureKPa;
    public string WorldKey;
    public Dictionary<int, double> Intensities = new();
    public HashSet<int> Claimed = new();
    #endregion
}
