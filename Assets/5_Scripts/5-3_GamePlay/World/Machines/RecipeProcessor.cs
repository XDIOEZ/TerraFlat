using System;
using System.Runtime.CompilerServices;
using UnityEngine;

/// <summary>可复用的一槽加工器；预检与提交统一经过 CraftingService，满输出或失败时不扣输入、不部分产出。</summary>
public class RecipeProcessor : IDisposable
{
    #region 状态与库存
    public RecipeProcessingState State { get; }
    public Inventory Input { get; }
    public Inventory Output { get; }
    public string Station { get; }
    public string ProcessCapability { get; }
    public CraftingCapabilities Capabilities { get; }
    public RuntimeRecipe Recipe { get; private set; }
    public float RequiredWork { get; private set; } = 1f;
    public bool IsCommitting => committing;
    public Func<CraftingResult> PreviewOverride;
    public Func<Player, CraftingResult> CommitOverride;
    private readonly bool registeredProcess;
    private readonly bool ownsInventories;
    private bool disposed;
    private const int MaxCompletionsPerAdvance = 64;
    public event Action Changed;
    public event Action<RecipeProcessor> StateChanged; // 世界快照增量同步入口。
    private bool committing;

    public RecipeProcessor(string station, RecipeProcessingState state, string processCapability = null)
    {
        Station = station;
        ProcessCapability = processCapability?.Trim() ?? string.Empty;
        State = state ?? throw new ArgumentNullException(nameof(state));
        RebaseInventory(State.Input);
        RebaseInventory(State.Output);
        Input = new ProcessingInventory(station, ProcessCapability) { Data = state.Input };
        Output = new Inventory { Data = state.Output };
        Capabilities = new CraftingCapabilities { StationId = station, InputSlotLimit = 1, ApplyDifficultyOutputMultiplier = false };
        registeredProcess = true;
        ownsInventories = true;
        Input.InitData();
        Output.InitData();
        Input.Data.Event_OnDataChanged += OnInputChanged;
        Output.Data.Event_OnDataChanged += OnOutputChanged;
        ReconcileRecipe();
    }

    /// <summary>工作台与热加工使用已有多槽库存，面板不拥有配方选择与工作进度。</summary>
    public RecipeProcessor(Inventory input, Inventory output, CraftingCapabilities capabilities, RecipeProcessingState state)
    {
        Input = input ?? throw new ArgumentNullException(nameof(input));
        Output = output ?? throw new ArgumentNullException(nameof(output));
        Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        State = state ?? throw new ArgumentNullException(nameof(state));
        Station = capabilities.StationId;
        State.Input = Input.Data;
        State.Output = Output.Data;
        Input.Data.Event_OnDataChanged += OnInputChanged;
        Output.Data.Event_OnDataChanged += OnOutputChanged;
    }

    /// <summary>相同配方重绑不会清空进度；换配方才重置，支持关闭面板后继续工作。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void SelectRecipe(RuntimeRecipe recipe, float requiredWork)
    {
        if (recipe != null && !MachineDefinition.Positive(requiredWork))
            throw new ArgumentOutOfRangeException(nameof(requiredWork));
        string id = recipe?.Id ?? string.Empty;
        if (!string.Equals(State.RecipeId, id, StringComparison.Ordinal)) State.Progress = 0f;
        State.RecipeId = id;
        Recipe = recipe;
        RequiredWork = recipe == null ? 1f : requiredWork;
    }

    public bool TryGetProcess(out MachineProcessDefinition process)
        => MachineCatalog.TryGetProcess(Station, Input.Data.GetItemSlot(0)?.itemData?.IDName, out process);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public virtual CraftingResult Preview()
    {
        ReconcileRecipe();
        return PreviewOverride != null ? PreviewOverride() : Recipe != null
            ? CraftingService.PreviewRecipe(Input, Output, Capabilities, Recipe)
            : CraftingResult.Failed(CraftingFailureReason.RecipeNotFound, "当前材料没有加工配方");
    }

    /// <summary>按有效工作时间推进加工；提交成功之后才清空当前加工进度。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public virtual bool Advance(float workSeconds, Player actor = null)
    {
        if (disposed || committing || !MachineDefinition.Positive(workSeconds)) return false;
        // 玩家正在拖动加工槽时保留当前物品，避免高速生产在松手前消费或替换来源堆。
        if (MachineInventory.IsBeingDragged(Input) || MachineInventory.IsBeingDragged(Output)) return false;
        ReconcileRecipe();
        if (Recipe == null || !Preview().Success) return false;
        State.Progress += workSeconds;
        bool success = false;
        int completed = 0;
        while (Recipe != null && State.Progress >= RequiredWork && completed < MaxCompletionsPerAdvance)
        {
            float cost = RequiredWork;
            committing = true;
            try
            {
                CraftingResult result = Commit(actor);
                if (!result.Success) break;
                State.Progress = Mathf.Max(0f, State.Progress - cost);
                success = true;
                completed++;
            }
            finally { committing = false; }
            ReconcileRecipe();
            if (!Preview().Success) break;
        }
        Changed?.Invoke(); StateChanged?.Invoke(this);
        return success;
    }

    /// <summary>独立具名事务入口供 Harmony 修改；默认仍使用统一库存锁和原子提交。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public virtual CraftingResult Commit(Player actor)
        => CommitOverride != null ? CommitOverride(actor)
            : CraftingService.CraftRecipe(Input, Output, Capabilities, Recipe, actor);

    /// <summary>直接手动推进加工时沿用自动加工的预检、进度和原子提交。</summary>
    public bool AdvanceManually(float workSeconds, Player actor = null) => Advance(workSeconds, actor);

    public float Progress01 => Recipe != null ? Mathf.Clamp01(State.Progress / RequiredWork) : 0f;

    /// <summary>客户端原位接收服务器库存和工作进度，不触发本地扣料或重新绑定面板。</summary>
    public void ApplyRemoteState(RecipeProcessingState incoming)
    {
        if (incoming == null) throw new ArgumentNullException(nameof(incoming));
        committing = true;
        try
        {
            MachineInventory.ApplySnapshot(Input, incoming.Input);
            MachineInventory.ApplySnapshot(Output, incoming.Output);
            State.RecipeId = incoming.RecipeId;
            State.Progress = incoming.Progress;
            ReconcileRecipe();
        }
        finally { committing = false; }
        Changed?.Invoke();
    }
    private void OnInputChanged(ItemSlot slot)
    { if (!committing) { State.Progress = 0f; ReconcileRecipe(); Changed?.Invoke(); StateChanged?.Invoke(this); } }
    private void OnOutputChanged(ItemSlot slot)
    { if (!committing) { Changed?.Invoke(); StateChanged?.Invoke(this); } }
    private void ReconcileRecipe()
    {
        if (registeredProcess)
        {
            if (!string.IsNullOrWhiteSpace(ProcessCapability))
            {
                bool found = ItemProcessingResolver.TryResolveRecipe(
                    Input, ProcessCapability, Capabilities, out RuntimeItemProcessingDefinition processing);
                SelectRecipe(found ? processing.Recipe : null, found ? processing.WorkRequired : 1f);
            }
            else
            {
                bool found = TryGetProcess(out var process);
                SelectRecipe(found ? process.Recipe : null, found ? process.WorkSeconds : 1f);
            }
        }
        if (!MachineDefinition.NonNegative(State.Progress)) State.Progress = 0;
    }
    /// <summary>独立二进制模块中的库存恢复时，同样按当前定义刷新静态物品配置。</summary>
    private static void RebaseInventory(Inventory_Data inventory)
    {
        if (inventory?.itemSlots == null || inventory.itemSlots.Count != 1)
            throw new InvalidOperationException("机械加工库存必须包含一个有效槽位。");
        var slot = inventory.itemSlots[0] ?? throw new InvalidOperationException("机械加工槽位不能为空。");
        if (slot.itemData != null && GameRes.ExistingInstance != null)
            slot.itemData = ItemDefinitionRuntime.RebasePersistedData(GameRes.ExistingInstance, slot.itemData);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Input.Data.Event_OnDataChanged -= OnInputChanged;
        Output.Data.Event_OnDataChanged -= OnOutputChanged;
        if (ownsInventories)
        {
            Input.UnbindController(); Output.UnbindController();
            Input.UnbindRuntimeDataEvents(); Output.UnbindRuntimeDataEvents();
            Input.item = null; Output.item = null;
        }
        Changed = null;
        StateChanged = null;
        PreviewOverride = null;
        CommitOverride = null;
    }
    #endregion

    /// <summary>输入槽只允许当前目录已注册的输入；覆盖统一转移入口，适配鼠标、触屏与快捷转移。</summary>
    private sealed class ProcessingInventory : Inventory
    {
        private readonly string station;
        private readonly string processCapability;
        public ProcessingInventory(string station, string processCapability)
        {
            this.station = station;
            this.processCapability = processCapability;
        }
        public override bool CanAcceptQuickTransfer(ItemSlot source, ItemSlot target)
            => base.CanAcceptQuickTransfer(source, target) &&
               (!string.IsNullOrWhiteSpace(processCapability)
                   ? ItemProcessingResolver.TryResolve(source?.itemData, processCapability, out _)
                   : MachineCatalog.TryGetProcess(station, source?.itemData?.IDName, out _));
    }
}
