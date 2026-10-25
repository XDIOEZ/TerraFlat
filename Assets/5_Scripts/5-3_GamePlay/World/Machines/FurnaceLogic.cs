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
    public float HeatingRatePercent; // 权威供热效率随炉体状态同步，面板与实际材料受热共用此值。
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
    public float HeatingRatePercent => State.HeatingRatePercent;
    public float FuelRatio => Fuel.Fuel.y > 0f ? Mathf.Clamp01(Fuel.Fuel.x / Fuel.Fuel.y) : 0f;
    public override bool IsBurning => Data.IsSmelting && !State.AwaitingIgnition && Fuel.Fuel.x > .01f;
    public override float TickInterval => .1f;
    public override GameObject PanelPrefab { get; }
    public override string ActionLabel => "点火";
    public override bool CanAct => !Data.IsSmelting && MachineWorld.CanSustainCombustion(Entity);
    public override string Status => FurnaceTemperatureFeedback.GetDisplayedTemperature(Data.Temperature);

    private readonly float burnSpeed;
    private readonly float baseHeatingRatePercent;
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
        baseHeatingRatePercent = config.Value("heatingRatePercent", authoring.heatingRatePercent);
        if (!MachineDefinition.NonNegative(baseHeatingRatePercent))
            throw new InvalidOperationException("材料升温系数百分比必须为有限非负数。");
        if (GameNetwork.HasStateAuthority) State.HeatingRatePercent = baseHeatingRatePercent;
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
        bool wasSmelting = Data.IsSmelting;
        // 缺氧直接停火，保留炉内燃值，后续恢复氧气仍需要重新点火。
        if (!MachineWorld.CanSustainCombustion(Entity)) Data.IsSmelting = false;
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

            float rate = Mathf.Max(0f, burnSpeed * GameRuleConfiguration.Current.Production.FuelConsumptionMultiplier);
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
        NotifyChanged(wasBurning != IsBurning || wasSmelting != Data.IsSmelting);
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
        float consumed = Mathf.Max(0f, seconds * burnSpeed * GameRuleConfiguration.Current.Production.FuelConsumptionMultiplier);
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
                    if (addedAmount > 0f) ItemDurabilityRules.AdvanceAfterRemoval(dropped);
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
        if (!MachineWorld.CanSustainCombustion(Entity))
        {
            ItemActionFeedback.Show(actor, "氧气不足 5%，无法点火。");
            return false;
        }
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
        => FurnaceFuelRules.TryResolve(item, IsIgnitionFuel(item), ignitionFuel, ignitionTemperature,
            out value, out limit) && Fuel.Fuel.y > 0f;

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
        => FurnaceFuelRules.IsTinder(item, ignitionIds, ignitionTags);

    private static bool HasHeldIgnition(Player actor)
        => FurnaceFuelRules.HasHeldIgnition(actor);

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
        float fuelUnits = Mathf.Min(Fuel.Fuel.x, Mathf.Max(0f, seconds * burnSpeed * GameRuleConfiguration.Current.Production.FuelConsumptionMultiplier));
        MachineWorld.SupplyCombustionSupport(this, fuelUnits, seconds);
        bool hasMaterials = HasMaterials(Input) || HasMaterials(Output);
        float airflow = AcceptsAirflow ? Entity.Airflow.Strength : 0f;
        float maximum = Mathf.Max(1f, CalculateMaximumTemperature(airflow));
        Data.Temperature = Mathf.Min(maximum, Data.Temperature + Data.TemperatureUpSpeed *
            (1f + airflow) * (hasMaterials ? 1f : 2f) * seconds);
        TransferMaterialHeat(seconds);
    }

    private void TransferMaterialHeat(float seconds)
    {
        State.HeatingRatePercent = Entity.Airflow.GetHeatingRatePercent(baseHeatingRatePercent, AcceptsAirflow);
        FurnaceMaterialHeating.Process(Input, Output, Data.Temperature, seconds, HeatingRatePercent / 100f,
            productionActor, FurnaceMaterialHeating.SampleInitialMaterialTemperature(Entity));
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

/// <summary>燃料炉和电炉复用同一材料供热流程，热源只提供温度与升温倍率。</summary>
public static class FurnaceMaterialHeating
{
    #region 炉内材料传热

    // 新材料采用所属环境基温，地表不把炉子自己发布的炉心热源当作初温。
    public static float SampleInitialMaterialTemperature(MachineEntity entity)
    {
        if (MachineWorld.CanPublishLocalEnvironment(entity) && TemperatureMgr.Instance != null &&
            TemperatureMgr.Instance.TryGetClimateBaseline(entity.Position, out float baseline)) return baseline;
        return MachineWorld.SampleAmbientTemperature(entity);
    }

    public static bool Process(Inventory input, Inventory output, float sourceTemperature, float seconds,
        float heatingRateMultiplier = 1f, Player actor = null,
        float initialMaterialTemperature = TemperatureMgr.DefaultAmbientTemperature)
    {
        if (input?.Data?.itemSlots == null || output?.Data?.itemSlots == null ||
            !float.IsFinite(sourceTemperature) || !float.IsFinite(seconds) || seconds <= 0f ||
            !float.IsFinite(heatingRateMultiplier) || heatingRateMultiplier < 0f ||
            !float.IsFinite(initialMaterialTemperature) ||
            MachineInventory.IsBeingDragged(input) || MachineInventory.IsBeingDragged(output)) return false;
        bool sameInventory = ReferenceEquals(input, output);
        using var inputRejectedLease = UnityEngine.Pool.HashSetPool<int>.Get(out var inputRejected);
        using var outputRejectedLease = UnityEngine.Pool.HashSetPool<int>.Get(out var separateOutputRejected);
        HashSet<int> outputRejected = sameInventory ? inputRejected : separateOutputRejected;
        bool changed = Initialize(output);
        if (!sameInventory) changed |= Initialize(input);
        if (LeavesCurrentWindow(output))
            changed |= MaterialThermalProcessor.Process(output, output, actor, rejectedMaterialSlots: outputRejected);
        if (!sameInventory && LeavesCurrentWindow(input))
            changed |= MaterialThermalProcessor.Process(input, output, actor, rejectedMaterialSlots: inputRejected);
        double remaining = seconds;
        ItemData boundaryItem = null;
        float boundaryTemperature = 0f;
        while (remaining > 0d)
        {
            boundaryItem = null;
            boundaryTemperature = 0f;
            double boundarySeconds = remaining;
            FindBoundary(output, outputRejected);
            if (!sameInventory) FindBoundary(input, inputRejected);
            float step = (float)boundarySeconds;
            // 输出先结算本段，新产物只在下一段领取相变后剩余的热量。
            changed |= Advance(output, output, step, outputRejected);
            if (!sameInventory) changed |= Advance(input, output, step, inputRejected);
            remaining -= boundarySeconds;

            void FindBoundary(Inventory inventory, ISet<int> rejected)
            {
                GameRes resources = GameRes.ExistingInstance;
                if (resources == null) return;
                foreach (ItemSlot slot in inventory.Data.itemSlots)
                {
                    ItemData material = slot?.itemData;
                    if (material?.MatterState?.Initialized != true || rejected.Contains(slot.Index) ||
                        !resources.TryGetItemDefinition(material.IDName, out RuntimeItemDefinition definition)) continue;
                    if (definition.Reactions.Count == 0 && (definition.Matter == null || definition.Matter.Transitions.Count == 0)) continue;
                    float current = material.MatterState.TemperatureCelsius;
                    float next = ThermalRuntime.AdvanceAmbient(current, sourceTemperature,
                        ThermalRuntime.ResolveHeatingSeconds(current, sourceTemperature, (float)boundarySeconds, heatingRateMultiplier));
                    foreach (RuntimeItemReactionDefinition reaction in definition.Reactions)
                    {
                        if (reaction.LiquidOutput != null) continue;
                        foreach (RuntimeRecipeIngredient ingredient in reaction.Recipe.inputs.RowItems_List)
                            if (CraftingIngredientMatcher.MatchesIdentity(ingredient, material))
                            { Consider(sourceTemperature > current ? reaction.MinTemperature : reaction.MaxTemperature); break; }
                    }
                    if (definition.Matter != null)
                        foreach (RuntimeItemMatterTransition transition in definition.Matter.Transitions)
                            if (transition.LiquidOutput == null)
                                Consider(sourceTemperature > current ? transition.MinTemperature : transition.MaxTemperature);

                    void Consider(float? boundary)
                    {
                        if (!boundary.HasValue || boundary.Value == current ||
                            boundary.Value < Math.Min(current, next) || boundary.Value > Math.Max(current, next)) return;
                        double time = ThermalRuntime.GetAmbientTransferSeconds(current, sourceTemperature,
                            boundary.Value, heatingRateMultiplier);
                        if (time <= 0d || time > boundarySeconds) return;
                        boundarySeconds = time;
                        boundaryItem = material;
                        boundaryTemperature = boundary.Value;
                    }
                }
            }
        }
        return changed;

        bool LeavesCurrentWindow(Inventory materials)
        {
            GameRes resources = GameRes.ExistingInstance;
            if (resources == null) return false;
            foreach (ItemSlot slot in materials.Data.itemSlots)
            {
                ItemData material = slot?.itemData;
                if (material?.MatterState?.Initialized != true ||
                    !resources.TryGetItemDefinition(material.IDName, out RuntimeItemDefinition definition)) continue;
                if (definition.Reactions.Count == 0 && (definition.Matter == null || definition.Matter.Transitions.Count == 0)) continue;
                float current = material.MatterState.TemperatureCelsius;
                float next = ThermalRuntime.AdvanceAmbient(current, sourceTemperature,
                    ThermalRuntime.ResolveHeatingSeconds(current, sourceTemperature, seconds, heatingRateMultiplier));
                foreach (RuntimeItemReactionDefinition reaction in definition.Reactions)
                    if (reaction.LiquidOutput == null && reaction.TemperatureMatches(current) &&
                        !reaction.TemperatureMatches(next)) return true;
                if (definition.Matter != null)
                    foreach (RuntimeItemMatterTransition transition in definition.Matter.Transitions)
                        if (transition.LiquidOutput == null && transition.Matches(material.MatterState) &&
                            (transition.MinTemperature.HasValue && next < transition.MinTemperature.Value ||
                             transition.MaxTemperature.HasValue && next > transition.MaxTemperature.Value)) return true;
            }
            return false;
        }

        bool Initialize(Inventory materials)
        {
            bool initialized = false;
            foreach (ItemSlot slot in materials.Data.itemSlots)
                if (ItemMatterRuntime.EnsureInitialized(slot?.itemData, initialMaterialTemperature))
                { materials.Data.NotifyItemStateChanged(slot.itemData); initialized = true; }
            return initialized;
        }

        bool Advance(Inventory materials, Inventory destination, float step, ISet<int> rejected)
        {
            bool inventoryChanged = false;
            using var originalGuidsLease = UnityEngine.Pool.HashSetPool<int>.Get(out var originalGuids);
            foreach (ItemSlot slot in materials.Data.itemSlots)
            {
                ItemData material = slot?.itemData;
                if (material == null) continue;
                originalGuids.Add(material.Guid);
                if (ItemMatterRuntime.EnsureInitialized(material, initialMaterialTemperature))
                {
                    materials.Data.NotifyItemStateChanged(material);
                    inventoryChanged = true;
                }
            }
            // 内容先读取容器本轮受热前的温度，避免给新装入材料重复加热。
            inventoryChanged |= InventoryVesselHeating.ProcessHeat(materials, destination, sourceTemperature, step,
                heatingRateMultiplier, initialMaterialTemperature);
            foreach (ItemSlot slot in materials.Data.itemSlots)
            {
                ItemData material = slot?.itemData;
                if (material == null || !originalGuids.Contains(material.Guid)) continue;
                bool advanced = ItemMatterRuntime.Advance(material,
                    rejected.Contains(slot.Index) ? material.MatterState.TemperatureCelsius : sourceTemperature,
                    1f, step, heatingRateMultiplier: heatingRateMultiplier);
                if (ReferenceEquals(material, boundaryItem))
                {
                    material.MatterState.TemperatureCelsius = boundaryTemperature;
                    advanced = true;
                }
                if (!advanced) continue;
                materials.Data.NotifyItemStateChanged(material);
                inventoryChanged = true;
            }
            rejected.Clear();
            inventoryChanged |= MaterialThermalProcessor.Process(materials, destination, actor, rejectedMaterialSlots: rejected);
            return inventoryChanged;
        }
    }

    #endregion
}
