using System;
using UnityEngine;

/// <summary>可复用的一槽加工器；预检与提交统一经过 CraftingService，满输出或失败时不扣输入、不部分产出。</summary>
public sealed class MechanicalProcessor : IDisposable
{
    #region 状态与库存
    public MechanicalProcessingState State { get; }
    public Inventory Input { get; }
    public Inventory Output { get; }
    public string Station { get; }
    private readonly CraftingCapabilities capabilities;
    public event Action Changed;
    private bool committing;

    public MechanicalProcessor(string station, MechanicalProcessingState state)
    {
        Station = station;
        State = state ?? throw new ArgumentNullException(nameof(state));
        RebaseInventory(State.Input);
        RebaseInventory(State.Output);
        Input = new ProcessingInventory(station) { Data = state.Input };
        Output = new Inventory { Data = state.Output };
        capabilities = new CraftingCapabilities { StationId = station, InputSlotLimit = 1, ApplyDifficultyOutputMultiplier = false };
        Input.InitData();
        Output.InitData();
        Input.Data.Event_OnDataChanged += OnInputChanged;
        Output.Data.Event_OnDataChanged += OnOutputChanged;
        ReconcileRecipe();
    }

    public bool TryGetProcess(out MechanicalProcessDefinition process)
        => MechanicalCatalog.TryGetProcess(Station, Input.Data.GetItemSlot(0)?.itemData?.IDName, out process);

    public CraftingResult Preview()
        => TryGetProcess(out var process)
            ? CraftingService.PreviewRecipe(Input, Output, capabilities, process.Recipe)
            : CraftingResult.Failed(CraftingFailureReason.RecipeNotFound, "当前材料没有加工配方");

    /// <summary>有扭矩且完整产物可接收时推进工作量；提交成功之后才清空当前加工进度。</summary>
    public bool Advance(float workSeconds, Player actor = null)
    {
        if (!MechanicalDefinition.Positive(workSeconds)) return false;
        ReconcileRecipe();
        if (!TryGetProcess(out var process) || !Preview().Success) return false;
        State.Progress = Mathf.Min(process.WorkSeconds, State.Progress + workSeconds);
        bool success = false;
        if (State.Progress >= process.WorkSeconds)
        {
            committing = true;
            try
            {
                success = CraftingService.CraftRecipe(Input, Output, capabilities, process.Recipe, actor).Success;
                if (success) State.Progress = 0;
            }
            finally { committing = false; }
            ReconcileRecipe();
        }
        Changed?.Invoke();
        return success;
    }

    public float Progress01 => TryGetProcess(out var process) ? Mathf.Clamp01(State.Progress / process.WorkSeconds) : 0f;
    private void OnInputChanged(ItemSlot slot) { if (!committing) { ReconcileRecipe(); Changed?.Invoke(); } }
    private void OnOutputChanged(ItemSlot slot) { if (!committing) Changed?.Invoke(); }
    private void ReconcileRecipe()
    {
        string id = TryGetProcess(out var process) ? process.Recipe.Id : string.Empty;
        if (!string.Equals(State.RecipeId, id, StringComparison.Ordinal)) { State.RecipeId = id; State.Progress = 0; }
        if (!MechanicalDefinition.NonNegative(State.Progress)) State.Progress = 0;
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
        Input.Data.Event_OnDataChanged -= OnInputChanged;
        Output.Data.Event_OnDataChanged -= OnOutputChanged;
        Input.UnbindController(); Output.UnbindController();
        Input.item = null; Output.item = null;
    }
    #endregion

    /// <summary>输入槽只允许当前目录已注册的输入；覆盖统一转移入口，适配鼠标、触屏与快捷转移。</summary>
    private sealed class ProcessingInventory : Inventory
    {
        private readonly string station;
        public ProcessingInventory(string station) { this.station = station; }
        public override bool CanAcceptQuickTransfer(ItemSlot source, ItemSlot target)
            => base.CanAcceptQuickTransfer(source, target) &&
               MechanicalCatalog.TryGetProcess(station, source?.itemData?.IDName, out _);
    }
}
