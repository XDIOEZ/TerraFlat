using System;
using System.Collections.Generic;
using System.Text;
using FlatWorld.Localization;
using UnityEngine;

/// <summary>所有工业设备沿同一机器世界调度，领域逻辑只消费真实输入、动力与输出余量。</summary>
public sealed class FluidMachineLogic : MachineLogic, IContainerPortProvider
{
    #region 设备状态与生命周期
    public FluidMachineState State { get; private set; }
    public FluidMachineDefinition Definition => Entity.Definition.Fluid;
    public FluidDeviceBehavior Behavior => MachineWorld.GetFluidDeviceBehavior(Entity);
    private Inventory portableTank;
    public Inventory FuelInventory { get; private set; }
    public MachineSolidFuelBurner SolidFuelBurner { get; private set; }
    public FuelData Fuel => SolidFuelBurner?.Fuel;
    public float FuelRatio => Fuel?.Fuel.y > 0 ? Mathf.Clamp01(Fuel.Fuel.x / Fuel.Fuel.y) : 0;
    public override bool IsBurning => !State.Ruptured && SolidFuelBurner?.IsBurning == true;
    public override bool CanAct => !State.Ruptured && (SolidFuelBurner?.CanAct ?? base.CanAct);
    public float GetFuelAbsorptionProgress(int slotIndex) => SolidFuelBurner?.Progress(slotIndex) ?? 0;
    private readonly IDisposable combustionSourceLease;
    public Inventory PortableTank { get { EnsureDeviceInventories(); return portableTank; } }
    private long deviceRegistryGeneration = -1;
    private readonly List<IContainerPort> containerPorts = new();
    private IndustrialLiquidTransferPort liquidResiduePort;
    private long portRegistryGeneration = -1;
    private readonly List<(string Chamber, FluidInventoryState State, long Revision)> tickContents = new();
    public ILiquidTransferPort LiquidResiduePort { get { EnsureContainerPorts(); return liquidResiduePort; } }
    public override float TickInterval => .1f;
    public override float Progress01 => (float)Math.Clamp(State.PumpProgress, 0, 1);
    public override string Status => Describe();
    public override string ActionLabel => SolidFuelBurner != null ? SolidFuelBurner.IsIgnited ? "熄火" : "点火" :
        string.IsNullOrEmpty(Definition.LiquidResidueFluidId) ? Behavior.ActionLabel(Entity) : "装入手持容器";
    public override GameObject PanelPrefab => GameRes.Instance.GetPrefab(Definition.PanelId);

    public FluidMachineLogic(MachineEntity entity) : base(entity)
    {
        State = MachineWorld.GetFluidState(entity);
        EnsureDeviceInventories();
        combustionSourceLease = MachineWorld.RegisterCombustionSupportSource(entity,
            () => Behavior as ICombustionSupportSource ?? (Behavior.EmitsToEnvironment ? FluidOutletCombustionSupport.Instance : null));
    }
    private void EnsureDeviceInventories()
    {
        if (deviceRegistryGeneration == MachineLogicRegistry.FluidDeviceGeneration) return;
        deviceRegistryGeneration = MachineLogicRegistry.FluidDeviceGeneration;
        if (Definition.SolidFuelCapacity > 0 && FuelInventory == null)
        {
            State.SolidFuel ??= new FurnaceRuntimeState { Fuel = new FuelData { Fuel = Vector2.zero } };
            FuelInventory = Track(MachineInventory.Create(null, State.FuelInventory, "燃料", 3));
            FuelInventory.Data.SetItemAcceptancePolicy((item, slot) => item?.Tags?.ContainsTag(Tag.CombustionFuel) == true);
            State.FuelInventory = FuelInventory.Data;
            SolidFuelBurner = new MachineSolidFuelBurner(Entity, FuelInventory, State.SolidFuel);
        }
        if (Definition.AllowPortableTank && portableTank == null)
        {
            portableTank = Track(MachineInventory.Create(null, State.PortableTank, "装填容器", 3));
            foreach (ItemSlot slot in portableTank.Data.itemSlots) slot.SlotMaxVolume = 1;
            // 接收规则在权威库存执行，三个槽位共用真实容器能力校验。
            portableTank.Data.SetItemAcceptancePolicy((item, slot) => Definition.Kind == "liquid-filler"
                ? Mod_WaterVessel.TryRead(item, out _, out _)
                : FluidTankStorage.TryGet(item, out _, out _));
            State.PortableTank = portableTank.Data;
        }

    }
    public override void Dispose()
    { combustionSourceLease?.Dispose(); base.Dispose(); }
    public override void Capture() => MachineWorld.CaptureFluidState(Entity);
    public override bool ApplyRemoteSnapshot(ItemData snapshot)
    {
        FluidMachineState incoming = MachinePersistence.Read<FluidMachineState>(snapshot, "fluid");
        if (incoming == null) return false;
        if (FuelInventory != null && (incoming.SolidFuel == null || incoming.FuelInventory == null)) return false;
        if (PortableTank != null) { MachineInventory.ApplySnapshot(PortableTank, incoming.PortableTank); incoming.PortableTank = PortableTank.Data; }
        if (FuelInventory != null)
        {
            MachineInventory.ApplySnapshot(FuelInventory, incoming.FuelInventory);
            incoming.FuelInventory = FuelInventory.Data;
            SolidFuelBurner.ApplyState(incoming.SolidFuel);
        }
        State = incoming; NotifyRemoteChanged(); return true;
    }

    public override void Tick(float seconds)
    {
        EnsureDeviceInventories();
        if (!MachineWorld.IsFluidActive(Entity) || State.Ruptured) return;
        if (SolidFuelBurner != null)
        {
            var fuelBefore = (Fuel.Fuel.x, State.SolidFuel.Smelting.IsSmelting, State.SolidFuel.AwaitingIgnition,
                State.SolidFuel.Absorption.SlotIndex, State.SolidFuel.Absorption.ElapsedSeconds);
            SolidFuelBurner.Tick(seconds);
            var fuelAfter = (Fuel.Fuel.x, State.SolidFuel.Smelting.IsSmelting, State.SolidFuel.AwaitingIgnition,
                State.SolidFuel.Absorption.SlotIndex, State.SolidFuel.Absorption.ElapsedSeconds);
            if (fuelBefore != fuelAfter) NotifyChanged();
        }
        FluidDeviceBehavior behavior = Behavior;
        // 内置设备没有实际变化就不推送整台状态，自定义策略仍保留原来的显式调度通知。
        if (!behavior.UsesObservedTickState) { behavior.Advance(Entity, seconds); NotifyChanged(); return; }
        var before = (State.Status, State.PumpProgress, State.WorldLiquidPumpLiters, State.RandomState);
        tickContents.Clear();
        foreach (string chamber in State.Chambers.Keys)
        {
            FluidInventoryState contents = MachineWorld.GetFluidInventory(Entity, chamber).State;
            tickContents.Add((chamber, contents, contents.StateRevision));
        }
        behavior.Advance(Entity, seconds);
        var after = (State.Status, State.PumpProgress, State.WorldLiquidPumpLiters, State.RandomState);
        bool changed = before != after || tickContents.Count != State.Chambers.Count;
        if (!changed)
            foreach (var previous in tickContents)
            {
                if (!State.Chambers.ContainsKey(previous.Chamber)) { changed = true; break; }
                FluidInventoryState current = MachineWorld.GetFluidInventory(Entity, previous.Chamber).State;
                if (!ReferenceEquals(current, previous.State) || current.StateRevision != previous.Revision) { changed = true; break; }
            }
        if (changed) NotifyChanged();
    }
    public void CollectContainerPorts(List<IContainerPort> ports)
    {
        EnsureDeviceInventories();
        EnsureContainerPorts();
        ports.AddRange(containerPorts);
    }
    private void EnsureContainerPorts()
    {
        if (portRegistryGeneration != ContainerPortFactoryRegistry.Generation)
        {
            containerPorts.Clear(); portRegistryGeneration = ContainerPortFactoryRegistry.Generation;
            liquidResiduePort = null;
            if (Behavior.IsStorage)
                foreach (FluidMachinePortDefinition port in Definition.Ports)
                    if (port.Phase != "gas") containerPorts.Add(new IndustrialLiquidTransferPort(this, port));
            // 取液口只供手动容器事务使用，不参与世界管道或相邻泵拓扑。
            if (!string.IsNullOrEmpty(Definition.LiquidResidueFluidId))
            {
                liquidResiduePort = new IndustrialLiquidTransferPort(this, new FluidMachinePortDefinition
                {
                    Id = FluidMachineDefinition.LiquidResiduePortId, Chamber = Definition.LiquidResidueChamber,
                    Mode = "output", Phase = "liquid", FluidId = Definition.LiquidResidueFluidId
                }, access: ContainerAccessKind.Manual | ContainerAccessKind.Mod);
                containerPorts.Add(liquidResiduePort);
            }
        }
    }
    #endregion

    #region 副产物容器取液
    public bool CanTransferLiquidResidueTo(ILiquidVessel target, Player actor)
    {
        if (actor == null || target == null || !target.CanOperate(actor)) return false;
        return ContainerTransferService.PreviewLiquid(LiquidResiduePort,
            LiquidVesselOperations.Port(target, ContainerPortDirection.Input),
            new ContainerTransferContext(actor, ContainerAccessKind.Manual, "liquid-residue"), Mod_WaterVessel.AmountStep).Success;
    }
    public bool TransferLiquidResidueTo(ILiquidVessel target, Player actor)
    {
        if (actor == null || target == null || LiquidResiduePort == null ||
            !ContainerTransferCommands.TryAddress(actor, target, ContainerPortDirection.Input, out var to)) return false;
        return ContainerTransferCommands.Request(new ContainerTransferIntent
        {
            Operation = "liquid", Amount = Mod_WaterVessel.AmountStep, Target = to,
            Source = new ContainerPortAddress
            {
                MachineId = Entity.Id, ItemGuid = Entity.Snapshot.Guid, ItemId = Entity.Snapshot.IDName,
                PortId = FluidMachineDefinition.LiquidResiduePortId
            }
        }, actor);
    }
    #endregion

    #region 权威配置操作
    public override bool Execute(string operation, string argument, Player actor)
    {
        if (operation == "begin-interaction") return true;
        if (operation == "work" && SolidFuelBurner != null)
        {
            if (State.Ruptured) return false;
            if (!SolidFuelBurner.Toggle(actor)) return false;
            NotifyChanged(true);
            return true;
        }
        if (operation == "fluid.rotate")
        {
            if (MachineWorld.IsFluidTankBlock(Entity) || State.Ruptured || Entity.State == null) return false;
            Entity.State.RotationQuarterTurns = (Entity.State.RotationQuarterTurns + 1) & 3;
            MachineWorld.TopologyChanged(Entity); return true;
        }
        else if (!Behavior.Execute(Entity, operation, argument)) return false;
        NotifyChanged(); return true;
    }
    #endregion

    #region 面板实时读数
    private string Describe()
    {
        var text = new StringBuilder();
        text.Append(FlatWorldLocalizationService.GetUiText(State.Status));
        text.Append("\n").Append(FlatWorldLocalizationService.GetUiFormat("血量 {0:0.#}/{1:0.#}", Entity.State?.Hp ?? MachineWorld.ReadMachineState(Entity.Snapshot).Hp, MachineWorld.ResolveMaximumHp(Entity.Snapshot)));
        foreach (string chamber in State.Chambers.Keys)
        {
            DescribeInventory(text, chamber);
        }
        if (State.Chambers.Count == 0) DescribeInventory(text, "main");
        Behavior.AppendDescription(Entity, text);
        if (State.LastEmittedStandardLiters > 0) text.Append("\n").Append(FlatWorldLocalizationService.GetUiFormat("排出 {0:0.###} 标准 L · 留存 {1:0.###} · 逸散 {2:0.###}", State.LastEmittedStandardLiters, State.LastRetainedStandardLiters, State.LastEscapedStandardLiters));
        foreach (var blocked in State.BlockedPorts) text.Append("\n").Append(blocked.Key).Append("：").Append(FlatWorldLocalizationService.GetUiText(blocked.Value));
        return text.ToString();
    }
    private void DescribeInventory(StringBuilder text, string chamber)
    {
        if (Definition.Kind is "pipe" or "valve" or "selector" or "outlet" or "filler" or "liquid-filler") return;
        FluidInventory inventory = MachineWorld.GetFluidInventory(Entity, chamber);
        bool gas = Array.Exists(Definition.Ports, port => port.Phase == "gas");
        text.Append("\n").Append(gas
            ? FlatWorldLocalizationService.GetUiFormat("气量 {0:0.###}/{1:0.###} 标准 L", FluidUnits.MolToStandardLiters(inventory.GasMoles), MachineWorld.GetFluidGasCapacityStandardLiters(Entity))
            : FlatWorldLocalizationService.GetUiFormat("液体 {0:0.###}/{1:0.###} L", inventory.GetLiquidLiters(), MachineWorld.GetFluidVolumeLiters(Entity)));
        foreach (FluidComponentState component in inventory.State.Components)
        {
            FluidDefinition substance = inventory.Catalog.Find(component.FluidId);
            text.Append("\n").Append(gas
                ? FlatWorldLocalizationService.GetUiFormat("{0}：{1:0.###} 标准 L", FlatWorldLocalizationService.GetUiText(substance.DisplayName), FluidUnits.MolToStandardLiters(component.GasMoles))
                : FlatWorldLocalizationService.GetUiFormat("{0}：液体 {1:0.###} L", FlatWorldLocalizationService.GetUiText(substance.DisplayName), FluidUnits.MolToLiquidLiters(substance, component.LiquidMoles)));
        }
        if (Definition.AllowManualAirFill)
            text.Append("\n").Append(FlatWorldLocalizationService.GetUiFormat("自然补气上限 {0:0.#}% · 动力充装可充满", Definition.NaturalFillFraction * 100d));
    }
    #endregion
}
