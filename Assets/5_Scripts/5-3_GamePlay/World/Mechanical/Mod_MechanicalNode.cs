using System;
using FlatWorld.Localization;
using FlatWorld.Networking;
using UnityEngine;

/// <summary>机械节点的 Item 表现与交互桥。手持朝向只存在于本组件，切换物品立即清除；世界状态由 MechanicalWorld 整网管理。</summary>
public sealed class Mod_MechanicalNode : Module, IInteractable, IBuildingPlacementCommitted, IBuildingPlacementExtension
{
    #region 配置与状态
    public const string ModuleId = "机械动力模块";
    public string DefinitionId; // 对应机械配置目录的稳定节点 ID
    public Ex_ModData_MemoryPackable Data = new() { ID = ModuleId };
    public override ModuleData _Data { get => Data; set => Data = (Ex_ModData_MemoryPackable)value; }
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public MechanicalDefinition Definition { get; private set; }
    public MechanicalNode Node { get; private set; }
    public MechanicalNodeState LocalState { get; private set; }
    public bool PlacementVertical { get; private set; }
    public int OccupancyLayer => (Definition ?? MechanicalCatalog.Get(DefinitionId))?.Layer ?? 0;
    private MechanicalPanelSession panel;
    private GameController controller;
    private SpriteRenderer spriteRenderer;
    private Quaternion originalRotation;
    private int originalSortingOrder;
    private bool placed;
    #endregion

    #region 生命周期
    public override void Load()
    {
        Definition = MechanicalCatalog.Get(string.IsNullOrWhiteSpace(DefinitionId) ? item.itemData.IDName : DefinitionId)
            ?? throw new InvalidOperationException("机械定义不存在：" + DefinitionId);
        LocalState = Data.GetData<MechanicalNodeState>() ?? new MechanicalNodeState();
        PlacementVertical = false;
        placed = Mod_Building.TryReadBuildingData(item.itemData, out _, out var building) && building.Role == BuildingRole.PlacedBuilding;
        spriteRenderer = item.GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            originalRotation = spriteRenderer.transform.localRotation;
            originalSortingOrder = spriteRenderer.sortingOrder;
        }
        item.OnInHandChanged += OnHandChanged;
        if (!placed) BindRotationInput();
        if (placed && building.State is BuildingState.Installed or BuildingState.Damaged) AttachWorld();
        ApplyVisual();
    }
    public override void Save()
    {
        if (placed) Data.WriteData(Node?.State ?? LocalState);
        // Summoner 不保存 PlacementVertical，保持同堆物品身份一致。
    }
    public override void Unload()
    {
        panel?.Dispose(); panel = null;
        if (item != null) item.OnInHandChanged -= OnHandChanged;
        if (controller != null) controller.BuildingRotationRequested -= RotatePlacement;
        controller = null;
        MechanicalWorld.Detach(this);
        Node = null; PlacementVertical = false;
        if (spriteRenderer != null)
        {
            spriteRenderer.transform.localRotation = originalRotation;
            spriteRenderer.sortingOrder = originalSortingOrder;
        }
    }
    public void OnBuildingPlacementCommitted() { placed = true; AttachWorld(); }
    private void AttachWorld() { Node = MechanicalWorld.Attach(this); if (Node != null) LocalState = Node.State; ApplyVisual(); }
    private void BindRotationInput()
    {
        if (controller != null) controller.BuildingRotationRequested -= RotatePlacement;
        controller = item.Owner?.GetComponentInChildren<GameController>();
        if (controller != null) controller.BuildingRotationRequested += RotatePlacement;
    }
    private void OnHandChanged(bool held)
    {
        PlacementVertical = false;
        if (held) BindRotationInput();
        else if (controller != null) { controller.BuildingRotationRequested -= RotatePlacement; controller = null; }
    }
    #endregion

    #region 放置朝向
    /// <summary>R 从统一控制器转发，仅影响当前真实手持的放置过程。</summary>
    public void RotatePlacement()
    {
        if (placed || !item.InHand || !Definition.Rotatable) return;
        PlacementVertical = !PlacementVertical;
        var building = item.itemMods.GetMod_ByID<Mod_Building>(ModText.Building);
        ApplyPreview(building?.GhostShadow);
    }
    public void ApplyPreview(BuildingShadow shadow)
    {
        if (shadow?.ShadowRenderer == null || !Definition.Rotatable) return;
        shadow.ShadowRenderer.transform.localRotation = Quaternion.Euler(0, 0, PlacementVertical ? 90 : 0);
    }
    public bool ValidatePlacement(Vector2Int cell, out string reason)
        => MechanicalWorld.ValidatePlacement(Definition, cell, PlacementVertical, out reason);

    /// <summary>候选本体建立后、Load 前写世界朝向；覆盖拆回快照中的旧方向。</summary>
    public void PreparePlacedData(ItemData data)
    {
        if (!MechanicalWorld.TryGetModuleData(data, out var module)) throw new InvalidOperationException("机械本体缺少模块。");
        var state = module.GetData<MechanicalNodeState>() ?? new MechanicalNodeState();
        state.Vertical = PlacementVertical;
        if (Definition.Source == "water") state.WaterSupported = MechanicalWorld.HasWaterNeighbor(MechanicalWorld.CellOf(data.transform.position));
        module.WriteData(state);
    }
    /// <summary>拆回快照只重置世界朝向，不改动仍在世界中的节点，也不丢弃机器库存。</summary>
    public void PrepareRepackedSnapshot(ItemData snapshot)
    {
        if (!MechanicalWorld.TryGetModuleData(snapshot, out var module)) return;
        var state = module.GetData<MechanicalNodeState>() ?? new MechanicalNodeState();
        state.Vertical = false;
        module.WriteData(state);
    }
    private void ApplyVisual()
    {
        if (spriteRenderer == null || !placed) return;
        spriteRenderer.transform.localRotation = originalRotation * Quaternion.Euler(0, 0, LocalState.Vertical ? 90 : 0);
        if (Definition.Layer == 1) spriteRenderer.sortingOrder = 2;
    }
    #endregion

    #region 操作面板
    public void OnInteractStart(Item actor)
    {
        if (!placed || Node == null || !GameNetwork.HasStateAuthority) return;
        // 手摇轮改为按住世界交互持续供能，不再通过机械面板按钮脉冲供能。
        if (Definition.Source == "manual") return;
        panel ??= new MechanicalPanelSession("UI_Mechanical", item, Node.Processor, PerformOperation, GetStatus, GetActionLabel);
        panel.Toggle(actor);
    }
    public void OnInteractUpdate(Item actor)
    {
        if (!placed || Node?.State == null || !GameNetwork.HasStateAuthority || Definition.Source != "manual") return;

        // 配置继续保留 Pulse/Reserve 字段兼容 MOD；默认值只覆盖约两个机械 Tick。
        float holdBufferSeconds = Mathf.Min(
            MechanicalCatalog.Settings.ManualReserveSeconds,
            Mathf.Max(MechanicalCatalog.Settings.ManualPulseSeconds, MechanicalCatalog.Settings.TickSeconds * 2f));
        LocalState.ManualSeconds = Mathf.Max(LocalState.ManualSeconds, holdBufferSeconds);
    }
    public void OnInteractCancel(Item actor) => panel?.Close();
    public void RefreshPanel() => panel?.Refresh();
    public string GetActionLabel()
    {
        if (Definition.Kind == "clutch") return LocalState.Engaged ? "断开" : "接合";
        if (Definition.Kind == "gearbox") return "切换传动比";
        return string.Empty;
    }
    private void PerformOperation(Player actor)
    {
        if (!GameNetwork.HasStateAuthority || Node?.State == null) return;
        if (Definition.Kind == "clutch") { LocalState.Engaged = !LocalState.Engaged; MechanicalWorld.TopologyChanged(Node); }
        else if (Definition.Kind == "gearbox") { LocalState.RatioIndex = (LocalState.RatioIndex + 1) % Definition.Ratios.Length; MechanicalWorld.TopologyChanged(Node); }
        Save(); panel?.Refresh();
    }
    private string GetStatus()
    {
        string state = FlatWorldLocalizationService.GetUiText(Node?.Network?.Status ?? "停止");
        string status = FlatWorldLocalizationService.GetUiFormat("{0} · 转速 {1:0} · 动力 {2:0.#}/{3:0.#}", state,
            Node?.Rpm ?? 0, Node?.Network?.Supply ?? 0, Node?.Network?.Demand ?? 0);
        if (Definition.Kind == "gearbox")
            status += FlatWorldLocalizationService.GetUiFormat(" · 传动比 {0:0.##}", Definition.Ratios[Mathf.Clamp(LocalState.RatioIndex, 0, Definition.Ratios.Length - 1)]);
        return status;
    }
    #endregion
}
