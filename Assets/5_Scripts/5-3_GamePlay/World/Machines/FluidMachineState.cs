using System;
using System.Collections.Generic;
using MemoryPack;

/// <summary>每台工业设备保存自己的物料、容器槽和操作设置。</summary>
[Serializable, MemoryPackable(GenerateType.VersionTolerant)]
public partial class FluidMachineState
{
    #region 权威持久状态
    [MemoryPackOrder(0)] public Dictionary<string, FluidInventoryState> Chambers = new(StringComparer.Ordinal);
    [MemoryPackOrder(1)] public Dictionary<string, string> BlockedPorts = new(StringComparer.Ordinal);
    [MemoryPackOrder(2)] public double PumpProgress;
    [MemoryPackOrder(3)] public double WorldLiquidPumpLiters; // 地表泵先累计十升路径吞吐额度，成批取水前不扣地表物料。
    [MemoryPackOrder(4)] public Dictionary<string, double> WorldLiquidOutputLiters = new(StringComparer.Ordinal); // 地表出口按端口保存已攒的吞吐预算。
    [MemoryPackOrder(5)] public string SelectedGasId = "";
    [MemoryPackOrder(6)] public string OutletPhase = "both";
    [MemoryPackOrder(7)] public bool ValveOpen = true;
    [MemoryPackOrder(8)] public double OutputLitersPerSecond = -1;
    [MemoryPackOrder(9)] public ulong RandomState = 1;
    [MemoryPackOrder(10)] public bool Ruptured;
    [MemoryPackOrder(11)] public string Status = "待机";
    [MemoryPackIgnore] public decimal LastEmittedStandardLiters;
    [MemoryPackIgnore] public decimal LastRetainedStandardLiters;
    [MemoryPackIgnore] public decimal LastEscapedStandardLiters;
    [MemoryPackInclude, MemoryPackOrder(12)] private string EmittedAmount { get => PersistentDecimalText.Format(LastEmittedStandardLiters); set => LastEmittedStandardLiters = PersistentDecimalText.Parse(value); }
    [MemoryPackInclude, MemoryPackOrder(13)] private string RetainedAmount { get => PersistentDecimalText.Format(LastRetainedStandardLiters); set => LastRetainedStandardLiters = PersistentDecimalText.Parse(value); }
    [MemoryPackInclude, MemoryPackOrder(14)] private string EscapedAmount { get => PersistentDecimalText.Format(LastEscapedStandardLiters); set => LastEscapedStandardLiters = PersistentDecimalText.Parse(value); }
    [MemoryPackIgnore] public Inventory_Data PortableTank = MachineInventory.NewData("容器", 3);
    [MemoryPackInclude, MemoryPackOrder(15)] private StandaloneInventorySaveSnapshot PortableTankSnapshot
    { get => StandaloneInventorySaveSnapshot.Capture(PortableTank); set => PortableTank = value?.Restore(); }
    [MemoryPackOrder(16)] public FurnaceRuntimeState SolidFuel;
    [MemoryPackIgnore] public Inventory_Data FuelInventory;
    [MemoryPackInclude, MemoryPackOrder(17)] private StandaloneInventorySaveSnapshot FuelInventorySnapshot
    { get => StandaloneInventorySaveSnapshot.Capture(FuelInventory); set => FuelInventory = value?.Restore(); }
    [MemoryPackOrder(18)] public int LastFlowDirection = -1;
    [MemoryPackOrder(19)] public string LastFlowPhase = "";
    #endregion
}
