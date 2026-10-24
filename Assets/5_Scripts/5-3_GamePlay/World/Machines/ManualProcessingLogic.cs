using System;
using System.Runtime.CompilerServices;
using FlatWorld.Networking;
using FlatWorld.Localization;
using UnityEngine;

/// <summary>落地手动加工台复用便携状态和目录配方，不保留完整 Item。</summary>
public class ManualProcessingLogic : MachineLogic
{
    #region 手動加工
    public RecipeProcessor Processor { get; }
    public Inventory ToolInventory => runtime.Tool;
    public float WorkPerClick { get; }
    public float BreakRecoveryFraction { get; }
    private readonly ManualProcessingRuntime runtime;
    public override GameObject PanelPrefab => GameRes.ExistingInstance.GetPrefab("UI_Anvil");
    public override string ActionLabel => "锻打";
    public override float Progress01 => Processor.Progress01;
    public override bool CanAct => runtime.CanWork;
    public override string Status => FlatWorldLocalizationService.GetUiFormat("耐久：{0} / {1}",
        Entity.Snapshot.Durability, Entity.Snapshot.MaxDurability);

    public ManualProcessingLogic(MachineEntity entity) : base(entity)
    {
        var config = entity.Definition.Content.Find<Mod_ManualProcessor>()
            ?? throw new InvalidOperationException("手动工作站缺少内容配置。");
        var authoring = (Mod_ManualProcessor)config.Authoring;
        string station = config.Value("Station", authoring.Station);
        WorkPerClick = config.Value("WorkPerClick", authoring.WorkPerClick);
        if (string.IsNullOrWhiteSpace(station) || !MachineDefinition.Positive(WorkPerClick))
            throw new InvalidOperationException("手动工作站配置无效。");
        var state = MachineModuleState.Read<ManualProcessingState>(entity.Snapshot, Mod_ManualProcessor.ModuleId) ?? new ManualProcessingState();
        BreakRecoveryFraction = config.Value("BreakRecoveryFraction", authoring.BreakRecoveryFraction);
        runtime = new ManualProcessingRuntime(() => entity.Snapshot, station, state,
            config.Value("DurabilityPerProduct", authoring.DurabilityPerProduct),
            config.Value("RequiredToolKind", authoring.RequiredToolKind) == ResourceToolKind.None ? null :
                new ManualProcessingToolRequirement(config.Value("RequiredToolKind", authoring.RequiredToolKind),
                    config.Value("RequiredToolTag", authoring.RequiredToolTag),
                    config.Value("ToolDurabilityPerClick", authoring.ToolDurabilityPerClick)));
        Processor = runtime.Processor;
        Track(Processor.Input); Track(Processor.Output);
        if (ToolInventory != null) Track(ToolInventory);
        Processor.Changed += OnProgressChanged;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public virtual bool PerformWork(Player actor)
    {
        if (!GameNetwork.HasStateAuthority) return false;
        bool completed = !runtime.Exhausted && Processor.Advance(WorkPerClick, actor);
        if (runtime.Exhausted)
        {
            if (!MachineWorld.TryDestroyWornMachine(Entity, BreakRecoveryFraction, out string reason))
                Debug.LogError("[工作站损毁] " + reason);
        }
        return completed;
    }

    public override bool Execute(string operation, string argument, Player actor)
    {
        if (operation == "select" && GameNetwork.HasStateAuthority && runtime.SelectRecipe(argument))
        { NotifyChanged(); return true; }
        if (operation != "work") return false;
        PerformWork(actor);
        return true;
    }
    private void OnProgressChanged() => NotifyChanged();
    protected override void OnInventoryChanged(ItemSlot slot)
    {
        if (!Processor.IsAdvancing) base.OnInventoryChanged(slot);
    }
    public override void Capture() => MachineModuleState.Write(Entity.Snapshot, Mod_ManualProcessor.ModuleId, runtime.State);
    public override bool ApplyRemoteSnapshot(ItemData snapshot)
    {
        runtime.ApplyRemoteState(MachineModuleState.Read<ManualProcessingState>(snapshot, Mod_ManualProcessor.ModuleId));
        NotifyRemoteChanged();
        return true;
    }
    public override void Dispose()
    { Processor.Changed -= OnProgressChanged; runtime.Dispose(); base.Dispose(); }
    #endregion
}

/// <summary>手钻在机器世界中保留独立钻头品质与磨损，拆回后仍是同一份共享状态。</summary>
public class HandDrillLogic : MachineLogic
{
    #region 钻孔与耐久
    public HandDrillRuntimeState State { get; }
    public RecipeProcessor Processor { get; }
    public float WorkPerClick { get; }
    public override GameObject PanelPrefab => GameRes.ExistingInstance.GetPrefab("UI_HandDrill");
    public override string ActionLabel => "钻孔";
    public override bool CanAct => State.Durability > 0f;
    public override float Progress01 => Processor.Progress01;

    public HandDrillLogic(MachineEntity entity) : base(entity)
    {
        var config = entity.Definition.Content.Find<Mod_HandDrill>()
            ?? throw new InvalidOperationException("手钻缺少内容配置。");
        var authoring = (Mod_HandDrill)config.Authoring;
        WorkPerClick = config.Value("WorkPerClick", authoring.WorkPerClick);
        if (!MachineDefinition.Positive(WorkPerClick)) throw new InvalidOperationException("手钻工作量无效。");
        State = MachineModuleState.Read<HandDrillRuntimeState>(entity.Snapshot, Mod_HandDrill.ModuleId) ?? new HandDrillRuntimeState
        {
            Durability = entity.Snapshot.Durability,
            MaxDurability = Mathf.Max(1f, entity.Snapshot.MaxDurability)
        };
        if (State.Version != HandDrillRuntimeState.CurrentVersion || State.Processing == null)
            throw new InvalidOperationException("手钻状态版本无效。");
        State.Durability = Mathf.Clamp(State.Durability, 0f, State.MaxDurability);
        Processor = new RecipeProcessor("hand_drill", State.Processing);
        Track(Processor.Input); Track(Processor.Output);
        Processor.Changed += OnProgressChanged;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool Drill(Player actor)
    {
        if (!GameNetwork.HasStateAuthority || !CanAct) return false;
        bool completed = Processor.Advance(WorkPerClick, actor);
        if (completed) State.Durability = Mathf.Max(0f, State.Durability - 1f);
        Entity.Snapshot.Durability = State.Durability;
        Entity.Snapshot.MaxDurability = State.MaxDurability;
        NotifyChanged();
        return completed;
    }

    public override bool Execute(string operation, string argument, Player actor)
    {
        if (operation != "work" || !CanAct) return false;
        Drill(actor); return true;
    }
    private void OnProgressChanged() => NotifyChanged();
    public override void Capture() => MachineModuleState.Write(Entity.Snapshot, Mod_HandDrill.ModuleId, State);
    public override bool ApplyRemoteSnapshot(ItemData snapshot)
    {
        var incoming = MachineModuleState.Read<HandDrillRuntimeState>(snapshot, Mod_HandDrill.ModuleId);
        if (incoming == null) return false;
        State.Durability = incoming.Durability;
        State.MaxDurability = incoming.MaxDurability;
        Processor.ApplyRemoteState(incoming.Processing);
        NotifyRemoteChanged();
        return true;
    }
    public override void Dispose()
    { Processor.Changed -= OnProgressChanged; Processor.Dispose(); base.Dispose(); }
    #endregion
}
