using System;
using System.Collections.Generic;
using MemoryPack;

/// <summary>每台工业设备保存自己的物料、容器槽和操作设置。</summary>
[Serializable, MemoryPackable]
public partial class FluidMachineState
{
    #region 权威持久状态
    public Dictionary<string, FluidInventoryState> Chambers = new(StringComparer.Ordinal);
    public Dictionary<string, string> BlockedPorts = new(StringComparer.Ordinal);
    public double PumpProgress;
    public double WorldLiquidPumpLiters; // 地表泵先累计十升路径吞吐额度，成批取水前不扣地表物料。
    public Dictionary<string, double> WorldLiquidOutputLiters = new(StringComparer.Ordinal); // 地表出口按端口保存已攒的吞吐预算。
    public string SelectedGasId = "";
    public string OutletPhase = "both";
    public bool ValveOpen = true;
    public double OutputLitersPerSecond = -1;
    public ulong RandomState = 1;
    public bool Ruptured;
    public string Status = "待机";
    public decimal LastEmittedStandardLiters;
    public decimal LastRetainedStandardLiters;
    public decimal LastEscapedStandardLiters;
    public Inventory_Data PortableTank = MachineInventory.NewData("容器", 3);
    public FurnaceRuntimeState SolidFuel;
    public Inventory_Data FuelInventory;
    public int LastFlowDirection = -1;
    public string LastFlowPhase = "";
    #endregion
}
