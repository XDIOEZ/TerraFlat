using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FlatWorld.Networking;
using UnityEngine;

/// <summary>工作台的配方选择、点击工作量和库存只属于机器；UI 销毁不重置加工。</summary>
public class WorkbenchLogic : MachineLogic
{
    #region 工作台状态
    public RecipeProcessor Processor { get; }
    public Inventory Input { get; }
    public Inventory Output => Processor.Output;
    public Inventory SharedInput => linkedInput.Shared;
    private readonly LinkedCraftingInput linkedInput;
    private int sharedNodeId;
    private MachineEntity sharedNode;
    private bool sharedLinkDirty = true;
    private bool disposed;
    public int RequiredClicks { get; }
    public IReadOnlyList<CraftingRecipeMatch> Candidates { get; private set; } = Array.Empty<CraftingRecipeMatch>();
    public override GameObject PanelPrefab { get; }
    public override float Progress01 => Processor.Progress01;
    public override string ActionLabel => "制作";
    private bool refreshing;

    public WorkbenchLogic(MachineEntity entity) : base(entity)
    {
        MachineModuleConfiguration config = entity.Definition.Content.Find<Mod_MakeTable>()
            ?? throw new InvalidOperationException("工作台缺少内容配置。");
        var authoring = (Mod_MakeTable)config.Authoring;
        int level = Mathf.Max(1, config.Value("workbenchLevel", authoring.workbenchLevel));
        int baseline = Mathf.Max(1, Mathf.Max(config.Value("minClickCount", authoring.minClickCount),
            config.Value("baseClickCount", authoring.baseClickCount) -
            (level - 1) * config.Value("clickReductionPerLevel", authoring.clickReductionPerLevel)));
        RequiredClicks = Mathf.Max(1, Mathf.RoundToInt(baseline * .7f));
        PanelPrefab = authoring.InventoryPanel_Prefab;
        var state = MachinePersistence.Read<RecipeProcessingState>(entity.Snapshot, "workbench");
        Input = Track(MachineInventory.Create(authoring.inputInventory, state?.Input, "输入", 5));
        Inventory output = Track(MachineInventory.Create(authoring.outputInventory, state?.Output, "输出", 2));
        linkedInput = new LinkedCraftingInput(Input);
        Processor = new RecipeProcessor(linkedInput, output, new CraftingCapabilities
        {
            RecipeType = RecipeType.Crafting, StationId = "workbench", CompatibleStationIds = new[] { "handcraft" },
            InputSlotLimit = Input.Data.itemSlots.Count, AllowOutputIntoInput = false
        }, state ?? new RecipeProcessingState());
        Processor.Changed += OnProgressChanged;
        MachineWorld.NodeCellChanged += OnNodeCellChanged;
        RefreshRecipes();
    }

    /// <summary>材料变化时保留仍然成立的所选配方，不能回退到目录首项消费错误材料。</summary>
    public void RefreshRecipes()
    {
        if (refreshing || Processor == null || Processor.IsCommitting || Processor.IsAdvancing) return;
        refreshing = true;
        try
        {
            RefreshSharedInput();
            string selected = Processor.State.RecipeId;
            Candidates = CraftingRecipeMatcher.TryMatchAll(linkedInput, Processor.Capabilities, out var matches, out _)
                ? matches : Array.Empty<CraftingRecipeMatch>();
            RuntimeRecipe next = null;
            foreach (CraftingRecipeMatch match in Candidates)
                if (match.Recipe.Id == selected) { next = match.Recipe; break; }
            if (Processor.PausedForWorkingTool)
            {
                if (next == null)
                {
                    next = Processor.Recipe;
                    if (next == null && GameRes.ExistingInstance != null)
                        GameRes.ExistingInstance.recipeById.TryGetValue(selected, out next);
                }
                else Processor.ResumeAfterWorkingToolReplacement();
            }
            else if (next == null && Candidates.Count > 0) next = Candidates[0].Recipe;
            Processor.SelectRecipe(next, RequiredClicks);
        }
        finally { refreshing = false; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool SelectRecipe(string recipeId)
    {
        if (!GameNetwork.HasStateAuthority) return false;
        RefreshRecipes();
        foreach (CraftingRecipeMatch match in Candidates)
            if (match.Recipe.Id == recipeId)
            { Processor.SelectRecipe(match.Recipe, RequiredClicks); NotifyChanged(); return true; }
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool PerformWork(float amount, Player actor, bool manual = false)
    {
        if (!GameNetwork.HasStateAuthority) return false;
        RefreshRecipes();
        bool result = manual ? Processor.AdvanceManually(amount, actor) : Processor.Advance(amount, actor);
        RefreshRecipes();
        return result;
    }

    public override bool Execute(string operation, string argument, Player actor)
    {
        if (operation == "select") return SelectRecipe(argument);
        if (operation != "work") return false;
        PerformWork(1f, actor, true);
        return true;
    }

    public override void Tick(float seconds)
    {
        if (Entity.Definition.HasMechanicalPorts && Entity.SpeedRpm > 0f)
            PerformWork(MachineWorld.CalculateWorkAmount(Entity, seconds), null);
    }

    protected override void OnInventoryChanged(ItemSlot slot)
    { RefreshRecipes(); base.OnInventoryChanged(slot); }
    private void OnProgressChanged() => NotifyChanged();
    public override void Capture() => MachinePersistence.Write(Entity.Snapshot, "workbench", new RecipeProcessingState
    {
        Input = Input.Data, Output = Output.Data, RecipeId = Processor.State.RecipeId, Progress = Processor.State.Progress,
        PausedForWorkingTool = Processor.PausedForWorkingTool
    });
    public override bool ApplyRemoteSnapshot(ItemData snapshot)
    {
        var incoming = MachinePersistence.Read<RecipeProcessingState>(snapshot, "workbench");
        if (incoming == null) return false;
        MachineInventory.ApplySnapshot(Input, incoming.Input);
        MachineInventory.ApplySnapshot(Output, incoming.Output);
        linkedInput.RebuildSlots();
        Processor.State.RecipeId = incoming.RecipeId;
        Processor.State.Progress = incoming.Progress;
        Processor.State.PausedForWorkingTool = incoming.PausedForWorkingTool;
        RefreshRecipes();
        NotifyRemoteChanged();
        return true;
    }
    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        MachineWorld.NodeCellChanged -= OnNodeCellChanged;
        if (sharedNode != null) sharedNode.SharedInventoryConsumers--;
        Processor.Changed -= OnProgressChanged; Processor.Dispose(); linkedInput.Dispose(); base.Dispose();
    }

    private void RefreshSharedInput()
    {
        if (!sharedLinkDirty && (sharedNode == null || sharedNode.Logic is StorageLogic existing &&
            existing.Inventories.Count == 1 && ReferenceEquals(existing.Inventories[0], linkedInput.Shared))) return;
        sharedLinkDirty = false;
        MachineEntity cabinet = MachineWorld.FindAdjacentCraftingInput(Entity, sharedNodeId);
        if (cabinet != null) MachineWorld.WakeForInteraction(cabinet);
        Inventory inventory = cabinet?.Logic is StorageLogic storage && storage.Inventories.Count == 1
            ? storage.Inventories[0] : null;
        MachineEntity nextNode = inventory != null ? cabinet : null;
        if (!ReferenceEquals(sharedNode, nextNode))
        {
            if (sharedNode != null) sharedNode.SharedInventoryConsumers--;
            if (nextNode != null) nextNode.SharedInventoryConsumers++;
        }
        sharedNodeId = inventory != null ? cabinet.Id : 0;
        sharedNode = nextNode;
        if (linkedInput.SetShared(inventory) && !Processor.PausedForWorkingTool) Processor.State.Progress = 0f;
        Processor.Capabilities.InputSlotLimit = linkedInput.Data.itemSlots.Count;
    }

    private void OnNodeCellChanged(string scopeKey, Vector2Int cell)
    {
        if (scopeKey != Entity.ScopeKey || !MachineWorld.IsAdjacentCraftingCell(Entity, cell)) return;
        sharedLinkDirty = true;
        RefreshRecipes();
        NotifyRemoteChanged();
    }
    #endregion
}
