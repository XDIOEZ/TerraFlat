using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FlatWorld.Gameplay.Progress;
using MemoryPack;
using UnityEngine;

[Serializable, MemoryPackable]
public partial class FireDrillRuntimeState
{
    #region 取火状态
    public Inventory_Data Input;
    public Inventory_Data Output;
    public float Progress;
    public bool HasClicked;
    public string RecipeId = "";
    #endregion
}

/// <summary>钻木进度、衰减和火种提交独立于面板，手持与落地共享同一模块状态。</summary>
public class FireDrillLogic : MachineLogic
{
    #region 摩擦取火
    public const string ModuleId = "钻木取火模块";
    public RecipeProcessor Processor { get; }
    public FireDrillRuntimeState State { get; }
    public override GameObject PanelPrefab { get; }
    public override float TickInterval => .1f;
    public override float Progress01 => Processor.Progress01;
    public override string ActionLabel => "摩擦";
    private readonly List<string> ids;
    private readonly List<string> tags;
    private readonly string outputId;
    private readonly int requiredClicks;
    private readonly float increment;
    private readonly float firstBonus;
    private readonly float decay;
    private string inputId;

    public FireDrillLogic(MachineEntity entity) : base(entity)
    {
        var config = entity.Definition.Content.Find<Mod_FireDrill>() ?? throw new InvalidOperationException("取火器缺少配置。");
        var source = (Mod_FireDrill)config.Authoring;
        State = MachineModuleState.Read<FireDrillRuntimeState>(entity.Snapshot, ModuleId) ?? new FireDrillRuntimeState();
        Inventory input = Track(MachineInventory.Create(source.InputInventory, State.Input, "输入", 1));
        Inventory output = Track(MachineInventory.Create(source.OutputInventory, State.Output, "输出", 1));
        State.Input = input.Data;
        State.Output = output.Data;
        requiredClicks = Mathf.Max(1, config.Value("RequiredClickCount", source.RequiredClickCount));
        increment = config.Value("ClickIncrement", source.ClickIncrement);
        firstBonus = config.Value("FirstClickBonus", source.FirstClickBonus);
        decay = config.Value("DecayRatePerSecond", source.DecayRatePerSecond);
        ids = config.Value("TinderItemIds", source.TinderItemIds) ?? new();
        tags = config.Value("TinderTags", source.TinderTags) ?? new();
        outputId = config.Value("FireSeedItemID", source.FireSeedItemID);
        PanelPrefab = source.UI_Prefab != null ? source.UI_Prefab : GameRes.Instance.GetPrefab("UI_FireDrill");
        Processor = new RecipeProcessor(input, output,
            new CraftingCapabilities { RecipeType = RecipeType.Crafting, StationId = "fire_drill", ApplyDifficultyOutputMultiplier = false },
            new RecipeProcessingState { Input = input.Data, Output = output.Data, Progress = State.Progress, RecipeId = State.RecipeId });
        Processor.Changed += OnProgressChanged;
        RefreshRecipe();
    }

    private void RefreshRecipe()
    {
        if (Processor == null || Processor.IsCommitting) return;
        ItemData input = Processor.Input.Data.GetItemSlot(0)?.itemData;
        bool valid = input != null && (ids.Contains(input.IDName) || input.Tags != null && input.Tags.ContainsAnyTag(tags));
        if (!valid) { inputId = null; Processor.SelectRecipe(null, requiredClicks); return; }
        if (inputId == input.IDName && Processor.Recipe != null) return;
        inputId = input.IDName;
        Processor.SelectRecipe(new RuntimeRecipe
        {
            Id = "machine.fire_drill." + inputId,
            RequiredStation = "fire_drill",
            inputs = new RuntimeRecipeInput { RowItems_List = new List<RuntimeRecipeIngredient> { new() { ItemName = inputId, amount = 1 } } },
            outputs = new RuntimeRecipeOutput { results = new List<RuntimeRecipeResult> { new() { ItemName = outputId, amount = 1 } } }
        }, requiredClicks);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool Rub(Player actor)
    {
        RefreshRecipe();
        if (!Processor.Preview().Success) return false;
        float amount = State.HasClicked ? increment : firstBonus;
        State.HasClicked = true;
        bool completed = Processor.Advance(Mathf.Min(amount, Mathf.Max(0f, requiredClicks - Processor.State.Progress)), actor);
        if (completed) GameplayProgressEvents.PublishFireSeedCreated(actor, outputId);
        return completed;
    }

    public override bool Execute(string operation, string argument, Player actor)
    {
        if (operation == "begin-interaction") { State.HasClicked = false; return true; }
        if (operation != "work") return false;
        Rub(actor); return true;
    }

    public override void Tick(float seconds)
    {
        if (Processor.State.Progress <= 0f) return;
        Processor.State.Progress = Mathf.Max(0f, Processor.State.Progress - Mathf.Max(0f, decay) * seconds);
        NotifyChanged();
    }

    protected override void OnInventoryChanged(ItemSlot slot)
    { if (Processor?.IsCommitting == true) return; State.HasClicked = false; RefreshRecipe(); base.OnInventoryChanged(slot); }
    private void OnProgressChanged() => NotifyChanged();
    public override void Capture()
    {
        State.Progress = Processor.State.Progress;
        State.RecipeId = Processor.State.RecipeId;
        MachineModuleState.Write(Entity.Snapshot, ModuleId, State);
    }
    public override bool ApplyRemoteSnapshot(ItemData snapshot)
    {
        var incoming = MachineModuleState.Read<FireDrillRuntimeState>(snapshot, ModuleId);
        if (incoming == null) return false;
        Processor.ApplyRemoteState(new RecipeProcessingState
        { Input = incoming.Input, Output = incoming.Output, Progress = incoming.Progress, RecipeId = incoming.RecipeId });
        State.HasClicked = incoming.HasClicked;
        RefreshRecipe();
        NotifyRemoteChanged();
        return true;
    }
    public override void Dispose()
    { Processor.Changed -= OnProgressChanged; Processor.Dispose(); base.Dispose(); }
    #endregion
}
