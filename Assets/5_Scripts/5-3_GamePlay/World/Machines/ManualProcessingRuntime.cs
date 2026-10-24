using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>手动工作站的配方选择与单件产物磨损，数据机器和完整物品共用。</summary>
public sealed class ManualProcessingRuntime : IDisposable
{
    #region 工作站配方与耐久
    public RecipeProcessor Processor { get; }
    public ManualProcessingState State { get; }
    public Inventory Tool { get; }
    public bool Exhausted => wearPerProduct > 0f && resolveStation().Durability <= 0f;
    public bool CanWork => !Exhausted && (toolRequirement == null || toolRequirement.CanUse(Tool));
    private readonly Func<ItemData> resolveStation;
    private readonly float wearPerProduct;
    private readonly ManualProcessingToolRequirement toolRequirement;

    public ManualProcessingRuntime(Func<ItemData> resolveStation, string stationId, ManualProcessingState state, float wearPerProduct,
        ManualProcessingToolRequirement toolRequirement = null)
    {
        this.resolveStation = resolveStation ?? throw new ArgumentNullException(nameof(resolveStation));
        if (!float.IsFinite(wearPerProduct) || wearPerProduct < 0f)
            throw new ArgumentOutOfRangeException(nameof(wearPerProduct));
        this.wearPerProduct = wearPerProduct;
        this.toolRequirement = toolRequirement;
        State = state ?? throw new ArgumentNullException(nameof(state));
        Processor = new RecipeProcessor(stationId, state.Processing, catalogRecipes: true);
        if (toolRequirement != null)
        {
            MachineInventory.Rebase(state.Tool);
            Tool = new RequiredToolInventory(toolRequirement) { Data = state.Tool };
            Tool.InitData();
            Tool.Data.Event_OnDataChanged += OnToolChanged;
            Processor.BeginWork = BeginToolWork;
        }
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
        if (Exhausted)
            return CraftingResult.Failed(CraftingFailureReason.ConditionsNotMet, "工作站耐久不足");
        if (!Processor.IsAdvancing && toolRequirement != null && !toolRequirement.CanUse(Tool))
            return CraftingResult.Failed(CraftingFailureReason.ConditionsNotMet, "需要符合要求且未损坏的工具");
        CraftingResult result = Processor.Recipe == null
            ? CraftingResult.Failed(CraftingFailureReason.RecipeNotFound, "当前材料没有加工配方")
            : CraftingService.PreviewRecipe(Processor.Input, Processor.Output, Processor.Capabilities, Processor.Recipe);
        return result.Success && !CanComplete(resolveStation(), result.Outputs, wearPerProduct, out _)
            ? CraftingResult.Failed(CraftingFailureReason.ConditionsNotMet, "工作站耐久不足以完成本批产物", result.Recipe)
            : result;
    }

    private bool BeginToolWork() => FlatWorld.Networking.GameNetwork.HasStateAuthority &&
        !MachineInventory.IsBeingDragged(Tool) && toolRequirement.TryConsume(Tool);

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
        CraftingResult result = CraftingService.CraftRecipe(Processor.Input, Processor.Output, Processor.Capabilities, Processor.Recipe, actor);
        // 只对成功交付的真实数量扣耐久，零耐久建筑由持有方统一销毁。
        if (result.Success && !TryConsumeCompletedProducts(resolveStation(), result.Outputs, wearPerProduct))
            throw new InvalidOperationException("已完成加工的工作站耐久状态发生变化。");
        return result;
    }

    public static bool TryConsumeCompletedProducts(ItemData station, IReadOnlyList<ItemData> products, float wearPerProduct)
    {
        if (!CanComplete(station, products, wearPerProduct, out float wear)) return false;
        if (wear > 0f) station.Durability = Mathf.Max(0f, station.Durability - wear);
        return true;
    }

    public static bool CanComplete(ItemData station, IReadOnlyList<ItemData> products, float wearPerProduct, out float wear)
    {
        wear = 0f;
        if (!float.IsFinite(wearPerProduct) || wearPerProduct < 0f || products == null || products.Count == 0) return false;
        foreach (ItemData product in products)
        {
            float amount = product?.Stack?.Amount ?? 0f;
            if (!float.IsFinite(amount) || amount <= 0f) return false;
            wear += amount * wearPerProduct;
        }
        return float.IsFinite(wear) && (wear == 0f || station != null && float.IsFinite(station.Durability) &&
            station.Durability + .0001f >= wear && float.IsFinite(station.MaxDurability) && station.MaxDurability > 0f);
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
