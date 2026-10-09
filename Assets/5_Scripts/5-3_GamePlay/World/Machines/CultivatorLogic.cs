using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FlatWorld.Networking;
using FlatWorld.Spaceflight;
using MemoryPack;
using UnityEngine;
using UnityEngine.SceneManagement;

[Serializable, MemoryPackable]
public sealed partial class CultivatorHarvestOutput
{
    #region 已抽定收获
    public string ItemId;
    public int Amount;
    #endregion
}

[Serializable, MemoryPackable]
public sealed partial class CultivatorPlotState
{
    #region 独立植株
    public string CropId;
    public float Growth;
    public bool HasCompletedFirstGrowth;
    public List<CultivatorHarvestOutput> PendingHarvest;
    public int YieldGeneVariantIndex = -1;
    public uint Seed;
    #endregion
}

[Serializable, MemoryPackable]
public sealed partial class CultivatorState
{
    #region 机器快照
    public InventoryInstanceSnapshot Seeds;
    public InventoryInstanceSnapshot Harvest;
    public InventoryInstanceSnapshot Fertilizer;
    public List<CultivatorPlotState> Plots = new();
    public float FertilizerSeconds;
    public ulong RandomState;
    #endregion
}

/// <summary>培育器复用农作物定义，缺少电或水时只暂停本轮有效成长。</summary>
public sealed class CultivatorLogic : MachineLogic, IContainerPortProvider
{
    #region 权威状态与真实库存
    public CultivatorState State { get; private set; }
    public CultivatorConfiguration Configuration { get; }
    public Inventory Seeds { get; }
    public Inventory Harvest { get; }
    public Inventory Fertilizer { get; }
    public FluidInventory Water { get { using var scope = MachineWorld.UseNodeScope(Entity); return MachineWorld.GetFluidInventory(Entity); } }
    public override float TickInterval => .2f;
    public override GameObject PanelPrefab => GameRes.Instance.GetPrefab(Configuration.PanelId);
    public override string ActionLabel => "收获成熟作物";
    public override bool CanAct => State.Plots.Any(plot => plot.CropId != null && plot.Growth >= 1f);
    public ILiquidTransferPort WaterInputPort => waterPort == null || !waterPort.IsValid ? waterPort = new CultivatorWaterPort(this) : waterPort;
    private CultivatorWaterPort waterPort;
    private readonly Dictionary<string, CropProfile> profiles = new(StringComparer.Ordinal);
    private bool committing;
    private float lastSuppliedWatts = float.NaN;
    private decimal lastIrrigationLiters = -1m;

    public CultivatorLogic(MachineEntity entity) : base(entity)
    {
        var config = entity.Definition.Content.Find<Mod_Cultivator>()
            ?? throw new InvalidOperationException("培育器缺少正式模块配置。");
        Configuration = config.Value(nameof(Mod_Cultivator.Configuration), ((Mod_Cultivator)config.Authoring).Configuration);
        Configuration.Validate();
        if (entity.Definition.Electrical?.IsConsumer != true || entity.Definition.Fluid == null)
            throw new InvalidOperationException("培育器必须组合真实电气输入和流体库存。");
        State = MachineModuleState.Read<CultivatorState>(entity.Snapshot, Mod_Cultivator.ModuleId) ?? new();
        if (State.Plots == null) throw new InvalidOperationException("培育器快照缺少种植槽状态。");
        if (State.Plots.Count == 0)
            for (int i = 0; i < Configuration.PlotCount; i++) State.Plots.Add(new());
        if (State.Plots.Count != Configuration.PlotCount)
            throw new InvalidOperationException("培育器存档的种植槽数量与当前定义不符。");
        if (State.RandomState == 0) State.RandomState = unchecked((ulong)(uint)entity.Snapshot.Guid * 0x9E3779B97F4A7C15UL) | 1UL;
        Seeds = Track(CreateInventory("培育器种子", Configuration.PlotCount, State.Seeds,
            value => TryResolveSeed(value, out _)));
        Harvest = Track(CreateInventory("培育器收获", Configuration.HarvestSlotCount, State.Harvest));
        Fertilizer = Track(CreateInventory("培育器肥料", 1, State.Fertilizer, IsFertilizer));
        // 辣椒等可播种果实不一定带 Seed 标签，接收以真实播种模块为准。
        foreach (ItemSlot slot in Seeds.Data.itemSlots) slot.CanAcceptTags = null;
        Fertilizer.Data.itemSlots[0].CanAcceptTags = null;
        // 输出只提供取物，收获提交在统一数据事务中绕过该用户存放开关。
        Harvest.Data.IsDepositBlocked = true;
        foreach (CultivatorPlotState plot in State.Plots)
            if (plot == null || !float.IsFinite(plot.Growth) || plot.Growth < 0f || plot.Growth > 1f)
                throw new InvalidOperationException("培育器植株进度无效。");
    }

    private static Inventory CreateInventory(string name, int count, InventoryInstanceSnapshot saved, Func<ItemData, bool> accepts = null)
    {
        Inventory inventory = accepts == null ? MachineInventory.Create(null, name: name, count: count) :
            new CultivatorFilteredInventory(accepts) { Data = MachineInventory.NewData(name, count) };
        if (accepts != null) inventory.InitData();
        inventory.Data.SetUnlimitedSlots(false);
        inventory.Data.SetUnlimitedStackSize(false);
        if (saved != null)
        {
            if (saved.SlotCount != count) throw new InvalidOperationException("培育器库存快照槽数无效。");
            saved.RestoreTo(inventory.Data, value => ItemDefinitionRuntime.RebasePersistedData(GameRes.ExistingInstance, value));
        }
        return inventory;
    }
    public override void Capture()
    {
        using var scope = MachineWorld.UseNodeScope(Entity);
        State.Seeds = InventoryInstanceSnapshot.Capture(Seeds.Data);
        State.Harvest = InventoryInstanceSnapshot.Capture(Harvest.Data);
        State.Fertilizer = InventoryInstanceSnapshot.Capture(Fertilizer.Data);
        MachineModuleState.Write(Entity.Snapshot, Mod_Cultivator.ModuleId, State);
        MachineWorld.CaptureFluidState(Entity);
    }
    public override bool ApplyRemoteSnapshot(ItemData snapshot)
    {
        CultivatorState incoming = MachineModuleState.Read<CultivatorState>(snapshot, Mod_Cultivator.ModuleId);
        if (incoming?.Plots?.Count != Configuration.PlotCount || incoming.Seeds?.SlotCount != Configuration.PlotCount ||
            incoming.Harvest?.SlotCount != Configuration.HarvestSlotCount || incoming.Fertilizer?.SlotCount != 1) return false;
        incoming.Seeds.RestoreTo(Seeds.Data, Rebase);
        incoming.Harvest.RestoreTo(Harvest.Data, Rebase);
        incoming.Fertilizer.RestoreTo(Fertilizer.Data, Rebase);
        State = incoming;
        NotifyRemoteChanged();
        return true;
    }
    private static ItemData Rebase(ItemData value) => ItemDefinitionRuntime.RebasePersistedData(GameRes.ExistingInstance, value);
    protected override void OnInventoryChanged(ItemSlot slot) { if (!committing) base.OnInventoryChanged(slot); }
    #endregion

    #region 生长与收获
    public override void Tick(float seconds)
    {
        using var scope = MachineWorld.UseNodeScope(Entity);
        if (!MachineDefinition.Positive(seconds)) return;
        bool changed = PlantEmptyPlots();
        decimal currentWater = IrrigationLiters();
        changed |= lastSuppliedWatts != Entity.ElectricalSuppliedWatts || lastIrrigationLiters != currentWater;
        lastSuppliedWatts = Entity.ElectricalSuppliedWatts;
        lastIrrigationLiters = currentWater;
        float ratio = Mathf.Clamp01(Entity.ElectricalSuppliedWatts / Entity.Definition.Electrical.PowerWatts);
        float difficulty = Mathf.Max(0f, GameDifficultyService.Current.Production.CropGrowthMultiplier);
        var growing = State.Plots.Where(plot => plot.CropId != null && plot.Growth < 1f && CanAdvance(plot, difficulty)).ToList();
        if (ratio <= 0f || growing.Count == 0 || MachineWorld.GetFluidState(Entity).Ruptured)
        { if (changed) NotifyChanged(); return; }
        decimal availableLiters = IrrigationLiters();
        decimal requestedLiters = Configuration.WaterLitersPerSecondPerPlot * (decimal)(seconds * ratio) * growing.Count;
        if (availableLiters <= 0m || requestedLiters <= 0m)
        { if (changed) NotifyChanged(); return; }
        decimal consumedLiters = Math.Min(availableLiters, requestedLiters);
        float effectiveSeconds = seconds * ratio * (float)(consumedLiters / requestedLiters);
        if (!ConsumeIrrigation(consumedLiters)) { if (changed) NotifyChanged(); return; }
        if (State.FertilizerSeconds <= 0f)
        {
            ItemSlot slot = Fertilizer.Data.itemSlots[0];
            if (slot.itemData != null && !Fertilizer.IsSlotBeingDragged(0) && IsFertilizer(slot.itemData) &&
                Fertilizer.Data.TryConsumeFromSlot(slot, 1, out _)) State.FertilizerSeconds = Configuration.FertilizerSecondsPerItem;
        }
        float boostedSeconds = Mathf.Min(effectiveSeconds, Mathf.Max(0f, State.FertilizerSeconds));
        float growthSeconds = effectiveSeconds + boostedSeconds * (Configuration.FertilizerGrowthMultiplier - 1f);
        State.FertilizerSeconds = Mathf.Max(0f, State.FertilizerSeconds - effectiveSeconds);
        foreach (CultivatorPlotState plot in growing)
        {
            CropProfile profile = Profile(plot.CropId);
            AdvanceGrowth(plot, profile, growthSeconds, difficulty);
            if (plot.Growth >= 1f && plot.PendingHarvest == null) plot.PendingHarvest = RollHarvest(plot, profile);
        }
        NotifyChanged();
    }
    private bool CanAdvance(CultivatorPlotState plot, float difficulty)
    {
        CropProfile profile = Profile(plot.CropId);
        if (profile.Production == null) return difficulty > 0f;
        float duration = plot.HasCompletedFirstGrowth ? profile.RepeatSeconds : profile.GrowthSeconds;
        float elapsed = plot.Growth * duration;
        if (!plot.HasCompletedFirstGrowth && elapsed < profile.CropGrowthSeconds) return difficulty > 0f;
        return profile.CalendarInterval || profile.ProductionRate > 0f && (!profile.ProductionUsesGrowthMultiplier || difficulty > 0f);
    }
    private static void AdvanceGrowth(CultivatorPlotState plot, CropProfile profile, float seconds, float difficulty)
    {
        float duration = plot.HasCompletedFirstGrowth && profile.Repeatable ? profile.RepeatSeconds : profile.GrowthSeconds;
        if (profile.Production == null)
        { plot.Growth = Mathf.Clamp01(plot.Growth + seconds * difficulty / duration); return; }
        float elapsed = plot.Growth * duration;
        if (!plot.HasCompletedFirstGrowth && elapsed < profile.CropGrowthSeconds)
        {
            if (difficulty <= 0f) return;
            float cropSeconds = Mathf.Min(seconds, (profile.CropGrowthSeconds - elapsed) / difficulty);
            elapsed += cropSeconds * difficulty;
            seconds -= cropSeconds;
        }
        // 周期果实继续使用原生日历时长，生长难度只影响声明接受它的阶段。
        float rate = profile.CalendarInterval ? 1f : profile.ProductionRate * (profile.ProductionUsesGrowthMultiplier ? difficulty : 1f);
        plot.Growth = Mathf.Clamp01((elapsed + seconds * rate) / duration);
    }
    private bool PlantEmptyPlots()
    {
        bool changed = false;
        for (int i = 0; i < State.Plots.Count; i++)
        {
            CultivatorPlotState plot = State.Plots[i];
            ItemSlot seedSlot = Seeds.Data.itemSlots[i];
            if (plot.CropId != null || Seeds.IsSlotBeingDragged(i) || !TryResolveSeed(seedSlot.itemData, out string cropId)) continue;
            if (!Seeds.Data.TryConsumeFromSlot(seedSlot, 1, out _)) continue;
            plot.CropId = cropId;
            plot.Seed = (uint)NextRandom();
            changed = true;
        }
        return changed;
    }
    public bool HarvestMature()
    {
        using var scope = MachineWorld.UseNodeScope(Entity);
        if (!GameNetwork.HasStateAuthority || MachineInventory.IsBeingDragged(Harvest)) return false;
        bool changed = false;
        foreach (CultivatorPlotState plot in State.Plots)
        {
            if (plot.CropId == null || plot.Growth < 1f) continue;
            CropProfile profile = Profile(plot.CropId);
            plot.PendingHarvest ??= RollHarvest(plot, profile);
            // 完整批次先在候选库存试放，满仓保留成熟植株和已抽定产量。
            Inventory_Data candidate = MachinePersistence.Clone(Harvest.Data);
            candidate.IsDepositBlocked = false;
            bool fits = true;
            foreach (CultivatorHarvestOutput output in plot.PendingHarvest)
            {
                ItemData value = GameRes.Instance.CreateItemData(output.ItemId);
                value.Stack.Amount = output.Amount;
                if (!candidate.TryAddItem(value, true, out float added) || added + .0001f < output.Amount) { fits = false; break; }
            }
            if (!fits) continue;
            candidate.IsDepositBlocked = true;
            committing = true;
            try { MachineInventory.ApplySnapshot(Harvest, candidate); }
            finally { committing = false; }
            plot.PendingHarvest = null;
            plot.Growth = 0f;
            plot.HasCompletedFirstGrowth = true;
            if (!profile.Repeatable) { plot.CropId = null; plot.HasCompletedFirstGrowth = false; }
            changed = true;
        }
        if (changed) { Capture(); NotifyChanged(); }
        return changed;
    }
    public override bool Execute(string operation, string argument, Player actor)
    {
        if (operation == "begin-interaction") return true;
        if (operation == "work" || operation == "cultivator.harvest") return HarvestMature();
        if (operation == "cultivator.fill") return FillFromHeldVessel(actor);
        if (operation == "cultivator.clear" && int.TryParse(argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) &&
            (uint)index < (uint)State.Plots.Count)
        { State.Plots[index] = new(); NotifyChanged(); return true; }
        return false;
    }
    private bool IsFertilizer(ItemData value) => Configuration.FertilizerItemIds.Contains(value.IDName, StringComparer.Ordinal);
    #endregion

    #region 真实水量与容器事务
    private decimal IrrigationLiters()
    {
        decimal result = 0m;
        foreach (string id in Configuration.WaterFluidIds)
            result += FluidUnits.MolToLiquidLiters(Water.Catalog.Find(id), Water.GetAvailableMoles(id, FluidPhase.Liquid));
        return result;
    }
    private bool ConsumeIrrigation(decimal liters)
    {
        if (liters <= 0m || IrrigationLiters() < liters) return false;
        FluidInventoryState before = Water.State.Clone();
        var consumed = new List<(string Id, decimal Moles)>();
        decimal remaining = liters;
        foreach (string id in Configuration.WaterFluidIds)
        {
            FluidDefinition definition = Water.Catalog.Find(id);
            decimal moles = Math.Min(Water.GetAvailableMoles(id, FluidPhase.Liquid), FluidUnits.LiquidLitersToMol(definition, remaining));
            if (moles <= 0m) continue;
            if (!Water.TryTakeExact(id, FluidPhase.Liquid, moles, out _)) { Water.Restore(before); return false; }
            consumed.Add((id, moles));
            remaining -= FluidUnits.MolToLiquidLiters(definition, moles);
            if (remaining > .0000000001m) continue;
            foreach (var entry in consumed) MachineWorld.ConsumeFluidStepStock(Water, entry.Id, 0m, entry.Moles);
            return true;
        }
        Water.Restore(before);
        return false;
    }
    public void CollectContainerPorts(List<IContainerPort> ports)
    { using var scope = MachineWorld.UseNodeScope(Entity); if (MachineWorld.Contains(Entity)) ports.Add(WaterInputPort); }
    public bool FillFromHeldVessel(Player actor)
    {
        var held = actor?.itemMods?.GetMod_ByID<Mod_HotBar>(ModText.Hotbar)?.CurentSelectItem
            ?.itemMods?.GetMod_ByID<Mod_WaterVessel>(Mod_WaterVessel.ModuleId);
        if (held == null || !held.CanOperate(actor)) return false;
        return ContainerTransferService.TransferLiquid(LiquidVesselOperations.Port(held, ContainerPortDirection.Output),
            WaterInputPort, new ContainerTransferContext(actor, ContainerAccessKind.Manual, "培育器加水"), held.Data.Amount).Success;
    }
    internal void CommitWater()
    { using var scope = MachineWorld.UseNodeScope(Entity); MachineWorld.CaptureFluidState(Entity); NotifyChanged(); }
    public override string Status
    {
        get
        {
            using var scope = MachineWorld.UseNodeScope(Entity);
            var text = new StringBuilder();
            text.AppendFormat("供电 {0:0.#}/{1:0.#} W · 可灌溉水 {2:0.###} L\n肥料加速剩余 {3:0.#} 秒\n",
                Entity.ElectricalSuppliedWatts, Entity.Definition.Electrical.PowerWatts, IrrigationLiters(), State.FertilizerSeconds);
            for (int i = 0; i < State.Plots.Count; i++)
            {
                CultivatorPlotState plot = State.Plots[i];
                string name = plot.CropId != null && GameRes.Instance.TryGetItemDefinition(plot.CropId, out var definition) ? definition.DisplayName : "空槽";
                string status = plot.CropId == null ? "等待种子（树木不支持）" : plot.Growth >= 1f ? "成熟，等待收获" :
                    Entity.ElectricalSuppliedWatts <= 0f ? "缺电，已暂停" : IrrigationLiters() <= 0m ? "缺水，已暂停" : "正在生长";
                text.AppendFormat("{0}：{1} {2:0.#}% · {3}\n", i + 1, name, plot.Growth * 100f, status);
            }
            return text.ToString();
        }
    }
    #endregion

    #region 从当前作物定义读取成长和产物
    private sealed class CropProfile
    {
        public float GrowthSeconds, CropGrowthSeconds, RepeatSeconds, ProductionRate = 1f;
        public bool Repeatable, CalendarInterval, ProductionUsesGrowthMultiplier;
        public List<CropYieldEntry> Outputs;
        public Mod_Production.ItemProductionData Production;
    }
    private bool TryResolveSeed(ItemData seed, out string cropId)
    {
        cropId = null;
        if (seed?.Stack?.Amount < 1f || seed == null ||
            !GameRes.Instance.TryGetItemDefinition(seed.IDName, out var definition)) return false;
        MachineModuleConfiguration planting = new MachineContent(definition).Find<Mod_Plantable>();
        string candidate = planting?.Value("cropItemId", string.Empty);
        if (string.IsNullOrEmpty(candidate) || !GameRes.Instance.TryGetItemDefinition(candidate, out var crop)) return false;
        MachineContent content = new(crop);
        if (!content.Has<Mod_Crop>() || crop.HasTag("Tree")) return false;
        Profile(candidate);
        cropId = candidate;
        return true;
    }
    private CropProfile Profile(string cropId)
    {
        if (profiles.TryGetValue(cropId, out CropProfile profile)) return profile;
        if (!GameRes.Instance.TryGetItemDefinition(cropId, out var definition))
            throw new InvalidOperationException("培育器中的作物定义缺失：" + cropId);
        MachineContent content = new(definition);
        MachineModuleConfiguration growth = content.Find<Mod_Crop>();
        if (growth == null) throw new InvalidOperationException("培育器只支持非树农作物：" + cropId);
        profile = new() { GrowthSeconds = growth.Value("growthDurationSeconds", 500f), Outputs = new() };
        profile.CropGrowthSeconds = profile.GrowthSeconds;
        MachineModuleConfiguration yield = content.Find<Mod_CropYield>();
        if (yield != null) profile.Outputs = yield.Value("outputs", ((Mod_CropYield)yield.Authoring).outputs);
        else
        {
            MachineModuleConfiguration production = content.Find<Mod_Production>();
            List<Mod_Production.ItemProductionData> entries = production?.Value<List<Mod_Production.ItemProductionData>>("ProductionList", null);
            if (entries?.Count != 1) throw new InvalidOperationException("周期作物必须提供一个有效生产定义：" + cropId);
            profile.Production = entries[0];
            profile.Repeatable = !entries[0].DestroySelf;
            profile.CalendarInterval = entries[0].ProductionIntervalDays > 0f;
            profile.ProductionRate = production.Value("ProductionSpeed", ((Mod_Production)production.Authoring).ProductionSpeed);
            profile.ProductionUsesGrowthMultiplier = production.Value("UseCropGrowthMultiplier", ((Mod_Production)production.Authoring).UseCropGrowthMultiplier);
            profile.RepeatSeconds = ProductionDuration(entries[0]);
            profile.GrowthSeconds += profile.RepeatSeconds;
            profile.Outputs.Add(new() { itemId = entries[0].itemName, minAmount = entries[0].itemCountMin,
                maxAmount = entries[0].itemCountMax, probability = entries[0].SpawnProbability });
        }
        if (!MachineDefinition.Positive(profile.GrowthSeconds) || profile.Outputs == null || profile.Outputs.Count == 0)
            throw new InvalidOperationException("培育器作物的成长时长或产物定义无效：" + cropId);
        foreach (CropYieldEntry output in profile.Outputs)
            if (output == null || !GameRes.Instance.TryGetItemDefinition(output.itemId, out _) || output.minAmount < 0 ||
                output.maxAmount < output.minAmount || !float.IsFinite(output.probability) || output.probability < 0f || output.probability > 1f)
                throw new InvalidOperationException("培育器作物收获表无效：" + cropId);
        profiles.Add(cropId, profile);
        return profile;
    }
    private List<CultivatorHarvestOutput> RollHarvest(CultivatorPlotState plot, CropProfile profile)
    {
        var outputs = new List<CultivatorHarvestOutput>();
        float multiplier = Mathf.Max(0f, GameDifficultyService.Current.World.LootAmountMultiplier);
        foreach (CropYieldEntry output in profile.Outputs)
        {
            if (NextRandom01() > Mathf.Clamp01(output.probability * (profile.Production == null ? multiplier : 1f))) continue;
            int min = output.minAmount, max = output.maxAmount;
            if (profile.Production != null)
            {
                profile.Production.ResolveYieldGene(plot.Seed, plot.YieldGeneVariantIndex, out int gene, out min, out max);
                plot.YieldGeneVariantIndex = gene;
            }
            int amount = min + (int)(NextRandom() % (uint)(max - min + 1));
            if (profile.Production == null) amount = GameDifficultyService.ScaleRandomizedAmount(amount, multiplier);
            if (amount > 0) outputs.Add(new() { ItemId = output.itemId, Amount = amount });
        }
        return outputs;
    }
    private ulong NextRandom()
    { ulong value = State.RandomState; value ^= value >> 12; value ^= value << 25; value ^= value >> 27; State.RandomState = value; return value * 2685821657736338717UL; }
    private float NextRandom01() => (NextRandom() >> 40) / 16777216f;
    private static float ProductionDuration(Mod_Production.ItemProductionData production)
    {
        if (production.ProductionIntervalDays > 0f && DayTimeSystem.Instance != null &&
            DayTimeSystem.Instance.TryGetActiveTimeData(out TimeData time) && time.DayLength > 0f)
            return Mathf.Max(.01f, production.ProductionIntervalDays * time.DayLength);
        return Mathf.Max(.01f, production.MaxProductionTime);
    }
    #endregion
}

internal sealed class CultivatorFilteredInventory : Inventory
{
    #region 种子和肥料接收条件
    private readonly Func<ItemData, bool> accepts;
    public CultivatorFilteredInventory(Func<ItemData, bool> accepts) => this.accepts = accepts;
    public override bool CanAcceptQuickTransfer(ItemSlot sourceSlot, ItemSlot targetSlot)
        => base.CanAcceptQuickTransfer(sourceSlot, targetSlot) && sourceSlot?.itemData != null && accepts(sourceSlot.itemData);
    #endregion
}

/// <summary>水桶与管路都访问培育器的同一实际腔体，手动转移沿公共两端回滚事务。</summary>
internal sealed class CultivatorWaterPort : ContainerPortBase, ILiquidTransferPort
{
    #region 唯一灌溉入水口
    private readonly CultivatorLogic logic;
    public CultivatorWaterPort(CultivatorLogic logic) : base("machine:" + logic.Entity.Id, "cultivator-water",
        new ContainerPortConfiguration { Id = "cultivator-water", Direction = ContainerPortDirection.Input,
            LiquidIds = logic.Configuration.WaterFluidIds, Access = ContainerAccessKind.Manual | ContainerAccessKind.Mod, Reach = 3f },
        () => IsCurrent(logic), position: logic.Entity.Position) => this.logic = logic;
    private static bool IsCurrent(CultivatorLogic logic)
    { using var scope = MachineWorld.UseNodeScope(logic.Entity); return MachineWorld.Contains(logic.Entity) && ReferenceEquals(logic.Entity.Logic, logic); }
    public override object StorageIdentity => logic.Water.State;
    public override long StateVersion => unchecked(logic.Water.GasMoles.GetHashCode() * 397L ^ logic.Water.LiquidMoles.GetHashCode() ^ logic.Water.State.TotalInternalEnergyJoules.GetHashCode());
    public override bool CanAccess(ContainerTransferContext context, out ContainerTransferFailure failure)
    {
        using var scope = MachineWorld.UseNodeScope(logic.Entity);
        failure = ContainerTransferFailure.None;
        if (!IsValid) { failure = ContainerTransferFailure.StaleReference; return false; }
        if ((Configuration.Access & context.Access) != context.Access || context.Access == 0)
        { failure = ContainerTransferFailure.AccessDenied; return false; }
        bool ship = logic.Entity.ScopeKey?.StartsWith("ship:", StringComparison.Ordinal) == true;
        Scene scene = ship ? default : SceneManager.GetSceneByName(MachineWorld.WorldKey);
        if ((ship && !SpaceSession.TryGetMachineScene(logic.Entity, out scene)) || !scene.IsValid())
        { failure = ContainerTransferFailure.OutOfRange; return false; }
        float distance = ship ? ((Vector2)logic.Entity.Position - context.Position).sqrMagnitude :
            WorldTopologyRuntime.SqrDistance(logic.Entity.Position, context.Position);
        if (context.SceneHandle != scene.handle || distance > Configuration.Reach * Configuration.Reach)
        { failure = ContainerTransferFailure.OutOfRange; return false; }
        if (MachineWorld.GetFluidState(logic.Entity).Ruptured)
        { failure = ContainerTransferFailure.StaleReference; return false; }
        return true;
    }
    public bool PeekLiquid(out LiquidTransferBatch batch) { batch = default; return false; }
    public bool ReserveLiquid(out LiquidTransferBatch batch) { batch = default; return false; }
    public bool ExtractLiquid(LiquidTransferBatch batch, float servings) => false;
    public float GetReceivableServings(string id, float requested)
    {
        using var scope = MachineWorld.UseNodeScope(logic.Entity);
        if (!IsValid || !Configuration.AllowsLiquid(id) || !logic.Water.Catalog.TryGet(id, out var definition)) return 0f;
        double availableLiters = Math.Max(0d, MachineWorld.GetFluidVolumeLiters(logic.Entity) -
            MachineWorld.GetFluidMinimumGasSpaceLiters(logic.Entity) - logic.Water.GetLiquidLiters());
        return Mathf.Min(requested, (float)((decimal)availableLiters / definition.LitersPerServing));
    }
    public bool InsertLiquid(LiquidTransferBatch batch, float servings)
    {
        using var scope = MachineWorld.UseNodeScope(logic.Entity);
        if (!GameNetwork.HasStateAuthority || !float.IsFinite(servings) || servings <= 0f || GetReceivableServings(batch.LiquidId, servings) < servings) return false;
        FluidDefinition definition = logic.Water.Catalog.Find(batch.LiquidId);
        FluidBatch actual = FluidInventory.CreateBatch(definition, 0m, FluidUnits.ServingsToMol(definition, (decimal)servings),
            FluidUnits.CelsiusToKelvin(batch.TemperatureCelsius));
        return logic.Water.TryAdd(actual, MachineWorld.GetFluidVolumeLiters(logic.Entity), MachineWorld.GetFluidMinimumGasSpaceLiters(logic.Entity));
    }
    public override object CaptureState() => logic.Water.State.Clone();
    public override void RestoreState(object snapshot) => logic.Water.Restore((FluidInventoryState)snapshot);
    public override void PublishState() => logic.CommitWater();
    #endregion
}
