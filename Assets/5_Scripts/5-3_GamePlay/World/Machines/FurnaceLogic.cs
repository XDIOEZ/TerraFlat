using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FlatWorld.Gameplay.Progress;
using FlatWorld.Networking;
using MemoryPack;
using UnityEngine;

[Serializable, MemoryPackable]
public partial class FurnaceRuntimeState
{
    #region 熔炉持久状态
    public ModSmeltingData Smelting = new();
    public FuelData Fuel = new();
    public RecipeProcessingState Processing = new();
    #endregion
}

/// <summary>炉温、燃料、熔炼与副产物内聚在托管领域对象中，图形与面板没有权威状态。</summary>
public class FurnaceLogic : MachineLogic
{
    #region 配置与状态
    public FurnaceRuntimeState State { get; private set; }
    public ModSmeltingData Data => State.Smelting;
    public FuelData Fuel => State.Fuel;
    public Inventory Input { get; }
    public Inventory Output { get; }
    public Inventory FuelInventory { get; }
    public RecipeProcessor Processor { get; }
    public bool AcceptsAirflow { get; }
    public float FuelRatio => Fuel.Fuel.y > 0f ? Mathf.Clamp01(Fuel.Fuel.x / Fuel.Fuel.y) : 0f;
    public override bool IsBurning => Data.IsSmelting && Fuel.Fuel.x > .01f;
    public override float TickInterval => .1f;
    public override float Progress01 => Processor.Progress01;
    public override GameObject PanelPrefab { get; }
    public override string ActionLabel => "点火";
    public override bool CanAct => !Data.IsSmelting;
    public override string Status => FurnaceTemperatureFeedback.GetDisplayedTemperature(Data.Temperature);

    private readonly float burnSpeed;
    private readonly List<FurnaceFuelByproductRule> byproducts;
    private readonly List<string> ignitionIds;
    private readonly List<string> ignitionTags;
    private readonly float ignitionFuel;
    private readonly float ignitionTemperature;
    private readonly bool heatVessels;
    private readonly bool hasLocalHeat;
    private readonly float heatRadius;
    private readonly float heatOffset;
    private TemperatureMgr temperatureManager;
    private Player productionActor;
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
        Data.MaxSmeltingSpeed = configured.MaxSmeltingSpeed;
        Data.MaxTemperatureLimit = configured.MaxTemperatureLimit;
        Data.TemperatureUpSpeed = configured.TemperatureUpSpeed;
        Data.TemperatureDownSpeed = configured.TemperatureDownSpeed;
        Fuel.Fuel = new Vector2(Mathf.Clamp(Fuel.Fuel.x, 0f, configuredFuel.Fuel.y), configuredFuel.Fuel.y);
        burnSpeed = fuelConfig.Value("burnSpeedMultiplier", fuelAuthoring.burnSpeedMultiplier);
        AcceptsAirflow = config.Value("acceptsMechanicalBellows", authoring.acceptsMechanicalBellows);
        byproducts = config.Value("fuelByproductRules", authoring.fuelByproductRules) ?? new();
        FurnaceFuelByproductProcessor.ValidateRules(byproducts);
        ignitionIds = config.Value("ignitionItemIds", authoring.ignitionItemIds) ?? new();
        ignitionTags = config.Value("ignitionTags", authoring.ignitionTags) ?? new();
        ignitionFuel = config.Value("ignitionFuelValueOverride", authoring.ignitionFuelValueOverride);
        ignitionTemperature = config.Value("ignitionMaxTemperatureOverride", authoring.ignitionMaxTemperatureOverride);
        heatVessels = entity.Definition.Content.Has<Mod_VesselHeating>();
        PanelPrefab = authoring.UI_Prefab;
        LocalTemperatureSource source = authoring.GetComponent<LocalTemperatureSource>();
        hasLocalHeat = source != null;
        heatRadius = source != null ? source.Radius : 0f;
        heatOffset = source != null ? source.CelsiusOffset : 0f;
        Input = RestoreInventory("furnace.input", authoring.InputInventory);
        Output = RestoreInventory("furnace.output", authoring.OutputInventory);
        FuelInventory = RestoreInventory("furnace.fuel", authoring.FuelInventory);
        State.Processing ??= new RecipeProcessingState();
        Processor = new RecipeProcessor(Input, Output,
            new CraftingCapabilities { RecipeType = RecipeType.Smelting, AllowCompactGrid = true }, State.Processing);
        Processor.PreviewOverride = PreviewSmelting;
        Processor.CommitOverride = CommitSmelting;
        Processor.Changed += OnProgressChanged;
        RefreshRecipe();
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
        Data.SmeltingProgress = Processor.State.Progress;
        MachinePersistence.Write(Entity.Snapshot, "furnace", State);
    }

    public override bool ApplyRemoteSnapshot(ItemData snapshot)
    {
        FurnaceRuntimeState incoming = MachinePersistence.Read<FurnaceRuntimeState>(snapshot, "furnace");
        if (incoming?.Smelting?.InvData == null || incoming.Processing == null) return false;
        MachineInventory.ApplySnapshot(FuelInventory, incoming.Smelting.InvData["furnace.fuel"]);
        Processor.ApplyRemoteState(incoming.Processing);
        State = incoming;
        State.Processing = Processor.State;
        Data.InvData["furnace.input"] = Input.Data;
        Data.InvData["furnace.output"] = Output.Data;
        Data.InvData["furnace.fuel"] = FuelInventory.Data;
        RefreshRecipe();
        NotifyRemoteChanged();
        return true;
    }

    public override void Dispose()
    {
        if (temperatureManager != null) temperatureManager.RemoveLocalTemperatureSource(this);
        temperatureManager = null;
        productionActor = null;
        Processor.Changed -= OnProgressChanged;
        Processor.Dispose();
        base.Dispose();
    }
    #endregion

    #region 燃料与炉温
    [MethodImpl(MethodImplOptions.NoInlining)]
    public override void Tick(float seconds)
    {
        bool wasBurning = IsBurning;
        if (Data.IsSmelting)
        {
            float remaining = seconds;
            int refills = 0;
            while (remaining > .00001f && refills < 64)
            {
                if (Fuel.Fuel.x <= .01f)
                {
                    if (!TryFeedFuel(false, null)) { Data.IsSmelting = false; break; }
                    refills++;
                }
                float rate = Mathf.Max(0f, burnSpeed * GameDifficultyService.Current.Production.FuelConsumptionMultiplier);
                float step = rate > 0f ? Mathf.Min(remaining, Fuel.Fuel.x / rate) : remaining;
                if (step <= 0f) break;
                HeatAndProcess(step);
                ConsumeFuel(step);
                remaining -= step;
            }
            if (!Data.IsSmelting && remaining > 0f) Cool(remaining);
        }
        else Cool(seconds);
        FurnaceFuelByproductProcessor.TryFlushPendingOutputs(FuelInventory.Data, byproducts, Data, GameRes.Instance);
        PublishTemperature();
        NotifyChanged(wasBurning != IsBurning);
    }

    private void Cool(float seconds)
    {
        Data.Temperature = Mathf.Max(20f, Data.Temperature - Data.TemperatureDownSpeed * seconds);
        Data.SmeltingSpeed = 0f;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public virtual float CalculateMaximumTemperature(float airflow)
    {
        float baseLimit = Data.MaxTemperature > 0f ? Mathf.Min(Data.MaxTemperature, Data.MaxTemperatureLimit) : Data.MaxTemperatureLimit;
        return baseLimit + Mathf.Clamp01(airflow) * MachineCatalog.Settings.BellowsHeatBonus;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public virtual void ConsumeFuel(float seconds)
    {
        float consumed = Mathf.Max(0f, seconds * burnSpeed * GameDifficultyService.Current.Production.FuelConsumptionMultiplier);
        Fuel.Fuel.x = Mathf.Max(0f, Fuel.Fuel.x - consumed);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public virtual bool Ignite(Player actor)
    {
        if (!GameNetwork.HasStateAuthority || Data.IsSmelting || !TryFeedFuel(true, actor)) return false;
        Data.IsSmelting = true;
        productionActor = actor;
        PublishTemperature();
        NotifyChanged(true);
        GameplayProgressEvents.PublishFurnaceIgnited(actor, Entity.Definition.Id);
        return true;
    }

    private bool TryFeedFuel(bool requireIgnition, Player actor)
    {
        ModuleData data = FuelInventory.Data.GetModuleByID(ModText.Fuel);
        ItemSlot slot = data == null ? null : FuelInventory.Data.GetItemSlotByModuleID(data.ID);
        if (slot?.itemData == null || data is not Ex_ModData_MemoryPackable binary || FuelInventory.IsSlotBeingDragged(slot.Index)) return false;
        bool tinder = IsIgnitionFuel(slot.itemData);
        if (requireIgnition && !tinder && !HasHeldIgnition(actor)) return false;
        binary.OutData(out FuelData offered);
        if (offered == null) return false;
        float value = tinder ? Mathf.Min(offered.Fuel.x, ignitionFuel) : offered.Fuel.x;
        float limit = tinder ? Mathf.Min(offered.MaxTemperature, ignitionTemperature) : offered.MaxTemperature;
        if (!MachineDefinition.Positive(value) || !MachineDefinition.Positive(limit) || Fuel.Fuel.y <= 0f) return false;
        if (!FuelInventory.Data.TryConsumeFromSlot(slot, 1, out ItemData consumed)) return false;
        Fuel.Fuel.x = Mathf.Min(Fuel.Fuel.y, Fuel.Fuel.x + value);
        Data.MaxTemperature = limit;
        FurnaceFuelByproductProcessor.RecordConsumedFuel(consumed, byproducts, Data);
        FurnaceFuelByproductProcessor.TryFlushPendingOutputs(FuelInventory.Data, byproducts, Data, GameRes.Instance);
        return true;
    }

    private bool IsIgnitionFuel(ItemData item)
        => ignitionIds.Contains(item.IDName) || item.Tags != null && item.Tags.ContainsAnyTag(ignitionTags);

    private static bool HasHeldIgnition(Player actor)
    {
        if (actor == null) return false;
        Item held = actor.itemMods?.GetMod_ByID<Inventory_HotBar>(ModText.Hotbar)?.CurentSelectItem;
        return held != null && held.InHand && held.Owner == actor &&
            held.itemMods?.GetMod_ByID<Mod_Combustion>(Mod_Combustion.ModuleId)?.IsActivelyBurning == true;
    }

    private void PublishTemperature()
    {
        if (!hasLocalHeat) return;
        if (!IsBurning)
        { if (temperatureManager != null) temperatureManager.RemoveLocalTemperatureSource(this); return; }
        temperatureManager = TemperatureMgr.Instance;
        if (temperatureManager != null)
            temperatureManager.SetLocalTemperatureSource(this, Entity.Position, heatRadius, heatOffset);
    }
    #endregion

    #region 熔炼
    [MethodImpl(MethodImplOptions.NoInlining)]
    public virtual void HeatAndProcess(float seconds)
    {
        bool hasInput = false;
        foreach (ItemSlot slot in Input.Data.itemSlots) hasInput |= slot?.itemData != null;
        float airflow = AcceptsAirflow ? Entity.Airflow : 0f;
        float maximum = Mathf.Max(1f, CalculateMaximumTemperature(airflow));
        Data.Temperature = Mathf.Min(maximum, Data.Temperature + Data.TemperatureUpSpeed *
            (1f + airflow) * (hasInput ? 1f : 2f) * seconds);
        if (!hasInput) { Processor.State.Progress = 0f; return; }
        if (MachineInventory.IsBeingDragged(Input) || MachineInventory.IsBeingDragged(Output)) return;
        if (heatVessels && InventoryVesselHeating.ProcessHeat(Input, Output, Data.Temperature, seconds))
        { Processor.State.Progress = 0f; return; }
        RefreshRecipe();
        Data.SmeltingSpeed = Mathf.Lerp(1f, Data.MaxSmeltingSpeed, Data.Temperature / maximum);
        Processor.Advance(Data.SmeltingSpeed * GameDifficultyService.Current.Production.SmeltingSpeedMultiplier * seconds, productionActor);
    }

    private void RefreshRecipe()
    {
        if (Processor == null || Processor.IsCommitting) return;
        bool found = CraftingRecipeMatcher.TryMatch(Input, Processor.Capabilities, out CraftingRecipeMatch match, out _);
        Processor.SelectRecipe(found ? match.Recipe : null, 100f);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public virtual CraftingResult ResolveOutputs()
    {
        RuntimeRecipe recipe = Processor.Recipe;
        if (recipe == null) return CraftingResult.Failed(CraftingFailureReason.RecipeNotFound, "当前材料没有熔炼配方");
        if (Data.Temperature < recipe.Temperature)
            return CraftingResult.Failed(CraftingFailureReason.ConditionsNotMet, "炉温不足", recipe);
        if (Data.Temperature <= recipe.Temperature_Max) return CraftingService.DescribeRecipe(recipe);
        ItemData charred = GameRes.Instance.CreateItemData("CharredMatter");
        charred.Stack.Amount = 1f;
        return CraftingResult.Succeeded(recipe, new[] { charred });
    }

    private CraftingResult PreviewSmelting()
    {
        CraftingResult outputs = ResolveOutputs();
        return outputs.Success ? CraftingService.PreviewRecipeOutputs(Input, Output, Processor.Capabilities, Processor.Recipe, outputs.Outputs) : outputs;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public virtual CraftingResult CommitSmelting(Player actor)
    {
        CraftingResult outputs = ResolveOutputs();
        if (!outputs.Success) return outputs;
        CraftingResult result = CraftingService.CraftRecipeOutputs(Input, Output, Processor.Capabilities, Processor.Recipe,
            outputs.Outputs, Data.Temperature <= Processor.Recipe.Temperature_Max, actor, publishCrafting: false);
        if (result.Success)
            foreach (ItemData output in result.Outputs)
                GameplayProgressEvents.PublishSmeltSucceeded(actor, output.IDName, output.Stack.Amount);
        return result;
    }

    public override bool Execute(string operation, string argument, Player actor) => operation == "work" && Ignite(actor);
    protected override void OnInventoryChanged(ItemSlot slot)
    { RefreshRecipe(); base.OnInventoryChanged(slot); }
    private void OnProgressChanged() => NotifyChanged();
    #endregion
}
