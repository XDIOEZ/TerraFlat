using System;
using FlatWorld.Networking;

/// <summary>背包内冷载荷只更新原槽位里的模块状态，不创建世界容器副本。</summary>
public sealed class ColdInventoryLiquidVessel : ILiquidVessel, IContainerPortProvider
{
    #region 冷容器适配
    private readonly Inventory inventory;
    private readonly Ex_ModData_MemoryPackable storage;
    private LiquidContainerState state;
    private byte[] observedBytes;
    private readonly uint ownerGeneration;
    private readonly MachineEntity machineOwner;
    private readonly System.Collections.Generic.List<ContainerPortConfiguration> configurations;
    private System.Collections.Generic.List<IContainerPort> ports;
    private long registryGeneration;
    public Inventory Inventory => inventory;
    public Item Owner { get; }
    public ItemData ItemData { get; }
    public ColdInventoryLiquidVessel(ItemData data, Inventory inventory, Item owner)
    {
        if (!Mod_WaterVessel.TryRead(data, out storage, out state)) throw new ArgumentException("物品不具备液体容器能力。");
        ItemData = data; this.inventory = inventory; Owner = owner; ownerGeneration = owner.RuntimeGeneration; observedBytes = storage.BitData;
        configurations = Mod_WaterVessel.ResolvePortConfigurations(data);
        machineOwner = inventory.MachineOwner;
    }
    public bool IsValid => Owner != null && !Owner.DestructionHandled && Owner.RuntimeGeneration == ownerGeneration &&
        HasCurrentStorage() && storage.Enabled &&
        (machineOwner == null || MachineWorld.Contains(machineOwner) && ReferenceEquals(inventory.MachineOwner, machineOwner)) &&
        inventory.Data.itemSlots.Exists(slot => ReferenceEquals(slot?.itemData, ItemData));
    private bool HasCurrentStorage()
    { foreach (ModuleData module in ItemData.ModuleDataDic.Values) if (ReferenceEquals(module, storage)) return true; return false; }
    public LiquidContainerState Data
    {
        get
        {
            if (!ReferenceEquals(observedBytes, storage.BitData))
            { storage.ReadData(ref state); state ??= new(); MixedLiquidContents.Ensure(state); observedBytes = storage.BitData; }
            return state;
        }
    }
    public int Capacity => Mod_WaterVessel.ResolveContainerCapacity(ItemData);
    public LiquidDefinition CurrentLiquid => MixedLiquidContents.DisplayLiquid(Data);
    public Item Item => null;
    public MachineEntity Machine => null;
    public IVesselContents ContentsSource => null;
    public event Action Changed;
    public bool CanOperate(Item actor) => IsValid && actor == Owner;
    public void CollectContainerPorts(System.Collections.Generic.List<IContainerPort> result)
    {
        if (!IsValid) return;
        if (ports == null || registryGeneration != ContainerPortFactoryRegistry.Generation)
        {
            ports = new(); registryGeneration = ContainerPortFactoryRegistry.Generation;
            foreach (var configuration in configurations)
                ports.Add(ContainerPortFactoryRegistry.Create(configuration.Type,
                    new ContainerPortFactoryContext(Owner, null, null, this, configuration, () => IsValid)));
        }
        result.AddRange(ports);
    }
    public bool Drink(Item actor) => LiquidVesselOperations.Drink(this, actor);
    public float PourToGround(Item actor, float amount) => LiquidVesselOperations.PourToGround(this, actor, amount);
    public bool TransferFromInventoryItem(ItemData source, Item actor, float maximumItemAmount = float.PositiveInfinity)
        => LiquidVesselOperations.TransferFromInventory(this, source, actor, maximumItemAmount);
    public void CommitVessel()
    {
        if (!IsValid) throw new InvalidOperationException("冷液体容器已离开原库存。");
        Mod_WaterVessel.Validate(Data, Capacity); storage.WriteData(state); observedBytes = storage.BitData;
        inventory.Data.NotifyItemStateChanged(ItemData); inventory.Save();
        ItemNetworkStateSerialization.NotifyRuntimeStateChanged(Owner); Changed?.Invoke();
    }
    #endregion
}
