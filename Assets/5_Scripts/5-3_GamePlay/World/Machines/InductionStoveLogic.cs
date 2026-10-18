using System;
using FlatWorld.Localization;
using FlatWorld.Networking;
using MemoryPack;
using UnityEngine;

/// <summary>电磁炉只消耗真实供电并管理炉温，材料规则由通用供热服务处理。</summary>
public sealed class InductionStoveLogic : MachineLogic
{
    #region 电力炉温与库存
    public InductionStoveState State { get; private set; }
    private InductionStoveDefinition Configuration => Entity.Definition.Induction;
    public Inventory Input { get; }
    public Inventory Output { get; }
    public float Temperature => State.Temperature;
    public float SuppliedWatts => State.SuppliedWatts;
    public float HeatingRatePercent => Configuration.HeatingRatePercent;
    public override float TickInterval => .1f;
    public override bool CanAct => false;
    public override GameObject PanelPrefab => GameRes.Instance.GetPrefab(Configuration.PanelId);
    public override string Status => FurnaceTemperatureFeedback.GetDisplayedTemperature(Temperature);

    public InductionStoveLogic(MachineEntity entity) : base(entity)
    {
        Configuration.Validate(entity.Definition.Id, entity.Definition.Electrical);
        State = MachinePersistence.Read<InductionStoveState>(entity.Snapshot, "induction-stove") ?? new InductionStoveState();
        if (GameNetwork.HasStateAuthority) State.SuppliedWatts = 0f;
        var inputTemplate = new Inventory
        {
            StorageMaxWeightKg = Configuration.InputMaxWeightKg,
            StorageMaxVolumeCubicMeters = Configuration.InputMaxVolumeLiters / Inventory_Data.LitersPerCubicMeter
        };
        Input = Track(MachineInventory.Create(inputTemplate, State.Input, "电磁炉输入", Configuration.InputSlots));
        Output = Track(MachineInventory.Create(null, State.Output ?? MachineInventory.NewData("电磁炉输出", Configuration.OutputSlots)));
        State.Input = Input.Data;
        State.Output = Output.Data;
    }

    public override void Tick(float seconds)
    {
        if (!MachineDefinition.Positive(seconds)) return;
        float ratedPower = Entity.Definition.Electrical.PowerWatts;
        State.SuppliedWatts = Mathf.Clamp(Entity.ElectricalSuppliedWatts, 0f, ratedPower);
        float powerFraction = State.SuppliedWatts / ratedPower;
        float ambient = MachineWorld.SampleAmbientTemperature(Entity);
        // 电力只控制主动升温，材料换热系数在停电后仍可使用炉体真实余热。
        State.Temperature = powerFraction > 0f
            ? Mathf.Min(Configuration.MaxTemperature, State.Temperature + Configuration.TemperatureUpSpeed * powerFraction * seconds)
            : ThermalRuntime.AdvanceAmbient(State.Temperature, ambient, seconds);
        FurnaceMaterialHeating.Process(Input, Output, State.Temperature, seconds, HeatingRatePercent / 100f,
            initialMaterialTemperature: ambient);
        NotifyChanged();
    }

    public override void Capture() => MachinePersistence.Write(Entity.Snapshot, "induction-stove", State);

    public override bool ApplyRemoteSnapshot(ItemData snapshot)
    {
        InductionStoveState incoming = MachinePersistence.Read<InductionStoveState>(snapshot, "induction-stove");
        if (incoming?.Input == null || incoming.Output == null) return false;
        MachineInventory.ApplySnapshot(Input, incoming.Input);
        MachineInventory.ApplySnapshot(Output, incoming.Output);
        State = incoming;
        State.Input = Input.Data;
        State.Output = Output.Data;
        NotifyRemoteChanged();
        return true;
    }
    #endregion
}

[Serializable]
public sealed class InductionStoveDefinition
{
    #region 炉体参数
    public int InputSlots = 3;
    public int OutputSlots = 2;
    public float MaxTemperature = 2000f;
    public float TemperatureUpSpeed = 10f;
    public float HeatingRatePercent = 150f;
    public float InputMaxWeightKg = 300f;
    public float InputMaxVolumeLiters = 300f;
    public string PanelId = "UI_Furnace";

    public void Validate(string id, ElectricalDefinition electrical)
    {
        if (InputSlots < 1 || OutputSlots < 1 || !MachineDefinition.Positive(MaxTemperature) ||
            !MachineDefinition.Positive(TemperatureUpSpeed) || !MachineDefinition.NonNegative(HeatingRatePercent) ||
            !MachineDefinition.Positive(InputMaxWeightKg) || !MachineDefinition.Positive(InputMaxVolumeLiters) ||
            string.IsNullOrWhiteSpace(PanelId) || electrical?.IsConsumer != true ||
            !MachineDefinition.Positive(electrical.PowerWatts))
            throw new ArgumentException("电磁炉必须声明有效库存、炉温、换热系数和供电配置：" + id);
    }
    #endregion
}

[Serializable, MemoryPackable]
public partial class InductionStoveState
{
    #region 持久状态
    public float Temperature = TemperatureMgr.DefaultAmbientTemperature;
    public float SuppliedWatts;
    public Inventory_Data Input;
    public Inventory_Data Output;
    #endregion
}
