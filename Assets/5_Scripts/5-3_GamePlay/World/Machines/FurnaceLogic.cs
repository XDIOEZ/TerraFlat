using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FlatWorld.Gameplay.Progress;
using FlatWorld.Localization;
using FlatWorld.Networking;
using MemoryPack;
using UnityEngine;

[Serializable, MemoryPackable]
public partial class FurnaceRuntimeState
{
    #region 熔炉持久状态
    public ModSmeltingData Smelting = new();
    public FuelData Fuel = new();
    public FuelAbsorptionState Absorption = new();
    public bool AwaitingIgnition;
    #endregion
}

/// <summary>炉温、燃料、供热与副产物内聚在托管领域对象中，图形与面板没有权威状态。</summary>
public class FurnaceLogic : MachineLogic, ICombustionSupportReceiver
{
    #region 配置与状态
    public FurnaceRuntimeState State { get; private set; }
    public ModSmeltingData Data => State.Smelting;
    public FuelData Fuel => State.Fuel;
    public Inventory Input { get; }
    public Inventory Output { get; }
    public Inventory FuelInventory { get; }
    public bool AcceptsAirflow { get; }
    public float FuelRatio => Fuel.Fuel.y > 0f ? Mathf.Clamp01(Fuel.Fuel.x / Fuel.Fuel.y) : 0f;
    public override bool IsBurning => Data.IsSmelting && !State.AwaitingIgnition && Fuel.Fuel.x > .01f;
    public override float TickInterval => .1f;
    public override GameObject PanelPrefab { get; }
    public override string ActionLabel => "点火";
    public override bool CanAct => !Data.IsSmelting;
    public override string Status => FurnaceTemperatureFeedback.GetDisplayedTemperature(Data.Temperature);

    private readonly float burnSpeed;
    private readonly float fuelAbsorptionSeconds;
    private readonly bool absorbDroppedFuel;
    private readonly Predicate<Vector2> fuelCellFilter;
    private readonly List<FurnaceFuelByproductRule> byproducts;
    private readonly List<string> ignitionIds;
    private readonly List<string> ignitionTags;
    private readonly float ignitionFuel;
    private readonly float ignitionTemperature;
    private readonly bool hasLocalHeat;
    private readonly bool publishCellTemperature;
    private readonly float neighborTargetTemperature;
    private readonly float heatRadius;
    private readonly float heatTargetTemperature;
    private readonly object coreHeatOwner = new();
    private const float FurnaceNeighborHeatRadius = 1.75f;
    private const float FurnaceCoreHeatRadius = 0.7f;
    private TemperatureMgr temperatureManager;
    private Player productionActor;
    private float combustionHeatBonus;
    #endregion

    #region 生命周期
    public FurnaceLogic(MachineEntity entity) : base(entity)
    {
        var config = entity.Definition.Content.Find<Mod_Furnace>()
            ?? throw new InvalidOperationException("炉体缺少熔炉配置。");
        var authoring = (Mod_Furnace)config.Authoring;
        var fuelConfig = entity.Definition.Content.Find<Mod_Fuel>()
            ?? throw new InvalidOperationException("炉体缺少燃料配置。");
        var fuelAuthoring = (Mod_Fuel)fuelConfig.Authoring;
        ModSmeltingData configured = config.Data("Data", authoring.Data ?? new ModSmeltingData());
        FuelData configuredFuel = fuelConfig.Data("Data", fuelAuthoring.Data ?? new FuelData());
        State = MachinePersistence.Read<FurnaceRuntimeState>(entity.Snapshot, "furnace") ?? new FurnaceRuntimeState
        { Smelting = configured, Fuel = configuredFuel };
        Data.InvData ??= new Dictionary<string, Inventory_Data>();
        Data.FuelByproductProgress ??= new Dictionary<string, int>();
        Data.PendingFuelByproductCount ??= new Dictionary<string, int>();
        Data.MaxTemperatureLimit = configured.MaxTemperatureLimit;
        Data.TemperatureUpSpeed = configured.TemperatureUpSpeed;
        // x 保存真实燃值，可高于 y；y 只作为面板显示容量和自动补充阈值。
        Fuel.Fuel = new Vector2(Mathf.Max(0f, Fuel.Fuel.x), configuredFuel.Fuel.y);
        burnSpeed = fuelConfig.Value("burnSpeedMultiplier", fuelAuthoring.burnSpeedMultiplier);
        fuelAbsorptionSeconds = config.Value("fuelAbsorptionSeconds", authoring.fuelAbsorptionSeconds);
        if (!MachineDefinition.Positive(fuelAbsorptionSeconds))
            throw new InvalidOperationException("燃料吸收时长必须大于零。");
        State.Absorption ??= new FuelAbsorptionState();
        AcceptsAirflow = config.Value("acceptsMechanicalBellows", authoring.acceptsMechanicalBellows);
        absorbDroppedFuel = config.Value("absorbDroppedFuel", authoring.absorbDroppedFuel);
        fuelCellFilter = IsFuelInSameCell;
        byproducts = config.Value("fuelByproductRules", authoring.fuelByproductRules) ?? new();
        FurnaceFuelByproductProcessor.ValidateRules(byproducts);
        ignitionIds = config.Value("ignitionItemIds", authoring.ignitionItemIds) ?? new();
        ignitionTags = config.Value("ignitionTags", authoring.ignitionTags) ?? new();
        ignitionFuel = config.Value("ignitionFuelValueOverride", authoring.ignitionFuelValueOverride);
        ignitionTemperature = config.Value("ignitionMaxTemperatureOverride", authoring.ignitionMaxTemperatureOverride);
        PanelPrefab = authoring.UI_Prefab;
        publishCellTemperature = config.Value("publishCellTemperature", authoring.publishCellTemperature);
        neighborTargetTemperature = config.Value("neighborTargetTemperature", authoring.neighborTargetTemperature);
        LocalTemperatureSource source = authoring.GetComponent<LocalTemperatureSource>();
        hasLocalHeat = source != null || publishCellTemperature;
        heatRadius = source != null ? source.Radius : 0f;
        heatTargetTemperature = source != null ? source.TargetTemperature : 0f;
        Input = RestoreInventory("furnace.input", authoring.InputInventory);
        Output = RestoreInventory("furnace.output", authoring.OutputInventory);
        FuelInventory = RestoreInventory("furnace.fuel", authoring.FuelInventory);
        PublishTemperature();
    }

    private Inventory RestoreInventory(string key, Inventory template)
    {
        Data.InvData.TryGetValue(key, out Inventory_Data saved);
        Inventory inventory = Track(MachineInventory.Create(template, saved));
        Data.InvData[key] = inventory.Data;
        return inventory;
    }

    public override void Capture()
    {
        MachinePersistence.Write(Entity.Snapshot, "furnace", State);
    }

    public override bool ApplyRemoteSnapshot(ItemData snapshot)
    {
        FurnaceRuntimeState incoming = MachinePersistence.Read<FurnaceRuntimeState>(snapshot, "furnace");
        if (incoming?.Smelting?.InvData == null) return false;
        MachineInventory.ApplySnapshot(FuelInventory, incoming.Smelting.InvData["furnace.fuel"]);
        MachineInventory.ApplySnapshot(Input, incoming.Smelting.InvData["furnace.input"]);
        MachineInventory.ApplySnapshot(Output, incoming.Smelting.InvData["furnace.output"]);
        State = incoming;
        Data.InvData["furnace.input"] = Input.Data;
        Data.InvData["furnace.output"] = Output.Data;
        Data.InvData["furnace.fuel"] = FuelInventory.Data;
        NotifyRemoteChanged();
        return true;
    }

    public override void Dispose()
    {
        if (temperatureManager != null)
        {
            temperatureManager.RemoveLocalTemperatureSource(this);
            temperatureManager.RemoveLocalTemperatureSource(coreHeatOwner);
        }
        temperatureManager = null;
        productionActor = null;
        base.Dispose();
    }
    #endregion

    #region 燃料与炉温
    [MethodImpl(MethodImplOptions.NoInlining)]
    public override void Tick(float seconds)
    {
        if (!MachineDefinition.Positive(seconds)) return;
        if (absorbDroppedFuel) AbsorbDroppedFuelFromCell();
        bool wasBurning = IsBurning;
        float remaining = seconds;
        while (remaining > .00001f)
        {
            if (!Data.IsSmelting)
            {
                State.Absorption.Reset();
                State.AwaitingIgnition = false;
                Cool(remaining);
                break;
            }

            ItemSlot source = PrepareFuelAbsorption();
            if (!IsBurning && source == null)
            {
                Data.IsSmelting = false;
                continue;
            }

            float rate = Mathf.Max(0f, burnSpeed * GameDifficultyService.Current.Production.FuelConsumptionMultiplier);
            float step = remaining;
            if (source != null) step = Mathf.Min(step, State.Absorption.RemainingSeconds(fuelAbsorptionSeconds));
            if (IsBurning && rate > 0f)
            {
                step = Mathf.Min(step, Fuel.Fuel.x / rate);
                // 隐藏储备烧回显示容量后就开始下一份吸收，后台长步也按同一时间线推进。
                if (source == null && Fuel.Fuel.y > 0f && Fuel.Fuel.x + .01f >= Fuel.Fuel.y)
                    step = Mathf.Min(step, (Fuel.Fuel.x - Fuel.Fuel.y + .02f) / rate);
            }
            if (step > 0f)
            {
                if (IsBurning) { HeatMaterials(step); ConsumeFuel(step); }
                else Cool(step);
                if (source != null) State.Absorption.Advance(step, fuelAbsorptionSeconds);
                remaining -= step;
            }
            if (source != null && State.Absorption.RemainingSeconds(fuelAbsorptionSeconds) <= .00001f)
                CompleteFuelAbsorption(source);
        }
        FurnaceFuelByproductProcessor.TryFlushPendingOutputs(FuelInventory.Data, byproducts, Data, GameRes.Instance);
        PublishTemperature();
        NotifyChanged(wasBurning != IsBurning);
    }

    private void Cool(float seconds)
    {
        // 先撤掉已熄火的自身热源，再按所属地表、船舱或后台世界的真实环境自然传热。
        PublishTemperature();
        Data.Temperature = ThermalRuntime.AdvanceAmbient(Data.Temperature,
            MachineWorld.SampleAmbientTemperature(Entity), seconds);
        TransferMaterialHeat(seconds);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public virtual float CalculateMaximumTemperature(float airflow)
    {
        float baseLimit = Data.MaxTemperature > 0f ? Mathf.Min(Data.MaxTemperature, Data.MaxTemperatureLimit) : Data.MaxTemperatureLimit;
        float bellowsBonus = Mathf.Clamp01(airflow) * MachineCatalog.Settings.BellowsHeatBonus;
        // 不同助燃方式只取最高加成，统一加在基础温度上限之后。
        return baseLimit + Mathf.Max(bellowsBonus, combustionHeatBonus);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public virtual void ConsumeFuel(float seconds)
    {
        float consumed = Mathf.Max(0f, seconds * burnSpeed * GameDifficultyService.Current.Production.FuelConsumptionMultiplier);
        Fuel.Fuel.x = Mathf.Max(0f, Fuel.Fuel.x - consumed);
    }

    public float GetFuelAbsorptionProgress(int slotIndex)
        => slotIndex == State.Absorption.SlotIndex && CurrentAbsorptionSlot() != null
            ? State.Absorption.Progress01(fuelAbsorptionSeconds) : 0f;

    private ItemSlot CurrentAbsorptionSlot()
    {
        int index = State.Absorption.SlotIndex;
        if (index < 0 || index >= FuelInventory.Data.itemSlots.Count || FuelInventory.IsSlotBeingDragged(index)) return null;
        ItemSlot slot = FuelInventory.Data.itemSlots[index];
        return State.Absorption.Matches(slot) ? slot : null;
    }

    private ItemSlot PrepareFuelAbsorption()
    {
        if (!State.AwaitingIgnition && (Fuel.Fuel.y <= 0f || Fuel.Fuel.x + .01f >= Fuel.Fuel.y))
        { State.Absorption.Reset(); return null; }
        ItemSlot source = CurrentAbsorptionSlot();
        if (source != null) return source;
        State.Absorption.Reset();
        if (State.AwaitingIgnition) return null;
        if (!TryFindFuelSlot(false, out source) && !TryFindFuelSlot(true, out source)) return null;
        State.Absorption.Observe(source);
        return source;
    }

    private void CompleteFuelAbsorption(ItemSlot source)
    {
        bool igniting = State.AwaitingIgnition;
        if (CurrentAbsorptionSlot() != source || !TryResolveFuel(source.itemData, out float value, out float limit) ||
            !State.Absorption.TryConsumeCompleted(FuelInventory.Data, fuelAbsorptionSeconds, out ItemData consumed))
        {
            State.Absorption.Reset();
            if (igniting) { State.AwaitingIgnition = false; Data.IsSmelting = false; }
            return;
        }
        Fuel.Fuel.x += value;
        Data.MaxTemperature = limit;
        State.AwaitingIgnition = false;
        FurnaceFuelByproductProcessor.RecordConsumedFuel(consumed, byproducts, Data);
        FurnaceFuelByproductProcessor.TryFlushPendingOutputs(FuelInventory.Data, byproducts, Data, GameRes.Instance);
        if (igniting) GameplayProgressEvents.PublishFurnaceIgnited(productionActor, Entity.Definition.Id);
    }

    /// <summary>把落在炉体同一格的有效燃料转入燃料库存，供自动补燃逻辑继续消费。</summary>
    private void AbsorbDroppedFuelFromCell()
    {
        if (FuelInventory?.Data?.itemSlots == null || FuelInventory.Data.itemSlots.Count == 0) return;
        if (!MachineWorld.CanPublishLocalEnvironment(Entity))
        { DroppedItemService.AbsorbWorldFuel(Entity, FuelInventory.Data); return; }

        Vector2 center = new(Entity.Cell.x + .5f, Entity.Cell.y + .5f);
        const float sameCellRadius = .72f;
        int safety = 0;
        while (safety++ < 64 && DroppedItemService.TryFindNearestTagged(
                   center,
                   sameCellRadius,
                   Tag.CombustionFuel,
                   out DroppedItemHandle handle,
                   fuelCellFilter))
        {
            if (!DroppedItemService.TryGetSnapshot(handle, out ItemData dropped) || dropped?.Stack == null ||
                !Mod_Fuel.TryResolveItemData(dropped, out FuelData fuelData) ||
                !MachineDefinition.Positive(fuelData.Fuel.x) ||
                !FuelInventory.Data.TryAddItem(dropped, false, out float availableAmount))
            {
                break;
            }

            int transferAmount = Mathf.FloorToInt(Mathf.Min(dropped.Stack.Amount, availableAmount) + .0001f);
            if (transferAmount <= 0) break;

            dropped.Stack.Amount = transferAmount;
            if (!DroppedItemService.TryConsumeTagged(handle, center, sameCellRadius, Tag.CombustionFuel, transferAmount))
                continue;

            if (!FuelInventory.Data.TryAddItem(dropped, true, out float addedAmount) || addedAmount + .0001f < transferAmount)
            {
                // 预检后正常不会失败；异常时把未入槽的部分重新放回原格，避免吞物品。
                float missing = Mathf.Max(0f, transferAmount - addedAmount);
                if (missing > .0001f)
                {
                    dropped.Stack.Amount = missing;
                    DroppedItemService.Spawn(dropped, center, randomizeRotation: true);
                }
                break;
            }
        }
    }

    private bool IsFuelInSameCell(Vector2 position) => MachineWorld.CellOf(position) == Entity.Cell;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public virtual bool Ignite(Player actor)
    {
        if (!GameNetwork.HasStateAuthority || Data.IsSmelting) return false;
        if (!TryFindFuelSlot(true, out ItemSlot source) &&
            (!HasHeldIgnition(actor) || !TryFindFuelSlot(false, out source))) return false;
        State.Absorption.Reset();
        State.Absorption.Observe(source);
        State.AwaitingIgnition = true;
        Data.IsSmelting = true;
        productionActor = actor;
        PublishTemperature();
        NotifyChanged(true);
        return true;
    }

    private bool TryResolveFuel(ItemData item, out float value, out float limit)
    {
        value = limit = 0f;
        if (item?.Stack == null || item.Stack.Amount < 1f || item.Tags?.ContainsTag(Tag.CombustionFuel) != true ||
            !Mod_Fuel.TryResolveItemData(item, out FuelData offered)) return false;
        bool tinder = IsIgnitionFuel(item);
        value = tinder ? Mathf.Min(offered.Fuel.x, ignitionFuel) : offered.Fuel.x;
        limit = tinder ? Mathf.Min(offered.MaxTemperature, ignitionTemperature) : offered.MaxTemperature;
        return MachineDefinition.Positive(value) && MachineDefinition.Positive(limit) && Fuel.Fuel.y > 0f;
    }

    /// <summary>燃料槽按语义挑选，避免槽位顺序决定点火是否成功。</summary>
    private bool TryFindFuelSlot(bool ignitionFuelOnly, out ItemSlot selected)
    {
        selected = null;
        if (FuelInventory?.Data?.itemSlots == null) return false;

        for (int i = 0; i < FuelInventory.Data.itemSlots.Count; i++)
        {
            ItemSlot slot = FuelInventory.Data.itemSlots[i];
            ItemData item = slot?.itemData;
            if (item == null || FuelInventory.IsSlotBeingDragged(i) ||
                item.Tags?.ContainsTag(Tag.CombustionFuel) != true ||
                IsIgnitionFuel(item) != ignitionFuelOnly ||
                !TryResolveFuel(item, out _, out _))
            {
                continue;
            }

            selected = slot;
            return true;
        }

        return false;
    }

    private bool IsIgnitionFuel(ItemData item)
        => ignitionIds.Contains(item.IDName) || item.Tags != null && item.Tags.ContainsAnyTag(ignitionTags);

    private static bool HasHeldIgnition(Player actor)
    {
        if (actor == null) return false;
        Item held = actor.itemMods?.GetMod_ByID<Mod_HotBar>(ModText.Hotbar)?.CurentSelectItem;
        return held != null && held.InHand && held.Owner == actor &&
            held.itemMods?.GetMod_ByID<Mod_Combustion>(Mod_Combustion.ModuleId)?.IsActivelyBurning == true;
    }

    private void PublishTemperature()
    {
        if (!hasLocalHeat) return;
        if (!IsBurning || !MachineWorld.CanPublishLocalEnvironment(Entity))
        {
            if (temperatureManager != null)
            {
                temperatureManager.RemoveLocalTemperatureSource(this);
                temperatureManager.RemoveLocalTemperatureSource(coreHeatOwner);
            }
            return;
        }
        temperatureManager = TemperatureMgr.Instance;
        if (temperatureManager == null)
            return;

        if (publishCellTemperature)
        {
            // 炉心真实炉温和周围空气目标温度共用普通热源索引与衰减算法。
            Vector2 center = new(Entity.Cell.x + .5f, Entity.Cell.y + .5f);
            temperatureManager.SetLocalTemperatureSource(this, center,
                FurnaceNeighborHeatRadius, neighborTargetTemperature);
            temperatureManager.SetLocalTemperatureSource(coreHeatOwner, center,
                FurnaceCoreHeatRadius, Data.Temperature);
        }
        else
        {
            temperatureManager.RemoveLocalTemperatureSource(coreHeatOwner);
            temperatureManager.SetLocalTemperatureSource(this, Entity.Position, heatRadius, heatTargetTemperature);
        }
    }
    #endregion

    #region 材料供热
    [MethodImpl(MethodImplOptions.NoInlining)]
    public virtual void HeatMaterials(float seconds)
    {
        combustionHeatBonus = 0;
        float fuelUnits = Mathf.Min(Fuel.Fuel.x, Mathf.Max(0f, seconds * burnSpeed * GameDifficultyService.Current.Production.FuelConsumptionMultiplier));
        MachineWorld.SupplyCombustionSupport(this, fuelUnits, seconds);
        bool hasMaterials = HasMaterials(Input) || HasMaterials(Output);
        float airflow = AcceptsAirflow ? Entity.Airflow : 0f;
        float maximum = Mathf.Max(1f, CalculateMaximumTemperature(airflow));
        Data.Temperature = Mathf.Min(maximum, Data.Temperature + Data.TemperatureUpSpeed *
            (1f + airflow) * (hasMaterials ? 1f : 2f) * seconds);
        TransferMaterialHeat(seconds);
    }

    private void TransferMaterialHeat(float seconds)
    {
        if (MachineInventory.IsBeingDragged(Input) || MachineInventory.IsBeingDragged(Output)) return;
        // 先推进已有产物，材料自己结算转化，避免新产物重复获得同一段热量。
        AdvanceMaterialInventory(Output, seconds);
        InventoryVesselHeating.ProcessHeat(Output, Output, Data.Temperature, seconds);
        MaterialThermalProcessor.Process(Output, Output, productionActor);
        AdvanceMaterialInventory(Input, seconds);
        InventoryVesselHeating.ProcessHeat(Input, Output, Data.Temperature, seconds);
        MaterialThermalProcessor.Process(Input, Output, productionActor);
    }

    private void AdvanceMaterialInventory(Inventory inventory, float seconds)
    {
        foreach (ItemSlot slot in inventory.Data.itemSlots)
        {
            ItemData material = slot?.itemData;
            if (material != null && ItemMatterRuntime.Advance(material, Data.Temperature, 1f, seconds))
                inventory.Data.NotifyItemStateChanged(material);
        }
    }

    private static bool HasMaterials(Inventory inventory)
    {
        foreach (ItemSlot slot in inventory.Data.itemSlots)
            if (slot?.itemData != null) return true;
        return false;
    }

    /// <summary>炉体只接收已结算的助燃效果，不查找设备或修改来源库存。</summary>
    public void AcceptCombustionSupport(float temperatureBonus)
    {
        if (MachineDefinition.NonNegative(temperatureBonus)) combustionHeatBonus = Mathf.Max(combustionHeatBonus, temperatureBonus);
    }

    /// <summary>面板只报告炉体供热状态，材料转化条件由材料定义负责。</summary>
    public string GetHeatingHint()
        => FlatWorldLocalizationService.GetUiText(IsBurning ? "正在加热" : "补充燃料并点火");

    public override bool Execute(string operation, string argument, Player actor) => operation == "work" && Ignite(actor);
    protected override void OnInventoryChanged(ItemSlot slot)
    {
        if (State.Absorption.SlotIndex >= 0 && CurrentAbsorptionSlot() == null)
            State.Absorption.Reset();
        base.OnInventoryChanged(slot);
    }
    #endregion
}
