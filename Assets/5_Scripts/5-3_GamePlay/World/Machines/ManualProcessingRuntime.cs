using System;
using UnityEngine;

/// <summary>手动工作站的配方选择与逐次工作磨损，数据机器和完整物品共用。</summary>
public sealed class ManualProcessingRuntime : IDisposable
{
    #region 工作站配方与耐久
    public RecipeProcessor Processor { get; }
    public ManualProcessingState State { get; }
    public Inventory Tool { get; }
    public bool Exhausted => wearPerClick > 0f && resolveStation().Durability <= 0f;
    public bool CanWork => !Exhausted && (toolRequirement == null || toolRequirement.CanUse(Tool));
    private readonly Func<ItemData> resolveStation;
    private readonly float wearPerClick;
    private readonly ManualProcessingToolRequirement toolRequirement;

    public ManualProcessingRuntime(Func<ItemData> resolveStation, string stationId, ManualProcessingState state, float wearPerClick,
        ManualProcessingToolRequirement toolRequirement = null)
    {
        this.resolveStation = resolveStation ?? throw new ArgumentNullException(nameof(resolveStation));
        if (!float.IsFinite(wearPerClick) || wearPerClick < 0f)
            throw new ArgumentOutOfRangeException(nameof(wearPerClick));
        this.wearPerClick = wearPerClick;
        this.toolRequirement = toolRequirement;
        State = state ?? throw new ArgumentNullException(nameof(state));
        Processor = new RecipeProcessor(stationId, state.Processing, catalogRecipes: true);
        if (toolRequirement != null)
        {
            MachineInventory.Rebase(state.Tool);
            Tool = new RequiredToolInventory(toolRequirement) { Data = state.Tool };
            Tool.InitData();
            Tool.Data.Event_OnDataChanged += OnToolChanged;
        }
        Processor.BeginWork = CanBeginStationWork;
        Processor.CompleteWork = ConsumeCompletedStationWork;
        Processor.PreviewOverride = Preview;
        Processor.CommitOverride = Commit;
    }

    public bool SelectRecipe(string id)
    {
        Processor.Preview();
        if (!GameRes.ExistingInstance.recipeById.TryGetValue(id ?? string.Empty, out RuntimeRecipe recipe) ||
            !CraftingRecipeMatcher.TryMatchRecipe(Processor.Input, recipe, Processor.Capabilities, out _)) return false;
        Processor.SelectRecipe(recipe, recipe.ProcessingSeconds > 0f ? recipe.ProcessingSeconds : 1f);
        return true;
    }

    private CraftingResult Preview()
    {
        if (Exhausted && !Processor.IsAdvancing)
            return CraftingResult.Failed(CraftingFailureReason.ConditionsNotMet, "工作站耐久不足");
        if (!Processor.IsAdvancing && toolRequirement != null && !toolRequirement.CanUse(Tool))
            return CraftingResult.Failed(CraftingFailureReason.ConditionsNotMet, "需要符合要求且未损坏的工具");
        return Processor.Recipe == null
            ? CraftingResult.Failed(CraftingFailureReason.RecipeNotFound, "当前材料没有加工配方")
            : CraftingService.PreviewRecipe(Processor.Input, Processor.Output, Processor.Capabilities, Processor.Recipe);
    }

    private bool CanBeginStationWork() => FlatWorld.Networking.GameNetwork.HasStateAuthority &&
        CanPerformStationWork(resolveStation(), wearPerClick) &&
        (toolRequirement == null || !MachineInventory.IsBeingDragged(Tool) && toolRequirement.CanUse(Tool));

    private void ConsumeCompletedStationWork()
    {
        toolRequirement?.TryConsume(Tool);
        TryConsumeStationWork(resolveStation(), wearPerClick);
    }

    private void OnToolChanged(ItemSlot _) { if (!Processor.IsAdvancing) Processor.RefreshConditions(); }

    public void ApplyRemoteState(ManualProcessingState incoming)
    {
        if (incoming == null) throw new ArgumentNullException(nameof(incoming));
        if (Tool != null) MachineInventory.ApplySnapshot(Tool, incoming.Tool);
        Processor.ApplyRemoteState(incoming.Processing);
    }

    private CraftingResult Commit(Player actor)
    {
        CraftingResult preview = Preview();
        if (!preview.Success) return preview;
        return CraftingService.CraftRecipe(Processor.Input, Processor.Output, Processor.Capabilities, Processor.Recipe, actor,
            Processor.CommittingWorkingToolMatch);
    }

    public static bool CanPerformStationWork(ItemData station, float wearPerClick)
    {
        return float.IsFinite(wearPerClick) && wearPerClick >= 0f &&
            (wearPerClick == 0f || ItemDurabilityRules.CanConsume(station, wearPerClick));
    }

    public static bool TryConsumeStationWork(ItemData station, float wearPerClick)
    {
        if (!CanPerformStationWork(station, wearPerClick)) return false;
        if (wearPerClick > 0f) station.Durability = Mathf.Max(0f, station.Durability - wearPerClick);
        return true;
    }

    public void Dispose()
    {
        if (Tool != null)
        {
            Tool.Data.Event_OnDataChanged -= OnToolChanged;
            Tool.UnbindController();
            Tool.UnbindRuntimeDataEvents();
            Tool.item = null;
        }
        Processor.Dispose();
    }

    private sealed class RequiredToolInventory : Inventory
    {
        private readonly ManualProcessingToolRequirement requirement;
        public RequiredToolInventory(ManualProcessingToolRequirement requirement) => this.requirement = requirement;
        public override bool CanAcceptQuickTransfer(ItemSlot source, ItemSlot target) =>
            base.CanAcceptQuickTransfer(source, target) && requirement.Matches(source?.itemData);
    }
    #endregion
}
