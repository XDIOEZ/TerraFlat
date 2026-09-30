using System;
using System.Runtime.CompilerServices;
using UnityEngine;

/// <summary>石臼把库存、配方和原子捣击保持为一个领域，不把手势动画变成权威对象。</summary>
public class MortarLogic : MachineLogic
{
    #region 石臼
    public Inventory Bowl { get; }
    public bool EnableStrikeGesture { get; }
    public string Label { get; }
    public override GameObject PanelPrefab { get; }
    private readonly MortarState state;
    private readonly CraftingCapabilities capabilities;
    private readonly string station;
    private RuntimeRecipe recipe;
    private bool processing;

    public MortarLogic(MachineEntity entity) : base(entity)
    {
        var config = entity.Definition.Content.Find<Mod_Mortar>() ?? throw new InvalidOperationException("石臼缺少内容配置。");
        var source = (Mod_Mortar)config.Authoring;
        station = config.Value("StationId", source.StationId);
        EnableStrikeGesture = config.Value("EnableStrikeGesture", source.EnableStrikeGesture);
        Label = config.Value("ContainerLabel", source.ContainerLabel);
        PanelPrefab = source.PanelPrefab;
        if (station == Mod_Mortar.CrucibleStationId)
            throw new InvalidOperationException("可落地坩埚需注册完整的热加工领域，不能当普通石臼装载。");
        state = MachineModuleState.Read<MortarState>(entity.Snapshot, Mod_Mortar.ModuleId)
            ?? new MortarState { Bowl = MachineInventory.NewData(Label, 1) };
        var bowl = new Mod_Mortar.MortarInventory { Data = state.Bowl, StationId = station };
        MachineInventory.Rebase(bowl.Data);
        bowl.Data.SetUnlimitedSlots(true);
        bowl.InitData();
        Bowl = Track(bowl);
        capabilities = new CraftingCapabilities { RecipeType = RecipeType.Crafting, StationId = station, AllowOutputIntoInput = true };
        RefreshRecipe();
    }

    private void RefreshRecipe()
    {
        if (processing) return;
        recipe = null;
        foreach (RuntimeRecipe candidate in GameRes.Instance.GetRecipes(RecipeType.Crafting))
        {
            if (!string.Equals(candidate.RequiredStation, station, StringComparison.OrdinalIgnoreCase)) continue;
            if (candidate.inputs.RowItems_List.Count != 1 || candidate.outputs.results.Count == 0 || candidate.action.Count != 0)
                throw new InvalidOperationException("石臼配方必须是无额外动作的单原料、多产物转换：" + candidate.Id);
            if (CraftingRecipeMatcher.TryMatchRecipe(Bowl, candidate, capabilities, out _)) { recipe = candidate; break; }
        }
        Mod_Mortar.AlignManualWorkProgress(state, recipe);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public virtual bool Strike(Player actor)
    {
        if (!EnableStrikeGesture || MachineInventory.IsBeingDragged(Bowl)) return false;
        RefreshRecipe();
        if (recipe == null) return false;
        if (!Mod_Mortar.AdvanceManualWork(state, recipe))
        {
            OnInventoryChanged(null);
            return true;
        }

        processing = true;
        CraftingResult result;
        try { result = CraftingService.CraftRecipe(Bowl, Bowl, capabilities, recipe, actor); }
        finally { processing = false; }
        if (result.Success)
            Mod_Mortar.CompleteManualWork(state, recipe);
        OnInventoryChanged(null);
        return true;
    }

    public override bool Execute(string operation, string argument, Player actor) => operation == "work" && Strike(actor);
    protected override void OnInventoryChanged(ItemSlot slot)
    {
        if (processing) return;
        Bowl.Data.EnsureSpareSlot();
        RefreshRecipe();
        NotifyChanged();
    }
    public override void Capture() => MachineModuleState.Write(Entity.Snapshot, Mod_Mortar.ModuleId, state);
    public override bool ApplyRemoteSnapshot(ItemData snapshot)
    {
        var incoming = MachineModuleState.Read<MortarState>(snapshot, Mod_Mortar.ModuleId);
        if (incoming?.Bowl == null) return false;
        MachineInventory.ApplySnapshot(Bowl, incoming.Bowl);
        state.ProcessingRecipeId = incoming.ProcessingRecipeId;
        state.ProcessingStep = incoming.ProcessingStep;
        RefreshRecipe();
        NotifyRemoteChanged();
        return true;
    }
    #endregion
}
