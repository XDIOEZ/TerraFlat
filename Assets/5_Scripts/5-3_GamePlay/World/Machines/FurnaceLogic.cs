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
public class FurnaceLogic : MachineLogic, ICombustionSupportReceiver
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
    private readonly bool absorbDroppedFuel;
    private readonly List<FurnaceFuelByproductRule> byproducts;
    private readonly List<string> ignitionIds;
    private readonly List<string> ignitionTags;
    private readonly float ignitionFuel;
    private readonly float ignitionTemperature;
    private readonly bool heatVessels;
    private readonly bool hasLocalHeat;
    private readonly bool publishCellTemperature;
    private readonly float neighborTemperatureOffset;
    private readonly float heatRadius;
    private readonly float heatOffset;
    private TemperatureMgr temperatureManager;
    private Player productionActor;
    private RuntimeItemReactionDefinition activeReaction;
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
        Data.MaxSmeltingSpeed = configured.MaxSmeltingSpeed;
        Data.MaxTemperatureLimit = configured.MaxTemperatureLimit;
        Data.TemperatureUpSpeed = configured.TemperatureUpSpeed;
        Data.TemperatureDownSpeed = configured.TemperatureDownSpeed;
        // x 保存真实燃值，可高于 y；y 只作为面板显示容量和自动补充阈值。
        Fuel.Fuel = new Vector2(Mathf.Max(0f, Fuel.Fuel.x), configuredFuel.Fuel.y);
        burnSpeed = fuelConfig.Value("burnSpeedMultiplier", fuelAuthoring.burnSpeedMultiplier);
        AcceptsAirflow = config.Value("acceptsMechanicalBellows", authoring.acceptsMechanicalBellows);
        absorbDroppedFuel = config.Value("absorbDroppedFuel", authoring.absorbDroppedFuel);
        byproducts = config.Value("fuelByproductRules", authoring.fuelByproductRules) ?? new();
        FurnaceFuelByproductProcessor.ValidateRules(byproducts);
        ignitionIds = config.Value("ignitionItemIds", authoring.ignitionItemIds) ?? new();
        ignitionTags = config.Value("ignitionTags", authoring.ignitionTags) ?? new();
        ignitionFuel = config.Value("ignitionFuelValueOverride", authoring.ignitionFuelValueOverride);
        ignitionTemperature = config.Value("ignitionMaxTemperatureOverride", authoring.ignitionMaxTemperatureOverride);
        heatVessels = entity.Definition.Content.Has<Mod_VesselHeating>();
        PanelPrefab = authoring.UI_Prefab;
        publishCellTemperature = config.Value("publishCellTemperature", authoring.publishCellTemperature);
        neighborTemperatureOffset = config.Value("neighborTemperatureOffset", authoring.neighborTemperatureOffset);
        LocalTemperatureSource source = authoring.GetComponent<LocalTemperatureSource>();
        hasLocalHeat = source != null || publishCellTemperature;
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
        if (temperatureManager != null)
        {
            temperatureManager.RemoveLocalTemperatureSource(this);
            temperatureManager.RemoveCellTemperatureSource(this);
        }
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
        if (absorbDroppedFuel) AbsorbDroppedFuelFromCell();
        bool wasBurning = IsBurning;
        if (Data.IsSmelting)
        {
            RefillFuelToVisibleCapacity();
            float remaining = seconds;
            int refills = 0;
            while (remaining > .00001f && refills < 64)
            {
                if (Fuel.Fuel.x <= .01f)
                {
                    RefillFuelToVisibleCapacity();
                    if (Fuel.Fuel.x <= .01f) { Data.IsSmelting = false; break; }
                    refills++;
                }
                float rate = Mathf.Max(0f, burnSpeed * GameDifficultyService.Current.Production.FuelConsumptionMultiplier);
                float step = rate > 0f ? Mathf.Min(remaining, Fuel.Fuel.x / rate) : remaining;
                if (step <= 0f) break;
                HeatAndProcess(step);
                ConsumeFuel(step);
                remaining -= step;
            }
            if (Data.IsSmelting)
            {
                // 每个 Tick 结束前重新补到显示上限以上，避免燃料条归零和火光闪灭。
                RefillFuelToVisibleCapacity();
                if (Fuel.Fuel.x <= .01f) Data.IsSmelting = false;
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
        float bellowsBonus = Mathf.Clamp01(airflow) * MachineCatalog.Settings.BellowsHeatBonus;
        return Mathf.Min(Data.MaxTemperatureLimit + bellowsBonus, baseLimit + bellowsBonus + combustionHeatBonus);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public virtual void ConsumeFuel(float seconds)
    {
        float consumed = Mathf.Max(0f, seconds * burnSpeed * GameDifficultyService.Current.Production.FuelConsumptionMultiplier);
        Fuel.Fuel.x = Mathf.Max(0f, Fuel.Fuel.x - consumed);
    }

    /// <summary>燃值低于显示容量时持续补料，最后一份燃料允许完整越过容量并作为隐藏储备保留。</summary>
    private void RefillFuelToVisibleCapacity()
    {
        if (Fuel.Fuel.y <= 0f) return;
        int safety = 0;
        while (Fuel.Fuel.x + .01f < Fuel.Fuel.y && safety++ < 4096)
        {
            if (!TryFeedFuel(false, null)) break;
        }
    }

    /// <summary>把落在炉体同一格的有效燃料转入燃料库存，供自动补燃逻辑继续消费。</summary>
    private void AbsorbDroppedFuelFromCell()
    {
        if (FuelInventory?.Data?.itemSlots == null || FuelInventory.Data.itemSlots.Count == 0) return;

        Vector2 center = new(Entity.Cell.x + .5f, Entity.Cell.y + .5f);
        const float sameCellRadius = .72f;
        int safety = 0;
        while (safety++ < 64 && DroppedItemService.TryFindNearestTagged(
                   center,
                   sameCellRadius,
                   Tag.CombustionFuel,
                   out DroppedItemHandle handle,
                   position => MachineWorld.CellOf(position) == Entity.Cell))
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
        ItemSlot slot;
        FuelData offered;
        bool tinder;
        if (requireIgnition)
        {
            if (!TryFindFuelSlot(true, out slot, out offered))
            {
                if (!HasHeldIgnition(actor) || !TryFindFuelSlot(false, out slot, out offered)) return false;
            }
        }
        else if (!TryFindFuelSlot(false, out slot, out offered) && !TryFindFuelSlot(true, out slot, out offered))
        {
            return false;
        }

        tinder = IsIgnitionFuel(slot.itemData);
        float value = tinder ? Mathf.Min(offered.Fuel.x, ignitionFuel) : offered.Fuel.x;
        float limit = tinder ? Mathf.Min(offered.MaxTemperature, ignitionTemperature) : offered.MaxTemperature;
        if (!MachineDefinition.Positive(value) || !MachineDefinition.Positive(limit) || Fuel.Fuel.y <= 0f) return false;
        if (!FuelInventory.Data.TryConsumeFromSlot(slot, 1, out ItemData consumed)) return false;
        Fuel.Fuel.x += value;
        Data.MaxTemperature = limit;
        FurnaceFuelByproductProcessor.RecordConsumedFuel(consumed, byproducts, Data);
        FurnaceFuelByproductProcessor.TryFlushPendingOutputs(FuelInventory.Data, byproducts, Data, GameRes.Instance);
        return true;
    }

    /// <summary>燃料槽按语义挑选，避免槽位顺序决定点火是否成功。</summary>
    private bool TryFindFuelSlot(bool ignitionFuelOnly, out ItemSlot selected, out FuelData offered)
    {
        selected = null;
        offered = null;
        if (FuelInventory?.Data?.itemSlots == null) return false;

        for (int i = 0; i < FuelInventory.Data.itemSlots.Count; i++)
        {
            ItemSlot slot = FuelInventory.Data.itemSlots[i];
            ItemData item = slot?.itemData;
            if (item == null || FuelInventory.IsSlotBeingDragged(i) ||
                item.Tags?.ContainsTag(Tag.CombustionFuel) != true ||
                IsIgnitionFuel(item) != ignitionFuelOnly ||
                !Mod_Fuel.TryResolveItemData(slot.itemData, out FuelData candidate))
            {
                continue;
            }

            selected = slot;
            offered = candidate;
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
        if (!IsBurning)
        {
            if (temperatureManager != null)
            {
                temperatureManager.RemoveLocalTemperatureSource(this);
                temperatureManager.RemoveCellTemperatureSource(this);
            }
            return;
        }
        temperatureManager = TemperatureMgr.Instance;
        if (temperatureManager == null)
            return;

        if (publishCellTemperature)
        {
            temperatureManager.RemoveLocalTemperatureSource(this);
            temperatureManager.SetCellTemperatureSource(
                this,
                Entity.Position,
                Data.Temperature,
                neighborTemperatureOffset);
        }
        else
        {
            temperatureManager.RemoveCellTemperatureSource(this);
            temperatureManager.SetLocalTemperatureSource(this, Entity.Position, heatRadius, heatOffset);
        }
    }
    #endregion

    #region 熔炼
    [MethodImpl(MethodImplOptions.NoInlining)]
    public virtual void HeatAndProcess(float seconds)
    {
        combustionHeatBonus = 0;
        float fuelUnits = Mathf.Min(Fuel.Fuel.x, Mathf.Max(0f, seconds * burnSpeed * GameDifficultyService.Current.Production.FuelConsumptionMultiplier));
        MachineWorld.SupplyCombustionSupport(this, fuelUnits, seconds);
        bool hasInput = false;
        foreach (ItemSlot slot in Input.Data.itemSlots) hasInput |= slot?.itemData != null;
        float airflow = AcceptsAirflow ? Entity.Airflow : 0f;
        float maximum = Mathf.Max(1f, CalculateMaximumTemperature(airflow));
        Data.Temperature = Mathf.Min(maximum, Data.Temperature + Data.TemperatureUpSpeed *
            (1f + airflow) * (hasInput ? 1f : 2f) * seconds);
        if (!hasInput) { Processor.State.Progress = 0f; return; }
        if (MachineInventory.IsBeingDragged(Input) || MachineInventory.IsBeingDragged(Output)) return;
        foreach (ItemSlot slot in Input.Data.itemSlots)
        {
            ItemData item = slot?.itemData;
            if (item != null && ItemMatterRuntime.Advance(item, Data.Temperature, 1f, seconds))
                Input.Data.NotifyItemStateChanged(item);
        }
        if (heatVessels && InventoryVesselHeating.ProcessHeat(Input, Output, Data.Temperature, seconds))
        { Processor.State.Progress = 0f; return; }
        RefreshRecipe();
        Data.SmeltingSpeed = Mathf.Lerp(1f, Data.MaxSmeltingSpeed, Data.Temperature / maximum);
        Processor.Advance(Data.SmeltingSpeed * GameDifficultyService.Current.Production.SmeltingSpeedMultiplier * seconds, productionActor);
    }

    /// <summary>炉体只接收已结算的助燃效果，不查找设备或修改来源库存。</summary>
    public void AcceptCombustionSupport(float temperatureBonus)
    {
        if (MachineDefinition.NonNegative(temperatureBonus)) combustionHeatBonus = temperatureBonus;
    }

    private void RefreshRecipe()
    {
        if (Processor == null || Processor.IsCommitting) return;
        if (ItemReactionResolver.TryResolve(Input, Processor.Capabilities,
                out RuntimeItemReactionDefinition reaction, out _, requireLiquidOutput: false))
        {
            activeReaction = reaction;
            Processor.SelectRecipe(reaction.Recipe, reaction.WorkRequired);
            return;
        }

        activeReaction = null;
        bool found = CraftingRecipeMatcher.TryMatch(Input, Processor.Capabilities, out CraftingRecipeMatch match, out _);
        Processor.SelectRecipe(found ? match.Recipe : null, 100f);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public virtual CraftingResult ResolveOutputs()
    {
        RuntimeRecipe recipe = Processor.Recipe;
        if (recipe == null) return CraftingResult.Failed(CraftingFailureReason.RecipeNotFound, "当前材料没有熔炼配方");
        float reactionTemperature = Data.Temperature;
        if (activeReaction != null &&
            CraftingRecipeMatcher.TryMatchRecipe(Input, recipe, Processor.Capabilities, out CraftingRecipeMatch reactionMatch))
        {
            reactionTemperature = ItemMatterRuntime.GetMinimumConsumedTemperature(Input, reactionMatch, Data.Temperature);
            if (reactionTemperature < (activeReaction.MinTemperature ?? float.MinValue))
                return CraftingResult.Failed(CraftingFailureReason.ConditionsNotMet, "材料温度不足", recipe);
            if (reactionTemperature > (activeReaction.MaxTemperature ?? float.MaxValue))
            {
                ItemData charredReaction = GameRes.Instance.CreateItemData("CharredMatter");
                charredReaction.Stack.Amount = 1f;
                return CraftingResult.Succeeded(recipe, new[] { charredReaction });
            }
            return CraftingService.DescribeRecipe(recipe);
        }

        if (reactionTemperature < recipe.Temperature)
            return CraftingResult.Failed(CraftingFailureReason.ConditionsNotMet, "炉温不足", recipe);
        if (reactionTemperature <= recipe.Temperature_Max) return CraftingService.DescribeRecipe(recipe);
        ItemData charred = GameRes.Instance.CreateItemData("CharredMatter");
        charred.Stack.Amount = 1f;
        return CraftingResult.Succeeded(recipe, new[] { charred });
    }

    /// <summary>说明材料加热与加工阻塞，避免只看炉温误判机器停转。</summary>
    public string GetProcessingHint()
    {
        CraftingResult preview = Processor.Preview();
        if (activeReaction != null && preview.Message == "材料温度不足" &&
            CraftingRecipeMatcher.TryMatchRecipe(Input, Processor.Recipe, Processor.Capabilities, out CraftingRecipeMatch match))
        {
            float current = ItemMatterRuntime.GetMinimumConsumedTemperature(Input, match, Data.Temperature);
            // 当前燃料的温度上限不足时明确提示换燃料，避免一直等待。
            float maximum = CalculateMaximumTemperature(AcceptsAirflow ? Entity.Airflow : 0f);
            string action = !IsBurning ? "补充燃料并点火" : maximum < activeReaction.MinTemperature
                ? $"当前燃料最高 {maximum:0}°C · 需要更高温燃料或鼓风"
                : "正在加热";
            return $"材料 {current:0}°C / 需要 {activeReaction.MinTemperature:0}°C · " +
                action;
        }
        if (!preview.Success) return preview.Message;
        return IsBurning ? "正在加工 · 等待产出" : "材料就绪 · 补充燃料并点火";
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
        float reactionTemperature = Data.Temperature;
        if (activeReaction != null &&
            CraftingRecipeMatcher.TryMatchRecipe(Input, Processor.Recipe, Processor.Capabilities, out CraftingRecipeMatch reactionMatch))
            reactionTemperature = ItemMatterRuntime.GetMinimumConsumedTemperature(Input, reactionMatch, Data.Temperature);
        CraftingResult result = CraftingService.CraftRecipeOutputs(Input, Output, Processor.Capabilities, Processor.Recipe,
            outputs.Outputs, reactionTemperature <= Processor.Recipe.Temperature_Max, actor, publishCrafting: false);
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
